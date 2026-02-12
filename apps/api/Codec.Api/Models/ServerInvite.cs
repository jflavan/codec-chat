namespace Codec.Api.Models;

/// <summary>
/// Represents a shareable invite code that allows users to join a server.
/// </summary>
public class ServerInvite
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }

    /// <summary>
    /// Short alphanumeric code used to join the server (e.g. "aB3xK7mQ").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// When the invite expires. Null means it never expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Maximum number of uses. Null or 0 means unlimited.
    /// </summary>
    public int? MaxUses { get; set; }

    /// <summary>
    /// How many times this invite has been used.
    /// </summary>
    public int UseCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Server? Server { get; set; }
    public User? CreatedByUser { get; set; }
}
