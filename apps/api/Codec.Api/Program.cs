using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Azure.Identity;
using Azure.Storage.Blobs;
using Serilog;
using Serilog.Formatting.Compact;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddControllers();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

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

builder.Services.AddSingleton<IAvatarService, AvatarService>();
builder.Services.AddSingleton<IImageUploadService, ImageUploadService>();

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
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Liveness probe: always 200 — proves the process is running.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});

// Readiness probe: includes DB connectivity check.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var result = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    };

    return context.Response.WriteAsJsonAsync(result);
}

app.Run();
