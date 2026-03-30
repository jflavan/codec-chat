namespace Codec.Api.Models;

/// <summary>
/// Join entity linking a server member to one of their assigned roles.
/// A member can have multiple roles; permissions are OR'd across all of them.
/// </summary>
public class ServerMemberRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ServerRoleEntity? Role { get; set; }
}
