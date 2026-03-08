using System.Collections.Concurrent;
using Codec.Api.Models;

namespace Codec.Api.Services;

public class PresenceTracker
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();

    public record ConnectionEntry(Guid UserId, DateTimeOffset LastHeartbeatAt, DateTimeOffset LastActiveAt, PresenceStatus Status);

    /// <summary>
    /// Register a new connection. Returns the user's new aggregate status.
    /// </summary>
    public PresenceStatus Connect(Guid userId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        _connections[connectionId] = new ConnectionEntry(userId, now, now, PresenceStatus.Online);
        return GetAggregateStatus(userId);
    }

    /// <summary>
    /// Remove a connection. Returns (previousAggregateStatus, newAggregateStatus, remainingConnections).
    /// </summary>
    public (PresenceStatus Previous, PresenceStatus Current, int RemainingConnections) Disconnect(string connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var entry))
            return (PresenceStatus.Offline, PresenceStatus.Offline, 0);

        // Compute previous aggregate BEFORE removing the connection
        var previous = GetAggregateStatus(entry.UserId);

        _connections.TryRemove(connectionId, out _);

        var current = GetAggregateStatus(entry.UserId);
        var remaining = _connections.Values.Count(c => c.UserId == entry.UserId);
        return (previous, current, remaining);
    }

    /// <summary>
    /// Process a heartbeat. Returns (userId, previousStatus, newStatus) if status changed, null otherwise.
    /// </summary>
    public (Guid UserId, PresenceStatus Previous, PresenceStatus Current)? Heartbeat(string connectionId, bool isActive)
    {
        if (!_connections.TryGetValue(connectionId, out var entry))
            return null;

        var now = DateTimeOffset.UtcNow;
        var previousAggregate = GetAggregateStatus(entry.UserId);

        var newEntry = entry with
        {
            LastHeartbeatAt = now,
            LastActiveAt = isActive ? now : entry.LastActiveAt,
            Status = isActive ? PresenceStatus.Online : entry.Status
        };
        _connections[connectionId] = newEntry;

        var newAggregate = GetAggregateStatus(entry.UserId);
        if (previousAggregate != newAggregate)
            return (entry.UserId, previousAggregate, newAggregate);

        return null;
    }

    /// <summary>
    /// Scan all connections for idle/offline transitions. Returns list of users whose aggregate status changed.
    /// </summary>
    public List<(Guid UserId, PresenceStatus Previous, PresenceStatus Current, List<string> StaleConnectionIds)> ScanForTimeouts(TimeSpan idleTimeout, TimeSpan offlineTimeout)
    {
        var now = DateTimeOffset.UtcNow;
        var changes = new List<(Guid UserId, PresenceStatus Previous, PresenceStatus Current, List<string> StaleConnectionIds)>();
        var usersBefore = new Dictionary<Guid, PresenceStatus>();

        // Snapshot current aggregate statuses
        foreach (var (connId, entry) in _connections)
        {
            if (!usersBefore.ContainsKey(entry.UserId))
                usersBefore[entry.UserId] = GetAggregateStatus(entry.UserId);
        }

        var staleConnections = new Dictionary<Guid, List<string>>();

        // Update individual connection statuses
        foreach (var (connId, entry) in _connections)
        {
            if (now - entry.LastHeartbeatAt > offlineTimeout)
            {
                // Connection is dead — remove it
                _connections.TryRemove(connId, out _);
                if (!staleConnections.ContainsKey(entry.UserId))
                    staleConnections[entry.UserId] = [];
                staleConnections[entry.UserId].Add(connId);
            }
            else if (entry.Status == PresenceStatus.Online && now - entry.LastActiveAt > idleTimeout)
            {
                _connections[connId] = entry with { Status = PresenceStatus.Idle };
            }
        }

        // Check for aggregate status changes
        foreach (var (userId, previousStatus) in usersBefore)
        {
            var currentStatus = GetAggregateStatus(userId);
            if (currentStatus != previousStatus)
            {
                staleConnections.TryGetValue(userId, out var stale);
                changes.Add((userId, previousStatus, currentStatus, stale ?? []));
            }
        }

        return changes;
    }

    public PresenceStatus GetAggregateStatus(Guid userId, string? includingConnectionId = null)
    {
        var best = PresenceStatus.Offline;
        foreach (var (connId, entry) in _connections)
        {
            if (entry.UserId == userId && connId != includingConnectionId)
            {
                if (entry.Status < best) // Lower enum = better (Online=0)
                    best = entry.Status;
            }
        }
        return best;
    }

    public Guid? GetUserId(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var entry) ? entry.UserId : null;
    }

    public List<string> GetConnectionIds(Guid userId)
    {
        return _connections.Where(c => c.Value.UserId == userId).Select(c => c.Key).ToList();
    }
}
