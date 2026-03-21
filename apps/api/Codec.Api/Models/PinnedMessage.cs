namespace Codec.Api.Models;

public class PinnedMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? PinnedByUserId { get; set; }
    public DateTimeOffset PinnedAt { get; set; } = DateTimeOffset.UtcNow;

    public Message? Message { get; set; }
    public Channel? Channel { get; set; }
    public User? PinnedByUser { get; set; }
}
