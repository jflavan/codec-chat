using Codec.Api.Data;
using Codec.Api.Models;

namespace Codec.Api.Services;

public class AdminActionService(CodecDbContext db)
{
    public async Task<AdminAction> LogAsync(
        Guid actorUserId,
        AdminActionType actionType,
        string targetType,
        string targetId,
        string? reason = null,
        string? details = null)
    {
        var action = new AdminAction
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            Reason = reason,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AdminActions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }
}
