namespace Codec.Api.Models;

public class DiscordUserMapping
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string DiscordUserId { get; set; } = string.Empty;
    public string DiscordUsername { get; set; } = string.Empty;
    public string? DiscordAvatarUrl { get; set; }
    public Guid? CodecUserId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public User? CodecUser { get; set; }
}
