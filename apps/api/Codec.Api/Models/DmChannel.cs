namespace Codec.Api.Models;

/// <summary>
/// Represents a direct message conversation between exactly two users.
/// Not attached to any server.
/// </summary>
public class DmChannel
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<DmChannelMember> Members { get; set; } = new();
    public List<DirectMessage> Messages { get; set; } = new();
}
