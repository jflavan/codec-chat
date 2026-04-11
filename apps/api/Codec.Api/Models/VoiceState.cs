namespace Codec.Api.Models;

/// <summary>
/// Tracks a user's active voice session in a voice channel.
/// Rows are created on join and deleted on disconnect or explicit leave.
/// </summary>
public class VoiceState
{
    public Guid Id { get; set; }

    /// <summary>The connected user.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The voice channel the user is in. Null for direct calls.</summary>
    public Guid? ChannelId { get; set; }
    public Channel? Channel { get; set; }

    /// <summary>The DM channel for a direct voice call. Null for server voice channels.</summary>
    public Guid? DmChannelId { get; set; }
    public DmChannel? DmChannel { get; set; }

    public bool IsMuted { get; set; }
    public bool IsDeafened { get; set; }

    /// <summary>
    /// SignalR connection ID used to route signaling messages and to clean up
    /// stale records when the connection drops.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    public bool IsVideoEnabled { get; set; }
    public bool IsScreenSharing { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
