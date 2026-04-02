namespace Codec.Api.Services;

public class MetricsCounterService
{
    private long _messagesThisMinute;

    public void IncrementMessages() => Interlocked.Increment(ref _messagesThisMinute);
    public long GetMessagesPerMinute() => Interlocked.Read(ref _messagesThisMinute);
    public void ResetMinuteCounter() => Interlocked.Exchange(ref _messagesThisMinute, 0);
}
