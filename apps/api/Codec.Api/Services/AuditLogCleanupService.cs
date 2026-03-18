using Codec.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class AuditLogCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditLogCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            await CleanupAsync(stoppingToken);
        }
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

            var deleted = await db.AuditLogEntries
                .Where(e => e.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
                logger.LogInformation("Purged {Count} audit log entries older than 90 days", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during audit log cleanup");
        }
    }
}
