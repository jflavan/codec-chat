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

    /// <summary>
    /// Relative path to a server-specific avatar uploaded by the user.
    /// When set, this overrides the user's global avatar within this server.
    /// </summary>
    public string? CustomAvatarPath { get; set; }

    public Server? Server { get; set; }
    public User? User { get; set; }
}
