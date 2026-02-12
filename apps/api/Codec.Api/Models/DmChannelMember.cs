namespace Codec.Api.Models;

/// <summary>
/// Join table linking a user to a DM channel.
/// Each DM channel has exactly two members.
/// </summary>
public class DmChannelMember
{
    public Guid DmChannelId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Whether this conversation appears in the user's DM sidebar.
    /// Set to <c>false</c> when the user closes the conversation.
    /// </summary>
    public bool IsOpen { get; set; } = true;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DmChannel? DmChannel { get; set; }
    public User? User { get; set; }
}
