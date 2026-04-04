using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class AdminMetricsService(
    IHubContext<AdminHub> hub,
    MetricsCounterService metrics,
    PresenceTracker presence,
    IServiceScopeFactory scopeFactory,
    ILogger<AdminMetricsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            try
            {
                var recentMessages = metrics.ReadAndReset();
                var messagesPerMinute = recentMessages * 12;
                metrics.SetMessagesPerMinute(messagesPerMinute);

                int openReports = 0;
                using (var scope = scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
                    openReports = await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, stoppingToken);
                }

                await hub.Clients.All.SendAsync("StatsUpdated", new
                {
                    activeUsers = presence.GetOnlineUserCount(),
                    activeConnections = presence.GetConnectionCount(),
                    messagesPerMinute,
                    openReports
                }, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error broadcasting admin metrics");
            }
        }
    }
}
