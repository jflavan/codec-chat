namespace Codec.Api.Models;

/// <summary>
/// Tracks a DM voice call from initiation through completion.
/// </summary>
public class VoiceCall
{
    public Guid Id { get; set; }
    public Guid DmChannelId { get; set; }
    public DmChannel? DmChannel { get; set; }
    public Guid CallerUserId { get; set; }
    public User? CallerUser { get; set; }
    public Guid RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }
    public VoiceCallStatus Status { get; set; }
    public VoiceCallEndReason? EndReason { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
