using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

/// <summary>
/// Dispatches webhook events to registered endpoints with retry logic.
/// Runs deliveries on background threads so controllers are not blocked.
/// </summary>
public class WebhookService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5)
    ];

    /// <summary>
    /// Dispatches an event to all active webhooks subscribed to it for the given server.
    /// Runs asynchronously on a background thread.
    /// </summary>
    public void DispatchEvent(Guid serverId, WebhookEventType eventType, object payload)
    {
        var eventTypeName = eventType.ToString();
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        _ = Task.Run(async () =>
        {
            try
            {
                await DispatchEventInternalAsync(serverId, eventTypeName, payloadJson);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch webhook event {EventType} for server {ServerId}",
                    eventTypeName, serverId);
            }
        });
    }

    private async Task DispatchEventInternalAsync(Guid serverId, string eventType, string payloadJson)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

        var webhooks = await db.Webhooks
            .AsNoTracking()
            .Where(w => w.ServerId == serverId && w.IsActive)
            .ToListAsync();

        var matchingWebhooks = webhooks
            .Where(w => w.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(eventType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (matchingWebhooks.Count == 0) return;

        var envelope = JsonSerializer.Serialize(new
        {
            @event = eventType,
            timestamp = DateTimeOffset.UtcNow,
            serverId,
            data = JsonSerializer.Deserialize<JsonElement>(payloadJson)
        }, JsonOptions);

        var tasks = matchingWebhooks.Select(w => DeliverWithRetryAsync(db, w, eventType, envelope));
        await Task.WhenAll(tasks);
    }

    private async Task DeliverWithRetryAsync(CodecDbContext db, Webhook webhook, string eventType, string payload)
    {
        var client = httpClientFactory.CreateClient("webhook");

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var log = new WebhookDeliveryLog
            {
                Id = Guid.NewGuid(),
                WebhookId = webhook.Id,
                EventType = eventType,
                Payload = payload,
                Attempt = attempt,
                CreatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrEmpty(webhook.Secret))
                {
                    var signature = ComputeHmacSha256(payload, webhook.Secret);
                    request.Headers.Add("X-Webhook-Signature", signature);
                }

                request.Headers.Add("X-Webhook-Event", eventType);
                request.Headers.Add("X-Webhook-Id", webhook.Id.ToString());

                using var response = await client.SendAsync(request);
                log.StatusCode = (int)response.StatusCode;
                log.Success = response.IsSuccessStatusCode;

                db.WebhookDeliveryLogs.Add(log);
                await db.SaveChangesAsync();

                if (response.IsSuccessStatusCode)
                {
                    logger.LogDebug("Webhook {WebhookId} delivered {EventType} on attempt {Attempt}",
                        webhook.Id, eventType, attempt);
                    return;
                }

                logger.LogWarning("Webhook {WebhookId} returned {StatusCode} for {EventType} on attempt {Attempt}",
                    webhook.Id, log.StatusCode, eventType, attempt);
            }
            catch (Exception ex)
            {
                log.ErrorMessage = ex.Message;
                log.Success = false;

                db.WebhookDeliveryLogs.Add(log);
                await db.SaveChangesAsync();

                logger.LogWarning(ex, "Webhook {WebhookId} delivery failed for {EventType} on attempt {Attempt}",
                    webhook.Id, eventType, attempt);
            }

            if (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelays[attempt - 1]);
            }
        }
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
