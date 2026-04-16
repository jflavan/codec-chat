using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class AdminMetricsServiceTests : IDisposable
{
    private static readonly TimeSpan TestTickInterval = TimeSpan.FromMilliseconds(50);

    private readonly CodecDbContext _db;
    private readonly Mock<IHubContext<AdminHub>> _hubContext = new();
    private readonly MetricsCounterService _metrics = new();
    private readonly PresenceTracker _presence = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<ILogger<AdminMetricsService>> _logger = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public AdminMetricsServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        // Setup scope factory to provide CodecDbContext
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        // Setup hub context
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    public void Dispose() => _db.Dispose();

    private AdminMetricsService CreateService(
        IServiceScopeFactory? scopeFactory = null,
        TimeSpan? tickInterval = null)
    {
        return new AdminMetricsService(
            _hubContext.Object, _metrics, _presence,
            scopeFactory ?? _scopeFactory.Object, _logger.Object,
            tickInterval ?? TestTickInterval);
    }

    private static async Task RunServiceForOneTick(AdminMetricsService service)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        var service = CreateService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        // Should not have broadcast anything
        _clientProxy.Verify(c => c.SendCoreAsync("StatsUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AfterDelay_BroadcastsStats()
    {
        var service = CreateService();

        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _metrics.IncrementMessages();
        _presence.Connect(Guid.NewGuid(), "conn-1");

        await RunServiceForOneTick(service);

        _clientProxy.Verify(c => c.SendCoreAsync("StatsUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsAndResetsMessageCounter()
    {
        var service = CreateService();

        _metrics.IncrementMessages();
        _metrics.IncrementMessages();

        await RunServiceForOneTick(service);

        // After ReadAndReset, the counter should be 0
        _metrics.GetCount().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_SetsMessagesPerMinute()
    {
        // Increment before creating service to ensure messages are available
        for (int i = 0; i < 5; i++)
            _metrics.IncrementMessages();

        _metrics.GetCount().Should().Be(5, "messages should be incremented before service starts");

        object?[]? firstCapturedArgs = null;
        _clientProxy.Setup(c => c.SendCoreAsync("StatsUpdated",
                It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
                firstCapturedArgs ??= args)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await RunServiceForOneTick(service);

        firstCapturedArgs.Should().NotBeNull("the service should have broadcast at least once");
        var payload = firstCapturedArgs![0]!;
        var messagesPerMinute = (int)payload.GetType().GetProperty("messagesPerMinute")!.GetValue(payload)!;
        var expectedRate = (int)(5 * (60.0 / TestTickInterval.TotalSeconds));
        messagesPerMinute.Should().Be(expectedRate);
    }

    [Fact]
    public async Task ExecuteAsync_CountsOpenReports()
    {
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Spam",
            Status = ReportStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            ReportType = ReportType.User,
            TargetId = Guid.NewGuid().ToString(),
            Reason = "Other",
            Status = ReportStatus.Resolved,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        object?[]? firstCapturedArgs = null;
        _clientProxy.Setup(c => c.SendCoreAsync("StatsUpdated",
                It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
                firstCapturedArgs ??= args)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await RunServiceForOneTick(service);

        firstCapturedArgs.Should().NotBeNull();
        var payload = firstCapturedArgs![0]!;
        var openReports = (int)payload.GetType().GetProperty("openReports")!.GetValue(payload)!;
        openReports.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsErrorAndContinues()
    {
        // Setup scope factory to throw
        var badScopeFactory = new Mock<IServiceScopeFactory>();
        var badScope = new Mock<IServiceScope>();
        var badProvider = new Mock<IServiceProvider>();
        badProvider.Setup(sp => sp.GetService(typeof(CodecDbContext)))
            .Throws(new InvalidOperationException("DB unavailable"));
        badScope.Setup(s => s.ServiceProvider).Returns(badProvider.Object);
        badScopeFactory.Setup(f => f.CreateScope()).Returns(badScope.Object);

        var service = CreateService(scopeFactory: badScopeFactory.Object);
        await RunServiceForOneTick(service);

        // Should have logged the error but not crashed
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
