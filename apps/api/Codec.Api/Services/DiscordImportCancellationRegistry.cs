using System.Collections.Concurrent;

namespace Codec.Api.Services;

public class DiscordImportCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly ConcurrentDictionary<Guid, byte> _pendingCancellations = new();

    public CancellationTokenSource Register(Guid importId, CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);

        if (_pendingCancellations.TryRemove(importId, out _))
        {
            cts.Cancel();
        }

        _tokens[importId] = cts;
        return cts;
    }

    public void Cancel(Guid importId)
    {
        if (_tokens.TryRemove(importId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        else
        {
            _pendingCancellations[importId] = 0;
        }
    }

    public void Remove(Guid importId)
    {
        _pendingCancellations.TryRemove(importId, out _);
        if (_tokens.TryRemove(importId, out var cts))
            cts.Dispose();
    }
}
