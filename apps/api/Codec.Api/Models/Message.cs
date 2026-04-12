namespace Codec.Api.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? FileContentType { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public MessageType MessageType { get; set; } = MessageType.Regular;
    public string? ImportedAuthorName { get; set; }
    public string? ImportedAuthorAvatarUrl { get; set; }
    public string? ImportedDiscordUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public User? AuthorUser { get; set; }
    public Channel? Channel { get; set; }
    public List<Reaction> Reactions { get; set; } = new();
    public List<LinkPreview> LinkPreviews { get; set; } = new();
}
