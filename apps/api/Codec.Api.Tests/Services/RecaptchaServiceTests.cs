using System.Net;
using System.Text.Json;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Codec.Api.Tests.Services;

public class RecaptchaServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RecaptchaSettings _settings = new()
    {
        SecretKey = "test-secret-key",
        SiteKey = "test-site-key",
        ProjectId = "test-project",
        ScoreThreshold = 0.5,
        Enabled = true
    };

    private RecaptchaService CreateService(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(_settings);
        var logger = Mock.Of<ILogger<RecaptchaService>>();
        return new RecaptchaService(httpClient, options, logger);
    }

    private static HttpResponseMessage CreateJsonResponse(object body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsSuccessWithScore()
    {
        var response = CreateJsonResponse(new
        {
            tokenProperties = new { valid = true, action = "login", invalidReason = (string?)null },
            riskAnalysis = new { score = 0.9 }
        });

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("valid-token", "login");

        success.Should().BeTrue();
        score.Should().Be(0.9);
        error.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_LowScore_ReturnsFailure()
    {
        var response = CreateJsonResponse(new
        {
            tokenProperties = new { valid = true, action = "login", invalidReason = (string?)null },
            riskAnalysis = new { score = 0.2 }
        });

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("low-score-token", "login");

        success.Should().BeFalse();
        score.Should().Be(0.2);
        error.Should().Be("reCAPTCHA score too low.");
    }

    [Fact]
    public async Task VerifyAsync_ActionMismatch_ReturnsFailure()
    {
        var response = CreateJsonResponse(new
        {
            tokenProperties = new { valid = true, action = "register", invalidReason = (string?)null },
            riskAnalysis = new { score = 0.9 }
        });

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("token", "login");

        success.Should().BeFalse();
        score.Should().Be(0.9);
        error.Should().Be("reCAPTCHA action mismatch.");
    }

    [Fact]
    public async Task VerifyAsync_InvalidToken_ReturnsFailure()
    {
        var response = CreateJsonResponse(new
        {
            tokenProperties = new { valid = false, action = "login", invalidReason = "EXPIRED" },
            riskAnalysis = new { score = 0.0 }
        });

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("expired-token", "login");

        success.Should().BeFalse();
        score.Should().Be(0);
        error.Should().Be("reCAPTCHA verification failed.");
    }

    [Fact]
    public async Task VerifyAsync_HttpError_ReturnsUnavailable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("token", "login");

        success.Should().BeFalse();
        score.Should().Be(0);
        error.Should().Be("reCAPTCHA verification unavailable.");
    }

    [Fact]
    public async Task VerifyAsync_CustomThreshold_IsRespected()
    {
        _settings.ScoreThreshold = 0.8;

        var response = CreateJsonResponse(new
        {
            tokenProperties = new { valid = true, action = "login", invalidReason = (string?)null },
            riskAnalysis = new { score = 0.7 }
        });

        var service = CreateService(response);
        var (success, score, error) = await service.VerifyAsync("token", "login");

        success.Should().BeFalse();
        score.Should().Be(0.7);
        error.Should().Be("reCAPTCHA score too low.");
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
