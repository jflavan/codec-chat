using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class PresenceBackgroundService(
    PresenceTracker tracker,
    IHubContext<ChatHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<PresenceBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OfflineTimeout = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Purge stale presence rows from previous server run
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
            await db.PresenceStates.ExecuteDeleteAsync(stoppingToken);
            logger.LogInformation("Purged stale presence rows on startup");
        }

        using var timer = new PeriodicTimer(ScanInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var changes = tracker.ScanForTimeouts(IdleTimeout, OfflineTimeout);
                if (changes.Count == 0) continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

                foreach (var (userId, previous, current, staleConnectionIds) in changes)
                {
                    // Remove stale connection rows from DB
                    if (staleConnectionIds.Count > 0)
                    {
                        await db.PresenceStates
                            .Where(ps => staleConnectionIds.Contains(ps.ConnectionId))
                            .ExecuteDeleteAsync(stoppingToken);
                    }

                    // Update remaining rows to new status
                    if (current != PresenceStatus.Offline)
                    {
                        await db.PresenceStates
                            .Where(ps => ps.UserId == userId)
                            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.Status, current), stoppingToken);
                    }
                    else
                    {
                        // All connections gone — delete all rows
                        await db.PresenceStates
                            .Where(ps => ps.UserId == userId)
                            .ExecuteDeleteAsync(stoppingToken);
                    }

                    // Broadcast status change
                    await BroadcastPresenceChange(db, userId, current, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in presence scan");
            }
        }
    }

    private async Task BroadcastPresenceChange(CodecDbContext db, Guid userId, PresenceStatus status, CancellationToken ct)
    {
        var payload = new { userId = userId.ToString(), status = status.ToString().ToLowerInvariant() };

        // Broadcast to all servers this user is a member of
        var serverIds = await db.ServerMembers
            .Where(sm => sm.UserId == userId)
            .Select(sm => sm.ServerId)
            .ToListAsync(ct);

        var tasks = serverIds.Select(serverId =>
            hubContext.Clients.Group($"server-{serverId}").SendAsync("UserPresenceChanged", payload, ct));

        // Also broadcast to DM contacts via user-{friendUserId} groups
        var friendUserIds = await db.Friendships
            .Where(f => f.RequesterId == userId || f.RecipientId == userId)
            .Select(f => f.RequesterId == userId ? f.RecipientId : f.RequesterId)
            .Distinct()
            .ToListAsync(ct);

        var dmTasks = friendUserIds.Select(friendId =>
            hubContext.Clients.Group($"user-{friendId}").SendAsync("UserPresenceChanged", payload, ct));

        await Task.WhenAll(tasks.Concat(dmTasks));
    }
}
