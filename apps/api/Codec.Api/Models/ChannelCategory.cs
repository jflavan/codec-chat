namespace Codec.Api.Models;

public class ChannelCategory
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public Server? Server { get; set; }
    public List<Channel> Channels { get; set; } = [];
}
