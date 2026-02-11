namespace Codec.Api.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Channel? Channel { get; set; }
}
