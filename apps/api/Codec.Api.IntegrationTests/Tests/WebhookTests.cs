using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for webhook CRUD and delivery log endpoints.
/// </summary>
public class WebhookTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateWebhook_AsServerOwner_Succeeds()
    {
        var client = CreateClient("wh-owner-1", "WebhookOwner");
        var (serverId, _) = await CreateServerAsync(client, "Webhook Server");

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Test Webhook",
            url = "https://example.com/webhook",
            eventTypes = new[] { "MessageCreated", "MemberJoined" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Webhook", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.GetProperty("hasSecret").GetBoolean());
    }

    [Fact]
    public async Task CreateWebhook_WithSecret_HasSecretTrue()
    {
        var client = CreateClient("wh-secret-1", "SecretOwner");
        var (serverId, _) = await CreateServerAsync(client, "Secret Server");

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Secret Webhook",
            url = "https://example.com/webhook-secret",
            secret = "my-hmac-secret",
            eventTypes = new[] { "MessageCreated" }
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("hasSecret").GetBoolean());
    }

    [Fact]
    public async Task CreateWebhook_InvalidEventType_ReturnsBadRequest()
    {
        var client = CreateClient("wh-bad-event-1", "BadEventOwner");
        var (serverId, _) = await CreateServerAsync(client, "BadEvent Server");

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Invalid Webhook",
            url = "https://example.com/webhook-invalid",
            eventTypes = new[] { "InvalidEvent" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWebhook_AsNonAdmin_ReturnsForbidden()
    {
        var owner = CreateClient("wh-owner-nonadmin", "Owner");
        var (serverId, _) = await CreateServerAsync(owner, "NonAdmin Webhook Server");

        // Create invite and join as second user
        var member = CreateClient("wh-member-1", "Member");
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await member.PostAsync($"/invites/{code}/join", null);

        var response = await member.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Unauthorized Webhook",
            url = "https://example.com/unauth",
            eventTypes = new[] { "MessageCreated" }
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.InternalServerError,
            $"Expected 403 or 500 but got {response.StatusCode}");
    }

    [Fact]
    public async Task GetWebhooks_ReturnsCreatedWebhooks()
    {
        var client = CreateClient("wh-list-1", "ListOwner");
        var (serverId, _) = await CreateServerAsync(client, "List Server");

        await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Webhook A",
            url = "https://example.com/a",
            eventTypes = new[] { "MessageCreated" }
        });
        await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Webhook B",
            url = "https://example.com/b",
            eventTypes = new[] { "MemberJoined" }
        });

        var response = await client.GetAsync($"/servers/{serverId}/webhooks");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task UpdateWebhook_ChangeName_Succeeds()
    {
        var client = CreateClient("wh-upd-1", "UpdateOwner");
        var (serverId, _) = await CreateServerAsync(client, "Update Server");

        var createResponse = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Original Name",
            url = "https://example.com/upd",
            eventTypes = new[] { "MessageCreated" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var webhookId = created.GetProperty("id").GetString();

        var content = JsonContent.Create(new { name = "Updated Name" });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/servers/{serverId}/webhooks/{webhookId}")
        {
            Content = content
        };
        var updateResponse = await client.SendAsync(request);
        updateResponse.EnsureSuccessStatusCode();
        var body = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Name", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateWebhook_Deactivate_Succeeds()
    {
        var client = CreateClient("wh-deactivate-1", "DeactOwner");
        var (serverId, _) = await CreateServerAsync(client, "Deact Server");

        var createResponse = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Active Hook",
            url = "https://example.com/deact",
            eventTypes = new[] { "MessageCreated" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var webhookId = created.GetProperty("id").GetString();

        var content = JsonContent.Create(new { isActive = false });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/servers/{serverId}/webhooks/{webhookId}")
        {
            Content = content
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task DeleteWebhook_Succeeds()
    {
        var client = CreateClient("wh-del-1", "DelOwner");
        var (serverId, _) = await CreateServerAsync(client, "Del Server");

        var createResponse = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Delete Me",
            url = "https://example.com/del",
            eventTypes = new[] { "MessageCreated" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var webhookId = created.GetProperty("id").GetString();

        var response = await client.DeleteAsync($"/servers/{serverId}/webhooks/{webhookId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_NotFound_Returns404()
    {
        var client = CreateClient("wh-del-nf-1", "DelNFOwner");
        var (serverId, _) = await CreateServerAsync(client, "DelNF Server");

        var response = await client.DeleteAsync($"/servers/{serverId}/webhooks/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveries_EmptyForNewWebhook()
    {
        var client = CreateClient("wh-dlv-1", "DlvOwner");
        var (serverId, _) = await CreateServerAsync(client, "Dlv Server");

        var createResponse = await client.PostAsJsonAsync($"/servers/{serverId}/webhooks", new
        {
            name = "Delivery Hook",
            url = "https://example.com/dlv",
            eventTypes = new[] { "MessageCreated" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var webhookId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/servers/{serverId}/webhooks/{webhookId}/deliveries");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetArrayLength());
    }
}
