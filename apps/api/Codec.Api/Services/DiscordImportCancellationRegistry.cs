using System.Collections.Concurrent;

namespace Codec.Api.Services;

public class DiscordImportCancellationRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly HashSet<Guid> _pendingCancellations = new();

    public CancellationTokenSource Register(Guid importId, CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);

        lock (_lock)
        {
            if (_pendingCancellations.Remove(importId))
            {
                cts.Cancel();
            }

            _tokens[importId] = cts;
        }

        return cts;
    }

    public void Cancel(Guid importId)
    {
        lock (_lock)
        {
            if (_tokens.Remove(importId, out var cts))
            {
                cts.Cancel();
            }
            else
            {
                _pendingCancellations.Add(importId);
            }
        }
    }

    public void Remove(Guid importId)
    {
        lock (_lock)
        {
            _pendingCancellations.Remove(importId);
            if (_tokens.Remove(importId, out var cts))
                cts.Dispose();
        }
    }
}
