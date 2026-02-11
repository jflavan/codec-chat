using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(CodecDbContext db)
    {
        if (await db.Servers.AnyAsync())
        {
            return;
        }

        var avery = new User { GoogleSubject = "seed-avery", DisplayName = "Avery" };
        var morgan = new User { GoogleSubject = "seed-morgan", DisplayName = "Morgan" };
        var rae = new User { GoogleSubject = "seed-rae", DisplayName = "Rae" };

        var server = new Server { Name = "Codec HQ" };
        var buildLog = new Channel { Name = "build-log", Server = server };
        var announcements = new Channel { Name = "announcements", Server = server };

        var memberships = new List<ServerMember>
        {
            new() { Server = server, User = avery, Role = ServerRole.Owner },
            new() { Server = server, User = morgan, Role = ServerRole.Admin },
            new() { Server = server, User = rae, Role = ServerRole.Member }
        };

        var messages = new List<Message>
        {
            new() { Channel = buildLog, AuthorName = avery.DisplayName, AuthorUser = avery, Body = "Kicking off the first app shell. We are live." },
            new() { Channel = buildLog, AuthorName = morgan.DisplayName, AuthorUser = morgan, Body = "API is up, Google auth next." },
            new() { Channel = announcements, AuthorName = rae.DisplayName, AuthorUser = rae, Body = "Channel layout feels good. Ready for theming." }
        };

        db.Users.AddRange(avery, morgan, rae);
        db.Servers.Add(server);
        db.Channels.AddRange(buildLog, announcements);
        db.Messages.AddRange(messages);
        db.ServerMembers.AddRange(memberships);
        await db.SaveChangesAsync();
    }
}
