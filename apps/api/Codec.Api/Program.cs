using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.Sqlite;
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
        options.MapInboundClaims = false;
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

app.MapGet("/servers", async (ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var servers = await db.ServerMembers
        .AsNoTracking()
        .Where(member => member.UserId == appUser.Id)
        .Select(member => new
        {
            member.ServerId,
            Name = member.Server!.Name,
            Role = member.Role.ToString()
        })
        .ToListAsync();

    return Results.Ok(servers);
}).RequireAuthorization();

app.MapGet("/servers/discover", async (ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var servers = await db.Servers
        .AsNoTracking()
        .Select(server => new
        {
            server.Id,
            server.Name,
            IsMember = db.ServerMembers.Any(member => member.ServerId == server.Id && member.UserId == appUser.Id)
        })
        .ToListAsync();

    return Results.Ok(servers);
}).RequireAuthorization();

app.MapPost("/servers/{serverId:guid}/join", async (Guid serverId, ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var serverExists = await db.Servers.AsNoTracking().AnyAsync(server => server.Id == serverId);
    if (!serverExists)
    {
        return Results.NotFound(new { error = "Server not found." });
    }

    var existing = await db.ServerMembers.FindAsync(serverId, appUser.Id);
    if (existing is not null)
    {
        return Results.Ok(new { serverId, userId = appUser.Id, role = existing.Role.ToString() });
    }

    var membership = new ServerMember
    {
        ServerId = serverId,
        UserId = appUser.Id,
        Role = ServerRole.Member,
        JoinedAt = DateTimeOffset.UtcNow
    };

    db.ServerMembers.Add(membership);
    await db.SaveChangesAsync();

    return Results.Created($"/servers/{serverId}/members/{appUser.Id}", new
    {
        serverId,
        userId = appUser.Id,
        role = membership.Role.ToString()
    });
}).RequireAuthorization();

app.MapPost("/servers", async (
    CreateServerRequest request,
    ClaimsPrincipal user,
    CodecDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Server name is required." });
    }

    if (request.Name.Trim().Length > 100)
    {
        return Results.BadRequest(new { error = "Server name must be 100 characters or fewer." });
    }

    var appUser = await GetOrCreateUserAsync(user, db);

    var server = new Server { Name = request.Name.Trim() };
    db.Servers.Add(server);

    var defaultChannel = new Channel { Name = "general", Server = server };
    db.Channels.Add(defaultChannel);

    var membership = new ServerMember
    {
        Server = server,
        UserId = appUser.Id,
        Role = ServerRole.Owner,
        JoinedAt = DateTimeOffset.UtcNow
    };
    db.ServerMembers.Add(membership);

    await db.SaveChangesAsync();

    return Results.Created($"/servers/{server.Id}", new
    {
        server.Id,
        server.Name,
        role = membership.Role.ToString()
    });
}).RequireAuthorization();

app.MapPost("/servers/{serverId:guid}/channels", async (
    Guid serverId,
    CreateChannelRequest request,
    ClaimsPrincipal user,
    CodecDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Channel name is required." });
    }

    if (request.Name.Trim().Length > 100)
    {
        return Results.BadRequest(new { error = "Channel name must be 100 characters or fewer." });
    }

    var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
    if (!serverExists)
    {
        return Results.NotFound(new { error = "Server not found." });
    }

    var appUser = await GetOrCreateUserAsync(user, db);
    var membership = await db.ServerMembers
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

    if (membership is null)
    {
        return Results.Forbid();
    }

    if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
    {
        return Results.Forbid();
    }

    var channel = new Channel
    {
        ServerId = serverId,
        Name = request.Name.Trim()
    };

    db.Channels.Add(channel);
    await db.SaveChangesAsync();

    return Results.Created($"/servers/{serverId}/channels/{channel.Id}", new
    {
        channel.Id,
        channel.Name,
        channel.ServerId
    });
}).RequireAuthorization();

app.MapGet("/servers/{serverId:guid}/members", async (Guid serverId, ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var isMember = await IsMemberAsync(serverId, appUser.Id, db);
    if (!isMember)
    {
        return Results.Forbid();
    }

    var members = await db.ServerMembers
        .AsNoTracking()
        .Where(member => member.ServerId == serverId)
        .Select(member => new
        {
            member.UserId,
            Role = member.Role.ToString(),
            member.JoinedAt,
            member.User!.DisplayName,
            member.User.Email,
            member.User.AvatarUrl
        })
        .OrderBy(member => member.DisplayName)
        .ToListAsync();

    return Results.Ok(members);
}).RequireAuthorization();

app.MapGet("/servers/{serverId:guid}/channels", async (Guid serverId, ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var isMember = await IsMemberAsync(serverId, appUser.Id, db);
    if (!isMember)
    {
        return Results.Forbid();
    }

    var channels = await db.Channels
        .AsNoTracking()
        .Where(channel => channel.ServerId == serverId)
        .Select(channel => new { channel.Id, channel.Name, channel.ServerId })
        .ToListAsync();

    return Results.Ok(channels);
}).RequireAuthorization();

app.MapGet("/channels/{channelId:guid}/messages", async (Guid channelId, ClaimsPrincipal user, CodecDbContext db) =>
{
    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
    if (channel is null)
    {
        return Results.NotFound(new { error = "Channel not found." });
    }

    var appUser = await GetOrCreateUserAsync(user, db);
    var isMember = await IsMemberAsync(channel.ServerId, appUser.Id, db);
    if (!isMember)
    {
        return Results.Forbid();
    }

    var messages = await db.Messages
        .AsNoTracking()
        .Where(message => message.ChannelId == channelId)
        .OrderBy(message => message.CreatedAt)
        .Select(message => new
        {
            message.Id,
            message.AuthorName,
            message.AuthorUserId,
            message.Body,
            message.CreatedAt,
            message.ChannelId
        })
        .ToListAsync();

    return Results.Ok(messages);
}).RequireAuthorization();

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

    var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
    if (channel is null)
    {
        return Results.NotFound(new { error = "Channel not found." });
    }

    var appUser = await GetOrCreateUserAsync(user, db);
    var isMember = await IsMemberAsync(channel.ServerId, appUser.Id, db);
    if (!isMember)
    {
        return Results.Forbid();
    }

    var authorName = string.IsNullOrWhiteSpace(appUser.DisplayName)
        ? "Unknown"
        : appUser.DisplayName;

    var message = new Message
    {
        ChannelId = channelId,
        AuthorUserId = appUser.Id,
        AuthorName = authorName,
        Body = request.Body.Trim()
    };

    db.Messages.Add(message);
    await db.SaveChangesAsync();

    return Results.Created($"/channels/{channelId}/messages/{message.Id}", new
    {
        message.Id,
        message.AuthorName,
        message.AuthorUserId,
        message.Body,
        message.CreatedAt,
        message.ChannelId
    });
}).RequireAuthorization();

app.MapGet("/me", async (ClaimsPrincipal user, CodecDbContext db) =>
{
    var appUser = await GetOrCreateUserAsync(user, db);
    var claims = user.Claims.Select(claim => new { claim.Type, claim.Value });
    return Results.Ok(new
    {
        user = new
        {
            appUser.Id,
            appUser.DisplayName,
            appUser.Email,
            appUser.AvatarUrl,
            appUser.GoogleSubject
        },
        claims
    });
}).RequireAuthorization();

// Resolves the application user from claims, creating one if missing.
// Handles concurrent requests for the same user by catching UNIQUE constraint
// violations and retrying the lookup.
static async Task<User> GetOrCreateUserAsync(ClaimsPrincipal user, CodecDbContext db)
{
    var subject = user.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(subject))
    {
        throw new InvalidOperationException("Missing Google subject claim.");
    }

    var displayName = user.FindFirst("name")?.Value ?? user.Identity?.Name ?? "Unknown";
    var email = user.FindFirst("email")?.Value;
    var avatarUrl = user.FindFirst("picture")?.Value;

    var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject);

    if (existing is not null)
    {
        existing.DisplayName = displayName;
        existing.Email = email;
        existing.AvatarUrl = avatarUrl;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    var appUser = new User
    {
        GoogleSubject = subject,
        DisplayName = displayName,
        Email = email,
        AvatarUrl = avatarUrl
    };

    db.Users.Add(appUser);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    {
        // Another concurrent request already created this user. Detach and re-fetch.
        db.Entry(appUser).State = EntityState.Detached;
        return await db.Users.FirstAsync(u => u.GoogleSubject == subject);
    }

    return appUser;
}

// Checks membership for a user within a server.
static async Task<bool> IsMemberAsync(Guid serverId, Guid userId, CodecDbContext db)
{
    return await db.ServerMembers
        .AsNoTracking()
        .AnyAsync(member => member.ServerId == serverId && member.UserId == userId);
}

app.Run();
