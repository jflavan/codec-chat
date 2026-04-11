using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Codec.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that boots the full API pipeline
/// against disposable Postgres and Redis containers.
/// </summary>
public class CodecWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("codec_test")
        .WithUsername("codec")
        .WithPassword("codec_test_password")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:8-alpine")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Google:ClientId", "test-client-id");
        builder.UseSetting("Api:BaseUrl", "http://localhost");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost");
        builder.UseSetting("Storage:Provider", "Local");
        builder.UseSetting("LiveKit:ApiKey", "testkey");
        builder.UseSetting("LiveKit:ApiSecret", "testsecretmustbe32charslong12345");
        builder.UseSetting("Recaptcha:Enabled", "false");
        builder.UseSetting("Saml:Enabled", "true");
        builder.UseSetting("Saml:EntityId", "https://test-sp.example.com/saml/metadata");
        builder.UseSetting("RateLimit:Fixed", "10000");
        builder.UseSetting("RateLimit:Auth", "10000");

        // Point Redis at the test container
        builder.UseSetting("Redis:ConnectionString", _redis.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            // Replace the Google JWT Bearer auth with our fake handler
            services.AddAuthentication(FakeAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(
                    FakeAuthHandler.SchemeName, _ => { });

            // Override default authentication scheme
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(FakeAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build());

        });
    }

    /// <summary>
    /// Creates an HttpClient with a Bearer token for the given test user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string googleSubject = "google-test-1",
        string name = "Test User",
        string email = "test@test.com",
        string? issuer = null)
    {
        var client = CreateClient();
        var token = FakeAuthHandler.CreateToken(googleSubject, name, email, issuer: issuer);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
