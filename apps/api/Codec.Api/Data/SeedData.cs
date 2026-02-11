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

        var server = new Server { Name = "Codec HQ" };
        var buildLog = new Channel { Name = "build-log", Server = server };
        var announcements = new Channel { Name = "announcements", Server = server };

        var messages = new List<Message>
        {
            new() { Channel = buildLog, AuthorName = "Avery", Body = "Kicking off the first app shell. We are live." },
            new() { Channel = buildLog, AuthorName = "Morgan", Body = "API is up, Google auth next." },
            new() { Channel = announcements, AuthorName = "Rae", Body = "Channel layout feels good. Ready for theming." }
        };

        db.Servers.Add(server);
        db.Channels.AddRange(buildLog, announcements);
        db.Messages.AddRange(messages);
        await db.SaveChangesAsync();
    }
}
