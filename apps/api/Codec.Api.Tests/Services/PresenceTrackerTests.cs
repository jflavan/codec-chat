using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;

namespace Codec.Api.Tests.Services;

public class PresenceTrackerTests
{
    private readonly PresenceTracker _tracker = new();

    [Fact]
    public void Connect_ReturnsOnlineStatus()
    {
        var userId = Guid.NewGuid();
        var status = _tracker.Connect(userId, "conn-1");
        status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public void Connect_MultipleTimes_StillOnline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        var status = _tracker.Connect(userId, "conn-2");
        status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public void Disconnect_UnknownConnection_ReturnsOffline()
    {
        var result = _tracker.Disconnect("unknown");
        result.Previous.Should().Be(PresenceStatus.Offline);
        result.Current.Should().Be(PresenceStatus.Offline);
        result.RemainingConnections.Should().Be(0);
    }

    [Fact]
    public void Disconnect_LastConnection_ReturnsOffline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        var result = _tracker.Disconnect("conn-1");

        result.Previous.Should().Be(PresenceStatus.Online);
        result.Current.Should().Be(PresenceStatus.Offline);
        result.RemainingConnections.Should().Be(0);
    }

    [Fact]
    public void Disconnect_WithRemainingConnection_StaysOnline()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.Connect(userId, "conn-2");

        var result = _tracker.Disconnect("conn-1");

        result.Previous.Should().Be(PresenceStatus.Online);
        result.Current.Should().Be(PresenceStatus.Online);
        result.RemainingConnections.Should().Be(1);
    }

    [Fact]
    public void Heartbeat_UnknownConnection_ReturnsNull()
    {
        var result = _tracker.Heartbeat("unknown", true);
        result.Should().BeNull();
    }

    [Fact]
    public void Heartbeat_ActiveHeartbeat_NoStatusChange_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        var result = _tracker.Heartbeat("conn-1", true);
        result.Should().BeNull(); // still Online → Online
    }

    [Fact]
    public void GetAggregateStatus_NoConnections_ReturnsOffline()
    {
        var status = _tracker.GetAggregateStatus(Guid.NewGuid());
        status.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void GetAggregateStatus_ExcludingConnection()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        var status = _tracker.GetAggregateStatus(userId, "conn-1");
        status.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public void GetUserId_KnownConnection_ReturnsUserId()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.GetUserId("conn-1").Should().Be(userId);
    }

    [Fact]
    public void GetUserId_UnknownConnection_ReturnsNull()
    {
        _tracker.GetUserId("unknown").Should().BeNull();
    }

    [Fact]
    public void GetConnectionIds_ReturnsAllConnections()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");
        _tracker.Connect(userId, "conn-2");

        var ids = _tracker.GetConnectionIds(userId);
        ids.Should().BeEquivalentTo(["conn-1", "conn-2"]);
    }

    [Fact]
    public void GetConnectionIds_NoConnections_ReturnsEmpty()
    {
        _tracker.GetConnectionIds(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task ScanForTimeouts_RemovesDeadConnections()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");

        // Wait briefly so the heartbeat age exceeds zero
        await Task.Delay(50);

        // Scan with zero timeout so the connection is immediately stale
        var changes = _tracker.ScanForTimeouts(TimeSpan.Zero, TimeSpan.Zero);

        changes.Should().ContainSingle();
        changes[0].UserId.Should().Be(userId);
        changes[0].Previous.Should().Be(PresenceStatus.Online);
        changes[0].Current.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public async Task ScanForTimeouts_TransitionsOnlineToIdle()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");

        // Wait briefly so the activity age exceeds zero
        await Task.Delay(50);

        // Idle timeout zero, but offline timeout large
        var changes = _tracker.ScanForTimeouts(TimeSpan.Zero, TimeSpan.FromHours(1));

        changes.Should().ContainSingle();
        changes[0].Current.Should().Be(PresenceStatus.Idle);
    }

    [Fact]
    public void ScanForTimeouts_NoChanges_WhenRecentHeartbeat()
    {
        var userId = Guid.NewGuid();
        _tracker.Connect(userId, "conn-1");

        var changes = _tracker.ScanForTimeouts(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        changes.Should().BeEmpty();
    }
}
