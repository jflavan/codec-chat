namespace Codec.Api.Models;

public class AdminAction
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public User? ActorUser { get; set; }
    public AdminActionType ActionType { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
