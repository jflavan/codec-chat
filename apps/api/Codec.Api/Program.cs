using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Azure.Identity;
using Azure.Storage.Blobs;
using Serilog;
using Serilog.Formatting.Compact;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using StackExchange.Redis;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Services;
using Codec.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseSerilog((ctx, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddControllers(options =>
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute()));

// Redis distributed cache + direct connection for tracking set operations.
// Two connections are intentional: AddStackExchangeRedisCache manages its own internal
// ConnectionMultiplexer, while the IConnectionMultiplexer singleton is needed for Redis SET
// operations (tracking keys for bulk invalidation) that IDistributedCache does not support.
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(redisConnectionString));
}

builder.Services.AddSingleton<MessageCacheService>();

var signalRBuilder = builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("codec");
    });
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var googleClientId = builder.Configuration["Google:ClientId"];
if (string.IsNullOrWhiteSpace(googleClientId))
{
    throw new InvalidOperationException("Google:ClientId is required for authentication.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = true,
            ValidAudience = googleClientId,
            ValidateLifetime = true
        };

        // Allow SignalR to read the JWT from the query string for WebSocket connections.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IUserService, UserService>();

// Named HTTP client for SFU internal API calls.
// Attaches the shared internal key header when configured.
builder.Services.AddHttpClient("sfu", (sp, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    var sfuKey = sp.GetRequiredService<IConfiguration>()["Voice:SfuInternalKey"];
    if (!string.IsNullOrEmpty(sfuKey))
        client.DefaultRequestHeaders.Add("X-Internal-Key", sfuKey);
    else
        sp.GetRequiredService<ILogger<Program>>()
          .LogWarning("SFU security configuration incomplete: Voice:SfuInternalKey is not configured.");
});

// Validate voice configuration at startup in non-development environments.
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["Voice:TurnSecret"]))
        throw new InvalidOperationException("Voice:TurnSecret must be configured in production.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["Voice:SfuInternalKey"]))
        throw new InvalidOperationException("Voice:SfuInternalKey must be configured in production.");
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CodecDbContext>("database", tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// File storage provider (Local or AzureBlob).
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Local";
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]?.TrimEnd('/') ?? "";

if (storageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
{
    var blobServiceUri = new Uri(builder.Configuration["Storage:AzureBlob:ServiceUri"]
        ?? throw new InvalidOperationException("Storage:AzureBlob:ServiceUri is required when using AzureBlob provider."));
    builder.Services.AddSingleton(new BlobServiceClient(blobServiceUri, new DefaultAzureCredential()));
    builder.Services.AddSingleton<IFileStorageService, AzureBlobStorageService>();
}
else
{
    Directory.CreateDirectory(uploadsPath);
    builder.Services.AddSingleton<IFileStorageService>(new LocalFileStorageService(uploadsPath, $"{apiBaseUrl}/uploads"));
}

builder.Services.AddSingleton<VoiceCallTimeoutService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceCallTimeoutService>());

builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddHostedService<PresenceBackgroundService>();

builder.Services.AddSingleton<IAvatarService, AvatarService>();
builder.Services.AddSingleton<IImageUploadService, ImageUploadService>();
builder.Services.AddScoped<ICustomEmojiService, CustomEmojiService>();

// Link preview HttpClient with DNS rebinding protection, redirect limits, and no cookies.
builder.Services.AddHttpClient<ILinkPreviewService, LinkPreviewService>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "CodecBot/1.0 (+https://codec.chat)");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 3,
    UseCookies = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectCallback = async (context, cancellationToken) =>
    {
        // DNS rebinding protection: resolve and validate before connecting.
        var entries = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        foreach (var ip in entries)
        {
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                throw new HttpRequestException($"Blocked connection to private IP {ip}.");
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                var isPrivate = bytes[0] switch
                {
                    10 => true,
                    172 => bytes[1] >= 16 && bytes[1] <= 31,
                    192 => bytes[1] == 168,
                    169 => bytes[1] == 254,
                    127 => true,
                    0 => true,
                    _ => false
                };

                if (isPrivate)
                {
                    throw new HttpRequestException($"Blocked connection to private IP {ip}.");
                }
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = ip.GetAddressBytes();
                if (bytes[0] is 0xFC or 0xFD)
                {
                    throw new HttpRequestException($"Blocked connection to private IP {ip}.");
                }
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});

// GitHub Issues API client for in-app bug reports.
var gitHubToken = builder.Configuration["GitHub:Token"];
if (!string.IsNullOrWhiteSpace(gitHubToken))
{
    builder.Services.AddHttpClient<IGitHubIssueService, GitHubIssueService>(client =>
    {
        client.BaseAddress = new Uri("https://api.github.com");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gitHubToken);
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("User-Agent", "CodecBot/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
}

var app = builder.Build();

// Ensure the default "Codec HQ" server exists in every environment.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

    if (app.Environment.IsDevelopment())
    {
        db.Database.Migrate();
    }

    await SeedData.EnsureDefaultServerAsync(db);

    var globalAdminEmail = app.Configuration["GlobalAdmin:Email"];
    await SeedData.EnsureGlobalAdminAsync(db, globalAdminEmail);

    if (app.Environment.IsDevelopment())
    {
        await SeedData.InitializeAsync(db);
    }
}

app.UseResponseCompression();

app.UseCors("default");

app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.ContentType = "application/problem+json";

        var (status, title, detail) = exception switch
        {
            Codec.Api.Services.Exceptions.CodecException ce => (ce.StatusCode, ce.StatusCode switch
            {
                403 => "Forbidden",
                404 => "Not Found",
                _ => "Error"
            }, ce.Message),
            _ => (500, "Internal Server Error", "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail
        });
    });
});

// Trust forwarded headers from Azure Container Apps reverse proxy.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseSerilogRequestLogging();

app.UseRateLimiter();

// Serve uploaded files as static content only when using local storage.
if (storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    var avatarStoragePath = Path.Combine(uploadsPath, "avatars");
    Directory.CreateDirectory(avatarStoragePath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(avatarStoragePath),
        RequestPath = "/uploads/avatars"
    });

    var imageStoragePath = Path.Combine(uploadsPath, "images");
    Directory.CreateDirectory(imageStoragePath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(imageStoragePath),
        RequestPath = "/uploads/images"
    });

    var emojiStoragePath = Path.Combine(uploadsPath, "emojis");
    Directory.CreateDirectory(emojiStoragePath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(emojiStoragePath),
        RequestPath = "/uploads/emojis"
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.MapDefaultEndpoints();

app.Run();
