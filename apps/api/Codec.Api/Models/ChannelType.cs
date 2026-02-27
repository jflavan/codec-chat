namespace Codec.Api.Models;

/// <summary>
/// Discriminates between text and voice channels within a server.
/// </summary>
public enum ChannelType
{
    Text = 0,
    Voice = 1
}
