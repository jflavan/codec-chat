using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Data;

public static class SeedData
{
    /// <summary>
    /// Ensures the default "Codec HQ" server exists with its standard channels.
    /// Safe to call in any environment; creates the server only if it is missing.
    /// </summary>
    public static async Task EnsureDefaultServerAsync(CodecDbContext db)
    {
        var exists = await db.Servers.AnyAsync(s => s.Id == Server.DefaultServerId);
        if (exists)
        {
            return;
        }

        var server = new Server { Id = Server.DefaultServerId, Name = "Codec HQ" };
        var general = new Channel { Name = "general", Server = server };
        var announcements = new Channel { Name = "announcements", Server = server };

        db.Servers.Add(server);
        db.Channels.AddRange(general, announcements);
        await db.SaveChangesAsync();
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

        var avery = new User { GoogleSubject = "seed-avery", DisplayName = "Avery" };
        var morgan = new User { GoogleSubject = "seed-morgan", DisplayName = "Morgan" };
        var rae = new User { GoogleSubject = "seed-rae", DisplayName = "Rae" };

        var buildLog = new Channel { Name = "build-log", Server = server };

        var memberships = new List<ServerMember>
        {
            new() { Server = server, User = avery, Role = ServerRole.Owner },
            new() { Server = server, User = morgan, Role = ServerRole.Admin },
            new() { Server = server, User = rae, Role = ServerRole.Member }
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
