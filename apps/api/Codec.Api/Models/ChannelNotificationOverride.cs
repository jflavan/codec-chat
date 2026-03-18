namespace Codec.Api.Models;

public class ChannelNotificationOverride
{
    public Guid UserId { get; set; }
    public Guid ChannelId { get; set; }
    public bool IsMuted { get; set; }
    public User? User { get; set; }
    public Channel? Channel { get; set; }
}
