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
    /// </summary>
    Task<User> GetOrCreateUserAsync(ClaimsPrincipal principal);

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
    /// Returns the <see cref="ServerMember"/> for the given user in the given server,
    /// or throws <see cref="Exceptions.NotFoundException"/> / <see cref="Exceptions.ForbiddenException"/>.
    /// Global admins bypass the membership requirement.
    /// </summary>
    Task<ServerMember> EnsureMemberAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Like <see cref="EnsureMemberAsync"/> but also requires the member to hold
    /// <see cref="ServerRole.Owner"/> or <see cref="ServerRole.Admin"/>.
    /// Global admins bypass the role check.
    /// </summary>
    Task<ServerMember> EnsureAdminAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Like <see cref="EnsureMemberAsync"/> but also requires the member to hold
    /// <see cref="ServerRole.Owner"/>.
    /// Global admins bypass the role check.
    /// </summary>
    Task<ServerMember> EnsureOwnerAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false);

    /// <summary>
    /// Verifies that the user is a participant in the specified DM channel,
    /// or throws <see cref="Exceptions.NotFoundException"/> / <see cref="Exceptions.ForbiddenException"/>.
    /// </summary>
    Task EnsureDmParticipantAsync(Guid dmChannelId, Guid userId);
}
