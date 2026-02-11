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
}
