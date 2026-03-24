using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Data;

public static class SeedData
{
    /// <summary>
    /// Ensures the default "Codec HQ" server exists with its standard channels and default roles.
    /// Safe to call in any environment; creates the server only if it is missing.
    /// </summary>
    public static async Task EnsureDefaultServerAsync(CodecDbContext db)
    {
        var exists = await db.Servers.AnyAsync(s => s.Id == Server.DefaultServerId);
        if (exists)
        {
            // Ensure default roles exist (idempotent for existing servers)
            var hasRoles = await db.ServerRoles.AnyAsync(r => r.ServerId == Server.DefaultServerId);
            if (!hasRoles)
            {
                var userService = new UserService(db);
                await userService.CreateDefaultRolesAsync(Server.DefaultServerId);
            }
            return;
        }

        var server = new Server { Id = Server.DefaultServerId, Name = "Codec HQ" };
        var general = new Channel { Name = "general", Server = server };
        var announcements = new Channel { Name = "announcements", Server = server };

        db.Servers.Add(server);
        db.Channels.AddRange(general, announcements);
        await db.SaveChangesAsync();

        // Create default roles for the server
        var svc = new UserService(db);
        await svc.CreateDefaultRolesAsync(Server.DefaultServerId);
    }

    /// <summary>
    /// Promotes the user with the specified email address to global admin.
    /// Safe to call repeatedly; only writes when the flag is not already set.
    /// Skips silently when <paramref name="email"/> is null or empty.
    /// </summary>
    public static async Task EnsureGlobalAdminAsync(CodecDbContext db, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && !user.IsGlobalAdmin)
        {
            user.IsGlobalAdmin = true;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds development-only sample users, memberships, and messages.
    /// </summary>
    public static async Task InitializeAsync(CodecDbContext db)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var server = await db.Servers.FirstAsync(s => s.Id == Server.DefaultServerId);

        // Get the system roles
        var ownerRole = await db.ServerRoles.FirstAsync(r => r.ServerId == server.Id && r.IsSystemRole && r.Name == "Owner");
        var adminRole = await db.ServerRoles.FirstAsync(r => r.ServerId == server.Id && r.IsSystemRole && r.Name == "Admin");
        var memberRole = await db.ServerRoles.FirstAsync(r => r.ServerId == server.Id && r.IsSystemRole && r.Name == "Member");

        var avery = new User { GoogleSubject = "seed-avery", DisplayName = "Avery" };
        var morgan = new User { GoogleSubject = "seed-morgan", DisplayName = "Morgan" };
        var rae = new User { GoogleSubject = "seed-rae", DisplayName = "Rae" };

        var buildLog = new Channel { Name = "build-log", Server = server };

        var memberships = new List<ServerMember>
        {
            new() { Server = server, User = avery, RoleId = ownerRole.Id },
            new() { Server = server, User = morgan, RoleId = adminRole.Id },
            new() { Server = server, User = rae, RoleId = memberRole.Id }
        };

        var channels = await db.Channels
            .Where(c => c.ServerId == server.Id && c.Name == "announcements")
            .FirstAsync();

        var messages = new List<Message>
        {
            new() { Channel = buildLog, AuthorName = avery.DisplayName, AuthorUser = avery, Body = "Kicking off the first app shell. We are live." },
            new() { Channel = buildLog, AuthorName = morgan.DisplayName, AuthorUser = morgan, Body = "API is up, Google auth next." },
            new() { Channel = channels, AuthorName = rae.DisplayName, AuthorUser = rae, Body = "Channel layout feels good. Ready for theming." }
        };

        db.Users.AddRange(avery, morgan, rae);
        db.Channels.Add(buildLog);
        db.Messages.AddRange(messages);
        db.ServerMembers.AddRange(memberships);
        await db.SaveChangesAsync();
    }
}
