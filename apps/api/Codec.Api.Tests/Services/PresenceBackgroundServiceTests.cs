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

public class PresenceBackgroundServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly PresenceTracker _tracker = new();
    private readonly Mock<IHubContext<ChatHub>> _hubContext = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<ILogger<PresenceBackgroundService>> _logger = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public PresenceBackgroundServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        // Setup scope factory
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        // Setup hub context
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    public void Dispose() => _db.Dispose();

    // Note: ExecuteAsync uses ExecuteDeleteAsync which is not supported by InMemory provider.
    // The startup purge path is tested in integration tests.

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        var service = new PresenceBackgroundService(_tracker, _hubContext.Object, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_NoChanges_DoesNotBroadcast()
    {
        var service = new PresenceBackgroundService(_tracker, _hubContext.Object, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(50, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // No presence changes = no broadcasts
        _clientProxy.Verify(c => c.SendCoreAsync("UserPresenceChanged",
            It.IsAny<object?[]>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringWait_ExitsCleanly()
    {
        var service = new PresenceBackgroundService(_tracker, _hubContext.Object, _scopeFactory.Object, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void PresenceTracker_Connect_ReturnsOnline()
    {
        var userId = Guid.NewGuid();
        var status = _tracker.Connect(userId, "conn-1");
        status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public void PresenceTracker_Disconnect_LastConnection_ReturnsOffline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        var (_, current, _) = _tracker.Disconnect("conn-1");
        current.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void PresenceTracker_Disconnect_MultipleConnections_RemainsOnline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.Connect(userId, "conn-2");
        var (_, current, remaining) = _tracker.Disconnect("conn-1");
        current.Should().Be(PresenceStatus.Online);
        remaining.Should().Be(1);
    }

    [Fact]
    public void PresenceTracker_GetAggregateStatus_NoConnections_ReturnsOffline()
    {
        var status = _tracker.GetAggregateStatus(Guid.NewGuid());
        status.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void PresenceTracker_Heartbeat_NoChange_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");

        // First heartbeat with active=true should not change status
        var change = _tracker.Heartbeat("conn-1", true);
        change.Should().BeNull();
    }

    [Fact]
    public void PresenceTracker_Heartbeat_UnknownConnection_ReturnsNull()
    {
        var change = _tracker.Heartbeat("unknown-conn", true);
        change.Should().BeNull();
    }

    [Fact]
    public void PresenceTracker_ScanForTimeouts_NoConnections_ReturnsEmpty()
    {
        var changes = _tracker.ScanForTimeouts(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2));
        changes.Should().BeEmpty();
    }

    [Fact]
    public void PresenceTracker_ScanForTimeouts_ActiveConnections_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");

        // Connection just established, should not time out
        var changes = _tracker.ScanForTimeouts(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2));
        changes.Should().BeEmpty();
    }

    [Fact]
    public void PresenceTracker_Disconnect_UnknownConnection_DoesNotThrow()
    {
        var (prev, current, remaining) = _tracker.Disconnect("nonexistent");
        current.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void PresenceTracker_Connect_SameConnectionTwice_DoesNotDuplicate()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.Connect(userId, "conn-1");

        var status = _tracker.GetAggregateStatus(userId);
        status.Should().Be(PresenceStatus.Online);

        // Disconnect should still work
        var (_, current, _) = _tracker.Disconnect("conn-1");
        current.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void PresenceTracker_MultipleUsers_IndependentTracking()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _tracker.Connect(user1, "conn-u1");
        _tracker.Connect(user2, "conn-u2");

        _tracker.GetAggregateStatus(user1).Should().Be(PresenceStatus.Online);
        _tracker.GetAggregateStatus(user2).Should().Be(PresenceStatus.Online);

        _tracker.Disconnect("conn-u1");

        _tracker.GetAggregateStatus(user1).Should().Be(PresenceStatus.Offline);
        _tracker.GetAggregateStatus(user2).Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public void PresenceTracker_Connect_ThreeConnections_DisconnectMiddle_RemainsOnline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-a");
        _tracker.Connect(userId, "conn-b");
        _tracker.Connect(userId, "conn-c");

        var (_, current, remaining) = _tracker.Disconnect("conn-b");
        current.Should().Be(PresenceStatus.Online);
        remaining.Should().Be(2);
    }

    [Fact]
    public void PresenceTracker_Disconnect_AllConnections_GoesOffline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-a");
        _tracker.Connect(userId, "conn-b");

        _tracker.Disconnect("conn-a");
        var (_, current, _) = _tracker.Disconnect("conn-b");
        current.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void PresenceTracker_Heartbeat_AfterDisconnect_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.Disconnect("conn-1");

        var change = _tracker.Heartbeat("conn-1", true);
        change.Should().BeNull();
    }
}
