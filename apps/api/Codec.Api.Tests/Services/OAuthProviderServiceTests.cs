using System.Net;
using System.Text.Json;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class OAuthProviderServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfiguration _configWithGitHub;
    private readonly IConfiguration _configWithDiscord;
    private readonly IConfiguration _emptyConfig;

    public OAuthProviderServiceTests()
    {
        _configWithGitHub = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OAuth:GitHub:ClientId"] = "gh-client-id",
                ["OAuth:GitHub:ClientSecret"] = "gh-client-secret"
            })
            .Build();

        _configWithDiscord = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OAuth:Discord:ClientId"] = "dc-client-id",
                ["OAuth:Discord:ClientSecret"] = "dc-client-secret",
                ["OAuth:Discord:RedirectUri"] = "http://localhost/callback"
            })
            .Build();

        _emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
    }

    private OAuthProviderService CreateService(IConfiguration config, params HttpResponseMessage[] responses)
    {
        var handler = new SequentialHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var logger = Mock.Of<ILogger<OAuthProviderService>>();
        return new OAuthProviderService(factory.Object, config, logger);
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    // --- GitHub: Configuration ---

    [Fact]
    public async Task GitHub_ReturnsNull_WhenNotConfigured()
    {
        var service = CreateService(_emptyConfig);
        var result = await service.ExchangeGitHubCodeAsync("some-code");
        result.Should().BeNull();
    }

    // --- GitHub: Token exchange failures ---

    [Fact]
    public async Task GitHub_ReturnsNull_WhenTokenExchangeFails()
    {
        var service = CreateService(_configWithGitHub,
            new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await service.ExchangeGitHubCodeAsync("bad-code");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GitHub_ReturnsNull_WhenTokenResponseHasError()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { error = "bad_verification_code", access_token = (string?)null }));

        var result = await service.ExchangeGitHubCodeAsync("bad-code");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GitHub_ReturnsNull_WhenTokenResponseHasEmptyAccessToken()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "" }));

        var result = await service.ExchangeGitHubCodeAsync("code");
        result.Should().BeNull();
    }

    // --- GitHub: User profile fetch failures ---

    [Fact]
    public async Task GitHub_ReturnsNull_WhenUserFetchFails()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "tok" }),
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await service.ExchangeGitHubCodeAsync("code");
        result.Should().BeNull();
    }

    // --- GitHub: Successful flows ---

    [Fact]
    public async Task GitHub_ReturnsUserInfo_WithPublicEmail()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = 12345,
                login = "octocat",
                name = "Mona Lisa",
                avatar_url = "https://avatars.githubusercontent.com/u/12345",
                email = "mona@github.com"
            }));

        var result = await service.ExchangeGitHubCodeAsync("valid-code");

        result.Should().NotBeNull();
        result!.Subject.Should().Be("12345");
        result.DisplayName.Should().Be("Mona Lisa");
        result.Email.Should().Be("mona@github.com");
        result.AvatarUrl.Should().Be("https://avatars.githubusercontent.com/u/12345");
    }

    [Fact]
    public async Task GitHub_UsesLogin_WhenNameIsNull()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = 99,
                login = "user99",
                email = "u@test.com"
            }));

        var result = await service.ExchangeGitHubCodeAsync("code");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("user99");
    }

    [Fact]
    public async Task GitHub_FetchesPrivateEmail_WhenPublicEmailIsNull()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "tok" }),
            // User profile with no email
            JsonResponse(new { id = 42, login = "private-user" }),
            // Emails endpoint
            JsonResponse(new object[]
            {
                new { email = "secondary@test.com", primary = false },
                new { email = "primary@test.com", primary = true }
            }));

        var result = await service.ExchangeGitHubCodeAsync("code");

        result.Should().NotBeNull();
        result!.Email.Should().Be("primary@test.com");
    }

    [Fact]
    public async Task GitHub_EmailRemainsNull_WhenEmailsFetchFails()
    {
        var service = CreateService(_configWithGitHub,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new { id = 42, login = "no-email-user" }),
            new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await service.ExchangeGitHubCodeAsync("code");

        result.Should().NotBeNull();
        result!.Email.Should().BeNull();
        result.Subject.Should().Be("42");
    }

    // --- Discord: Configuration ---

    [Fact]
    public async Task Discord_ReturnsNull_WhenNotConfigured()
    {
        var service = CreateService(_emptyConfig);
        var result = await service.ExchangeDiscordCodeAsync("some-code");
        result.Should().BeNull();
    }

    // --- Discord: Token exchange failures ---

    [Fact]
    public async Task Discord_ReturnsNull_WhenTokenExchangeFails()
    {
        var service = CreateService(_configWithDiscord,
            new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await service.ExchangeDiscordCodeAsync("bad-code");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Discord_ReturnsNull_WhenTokenResponseHasNoAccessToken()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "" }));

        var result = await service.ExchangeDiscordCodeAsync("code");
        result.Should().BeNull();
    }

    // --- Discord: User profile fetch failures ---

    [Fact]
    public async Task Discord_ReturnsNull_WhenUserFetchFails()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await service.ExchangeDiscordCodeAsync("code");
        result.Should().BeNull();
    }

    // --- Discord: Successful flows ---

    [Fact]
    public async Task Discord_ReturnsUserInfo_WithGlobalName()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = "987654321",
                username = "discorduser",
                global_name = "Cool Name",
                email = "discord@test.com",
                avatar = "abc123"
            }));

        var result = await service.ExchangeDiscordCodeAsync("valid-code");

        result.Should().NotBeNull();
        result!.Subject.Should().Be("987654321");
        result.DisplayName.Should().Be("Cool Name");
        result.Email.Should().Be("discord@test.com");
        result.AvatarUrl.Should().Be("https://cdn.discordapp.com/avatars/987654321/abc123.png?size=256");
    }

    [Fact]
    public async Task Discord_UsesUsername_WhenGlobalNameIsNull()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = "111",
                username = "fallback_user",
                email = "e@test.com"
            }));

        var result = await service.ExchangeDiscordCodeAsync("code");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("fallback_user");
    }

    [Fact]
    public async Task Discord_AnimatedAvatar_UsesGifExtension()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = "222",
                username = "animated",
                avatar = "a_animated_hash"
            }));

        var result = await service.ExchangeDiscordCodeAsync("code");

        result.Should().NotBeNull();
        result!.AvatarUrl.Should().Be("https://cdn.discordapp.com/avatars/222/a_animated_hash.gif?size=256");
    }

    [Fact]
    public async Task Discord_NullAvatar_ReturnsNullAvatarUrl()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = "333",
                username = "no_avatar"
            }));

        var result = await service.ExchangeDiscordCodeAsync("code");

        result.Should().NotBeNull();
        result!.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public async Task Discord_NullEmail_ReturnsNullEmail()
    {
        var service = CreateService(_configWithDiscord,
            JsonResponse(new { access_token = "tok" }),
            JsonResponse(new
            {
                id = "444",
                username = "no_email"
            }));

        var result = await service.ExchangeDiscordCodeAsync("code");

        result.Should().NotBeNull();
        result!.Email.Should().BeNull();
    }

    /// <summary>
    /// Handler that returns responses in order for sequential HTTP requests.
    /// </summary>
    private class SequentialHttpMessageHandler(HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _callIndex;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_callIndex >= responses.Length)
                throw new InvalidOperationException($"Unexpected HTTP request #{_callIndex + 1}: {request.Method} {request.RequestUri}");

            return Task.FromResult(responses[_callIndex++]);
        }
    }
}
