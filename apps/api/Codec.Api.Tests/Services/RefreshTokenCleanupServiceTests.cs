using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codec.Api.Tests.Services;

/// <summary>
/// Unit tests for RefreshTokenCleanupService.
/// Note: ExecuteDeleteAsync is a bulk SQL operation not supported by the InMemory provider.
/// The CleanupAsync method is tested here to verify it handles errors gracefully.
/// Full behavior (actual deletion) is covered by integration tests against a real database.
/// </summary>
public class RefreshTokenCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly CodecDbContext _db;
    private readonly RefreshTokenCleanupService _service;
    private readonly User _testUser;

    public RefreshTokenCleanupServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CodecDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<CodecDbContext>();

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "CleanupUser",
            Email = "cleanup@test.com"
        };
        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _service = new RefreshTokenCleanupService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RefreshTokenCleanupService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task CleanupAsync_DoesNotThrowOnInMemoryProvider()
    {
        // ExecuteDeleteAsync isn't supported by InMemory, but CleanupAsync
        // catches non-cancellation exceptions gracefully.
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = _testUser.Id,
            TokenHash = "expired-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8)
        });
        await _db.SaveChangesAsync();

        // Should not throw — errors are caught and logged
        await _service.Invoking(s => s.CleanupAsync())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task CleanupAsync_DoesNotThrowOnCancellation()
    {
        // Cancellation causes OperationCanceledException which is allowed to propagate
        // (the BackgroundService host handles it). Verify it doesn't crash ungracefully.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // When cancelled with no DB work to do, it should propagate the cancellation
        // or complete without error depending on provider timing
        var act = () => _service.CleanupAsync(cts.Token);
        // Either outcome is acceptable — the important thing is no unhandled crash
        try { await act(); } catch (OperationCanceledException) { /* expected */ }
    }
}
