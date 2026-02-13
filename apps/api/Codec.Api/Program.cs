using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
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
