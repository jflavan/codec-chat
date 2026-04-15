using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class AdminMetricsServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IHubContext<AdminHub>> _hubContext = new();
    private readonly MetricsCounterService _metrics = new();
    private readonly PresenceTracker _presence = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<ILogger<AdminMetricsService>> _logger = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public AdminMetricsServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        // Setup scope factory to provide CodecDbContext
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        // Setup hub context
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        // Should not have broadcast anything
        _clientProxy.Verify(c => c.SendCoreAsync("StatsUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AfterDelay_BroadcastsStats()
    {
        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        // Add some messages to the counter
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        // Connect a user for presence
        _presence.Connect(Guid.NewGuid(), "conn-1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        try
        {
            await service.StartAsync(cts.Token);
            // Wait long enough for at least one tick (5 seconds delay in the service)
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync("StatsUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsAndResetsMessageCounter()
    {
        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // After ReadAndReset, the counter should be 0
        _metrics.GetCount().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_SetsMessagesPerMinute()
    {
        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        // 5 messages in a 5-second window = 5 * 12 = 60 messages per minute
        for (int i = 0; i < 5; i++)
            _metrics.IncrementMessages();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _metrics.GetMessagesPerMinute().Should().Be(60);
    }

    [Fact]
    public async Task ExecuteAsync_CountsOpenReports()
    {
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Spam",
            Status = ReportStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Other",
            Status = ReportStatus.Resolved,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync("StatsUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsErrorAndContinues()
    {
        // Setup scope factory to throw
        var badScopeFactory = new Mock<IServiceScopeFactory>();
        var badScope = new Mock<IServiceScope>();
        var badProvider = new Mock<IServiceProvider>();
        badProvider.Setup(sp => sp.GetService(typeof(CodecDbContext)))
            .Throws(new InvalidOperationException("DB unavailable"));
        badScope.Setup(s => s.ServiceProvider).Returns(badProvider.Object);
        badScopeFactory.Setup(f => f.CreateScope()).Returns(badScope.Object);

        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, badScopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Should have logged the error but not crashed
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
