using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codec.Api.Tests.Services;

/// <summary>
/// Unit tests for AuditLogCleanupService.
/// Note: ExecuteDeleteAsync is a bulk SQL operation not supported by the InMemory provider.
/// The CleanupAsync method is tested here to verify it handles errors gracefully.
/// Full behavior (actual deletion) is covered by integration tests against a real database.
/// </summary>
public class AuditLogCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly AuditLogCleanupService _service;

    public AuditLogCleanupServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CodecDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _sp = services.BuildServiceProvider();

        _service = new AuditLogCleanupService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditLogCleanupService>.Instance);
    }

    [Fact]
    public async Task CleanupAsync_DoesNotThrowOnInMemoryProvider()
    {
        // ExecuteDeleteAsync isn't supported by InMemory, but CleanupAsync
        // catches non-cancellation exceptions gracefully.
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
        var serverId = Guid.NewGuid();

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Action = AuditAction.ServerRenamed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-91)
        });
        await db.SaveChangesAsync();

        // Should not throw — errors from InMemory provider are caught and logged
        await _service.Invoking(s => s.CleanupAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task CleanupAsync_DoesNotThrow()
    {
        await _service.Invoking(s => s.CleanupAsync()).Should().NotThrowAsync();
    }

    public void Dispose() => _sp.Dispose();
}
