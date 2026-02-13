using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

/// <summary>
/// Resolves and manages application users from authentication claims.
/// Handles concurrent user creation gracefully for SQLite.
/// </summary>
public class UserService(CodecDbContext db) : IUserService
{
    /// <inheritdoc />
    public async Task<User> GetOrCreateUserAsync(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("Missing Google subject claim.");
        }

        var displayName = principal.FindFirst("name")?.Value ?? principal.Identity?.Name ?? "Unknown";
        var email = principal.FindFirst("email")?.Value;
        var avatarUrl = principal.FindFirst("picture")?.Value;

        var existing = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject);

        if (existing is not null)
        {
            existing.DisplayName = displayName;
            existing.Email = email;
            // Only update the Google avatar URL; preserve the custom upload path.
            existing.AvatarUrl = avatarUrl;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return existing;
        }

        var appUser = new User
        {
            GoogleSubject = subject,
            DisplayName = displayName,
            Email = email,
            AvatarUrl = avatarUrl
        };

        db.Users.Add(appUser);

        // Auto-join the new user to the default "Codec HQ" server.
        var defaultServerExists = await db.Servers
            .AsNoTracking()
            .AnyAsync(s => s.Id == Server.DefaultServerId);

        if (defaultServerExists)
        {
            db.ServerMembers.Add(new ServerMember
            {
                ServerId = Server.DefaultServerId,
                User = appUser,
                Role = ServerRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Another concurrent request already created this user. Detach and re-fetch.
            db.Entry(appUser).State = EntityState.Detached;
            return await db.Users.FirstAsync(u => u.GoogleSubject == subject);
        }

        return appUser;
    }

    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(Guid serverId, Guid userId)
    {
        return await db.ServerMembers
            .AsNoTracking()
            .AnyAsync(member => member.ServerId == serverId && member.UserId == userId);
    }

    /// <inheritdoc />
    public string GetEffectiveDisplayName(User user)
    {
        return string.IsNullOrWhiteSpace(user.Nickname) ? user.DisplayName : user.Nickname;
    }
}
