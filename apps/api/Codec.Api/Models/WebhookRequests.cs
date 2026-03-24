using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CreateWebhookRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [Url]
    [MaxLength(2048)]
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Optional secret for HMAC-SHA256 payload signing.
    /// </summary>
    [MaxLength(256)]
    public string? Secret { get; init; }

    /// <summary>
    /// Event types to subscribe to. Must contain at least one valid event type.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] EventTypes { get; init; } = [];
}

public record UpdateWebhookRequest
{
    [MaxLength(100)]
    public string? Name { get; init; }

    [Url]
    [MaxLength(2048)]
    public string? Url { get; init; }

    [MaxLength(256)]
    public string? Secret { get; init; }

    public string[]? EventTypes { get; init; }

    public bool? IsActive { get; init; }
}
