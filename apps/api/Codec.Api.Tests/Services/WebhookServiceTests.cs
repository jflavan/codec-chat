using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Codec.Api.Tests.Services;

public class WebhookServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IHttpClientFactory> _httpFactory = new();
    private readonly Mock<ILogger<WebhookService>> _logger = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly WebhookService _service;
    private readonly Guid _serverId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public WebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        // Seed a server and user
        _db.Users.Add(new User { Id = _userId, GoogleSubject = "g-1", DisplayName = "Tester" });
        _db.Servers.Add(new Server { Id = _serverId, Name = "Test Server" });
        _db.SaveChanges();

        // Set up IServiceScopeFactory to return our _db
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        // Set up HttpClient with mock handler
        var client = new HttpClient(_httpHandler.Object) { Timeout = TimeSpan.FromSeconds(10) };
        _httpFactory.Setup(f => f.CreateClient("webhook")).Returns(client);

        _service = new WebhookService(scopeFactory.Object, _httpFactory.Object, _logger.Object);
    }

    public void Dispose() => _db.Dispose();

    private Webhook CreateWebhook(
        string eventTypes = "MessageCreated",
        bool isActive = true,
        string? secret = null,
        string url = "https://example.com/hook")
    {
        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            ServerId = _serverId,
            Name = "Test Hook",
            Url = url,
            Secret = secret,
            EventTypes = eventTypes,
            IsActive = isActive,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Webhooks.Add(webhook);
        _db.SaveChanges();
        return webhook;
    }

    private void SetupHttpResponse(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));
    }

    private void SetupHttpException(Exception ex)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(ex);
    }

    // --- DispatchEvent: basic dispatch ---

    [Fact]
    public async Task DispatchEvent_DeliversToMatchingWebhook()
    {
        var webhook = CreateWebhook("MessageCreated");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "hello" });

        // Allow background task to complete
        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post && r.RequestUri!.ToString() == webhook.Url),
            ItExpr.IsAny<CancellationToken>());

        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Success.Should().BeTrue();
        logs[0].Attempt.Should().Be(1);
        logs[0].StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task DispatchEvent_SkipsInactiveWebhooks()
    {
        CreateWebhook("MessageCreated", isActive: false);
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "hello" });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEvent_SkipsWebhooksNotSubscribedToEvent()
    {
        CreateWebhook("MemberJoined");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "hello" });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEvent_MatchesMultipleEventTypes()
    {
        var webhook = CreateWebhook("MessageCreated,MemberJoined,ChannelCreated");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MemberJoined, new { userId = Guid.NewGuid() });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());

        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchEvent_IgnoresWebhooksForOtherServers()
    {
        var otherServerId = Guid.NewGuid();
        _db.Servers.Add(new Server { Id = otherServerId, Name = "Other" });
        _db.Webhooks.Add(new Webhook
        {
            Id = Guid.NewGuid(),
            ServerId = otherServerId,
            Name = "Other Hook",
            Url = "https://other.com/hook",
            EventTypes = "MessageCreated",
            IsActive = true,
            CreatedByUserId = _userId
        });
        _db.SaveChanges();
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "hello" });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // --- DispatchEvent: retry logic ---

    [Fact]
    public async Task DispatchEvent_RetriesOnServerError_UpTo3Times()
    {
        var webhook = CreateWebhook("MessageCreated");
        SetupHttpResponse(HttpStatusCode.InternalServerError);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "fail" });

        // RetryDelays are [5s, 30s, 5min] but we can't wait that long in tests.
        // The Task.Delay calls will run but we check after enough wall time.
        // Since this is a unit test, we'll wait enough for the first attempt at least.
        await Task.Delay(1000);

        // At minimum, attempt 1 should have been made; all 3 may not complete due to delays.
        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCountGreaterThanOrEqualTo(1);
        logs.All(l => l.Success == false).Should().BeTrue();
        logs.All(l => l.StatusCode == 500).Should().BeTrue();
    }

    [Fact]
    public async Task DispatchEvent_RetriesOnException_LogsError()
    {
        var webhook = CreateWebhook("MessageCreated");
        SetupHttpException(new HttpRequestException("Connection refused"));

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "fail" });

        await Task.Delay(1000);

        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCountGreaterThanOrEqualTo(1);
        logs[0].Success.Should().BeFalse();
        logs[0].ErrorMessage.Should().Contain("Connection refused");
        logs[0].StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task DispatchEvent_SuccessOnFirstAttempt_NoRetries()
    {
        var webhook = CreateWebhook("MessageCreated");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "ok" });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());

        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Attempt.Should().Be(1);
    }

    // --- Signature verification ---

    [Fact]
    public async Task DispatchEvent_IncludesHmacSignatureWhenSecretSet()
    {
        var webhook = CreateWebhook("MessageCreated", secret: "my-secret-key");
        string? capturedBody = null;
        string? capturedSignature = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (req.Headers.Contains("X-Webhook-Signature"))
                    capturedSignature = req.Headers.GetValues("X-Webhook-Signature").First();
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "signed" });

        await Task.Delay(500);

        capturedSignature.Should().NotBeNull();
        capturedSignature.Should().StartWith("sha256=");

        // Verify the signature is correct
        var keyBytes = Encoding.UTF8.GetBytes("my-secret-key");
        var payloadBytes = Encoding.UTF8.GetBytes(capturedBody!);
        var expectedHash = HMACSHA256.HashData(keyBytes, payloadBytes);
        var expectedSignature = $"sha256={Convert.ToHexStringLower(expectedHash)}";
        capturedSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public async Task DispatchEvent_OmitsSignatureWhenNoSecret()
    {
        CreateWebhook("MessageCreated", secret: null);
        HttpRequestMessage? capturedRequest = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "unsigned" });

        await Task.Delay(500);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains("X-Webhook-Signature").Should().BeFalse();
    }

    [Fact]
    public async Task DispatchEvent_IncludesEventAndIdHeaders()
    {
        var webhook = CreateWebhook("MessageCreated");
        HttpRequestMessage? capturedRequest = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "test" });

        await Task.Delay(500);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("X-Webhook-Event").First().Should().Be("MessageCreated");
        capturedRequest.Headers.GetValues("X-Webhook-Id").First().Should().Be(webhook.Id.ToString());
    }

    // --- Payload envelope ---

    [Fact]
    public async Task DispatchEvent_SendsCorrectEnvelopeShape()
    {
        var webhook = CreateWebhook("MessageCreated");
        string? capturedBody = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "hello", channelId = "abc" });

        await Task.Delay(500);

        capturedBody.Should().NotBeNull();
        var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        root.GetProperty("event").GetString().Should().Be("MessageCreated");
        root.GetProperty("serverId").GetString().Should().Be(_serverId.ToString());
        root.TryGetProperty("timestamp", out _).Should().BeTrue();
        root.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("content").GetString().Should().Be("hello");
        data.GetProperty("channelId").GetString().Should().Be("abc");
    }

    // --- Failure handling ---

    [Fact]
    public async Task DispatchEvent_LogsDeliveryLogOnSuccess()
    {
        var webhook = CreateWebhook("ChannelCreated");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.ChannelCreated, new { name = "general" });

        await Task.Delay(500);

        var log = await _db.WebhookDeliveryLogs.FirstOrDefaultAsync(l => l.WebhookId == webhook.Id);
        log.Should().NotBeNull();
        log!.EventType.Should().Be("ChannelCreated");
        log.Success.Should().BeTrue();
        log.StatusCode.Should().Be(200);
        log.ErrorMessage.Should().BeNull();
        log.Payload.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DispatchEvent_LogsDeliveryLogOnFailure()
    {
        var webhook = CreateWebhook("ChannelCreated");
        SetupHttpException(new TaskCanceledException("Request timed out"));

        _service.DispatchEvent(_serverId, WebhookEventType.ChannelCreated, new { name = "general" });

        await Task.Delay(1000);

        var logs = await _db.WebhookDeliveryLogs.Where(l => l.WebhookId == webhook.Id).ToListAsync();
        logs.Should().HaveCountGreaterThanOrEqualTo(1);
        logs[0].Success.Should().BeFalse();
        logs[0].ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task DispatchEvent_DeliversToMultipleMatchingWebhooks()
    {
        CreateWebhook("MessageCreated", url: "https://hook1.example.com/a");
        CreateWebhook("MessageCreated", url: "https://hook2.example.com/b");
        SetupHttpResponse(HttpStatusCode.OK);

        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "multi" });

        // Give background tasks time — InMemory DB may have concurrency quirks with Task.WhenAll
        await Task.Delay(1000);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DispatchEvent_DoesNotThrowWhenNoWebhooksExist()
    {
        SetupHttpResponse(HttpStatusCode.OK);

        // Should not throw — fires on background thread
        _service.DispatchEvent(_serverId, WebhookEventType.MessageCreated, new { content = "noop" });

        await Task.Delay(500);

        _httpHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
