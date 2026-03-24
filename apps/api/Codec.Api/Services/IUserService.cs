using System.Security.Claims;
using Codec.Api.Models;

namespace Codec.Api.Services;

/// <summary>
/// Resolves and manages application users from authentication claims.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Resolves an application user from the authenticated claims principal,
    /// creating a new user record on first sign-in.
    /// Returns the user and a flag indicating whether the user was newly created.
    /// </summary>
    Task<(User user, bool isNewUser)> GetOrCreateUserAsync(ClaimsPrincipal principal);

    /// <summary>
    /// Resolves a user from authentication claims. For Google tokens, matches on GoogleSubject.
    /// For local tokens, matches on user ID (sub claim).
    /// </summary>
    Task<User?> ResolveUserAsync(ClaimsPrincipal principal);

    /// <summary>
    /// Checks whether a user is a member of the specified server.
    /// </summary>
    Task<bool> IsMemberAsync(Guid serverId, Guid userId);

    /// <summary>
    /// Returns the effective display name for a user: nickname if set,
    /// otherwise the Google-provided display name.
    /// </summary>
    string GetEffectiveDisplayName(User user);

    /// <summary>
    /// Returns the <see cref="ServerMember"/> (with Role loaded) for the given user in the given server,
    /// or throws <see cref="Exceptions.NotFoundException"/> / <see cref="Exceptions.ForbiddenException"/>.
    /// Global admins bypass the membership requirement.
    /// </summary>
    Task<ServerMember> EnsureMemberAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Ensures the member has the specified <see cref="Permission"/> in the server.
    /// Global admins bypass all permission checks.
    /// Throws <see cref="Exceptions.ForbiddenException"/> if the member lacks the permission.
    /// </summary>
    Task<ServerMember> EnsurePermissionAsync(Guid serverId, Guid userId, Permission permission, bool isGlobalAdmin = false);

    /// <summary>
    /// Like <see cref="EnsureMemberAsync"/> but also requires the member to hold
    /// <see cref="Permission.Administrator"/> or be in a role with admin-equivalent permissions.
    /// Global admins bypass the role check.
    /// </summary>
    Task<ServerMember> EnsureAdminAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Ensures the member is the server owner (position-0 system role).
    /// Global admins bypass the role check.
    /// </summary>
    Task<ServerMember> EnsureOwnerAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Verifies that the user is a participant in the specified DM channel,
    /// or throws <see cref="Exceptions.NotFoundException"/> / <see cref="Exceptions.ForbiddenException"/>.
    /// </summary>
    Task EnsureDmParticipantAsync(Guid dmChannelId, Guid userId);

    /// <summary>
    /// Returns the computed permissions for a member based on their assigned role.
    /// </summary>
    Task<Permission> GetPermissionsAsync(Guid serverId, Guid userId);

    /// <summary>
    /// Returns true if the member's role is the Owner system role (position 0).
    /// </summary>
    Task<bool> IsOwnerAsync(Guid serverId, Guid userId);

    /// <summary>
    /// Creates the default system roles (Owner, Admin, Member) for a new server.
    /// Returns the three roles.
    /// </summary>
    Task<(ServerRoleEntity owner, ServerRoleEntity admin, ServerRoleEntity member)> CreateDefaultRolesAsync(Guid serverId);
}
