using Codec.Api.Data;
using Codec.Api.Models;

namespace Codec.Api.Services;

public class AuditService(CodecDbContext db)
{
    public async Task LogAsync(
        Guid serverId,
        Guid actorUserId,
        AuditAction action,
        string? targetType = null,
        string? targetId = null,
        string? details = null)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
