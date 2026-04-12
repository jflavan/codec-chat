namespace Codec.Api.Models;

public enum DiscordImportStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    RehostingMedia = 5
}

public class DiscordImport
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordGuildId { get; set; } = string.Empty;
    public string? EncryptedBotToken { get; set; }
    public DiscordImportStatus Status { get; set; } = DiscordImportStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int ImportedChannels { get; set; }
    public int ImportedMessages { get; set; }
    public int ImportedMembers { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public Guid InitiatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public User? InitiatedByUser { get; set; }
}
