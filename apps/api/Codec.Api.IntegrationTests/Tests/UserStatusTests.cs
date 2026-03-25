using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for user status (custom status text/emoji):
/// set, clear, broadcast verification, and validation.
/// </summary>
public class UserStatusTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SetStatus_WithTextAndEmoji_Succeeds()
    {
        var client = CreateClient("status-set-1", "StatusUser1");

        var response = await client.PutAsJsonAsync("/me/status", new
        {
            statusText = "Working",
            statusEmoji = "\U0001F4BB"  // laptop emoji
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Working", body.GetProperty("statusText").GetString());
        Assert.Equal("\U0001F4BB", body.GetProperty("statusEmoji").GetString());
    }

    [Fact]
    public async Task SetStatus_TextOnly_Succeeds()
    {
        var client = CreateClient("status-text-1", "StatusTextUser");

        var response = await client.PutAsJsonAsync("/me/status", new
        {
            statusText = "In a meeting"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("In a meeting", body.GetProperty("statusText").GetString());
    }

    [Fact]
    public async Task SetStatus_EmojiOnly_Succeeds()
    {
        var client = CreateClient("status-emoji-1", "StatusEmojiUser");

        var response = await client.PutAsJsonAsync("/me/status", new
        {
            statusEmoji = "\U0001F3AE"  // game controller emoji
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("\U0001F3AE", body.GetProperty("statusEmoji").GetString());
    }

    [Fact]
    public async Task SetStatus_EmptyBoth_ReturnsBadRequest()
    {
        var client = CreateClient("status-empty-1", "StatusEmptyUser");

        var response = await client.PutAsJsonAsync("/me/status", new
        {
            statusText = "",
            statusEmoji = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClearStatus_WhenSet_Succeeds()
    {
        var client = CreateClient("status-clear-1", "StatusClearUser");

        // Set status first
        await client.PutAsJsonAsync("/me/status", new { statusText = "Busy" });

        // Clear it
        var response = await client.DeleteAsync("/me/status");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("statusText").ValueKind == JsonValueKind.Null);
        Assert.True(body.GetProperty("statusEmoji").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ClearStatus_WhenNotSet_ReturnsNotFound()
    {
        var client = CreateClient("status-clear-nf-1", "StatusClearNFUser");

        // Make sure user exists (no status set)
        await client.GetAsync("/me");

        var response = await client.DeleteAsync("/me/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetStatus_VisibleOnProfile()
    {
        var client = CreateClient("status-profile-1", "StatusProfileUser");

        // Set status
        await client.PutAsJsonAsync("/me/status", new
        {
            statusText = "Available",
            statusEmoji = "\u2705"  // check mark
        });

        // Fetch profile and verify status is included
        var meResponse = await client.GetFromJsonAsync<JsonElement>("/me");
        var user = meResponse.GetProperty("user");
        Assert.Equal("Available", user.GetProperty("statusText").GetString());
        Assert.Equal("\u2705", user.GetProperty("statusEmoji").GetString());
    }

    [Fact]
    public async Task SetStatus_UpdateExisting_Succeeds()
    {
        var client = CreateClient("status-update-1", "StatusUpdateUser");

        // Set initial status
        await client.PutAsJsonAsync("/me/status", new { statusText = "First status" });

        // Update to new status
        var response = await client.PutAsJsonAsync("/me/status", new
        {
            statusText = "Updated status",
            statusEmoji = "\U0001F680"  // rocket emoji
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated status", body.GetProperty("statusText").GetString());
        Assert.Equal("\U0001F680", body.GetProperty("statusEmoji").GetString());
    }

    [Fact]
    public async Task SetStatus_Unauthenticated_Returns401()
    {
        var client = Factory.CreateClient();
        var response = await client.PutAsJsonAsync("/me/status", new { statusText = "Hello" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClearStatus_ThenSetNew_Succeeds()
    {
        var client = CreateClient("status-cycle-1", "StatusCycleUser");

        // Set, clear, set again
        await client.PutAsJsonAsync("/me/status", new { statusText = "First" });
        await client.DeleteAsync("/me/status");
        var response = await client.PutAsJsonAsync("/me/status", new { statusText = "Second" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Second", body.GetProperty("statusText").GetString());
    }
}
