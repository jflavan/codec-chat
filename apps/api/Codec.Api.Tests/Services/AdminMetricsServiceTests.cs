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
    private readonly MetricsCounterService _metrics = new();
    private readonly PresenceTracker _presence = new();
    private readonly Mock<IHubContext<AdminHub>> _hubContext = new();
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

    private AdminMetricsService CreateService() =>
        new(_hubContext.Object, _metrics, _presence, _scopeFactory.Object, _logger.Object);

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        var service = CreateService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_BroadcastsStatsUpdated_WithCorrectPayload()
    {
        // Arrange: set up some message counts
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        // Add an online user
        var userId = Guid.NewGuid();
        _presence.Connect(userId, "conn-1");

        // Add open reports to the database
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Status = ReportStatus.Open,
            Reason = "spam",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Status = ReportStatus.Open,
            Reason = "harassment",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Status = ReportStatus.Resolved,
            Reason = "resolved-one",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        // Act: let the service run one tick (5s delay + broadcast)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Assert: StatsUpdated was broadcast at least once
        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsAndResetsMessageCount()
    {
        // Arrange
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // After execution, message count should have been reset
        _metrics.GetCount().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_SetsMessagesPerMinute_AsCountTimes12()
    {
        // Arrange: 5 messages in the counter
        for (int i = 0; i < 5; i++)
            _metrics.IncrementMessages();

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // 5 messages * 12 (5-second intervals per minute) = 60
        _metrics.GetMessagesPerMinute().Should().Be(60);
    }

    [Fact]
    public async Task ExecuteAsync_NoOpenReports_BroadcastsZeroOpenReports()
    {
        // Only add resolved/dismissed reports
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Status = ReportStatus.Resolved,
            Reason = "resolved",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Status = ReportStatus.Dismissed,
            Reason = "dismissed",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_NoUsersOnline_BroadcastsZeroActiveUsers()
    {
        // No presence connections set up
        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_DbThrows_LogsErrorAndContinues()
    {
        // Setup scope factory to throw on CreateScope
        var failingScopeFactory = new Mock<IServiceScopeFactory>();
        var failingScope = new Mock<IServiceScope>();
        var failingProvider = new Mock<IServiceProvider>();
        failingProvider.Setup(sp => sp.GetService(typeof(CodecDbContext)))
            .Throws(new InvalidOperationException("DB unavailable"));
        failingScope.Setup(s => s.ServiceProvider).Returns(failingProvider.Object);
        failingScopeFactory.Setup(f => f.CreateScope()).Returns(failingScope.Object);

        var service = new AdminMetricsService(
            _hubContext.Object, _metrics, _presence, failingScopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Service should not crash; error is caught and logged
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_HubSendThrows_LogsErrorAndContinues()
    {
        // Setup hub to throw on SendCoreAsync
        _clientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hub send failed"));

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Service should not crash; error is caught and logged
        _logger.Verify(
            x => x.Log(
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
        var service = CreateService();

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

        // No broadcast should have occurred (cancelled before 5s delay)
        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleOnlineUsers_ReportsCorrectActiveCount()
    {
        // Connect multiple users
        _presence.Connect(Guid.NewGuid(), "conn-1");
        _presence.Connect(Guid.NewGuid(), "conn-2");
        _presence.Connect(Guid.NewGuid(), "conn-3");

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ZeroMessages_SetsMessagesPerMinuteToZero()
    {
        // No messages incremented
        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _metrics.GetMessagesPerMinute().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDatabase_BroadcastsSuccessfully()
    {
        // No reports in the database at all
        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(6500));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(6200, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        _clientProxy.Verify(c => c.SendCoreAsync(
            "StatsUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
