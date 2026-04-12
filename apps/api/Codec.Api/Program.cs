using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Azure.Identity;
using Azure.Storage.Blobs;
using Serilog;
using Serilog.Formatting.Compact;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using StackExchange.Redis;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
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

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "Codec Chat API",
            Version = "v1",
            Description = "REST API for Codec, a Discord-like chat application. Supports servers, channels, messaging, voice, friends, and direct messages."
        };
        return Task.CompletedTask;
    });
});

// Redis distributed cache + direct connection for tracking set operations.
// Two connections are intentional: AddStackExchangeRedisCache manages its own internal
// ConnectionMultiplexer, while the IConnectionMultiplexer singleton is needed for Redis SET
// operations (tracking keys for bulk invalidation) that IDistributedCache does not support.
// Prefer the standard Aspire-injected connection string (includes ssl=true for TLS containers),
// falling back to the legacy Redis:ConnectionString key for non-Aspire environments.
var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? builder.Configuration["Redis:ConnectionString"];
ConfigurationOptions? redisOptions = null;
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    redisOptions = ConfigurationOptions.Parse(redisConnectionString);

    // In development, Aspire's Redis container uses a self-signed TLS certificate.
    // Accept it so the connection succeeds locally.
    if (builder.Environment.IsDevelopment())
    {
        redisOptions.CertificateValidation += (_, _, _, _) => true;
    }

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConfigurationOptions = redisOptions;
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(redisOptions));
}

builder.Services.AddSingleton<MessageCacheService>();
builder.Services.AddSingleton<MetricsCounterService>();

builder.Services.AddSingleton<Codec.Api.Filters.HubRateLimitFilter>();
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.AddFilter<Codec.Api.Filters.HubRateLimitFilter>();
    // Tolerate inactive browser tabs that throttle WebSocket pings.
    // KeepAliveInterval sends pings every 30 s (default: 15 s).
    // ClientTimeoutInterval allows 90 s of silence before disconnecting
    // (default: 30 s), giving throttled tabs time to respond.
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(90);
})
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

if (redisOptions is not null)
{
    signalRBuilder.AddStackExchangeRedis(options =>
    {
        options.Configuration = redisOptions;
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

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "";
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment())
        jwtSecret = "dev-only-jwt-secret-that-is-at-least-32-chars-long!!";
    else
        throw new InvalidOperationException("Jwt:Secret must be at least 32 characters in non-development environments.");
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "codec-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "codec-api";

builder.Services.AddAuthentication("Selector")
    .AddPolicyScheme("Selector", "Google or Local", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? authHeader["Bearer ".Length..]
                : context.Request.Query["access_token"].FirstOrDefault();

            if (!string.IsNullOrEmpty(token))
            {
                // Peek at the issuer claim without validating
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    var issuer = jwt.Issuer;
                    if (issuer == "codec-api")
                        return "Local";
                }
                catch
                {
                    // If we can't read it, fall through to Google
                }
            }
            return "Google";
        };
    })
    .AddJwtBearer("Google", options =>
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

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/chat") || path.StartsWithSegments("/hubs/admin")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer("Local", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSecret))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/chat") || path.StartsWithSegments("/hubs/admin")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new Codec.Api.Filters.ActiveUserRequirement())
        .Build();
    options.AddPolicy("GlobalAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new Codec.Api.Filters.GlobalAdminRequirement());
    });
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Codec.Api.Filters.ActiveUserHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Codec.Api.Filters.GlobalAdminHandler>();
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPermissionResolverService, PermissionResolverService>();
builder.Services.AddScoped<TokenService>();
builder.Services.Configure<Codec.Api.Models.RecaptchaSettings>(builder.Configuration.GetSection("Recaptcha"));
builder.Services.AddHttpClient<RecaptchaService>();

if (!builder.Environment.IsDevelopment() && !string.IsNullOrEmpty(builder.Configuration["Email:ConnectionString"]))
    builder.Services.AddSingleton<IEmailSender, AzureEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();

builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<OAuthProviderService>();

builder.Services.Configure<Codec.Api.Models.SamlSettings>(builder.Configuration.GetSection("Saml"));
builder.Services.AddScoped<SamlService>();
builder.Services.AddMemoryCache();

// Validate LiveKit configuration at startup in non-development environments.
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["LiveKit:ApiKey"]))
        throw new InvalidOperationException("LiveKit:ApiKey must be configured in production.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["LiveKit:ApiSecret"]))
        throw new InvalidOperationException("LiveKit:ApiSecret must be configured in production.");
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<CodecDbContext>("database", tags: ["ready"]);

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    healthChecks.AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis", tags: ["ready"],
        failureStatus: HealthStatus.Degraded);
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var fixedLimit = builder.Configuration.GetValue("RateLimit:Fixed", 100);
    var authLimit = builder.Configuration.GetValue("RateLimit:Auth", 10);
    options.AddFixedWindowLimiter("fixed", limiter =>
    {
        limiter.PermitLimit = fixedLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = authLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("admin-writes", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("reports", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromHours(1);
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

builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AdminActionService>();
builder.Services.AddHostedService<AuditLogCleanupService>();

builder.Services.AddSingleton<VoiceCallTimeoutService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceCallTimeoutService>());

builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddHostedService<PresenceBackgroundService>();
builder.Services.AddHostedService<AdminMetricsService>();

// Discord import
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>(
    new UnboundedChannelOptions { SingleReader = true }));
builder.Services.AddSingleton<DiscordImportCancellationRegistry>();
builder.Services.AddScoped<DiscordImportService>();
builder.Services.AddHostedService<DiscordImportWorker>();
builder.Services.AddHttpClient<DiscordApiClient>()
    .AddHttpMessageHandler<DiscordRateLimitHandler>();
builder.Services.AddTransient<DiscordRateLimitHandler>();
builder.Services.AddHttpClient<DiscordMediaRehostService>()
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
            foreach (var entry in entries)
            {
                // Normalize IPv4-mapped IPv6 addresses (e.g. ::ffff:10.0.0.1) to IPv4
                var ip = entry.IsIPv4MappedToIPv6 ? entry.MapToIPv4() : entry;

                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                    throw new HttpRequestException($"Blocked rehost connection to private IP {ip}.");

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
                        throw new HttpRequestException($"Blocked rehost connection to private IP {ip}.");
                }
                else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    var bytes = ip.GetAddressBytes();
                    if (bytes[0] is 0xFC or 0xFD)
                        throw new HttpRequestException($"Blocked rehost connection to private IP {ip}.");
                }
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(entries[0], context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    });

// Web Push notification service (VAPID-authenticated).
var vapidPublicKey = builder.Configuration["Vapid:PublicKey"];
var vapidPrivateKey = builder.Configuration["Vapid:PrivateKey"];
var vapidSubject = builder.Configuration["Vapid:Subject"] ?? "mailto:noreply@codec.chat";
if (!string.IsNullOrWhiteSpace(vapidPublicKey) && !string.IsNullOrWhiteSpace(vapidPrivateKey))
{
    builder.Services.AddSingleton(new PushServiceClient
    {
        DefaultAuthentication = new VapidAuthentication(vapidPublicKey, vapidPrivateKey)
        {
            Subject = vapidSubject
        }
    });
    builder.Services.AddSingleton<IPushClient, PushClientAdapter>();
    builder.Services.AddSingleton<PushNotificationService>();
}

builder.Services.AddSingleton<IAvatarService, AvatarService>();
builder.Services.AddSingleton<IImageUploadService, ImageUploadService>();
builder.Services.AddSingleton<IFileUploadService, FileUploadService>();
builder.Services.AddScoped<ICustomEmojiService, CustomEmojiService>();

builder.Services.AddSingleton<WebhookService>();
builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "CodecWebhook/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectCallback = async (context, cancellationToken) =>
    {
        // DNS rebinding protection: resolve and validate before connecting.
        var entries = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        foreach (var entry in entries)
        {
            var ip = entry.IsIPv4MappedToIPv6 ? entry.MapToIPv4() : entry;

            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                throw new HttpRequestException($"Blocked webhook connection to private IP {ip}.");

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
                    throw new HttpRequestException($"Blocked webhook connection to private IP {ip}.");
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = ip.GetAddressBytes();
                if (bytes[0] is 0xFC or 0xFD)
                    throw new HttpRequestException($"Blocked webhook connection to private IP {ip}.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(entries[0], context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});

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
        foreach (var entry in entries)
        {
            var ip = entry.IsIPv4MappedToIPv6 ? entry.MapToIPv4() : entry;

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
            await socket.ConnectAsync(new IPEndPoint(entries[0], context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});

// Image proxy HttpClient with DNS rebinding protection, redirect limits, and no cookies.
builder.Services.AddHttpClient<IImageProxyService, ImageProxyService>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "CodecBot/1.0 (+https://codec.chat)");
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 3,
    UseCookies = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectCallback = async (context, cancellationToken) =>
    {
        var entries = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        foreach (var entry in entries)
        {
            var ip = entry.IsIPv4MappedToIPv6 ? entry.MapToIPv4() : entry;

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
            await socket.ConnectAsync(new IPEndPoint(entries[0], context.DnsEndPoint.Port), cancellationToken);
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
app.MapHub<AdminHub>("/hubs/admin");

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

app.Run();
