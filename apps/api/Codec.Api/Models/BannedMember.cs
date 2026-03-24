namespace Codec.Api.Models;

/// <summary>
/// Records a user ban within a server. Banned users cannot rejoin via invite.
/// </summary>
public class BannedMember
{
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset BannedAt { get; set; } = DateTimeOffset.UtcNow;

    public Server? Server { get; set; }
    public User? User { get; set; }
    public User? BannedByUser { get; set; }
}
