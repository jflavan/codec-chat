using System.Net.Http.Headers;
using System.Text.Json;

namespace Codec.Api.Services;

public record OAuthUserInfo(string Subject, string DisplayName, string? Email, string? AvatarUrl);

public class OAuthProviderService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<OAuthProviderService> logger)
{
    public virtual async Task<OAuthUserInfo?> ExchangeGitHubCodeAsync(string code)
    {
        var clientId = configuration["OAuth:GitHub:ClientId"];
        var clientSecret = configuration["OAuth:GitHub:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("GitHub OAuth not configured");
            return null;
        }

        var client = httpClientFactory.CreateClient();

        // Exchange code for access token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = JsonContent.Create(new { client_id = clientId, client_secret = clientSecret, code })
        };
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub token exchange failed: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        if (!tokenJson.TryGetProperty("access_token", out var atProp) || string.IsNullOrWhiteSpace(atProp.GetString()))
        {
            var error = tokenJson.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
            logger.LogWarning("GitHub token exchange returned error: {Error}", error);
            return null;
        }
        var accessToken = atProp.GetString();

        // Fetch user profile
        var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        userRequest.Headers.Add("User-Agent", "CodecChat/1.0");

        var userResponse = await client.SendAsync(userRequest);
        if (!userResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub user fetch failed: {Status}", userResponse.StatusCode);
            return null;
        }

        var userJson = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = userJson.GetProperty("id").GetInt64().ToString();
        var login = userJson.GetProperty("login").GetString() ?? "Unknown";
        var name = userJson.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString() : login;
        var avatarUrl = userJson.TryGetProperty("avatar_url", out var avProp) ? avProp.GetString() : null;

        // Fetch primary email (may be private)
        string? email = userJson.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String
            ? emailProp.GetString() : null;

        if (email is null)
        {
            var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
            emailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            emailRequest.Headers.Add("User-Agent", "CodecChat/1.0");

            var emailResponse = await client.SendAsync(emailRequest);
            if (emailResponse.IsSuccessStatusCode)
            {
                var emails = await emailResponse.Content.ReadFromJsonAsync<JsonElement>();
                foreach (var entry in emails.EnumerateArray())
                {
                    if (entry.TryGetProperty("primary", out var primary) && primary.GetBoolean())
                    {
                        email = entry.GetProperty("email").GetString();
                        break;
                    }
                }
            }
        }

        return new OAuthUserInfo(id, name ?? login, email, avatarUrl);
    }

    public virtual async Task<OAuthUserInfo?> ExchangeDiscordCodeAsync(string code)
    {
        var clientId = configuration["OAuth:Discord:ClientId"];
        var clientSecret = configuration["OAuth:Discord:ClientSecret"];
        var redirectUri = configuration["OAuth:Discord:RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("Discord OAuth not configured");
            return null;
        }

        var client = httpClientFactory.CreateClient();

        // Exchange code for access token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri ?? ""
            })
        };

        var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord token exchange failed: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        if (!tokenJson.TryGetProperty("access_token", out var atProp) || string.IsNullOrWhiteSpace(atProp.GetString()))
        {
            logger.LogWarning("Discord token exchange returned no access_token");
            return null;
        }
        var accessToken = atProp.GetString();

        // Fetch user profile
        var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await client.SendAsync(userRequest);
        if (!userResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord user fetch failed: {Status}", userResponse.StatusCode);
            return null;
        }

        var userJson = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = userJson.GetProperty("id").GetString() ?? throw new InvalidOperationException("Missing Discord user id");
        var username = userJson.GetProperty("username").GetString() ?? "Unknown";
        var globalName = userJson.TryGetProperty("global_name", out var gnProp) && gnProp.ValueKind == JsonValueKind.String
            ? gnProp.GetString() : username;
        var email = userJson.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String
            ? emailProp.GetString() : null;

        // Discord avatar URL
        string? avatarUrl = null;
        if (userJson.TryGetProperty("avatar", out var avatarProp) && avatarProp.ValueKind == JsonValueKind.String)
        {
            var avatarHash = avatarProp.GetString();
            if (avatarHash is not null)
            {
                var ext = avatarHash.StartsWith("a_") ? "gif" : "png";
                avatarUrl = $"https://cdn.discordapp.com/avatars/{id}/{avatarHash}.{ext}?size=256";
            }
        }

        return new OAuthUserInfo(id, globalName ?? username, email, avatarUrl);
    }
}
