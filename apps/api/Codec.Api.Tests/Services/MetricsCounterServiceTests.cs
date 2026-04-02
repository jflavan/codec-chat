using Codec.Api.Services;
using FluentAssertions;

namespace Codec.Api.Tests.Services;

public class MetricsCounterServiceTests
{
    [Fact]
    public void IncrementMessages_IncrementsCount()
    {
        var service = new MetricsCounterService();
        service.IncrementMessages();
        service.IncrementMessages();
        service.IncrementMessages();
        service.GetMessagesPerMinute().Should().Be(3);
    }

    [Fact]
    public void GetMessagesPerMinute_ReturnsZero_WhenNoMessages()
    {
        var service = new MetricsCounterService();
        service.GetMessagesPerMinute().Should().Be(0);
    }

    [Fact]
    public void ResetMinuteCounter_ClearsCount()
    {
        var service = new MetricsCounterService();
        service.IncrementMessages();
        service.IncrementMessages();
        service.ResetMinuteCounter();
        service.GetMessagesPerMinute().Should().Be(0);
    }
}
