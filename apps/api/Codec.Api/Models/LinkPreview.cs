using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Stores metadata fetched from a URL found in a chat message,
/// used to render a rich link preview card in the UI.
/// </summary>
public class LinkPreview
{
    public Guid Id { get; set; }

    /// <summary>Reference to a server channel message (nullable).</summary>
    public Guid? MessageId { get; set; }

    /// <summary>Reference to a DM message (nullable).</summary>
    public Guid? DirectMessageId { get; set; }

    /// <summary>The original URL found in the message body.</summary>
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Page title from og:title or the HTML title tag.</summary>
    [MaxLength(512)]
    public string? Title { get; set; }

    /// <summary>Page description from og:description or meta description.</summary>
    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>Thumbnail URL from og:image.</summary>
    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    /// <summary>Site name from og:site_name.</summary>
    [MaxLength(256)]
    public string? SiteName { get; set; }

    /// <summary>Canonical URL from og:url, used as the click-through target.</summary>
    [MaxLength(2048)]
    public string? CanonicalUrl { get; set; }

    /// <summary>When metadata was fetched.</summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>The outcome of the metadata fetch.</summary>
    public LinkPreviewStatus Status { get; set; } = LinkPreviewStatus.Pending;

    public Message? Message { get; set; }
    public DirectMessage? DirectMessage { get; set; }
}
