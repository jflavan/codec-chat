namespace Codec.Api.Services;

public class MetricsCounterService
{
    private long _messageCount;
    private long _lastMessagesPerMinute;

    public void IncrementMessages() => Interlocked.Increment(ref _messageCount);
    public long GetCount() => Interlocked.Read(ref _messageCount);
    public long ReadAndReset() => Interlocked.Exchange(ref _messageCount, 0);
    public long GetMessagesPerMinute() => Interlocked.Read(ref _lastMessagesPerMinute);
    public void SetMessagesPerMinute(long value) => Interlocked.Exchange(ref _lastMessagesPerMinute, value);
}
