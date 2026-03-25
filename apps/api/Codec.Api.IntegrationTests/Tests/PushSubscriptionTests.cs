using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for push notification subscription lifecycle:
/// subscribe, re-subscribe (upsert), unsubscribe, and VAPID key retrieval.
/// </summary>
public class PushSubscriptionTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Subscribe_CreatesSubscription()
    {
        var client = CreateClient("push-sub-1", "PushUser1");

        var response = await client.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/user1",
            p256dh = "BNcRd3456abcdef",
            auth = "auth-secret-1"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_UpsertSameEndpoint_UpdatesKeys()
    {
        var client = CreateClient("push-upsert-1", "UpsertUser");

        // First subscription
        await client.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/upsert",
            p256dh = "original-key",
            auth = "original-auth"
        });

        // Re-subscribe with same endpoint but new keys
        var response = await client.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/upsert",
            p256dh = "updated-key",
            auth = "updated-auth"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify only one subscription exists for this endpoint
        await WithDbAsync(async db =>
        {
            var count = db.PushSubscriptions.Count(s => s.Endpoint == "https://push.example.com/sub/upsert");
            Assert.Equal(1, count);
            var sub = db.PushSubscriptions.First(s => s.Endpoint == "https://push.example.com/sub/upsert");
            Assert.Equal("updated-key", sub.P256dh);
            Assert.Equal("updated-auth", sub.Auth);
        });
    }

    [Fact]
    public async Task Unsubscribe_RemovesSubscription()
    {
        var client = CreateClient("push-unsub-1", "UnsubUser");

        // Create subscription first
        await client.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/unsub",
            p256dh = "key-unsub",
            auth = "auth-unsub"
        });

        // Unsubscribe
        var request = new HttpRequestMessage(HttpMethod.Delete, "/push-subscriptions")
        {
            Content = JsonContent.Create(new { endpoint = "https://push.example.com/sub/unsub" })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        await WithDbAsync(async db =>
        {
            var exists = db.PushSubscriptions.Any(s => s.Endpoint == "https://push.example.com/sub/unsub");
            Assert.False(exists);
        });
    }

    [Fact]
    public async Task Unsubscribe_NonExistentEndpoint_ReturnsNotFound()
    {
        var client = CreateClient("push-unsub-nf-1", "UnsubNFUser");

        var request = new HttpRequestMessage(HttpMethod.Delete, "/push-subscriptions")
        {
            Content = JsonContent.Create(new { endpoint = "https://push.example.com/nonexistent" })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVapidKey_WhenNotConfigured_ReturnsNotFound()
    {
        // VAPID key is not configured in test settings
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/push-subscriptions/vapid-key");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_Unauthenticated_Returns401()
    {
        var client = Factory.CreateClient(); // no auth header
        var response = await client.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/unauth",
            p256dh = "key",
            auth = "auth"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_MultipleUsers_IndependentSubscriptions()
    {
        var client1 = CreateClient("push-multi-1", "MultiUser1");
        var client2 = CreateClient("push-multi-2", "MultiUser2");

        await client1.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/multi1",
            p256dh = "key-multi1",
            auth = "auth-multi1"
        });

        await client2.PostAsJsonAsync("/push-subscriptions", new
        {
            endpoint = "https://push.example.com/sub/multi2",
            p256dh = "key-multi2",
            auth = "auth-multi2"
        });

        // Both should exist
        await WithDbAsync(async db =>
        {
            Assert.True(db.PushSubscriptions.Any(s => s.Endpoint == "https://push.example.com/sub/multi1"));
            Assert.True(db.PushSubscriptions.Any(s => s.Endpoint == "https://push.example.com/sub/multi2"));
        });

        // User 1 can't unsubscribe user 2's endpoint (it belongs to different user)
        var request = new HttpRequestMessage(HttpMethod.Delete, "/push-subscriptions")
        {
            Content = JsonContent.Create(new { endpoint = "https://push.example.com/sub/multi2" })
        };
        var response = await client1.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
