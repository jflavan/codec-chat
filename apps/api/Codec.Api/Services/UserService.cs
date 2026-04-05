using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Codec.Api.Services;

/// <summary>
/// Resolves and manages application users from authentication claims.
/// Handles concurrent user creation gracefully for PostgreSQL.
/// </summary>
public class UserService(CodecDbContext db, IPermissionResolverService permissionResolver) : IUserService
{
    /// <inheritdoc />
    public async Task<(User user, bool isNewUser)> GetOrCreateUserAsync(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst("iss")?.Value;

        // Local JWT — sub claim is the user's GUID; user already exists (created during registration)
        if (issuer == "codec-api")
        {
            var sub = principal.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId))
                throw new InvalidOperationException("Missing or invalid sub claim in local JWT.");

            var localUser = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                ?? throw new InvalidOperationException($"Local user {userId} not found.");
            return (localUser, false);
        }

        // Google JWT — sub claim is the Google subject
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
            // Only write to the database when profile data has actually changed.
            var hasChanges = existing.DisplayName != displayName
                          || existing.Email != email
                          || existing.AvatarUrl != avatarUrl;

            if (hasChanges)
            {
                existing.DisplayName = displayName;
                existing.Email = email;
                // Only update the Google avatar URL; preserve the custom upload path.
                existing.AvatarUrl = avatarUrl;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            return (existing, false);
        }

        var appUser = new User
        {
            GoogleSubject = subject,
            DisplayName = displayName,
            Email = email,
            AvatarUrl = avatarUrl,
            EmailVerified = true
        };

        db.Users.Add(appUser);

        // Auto-join the new user to the default "Codec HQ" server.
        var defaultMemberRole = await db.ServerRoles
            .AsNoTracking()
            .Where(r => r.ServerId == Server.DefaultServerId && r.IsSystemRole && r.Name == "Member")
            .FirstOrDefaultAsync();

        if (defaultMemberRole is not null)
        {
            var newMember = new ServerMember
            {
                ServerId = Server.DefaultServerId,
                User = appUser,
                JoinedAt = DateTimeOffset.UtcNow
            };
            db.ServerMembers.Add(newMember);
            db.ServerMemberRoles.Add(new ServerMemberRole { UserId = appUser.Id, RoleId = defaultMemberRole.Id });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another concurrent request already created this user. Detach and re-fetch.
            db.Entry(appUser).State = EntityState.Detached;
            var raceUser = await db.Users.FirstAsync(u => u.GoogleSubject == subject);
            return (raceUser, false);
        }


        return (appUser, true);
    }

    /// <inheritdoc />
    public async Task<User?> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst("iss")?.Value;

        // Local JWT — sub claim is the user's GUID
        if (issuer == "codec-api")
        {
            var sub = principal.FindFirst("sub")?.Value;
            if (sub is null || !Guid.TryParse(sub, out var userId)) return null;
            return await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        // Google JWT — sub claim is the Google subject
        var googleSubject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(googleSubject)) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSubject);
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

    /// <inheritdoc />
    public async Task<ServerMember> EnsureMemberAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false)
    {
        if (isGlobalAdmin)
        {
            var member = await db.ServerMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
            if (member is not null) return member;
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            return new ServerMember { ServerId = serverId, UserId = userId };
        }

        var membership = await db.ServerMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (membership is null)
        {
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            throw new Exceptions.ForbiddenException();
        }
        return membership;
    }

    /// <inheritdoc />
    public async Task<ServerMember> EnsurePermissionAsync(Guid serverId, Guid userId, Permission permission, bool isGlobalAdmin = false)
    {
        if (isGlobalAdmin)
        {
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            var member = await db.ServerMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
            return member ?? new ServerMember { ServerId = serverId, UserId = userId };
        }

        var membership = await db.ServerMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (membership is null)
        {
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            throw new Exceptions.ForbiddenException();
        }

        var hasPermission = await permissionResolver.HasServerPermissionAsync(serverId, userId, permission);
        if (!hasPermission) throw new Exceptions.ForbiddenException();
        return membership;
    }

    /// <inheritdoc />
    public async Task<ServerMember> EnsureAdminAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false)
    {
        // Admin-level access requires either Administrator or ManageServer permission
        return await EnsurePermissionAsync(serverId, userId, Permission.ManageServer, isGlobalAdmin);
    }

    /// <inheritdoc />
    public async Task<ServerMember> EnsureOwnerAsync(Guid serverId, Guid userId, bool isGlobalAdmin = false)
    {
        if (isGlobalAdmin)
        {
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            var member = await db.ServerMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
            return member ?? new ServerMember { ServerId = serverId, UserId = userId };
        }

        var membership = await db.ServerMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (membership is null)
        {
            var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
            if (!serverExists) throw new Exceptions.NotFoundException("Server not found.");
            throw new Exceptions.ForbiddenException();
        }

        var isOwner = await permissionResolver.IsOwnerAsync(serverId, userId);
        if (!isOwner) throw new Exceptions.ForbiddenException();
        return membership;
    }

    /// <inheritdoc />
    public async Task EnsureDmParticipantAsync(Guid dmChannelId, Guid userId)
    {
        var isMember = await db.DmChannelMembers.AsNoTracking()
            .AnyAsync(m => m.DmChannelId == dmChannelId && m.UserId == userId);

        if (!isMember)
        {
            var channelExists = await db.DmChannels.AsNoTracking().AnyAsync(c => c.Id == dmChannelId);
            if (!channelExists) throw new Exceptions.NotFoundException("DM channel not found.");
            throw new Exceptions.ForbiddenException("You are not a participant in this conversation.");
        }
    }

    /// <inheritdoc />
    public async Task<Permission> GetPermissionsAsync(Guid serverId, Guid userId)
        => await permissionResolver.ResolveServerPermissionsAsync(serverId, userId);

    /// <inheritdoc />
    public async Task<bool> IsOwnerAsync(Guid serverId, Guid userId)
        => await permissionResolver.IsOwnerAsync(serverId, userId);

    /// <inheritdoc />
    public async Task<(ServerRoleEntity owner, ServerRoleEntity admin, ServerRoleEntity member)> CreateDefaultRolesAsync(Guid serverId)
    {
        var ownerRole = new ServerRoleEntity
        {
            ServerId = serverId,
            Name = "Owner",
            Color = null,
            Position = 0,
            Permissions = Permission.Administrator,
            IsSystemRole = true,
            IsHoisted = true,
        };

        var adminRole = new ServerRoleEntity
        {
            ServerId = serverId,
            Name = "Admin",
            Color = "#f0b232",
            Position = 1,
            Permissions = PermissionExtensions.AdminDefaults,
            IsSystemRole = true,
            IsHoisted = true,
        };

        var memberRole = new ServerRoleEntity
        {
            ServerId = serverId,
            Name = "Member",
            Color = null,
            Position = 2,
            Permissions = PermissionExtensions.MemberDefaults,
            IsSystemRole = true,
            IsHoisted = false,
        };

        db.ServerRoles.AddRange(ownerRole, adminRole, memberRole);
        await db.SaveChangesAsync();

        return (ownerRole, adminRole, memberRole);
    }

    /// <inheritdoc />
    public async Task<List<(Guid ServerId, string ServerName)>> GetOwnedServersAsync(Guid userId)
    {
        // Owner = position-0 system role
        var ownedServers = await db.ServerMemberRoles
            .AsNoTracking()
            .Where(mr => mr.UserId == userId)
            .Join(db.ServerRoles.Where(r => r.IsSystemRole && r.Position == 0),
                mr => mr.RoleId, r => r.Id, (mr, r) => r.ServerId)
            .Join(db.Servers, sid => sid, s => s.Id, (sid, s) => new { s.Id, s.Name })
            .ToListAsync();

        return ownedServers.Select(s => (s.Id, s.Name)).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(Guid userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        // 1. Revoke all refresh tokens
        await db.RefreshTokens.Where(rt => rt.UserId == userId).ExecuteDeleteAsync();

        // 2. Clean up Restrict-FK entities that would block user deletion
        await db.Friendships
            .Where(f => f.RequesterId == userId || f.RecipientId == userId)
            .ExecuteDeleteAsync();

        await db.VoiceCalls
            .Where(vc => vc.CallerUserId == userId || vc.RecipientUserId == userId)
            .ExecuteDeleteAsync();

        await db.CustomEmojis
            .Where(e => e.UploadedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.UploadedByUserId, (Guid?)null));

        await db.Webhooks
            .Where(w => w.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreatedByUserId, (Guid?)null));

        await db.ServerInvites
            .Where(i => i.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedByUserId, (Guid?)null));

        await db.AdminActions
            .Where(a => a.ActorUserId == userId)
            .ExecuteDeleteAsync();

        await db.SystemAnnouncements
            .Where(a => a.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedByUserId, (Guid?)null));

        await db.Reports
            .Where(r => r.ReporterId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReporterId, (Guid?)null));

        await db.BannedMembers
            .Where(b => b.BannedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.BannedByUserId, (Guid?)null));

        // 3. Anonymize messages (set AuthorUserId to null)
        await db.Messages
            .Where(m => m.AuthorUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AuthorUserId, (Guid?)null));

        await db.DirectMessages
            .Where(dm => dm.AuthorUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(dm => dm.AuthorUserId, (Guid?)null));

        // 4. Delete reactions by this user
        await db.Reactions
            .Where(r => r.UserId == userId)
            .ExecuteDeleteAsync();

        // 5. Delete the user row (cascades: ServerMember, ServerMemberRole,
        //    DmChannelMember, PresenceState, VoiceState, PushSubscription,
        //    ChannelNotificationOverride, BannedMember)
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();

        await transaction.CommitAsync();
    }
}
