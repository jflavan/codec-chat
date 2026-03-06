namespace Codec.Api.Models;

public class Reaction
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? DirectMessageId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Message? Message { get; set; }
    public DirectMessage? DirectMessage { get; set; }
    public User? User { get; set; }
}
