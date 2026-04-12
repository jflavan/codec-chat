using System.Collections.Concurrent;

namespace Codec.Api.Services;

public class DiscordImportCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationTokenSource Register(Guid importId, CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
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
    }

    public void Remove(Guid importId)
    {
        if (_tokens.TryRemove(importId, out var cts))
            cts.Dispose();
    }
}
