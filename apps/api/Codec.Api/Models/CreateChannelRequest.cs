namespace Codec.Api.Models;

/// <summary>
/// Request body for creating a new channel within a server.
/// </summary>
/// <param name="Name">Display name for the channel (required, max 100 characters).</param>
/// <param name="Type">Channel type: "text" (default) or "voice".</param>
public record CreateChannelRequest(string Name, string? Type = null);
