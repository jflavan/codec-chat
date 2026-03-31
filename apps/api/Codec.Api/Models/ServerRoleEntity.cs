namespace Codec.Api.Models;

/// <summary>
/// A custom or system-generated role within a server.
/// Each role carries a bitmask of <see cref="Permission"/> flags and a position
/// in the role hierarchy (lower position = higher rank).
/// </summary>
public class ServerRoleEntity
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }

    /// <summary>Display name of the role (e.g. "Moderator", "Admin").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color for the role badge (e.g. "#f0b232"). Null uses default text color.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Position in the role hierarchy. Lower values = higher rank.
    /// The Owner role is always position 0. The @everyone role is always last.
    /// </summary>
    public int Position { get; set; }

    /// <summary>Bitmask of granted permissions.</summary>
    public Permission Permissions { get; set; }

    /// <summary>
    /// Whether this is a system-generated role (Owner, Admin, Member/@everyone).
    /// System roles cannot be deleted or renamed.
    /// </summary>
    public bool IsSystemRole { get; set; }

    /// <summary>Whether to display role members separately in the member sidebar.</summary>
    public bool IsHoisted { get; set; }

    /// <summary>Whether the role can be @mentioned by anyone.</summary>
    public bool IsMentionable { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Server? Server { get; set; }
    public List<ServerMemberRole> MemberRoles { get; set; } = [];
}
