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

    public bool IsMuted { get; set; }
    public bool IsDeafened { get; set; }

    /// <summary>
    /// SignalR connection ID used to route signaling messages and to clean up
    /// stale records when the connection drops.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// mediasoup participant ID (equals ConnectionId) used by the SFU to
    /// identify this participant's transports and producers.
    /// </summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// mediasoup producer ID set after the participant calls Produce.
    /// Null until the participant starts sending audio.
    /// Returned to late joiners so they can immediately consume this participant's audio.
    /// </summary>
    public string? ProducerId { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
