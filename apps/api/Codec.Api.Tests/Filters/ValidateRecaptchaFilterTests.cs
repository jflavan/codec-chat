using Codec.Api.Filters;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Codec.Api.Tests.Filters;

public class ValidateRecaptchaFilterTests
{
    private readonly Mock<RecaptchaService> _recaptchaService;
    private readonly RecaptchaSettings _settings;
    private readonly ValidateRecaptchaFilter _filter;
    private bool _nextCalled;

    public ValidateRecaptchaFilterTests()
    {
        _settings = new RecaptchaSettings
        {
            Enabled = true,
            SecretKey = "test-key",
            SiteKey = "test-site",
            ProjectId = "test-project",
            ScoreThreshold = 0.5
        };

        _recaptchaService = new Mock<RecaptchaService>(
            new HttpClient(),
            Options.Create(_settings),
            Mock.Of<ILogger<RecaptchaService>>());

        _filter = new ValidateRecaptchaFilter(
            _recaptchaService.Object,
            Options.Create(_settings),
            Mock.Of<ILogger<ValidateRecaptchaFilter>>(),
            "login");

        _nextCalled = false;
    }

    private ActionExecutionDelegate CreateNext()
    {
        return () =>
        {
            _nextCalled = true;
            var ctx = new ActionExecutedContext(
                CreateActionContext(new Dictionary<string, object?>()),
                new List<IFilterMetadata>(),
                new object());
            return Task.FromResult(ctx);
        };
    }

    private static ActionExecutingContext CreateActionContext(Dictionary<string, object?> arguments)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            arguments!,
            new object());
    }

    [Fact]
    public async Task WhenDisabled_CallsNextWithoutValidation()
    {
        _settings.Enabled = false;

        var context = CreateActionContext(new Dictionary<string, object?>());
        await _filter.OnActionExecutionAsync(context, CreateNext());

        _nextCalled.Should().BeTrue();
        _recaptchaService.Verify(
            s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingToken_Returns400()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "pass", RecaptchaToken = null };
        var context = CreateActionContext(new Dictionary<string, object?> { ["request"] = request });

        await _filter.OnActionExecutionAsync(context, CreateNext());

        _nextCalled.Should().BeFalse();
        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ValidToken_CallsNext()
    {
        _recaptchaService
            .Setup(s => s.VerifyAsync("valid-token", "login"))
            .ReturnsAsync((true, 0.9, (string?)null));

        var request = new LoginRequest { Email = "test@test.com", Password = "pass", RecaptchaToken = "valid-token" };
        var context = CreateActionContext(new Dictionary<string, object?> { ["request"] = request });

        await _filter.OnActionExecutionAsync(context, CreateNext());

        _nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task FailedVerification_Returns403()
    {
        _recaptchaService
            .Setup(s => s.VerifyAsync("bad-token", "login"))
            .ReturnsAsync((false, 0.1, "reCAPTCHA score too low."));

        var request = new LoginRequest { Email = "test@test.com", Password = "pass", RecaptchaToken = "bad-token" };
        var context = CreateActionContext(new Dictionary<string, object?> { ["request"] = request });

        await _filter.OnActionExecutionAsync(context, CreateNext());

        _nextCalled.Should().BeFalse();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
