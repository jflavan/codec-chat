namespace Codec.Api.Models;

public class PresenceState
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public PresenceStatus Status { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public DateTimeOffset LastHeartbeatAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
}
