using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codec.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Authentication handler that bypasses Google JWT validation.
/// Decodes a simple base64-encoded JSON payload from the Bearer token
/// to populate claims (sub, name, email, picture).
/// </summary>
public class FakeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check Authorization header first, then fall back to query string (for SignalR WebSocket)
        string? token = null;

        if (Request.Headers.ContainsKey("Authorization"))
        {
            var authHeader = Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(token) && Request.Query.TryGetValue("access_token", out var queryToken))
        {
            token = queryToken.ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

            var claims = new List<Claim>();
            if (payload.TryGetValue("sub", out var sub)) claims.Add(new Claim("sub", sub));
            if (payload.TryGetValue("name", out var name)) claims.Add(new Claim("name", name));
            if (payload.TryGetValue("email", out var email)) claims.Add(new Claim("email", email));
            if (payload.TryGetValue("picture", out var picture)) claims.Add(new Claim("picture", picture));
            if (payload.TryGetValue("iss", out var iss)) claims.Add(new Claim("iss", iss));
            else claims.Add(new Claim("iss", "accounts.google.com"));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid test token"));
        }
    }

    /// <summary>
    /// Creates a base64-encoded token from the given claims for use in test requests.
    /// </summary>
    public static string CreateToken(string googleSubject, string name, string? email = null, string? picture = null)
    {
        var payload = new Dictionary<string, string>
        {
            ["sub"] = googleSubject,
            ["name"] = name,
            ["email"] = email ?? $"{googleSubject}@test.com"
        };
        if (picture is not null) payload["picture"] = picture;

        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
