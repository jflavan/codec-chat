namespace Codec.Api.Models;

/// <summary>
/// Per-channel permission override for a specific role.
/// Allow bits grant permissions beyond the server-level role grants.
/// Deny bits revoke permissions even if granted by the role.
/// Deny is applied last and always wins.
/// </summary>
public class ChannelPermissionOverride
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid RoleId { get; set; }

    /// <summary>Permission bits explicitly granted in this channel.</summary>
    public Permission Allow { get; set; } = Permission.None;

    /// <summary>Permission bits explicitly denied in this channel. Deny always wins over Allow.</summary>
    public Permission Deny { get; set; } = Permission.None;

    // Navigation properties
    public Channel? Channel { get; set; }
    public ServerRoleEntity? Role { get; set; }
}
