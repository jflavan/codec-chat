namespace Codec.Api.Models;

/// <summary>
/// Represents a friend request or confirmed friendship between two users.
/// Only one record may exist per user pair regardless of direction.
/// </summary>
public class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid RecipientId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public User? Requester { get; set; }
    public User? Recipient { get; set; }
}
