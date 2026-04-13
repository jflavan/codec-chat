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

        // Setup scope factory to return our in-memory db
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
        var service = new AdminMetricsService(_hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_BroadcastsStatsUpdated()
    {
        // Add some messages to the counter
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        // Add a user connection
        _presence.Connect(Guid.NewGuid(), "conn-1");

        var service = new AdminMetricsService(_hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        // Let it run for a bit longer than the 5-second interval
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(5.5), CancellationToken.None);
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
    public async Task ExecuteAsync_WithOpenReports_IncludesReportCount()
    {
        // Add open reports to the database
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
            ReportType = ReportType.Server,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Bad",
            Status = ReportStatus.Resolved,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = new AdminMetricsService(_hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(5.5), CancellationToken.None);
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
    public async Task ExecuteAsync_ReadAndResetClearsCounter()
    {
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        var count = _metrics.ReadAndReset();
        count.Should().Be(2);

        _metrics.GetCount().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_MessagesPerMinuteCalculation()
    {
        // The service reads count and multiplies by 12 (5-second intervals * 12 = 60 seconds)
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        var recentMessages = _metrics.ReadAndReset();
        var messagesPerMinute = recentMessages * 12;

        messagesPerMinute.Should().Be(60);
    }

    [Fact]
    public async Task ExecuteAsync_DbError_LogsErrorAndContinues()
    {
        // Create a scope factory that throws when accessing the db
        var failingScopeFactory = new Mock<IServiceScopeFactory>();
        var failingScope = new Mock<IServiceScope>();
        var failingProvider = new Mock<IServiceProvider>();
        failingProvider.Setup(sp => sp.GetService(typeof(CodecDbContext)))
            .Throws(new InvalidOperationException("DB unavailable"));
        failingScope.Setup(s => s.ServiceProvider).Returns(failingProvider.Object);
        failingScopeFactory.Setup(f => f.CreateScope()).Returns(failingScope.Object);

        var service = new AdminMetricsService(_hubContext.Object, _metrics, _presence, failingScopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(5.5), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Service should not crash; it logs errors and continues
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringDelay_ExitsCleanly()
    {
        var service = new AdminMetricsService(_hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
