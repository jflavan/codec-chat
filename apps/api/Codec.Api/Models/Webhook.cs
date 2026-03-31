namespace Codec.Api.Models;

/// <summary>
/// Represents an outgoing webhook configured for a server.
/// When subscribed events occur, the API sends an HTTP POST to the webhook URL.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }

    /// <summary>
    /// Human-readable name for this webhook (e.g. "Slack notifications").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The URL to POST event payloads to.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional secret used to sign payloads (HMAC-SHA256).
    /// Stored in plaintext; the consumer uses it to verify authenticity.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Comma-separated list of event types this webhook subscribes to.
    /// E.g. "MessageCreated,MemberJoined,ChannelCreated"
    /// </summary>
    public string EventTypes { get; set; } = string.Empty;

    /// <summary>
    /// Whether this webhook is currently active and receiving events.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Server? Server { get; set; }
    public User? CreatedByUser { get; set; }
    public List<WebhookDeliveryLog> DeliveryLogs { get; set; } = [];
}

/// <summary>
/// Supported webhook event types.
/// </summary>
public enum WebhookEventType
{
    MessageCreated,
    MessageUpdated,
    MessageDeleted,
    MemberJoined,
    MemberLeft,
    MemberRoleChanged,
    MemberRolesUpdated,
    ChannelCreated,
    ChannelUpdated,
    ChannelDeleted
}

/// <summary>
/// Records each delivery attempt for a webhook, including retries.
/// </summary>
public class WebhookDeliveryLog
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }

    /// <summary>
    /// The event type that triggered this delivery.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// JSON payload that was sent.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned by the webhook endpoint, or null if the request failed.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Error message if the delivery failed (timeout, DNS, etc.).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the delivery was considered successful (2xx status code).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Which attempt number this was (1 = first try, 2 = first retry, etc.).
    /// </summary>
    public int Attempt { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Webhook? Webhook { get; set; }
}
