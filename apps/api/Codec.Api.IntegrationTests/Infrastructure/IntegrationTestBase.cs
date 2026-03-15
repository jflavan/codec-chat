using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Codec.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for integration tests. Provides factory access, authenticated clients,
/// and helpers for common operations.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<CodecWebFactory>
{
    protected readonly CodecWebFactory Factory;

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    protected IntegrationTestBase(CodecWebFactory factory)
    {
        Factory = factory;
    }

    /// <summary>
    /// Creates an authenticated HTTP client for a test user.
    /// Each unique googleSubject creates a new user on first API call.
    /// </summary>
    protected HttpClient CreateClient(string googleSubject = "google-test-1", string name = "Test User", string email = "test@test.com")
        => Factory.CreateAuthenticatedClient(googleSubject, name, email);

    /// <summary>
    /// Creates a second authenticated user for multi-user test scenarios.
    /// </summary>
    protected HttpClient CreateSecondClient()
        => Factory.CreateAuthenticatedClient("google-test-2", "Other User", "other@test.com");

    /// <summary>
    /// Creates a server via the API and returns (serverId, response).
    /// </summary>
    protected async Task<(Guid ServerId, HttpResponseMessage Response)> CreateServerAsync(HttpClient client, string name = "Test Server")
    {
        var response = await client.PostAsJsonAsync("/servers", new { name });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), response);
    }

    /// <summary>
    /// Creates a channel in a server via the API.
    /// </summary>
    protected async Task<Guid> CreateChannelAsync(HttpClient client, Guid serverId, string name = "test-channel", string type = "text")
    {
        var response = await client.PostAsJsonAsync($"/servers/{serverId}/channels", new { name, type });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Posts a message to a channel.
    /// </summary>
    protected async Task<Guid> PostMessageAsync(HttpClient client, Guid channelId, string body = "Hello world")
    {
        var response = await client.PostAsJsonAsync($"/channels/{channelId}/messages", new { body });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Gets the current user's ID via /me.
    /// </summary>
    protected async Task<Guid> GetUserIdAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/me");
        return response.GetProperty("user").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Sends a friend request and accepts it, establishing a friendship between two users.
    /// </summary>
    protected async Task<Guid> EstablishFriendshipAsync(HttpClient requester, HttpClient accepter)
    {
        var accepterId = await GetUserIdAsync(accepter);

        // Send friend request
        var sendResponse = await requester.PostAsJsonAsync("/friends/requests", new { recipientUserId = accepterId });
        sendResponse.EnsureSuccessStatusCode();
        var sendBody = await sendResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = sendBody.GetProperty("id").GetGuid();

        // Accept it
        var acceptResponse = await accepter.PutAsJsonAsync($"/friends/requests/{requestId}", new { action = "accept" });
        acceptResponse.EnsureSuccessStatusCode();

        return requestId;
    }

    /// <summary>
    /// Creates a DM channel between two users (requires established friendship).
    /// </summary>
    protected async Task<Guid> CreateDmChannelAsync(HttpClient client, Guid recipientUserId)
    {
        var response = await client.PostAsJsonAsync("/dm/channels", new { recipientUserId });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Runs an action within a fresh DI scope with the DbContext.
    /// </summary>
    protected async Task WithDbAsync(Func<CodecDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
        await action(db);
    }
}
