namespace Codec.Api.Models;

/// <summary>
/// Defines a user's membership within a server.
/// </summary>
public class ServerMember
{
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public ServerRole Role { get; set; } = ServerRole.Member;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public Server? Server { get; set; }
    public User? User { get; set; }
}
