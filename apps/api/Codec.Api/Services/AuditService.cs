using Codec.Api.Data;
using Codec.Api.Models;

namespace Codec.Api.Services;

public class AuditService(CodecDbContext db)
{
    /// <summary>
    /// Stages an audit log entry in the current DbContext without saving.
    /// Callers are responsible for calling <c>db.SaveChangesAsync()</c> to persist the entry,
    /// allowing the audit write to be batched with the caller's own SaveChanges call.
    /// </summary>
    public void Log(
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
    }
}
