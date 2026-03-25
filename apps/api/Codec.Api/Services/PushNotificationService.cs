using System.Text.Json;
using Codec.Api.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class PushNotificationService(
    IPushClient pushClient,
    IServiceScopeFactory scopeFactory,
    ILogger<PushNotificationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Send a push notification to all active subscriptions for a user.
    /// Silently deactivates subscriptions that return 404/410 (expired/unsubscribed).
    /// </summary>
    public async Task SendToUserAsync(Guid userId, PushPayload payload, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

        var subscriptions = await db.PushSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var message = new PushMessage(json)
        {
            Urgency = payload.Type switch
            {
                "dm" or "mention" or "friend_request" or "call" => PushMessageUrgency.High,
                _ => PushMessageUrgency.Normal
            }
        };

        var deactivateIds = new List<Guid>();

        foreach (var sub in subscriptions)
        {
            var pushSub = new PushSubscription
            {
                Endpoint = sub.Endpoint
            };
            pushSub.SetKey(PushEncryptionKeyName.P256DH, sub.P256dh);
            pushSub.SetKey(PushEncryptionKeyName.Auth, sub.Auth);

            try
            {
                await pushClient.RequestPushMessageDeliveryAsync(pushSub, message, ct);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                logger.LogInformation("Push subscription {Id} expired (HTTP {Status}), deactivating", sub.Id, ex.StatusCode);
                deactivateIds.Add(sub.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send push notification to subscription {Id}", sub.Id);
            }
        }

        if (deactivateIds.Count > 0)
        {
            var toDeactivate = await db.PushSubscriptions
                .Where(s => deactivateIds.Contains(s.Id))
                .ToListAsync(ct);

            foreach (var s in toDeactivate)
                s.IsActive = false;

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Send push notifications to multiple users (e.g. all members of a channel).
    /// </summary>
    public async Task SendToUsersAsync(IEnumerable<Guid> userIds, PushPayload payload, CancellationToken ct = default)
    {
        // Limit concurrency to avoid exhausting DB connections and HTTP sockets.
        await Parallel.ForEachAsync(userIds, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
            async (id, token) => await SendToUserAsync(id, payload, token));
    }
}

/// <summary>
/// Push notification payload sent to the service worker.
/// </summary>
public record PushPayload
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Icon { get; init; }
    public string? Tag { get; init; }
    public string? Url { get; init; }
}
