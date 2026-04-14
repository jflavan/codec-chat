using System.Threading.Channels;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

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

        // Recover orphaned imports from a previous container lifecycle
        await RecoverOrphanedImportsAsync(stoppingToken);

        await foreach (var importId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Processing Discord import {ImportId}", importId);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
                var importService = scope.ServiceProvider.GetRequiredService<DiscordImportService>();

                var import = await db.DiscordImports.FindAsync([importId], stoppingToken);
                if (import is null)
                {
                    _logger.LogWarning("Import {ImportId} not found", importId);
                    continue;
                }

                if (import.Status == DiscordImportStatus.RehostingMedia)
                {
                    // Resumed orphaned import — skip text import, just re-host media
                    using var cts = _cancellationRegistry.Register(importId, stoppingToken);
                    try
                    {
                        await importService.ResumeMediaRehostAsync(importId, cts.Token);
                    }
                    finally
                    {
                        _cancellationRegistry.Remove(importId);
                    }
                    continue;
                }

                if (import.EncryptedBotToken is null)
                {
                    _logger.LogWarning("Import {ImportId} has no bot token", importId);
                    continue;
                }

                var protector = _dataProtection.CreateProtector("DiscordBotToken");
                var botToken = protector.Unprotect(import.EncryptedBotToken);

                using var cts2 = _cancellationRegistry.Register(importId, stoppingToken);
                try
                {
                    await importService.RunImportAsync(importId, botToken, cts2.Token);
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

    private async Task RecoverOrphanedImportsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

            var orphanedImports = await db.DiscordImports
                .Where(i => i.Status == DiscordImportStatus.InProgress || i.Status == DiscordImportStatus.RehostingMedia)
                .ToListAsync(ct);

            if (orphanedImports.Count == 0) return;

            _logger.LogInformation("Found {Count} orphaned import(s) to recover", orphanedImports.Count);

            foreach (var import in orphanedImports)
            {
                if (import.Status == DiscordImportStatus.RehostingMedia)
                {
                    _logger.LogInformation("Re-queuing orphaned RehostingMedia import {ImportId}", import.Id);
                    await _queue.Writer.WriteAsync(import.Id, ct);
                }
                else
                {
                    _logger.LogWarning("Marking orphaned InProgress import {ImportId} as Failed", import.Id);
                    import.Status = DiscordImportStatus.Failed;
                    import.ErrorMessage = "Import was interrupted by a server restart. Please re-sync to retry.";
                    import.EncryptedBotToken = null;
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover orphaned imports on startup");
        }
    }
}
