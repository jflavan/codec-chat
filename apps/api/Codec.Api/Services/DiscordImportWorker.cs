using System.Threading.Channels;
using Codec.Api.Data;

namespace Codec.Api.Services;

public class DiscordImportWorker : BackgroundService
{
    private readonly Channel<Guid> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscordImportWorker> _logger;

    public DiscordImportWorker(
        Channel<Guid> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordImportWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Discord import worker started");

        await foreach (var importId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Processing Discord import {ImportId}", importId);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
                var importService = scope.ServiceProvider.GetRequiredService<DiscordImportService>();

                var import = await db.DiscordImports.FindAsync([importId], stoppingToken);
                if (import?.EncryptedBotToken is null)
                {
                    _logger.LogWarning("Import {ImportId} not found or has no bot token", importId);
                    continue;
                }

                await importService.RunImportAsync(importId, import.EncryptedBotToken, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing import {ImportId}", importId);
            }
        }
    }
}
