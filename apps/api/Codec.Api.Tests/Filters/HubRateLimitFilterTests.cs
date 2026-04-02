using Codec.Api.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Codec.Api.Tests.Filters;

public class HubRateLimitFilterTests
{
    [Fact]
    public void SlidingWindow_UnderLimit_AllowsRequests()
    {
        var window = new HubRateLimitFilter.SlidingWindow();

        for (var i = 0; i < 60; i++)
        {
            window.TryAcquire(60).Should().BeTrue($"request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void SlidingWindow_OverLimit_RejectsRequests()
    {
        var window = new HubRateLimitFilter.SlidingWindow();

        for (var i = 0; i < 60; i++)
        {
            window.TryAcquire(60).Should().BeTrue();
        }

        window.TryAcquire(60).Should().BeFalse("61st request within one minute should be rejected");
    }

    [Fact]
    public void SlidingWindow_DifferentInstances_HaveIndependentLimits()
    {
        var window1 = new HubRateLimitFilter.SlidingWindow();
        var window2 = new HubRateLimitFilter.SlidingWindow();

        // Exhaust window1
        for (var i = 0; i < 60; i++)
        {
            window1.TryAcquire(60).Should().BeTrue();
        }

        window1.TryAcquire(60).Should().BeFalse("window1 should be exhausted");
        window2.TryAcquire(60).Should().BeTrue("window2 should be independent and still allow requests");
    }

    [Fact]
    public async Task InvokeMethodAsync_UnderLimit_CallsNext()
    {
        var filter = new HubRateLimitFilter();
        var nextCalled = false;

        var mockContext = CreateMockHubInvocationContext("conn-1");

        ValueTask<object?> Next(HubInvocationContext ctx)
        {
            nextCalled = true;
            return new ValueTask<object?>((object?)null);
        }

        await filter.InvokeMethodAsync(mockContext, Next);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeMethodAsync_OverLimit_ThrowsHubException()
    {
        var filter = new HubRateLimitFilter();

        var mockContext = CreateMockHubInvocationContext("conn-1");

        ValueTask<object?> Next(HubInvocationContext ctx)
        {
            return new ValueTask<object?>((object?)null);
        }

        // Exhaust the limit
        for (var i = 0; i < 60; i++)
        {
            await filter.InvokeMethodAsync(mockContext, Next);
        }

        // 61st should throw
        var act = () => filter.InvokeMethodAsync(mockContext, Next).AsTask();
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Rate limit exceeded.");
    }

    [Fact]
    public async Task InvokeMethodAsync_DifferentConnections_HaveIndependentLimits()
    {
        var filter = new HubRateLimitFilter();

        var context1 = CreateMockHubInvocationContext("conn-1");
        var context2 = CreateMockHubInvocationContext("conn-2");

        ValueTask<object?> Next(HubInvocationContext ctx)
        {
            return new ValueTask<object?>((object?)null);
        }

        // Exhaust conn-1
        for (var i = 0; i < 60; i++)
        {
            await filter.InvokeMethodAsync(context1, Next);
        }

        // conn-2 should still work
        var act = () => filter.InvokeMethodAsync(context2, Next).AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnDisconnectedAsync_UnknownConnection_DoesNotThrow()
    {
        var filter = new HubRateLimitFilter();

        var mockLifetimeContext = CreateMockHubLifetimeContext("unknown-conn");

        var act = () => filter.OnDisconnectedAsync(
            mockLifetimeContext,
            null,
            (ctx, ex) => Task.CompletedTask);

        await act.Should().NotThrowAsync();
    }

    private static HubInvocationContext CreateMockHubInvocationContext(string connectionId)
    {
        var mockHubCallerContext = new Mock<HubCallerContext>();
        mockHubCallerContext.Setup(c => c.ConnectionId).Returns(connectionId);

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockHub = new Mock<Hub>();
        var hubType = typeof(Hub);

        return new HubInvocationContext(
            mockHubCallerContext.Object,
            mockServiceProvider.Object,
            mockHub.Object,
            typeof(Hub).GetMethod(nameof(Hub.Dispose))!,
            Array.Empty<object>());
    }

    private static HubLifetimeContext CreateMockHubLifetimeContext(string connectionId)
    {
        var mockHubCallerContext = new Mock<HubCallerContext>();
        mockHubCallerContext.Setup(c => c.ConnectionId).Returns(connectionId);

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockHub = new Mock<Hub>();

        return new HubLifetimeContext(
            mockHubCallerContext.Object,
            mockServiceProvider.Object,
            mockHub.Object);
    }
}
