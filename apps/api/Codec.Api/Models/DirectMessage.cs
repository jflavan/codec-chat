namespace Codec.Api.Models;

/// <summary>
/// An individual message within a DM conversation.
/// Follows the same shape as the existing <see cref="Message"/> entity.
/// </summary>
public class DirectMessage
{
    public Guid Id { get; set; }
    public Guid DmChannelId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DmChannel? DmChannel { get; set; }
    public User? AuthorUser { get; set; }
    public List<LinkPreview> LinkPreviews { get; set; } = new();
}
