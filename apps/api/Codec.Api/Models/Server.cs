namespace Codec.Api.Models;

public class Server
{
    /// <summary>
    /// Well-known identifier for the default "Codec HQ" server that every user auto-joins.
    /// </summary>
    public static readonly Guid DefaultServerId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL of the server icon image. Null when no custom icon has been uploaded.
    /// </summary>
    public string? IconUrl { get; set; }

    public string? Description { get; set; }
    public List<Channel> Channels { get; set; } = [];
    public List<ServerMember> Members { get; set; } = [];
    public List<ServerInvite> Invites { get; set; } = [];
    public List<CustomEmoji> CustomEmojis { get; set; } = [];
    public List<ChannelCategory> Categories { get; set; } = [];
    public List<AuditLogEntry> AuditLogEntries { get; set; } = [];
    public List<Webhook> Webhooks { get; set; } = [];
}
