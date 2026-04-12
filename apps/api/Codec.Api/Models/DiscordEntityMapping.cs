namespace Codec.Api.Models;

public enum DiscordEntityType
{
    Role = 0,
    Category = 1,
    Channel = 2,
    Message = 3,
    Emoji = 4,
    PinnedMessage = 5,
    PendingReply = 6
}

public class DiscordEntityMapping
{
    public Guid Id { get; set; }
    public Guid DiscordImportId { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordEntityId { get; set; } = string.Empty;
    public DiscordEntityType EntityType { get; set; }
    public Guid CodecEntityId { get; set; }

    // Navigation properties
    public DiscordImport? DiscordImport { get; set; }
    public Server? Server { get; set; }
}
