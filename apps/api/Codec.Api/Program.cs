using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using System.Net;
using System.Net.Sockets;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy =>
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
    options.UseSqlite(connectionString);
});

builder.Services.AddScoped<IUserService, UserService>();

// Avatar storage configuration.
var avatarStoragePath = Path.Combine(builder.Environment.ContentRootPath, "uploads", "avatars");
Directory.CreateDirectory(avatarStoragePath);
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]?.TrimEnd('/') ?? "";
builder.Services.AddSingleton<IAvatarService>(new AvatarService(avatarStoragePath, $"{apiBaseUrl}/uploads/avatars"));

// Chat image storage configuration.
var imageStoragePath = Path.Combine(builder.Environment.ContentRootPath, "uploads", "images");
Directory.CreateDirectory(imageStoragePath);
builder.Services.AddSingleton<IImageUploadService>(new ImageUploadService(imageStoragePath, $"{apiBaseUrl}/uploads/images"));

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

    if (app.Environment.IsDevelopment())
    {
        await SeedData.InitializeAsync(db);
    }
}

app.UseCors("dev");

// Serve uploaded avatar files as static content.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(avatarStoragePath),
    RequestPath = "/uploads/avatars"
});

// Serve uploaded chat images as static content.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageStoragePath),
    RequestPath = "/uploads/images"
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
