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
        service.GetCount().Should().Be(3);
    }

    [Fact]
    public void GetCount_ReturnsZero_WhenNoMessages()
    {
        var service = new MetricsCounterService();
        service.GetCount().Should().Be(0);
    }

    [Fact]
    public void ReadAndReset_ReturnsCountAndClears()
    {
        var service = new MetricsCounterService();
        service.IncrementMessages();
        service.IncrementMessages();
        service.ReadAndReset().Should().Be(2);
        service.GetCount().Should().Be(0);
    }

    [Fact]
    public void MessagesPerMinute_StoresAndReturnsValue()
    {
        var service = new MetricsCounterService();
        service.GetMessagesPerMinute().Should().Be(0);
        service.SetMessagesPerMinute(120);
        service.GetMessagesPerMinute().Should().Be(120);
    }
}
