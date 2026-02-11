namespace Codec.Api.Models;

public class Server
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Channel> Channels { get; set; } = new();
}
