namespace Codec.Api.Models;

public class Channel
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Text (default) or Voice channel.</summary>
    public ChannelType Type { get; set; } = ChannelType.Text;
    public Server? Server { get; set; }
    public List<Message> Messages { get; set; } = new();
}
