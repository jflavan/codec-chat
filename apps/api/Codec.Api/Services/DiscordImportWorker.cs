using System.Threading.Channels;
using Codec.Api.Data;
using Microsoft.AspNetCore.DataProtection;

namespace Codec.Api.Services;

public class DiscordImportWorker : BackgroundService
{
    private readonly Channel<Guid> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly ILogger<DiscordImportWorker> _logger;
    private readonly DiscordImportCancellationRegistry _cancellationRegistry;

    public DiscordImportWorker(
        Channel<Guid> queue,
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider dataProtection,
        ILogger<DiscordImportWorker> logger,
        DiscordImportCancellationRegistry cancellationRegistry)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _dataProtection = dataProtection;
        _logger = logger;
        _cancellationRegistry = cancellationRegistry;
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

                var protector = _dataProtection.CreateProtector("DiscordBotToken");
                var botToken = protector.Unprotect(import.EncryptedBotToken);

                using var cts = _cancellationRegistry.Register(importId, stoppingToken);
                try
                {
                    await importService.RunImportAsync(importId, botToken, cts.Token);
                }
                finally
                {
                    _cancellationRegistry.Remove(importId);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Import {ImportId} was cancelled", importId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing import {ImportId}", importId);
            }
        }
    }
}
