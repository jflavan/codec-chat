using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// OpenAPI intentionally omitted for the initial skeleton.

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
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
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = true,
            ValidAudience = googleClientId,
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddDbContext<CodecDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(connectionString);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Ok(new { name = "Codec API", status = "dev" }));

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(db);
}

app.UseCors("dev");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(claim => new { claim.Type, claim.Value });
    return Results.Ok(new { name = user.Identity?.Name, claims });
})
.RequireAuthorization();

app.MapGet("/servers", async (CodecDbContext db) =>
{
    var servers = await db.Servers
        .AsNoTracking()
        .Select(server => new { server.Id, server.Name })
        .ToListAsync();

    return Results.Ok(servers);
});

app.MapGet("/servers/{serverId:guid}/channels", async (Guid serverId, CodecDbContext db) =>
{
    var channels = await db.Channels
        .AsNoTracking()
        .Where(channel => channel.ServerId == serverId)
        .Select(channel => new { channel.Id, channel.Name, channel.ServerId })
        .ToListAsync();

    return Results.Ok(channels);
});

app.MapGet("/channels/{channelId:guid}/messages", async (Guid channelId, CodecDbContext db) =>
{
    var messages = await db.Messages
        .AsNoTracking()
        .Where(message => message.ChannelId == channelId)
        .OrderBy(message => message.CreatedAt)
        .Select(message => new
        {
            message.Id,
            message.AuthorName,
            message.Body,
            message.CreatedAt,
            message.ChannelId
        })
        .ToListAsync();

    return Results.Ok(messages);
});

app.MapPost("/channels/{channelId:guid}/messages", async (
    Guid channelId,
    CreateMessageRequest request,
    ClaimsPrincipal user,
    CodecDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new { error = "Message body is required." });
    }

    var channel = await db.Channels.FindAsync(channelId);
    if (channel is null)
    {
        return Results.NotFound(new { error = "Channel not found." });
    }

    var authorName = user.FindFirst("name")?.Value ?? user.Identity?.Name ?? "Unknown";

    var message = new Message
    {
        ChannelId = channelId,
        AuthorName = authorName,
        Body = request.Body.Trim()
    };

    db.Messages.Add(message);
    await db.SaveChangesAsync();

    return Results.Created($"/channels/{channelId}/messages/{message.Id}", new
    {
        message.Id,
        message.AuthorName,
        message.Body,
        message.CreatedAt,
        message.ChannelId
    });
}).RequireAuthorization();

app.Run();
