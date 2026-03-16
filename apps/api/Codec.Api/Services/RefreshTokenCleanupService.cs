using Codec.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

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
            var now = DateTimeOffset.UtcNow;
            var revocationCutoff = now.AddHours(-24);

            var deleted = await db.RefreshTokens
                .Where(rt => rt.ExpiresAt < now || (rt.RevokedAt != null && rt.RevokedAt < revocationCutoff))
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
                logger.LogInformation("Purged {Count} expired/revoked refresh tokens", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during refresh token cleanup");
        }
    }
}
