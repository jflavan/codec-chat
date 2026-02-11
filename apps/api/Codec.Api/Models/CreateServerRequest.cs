namespace Codec.Api.Models;

/// <summary>
/// Request body for creating a new server.
/// </summary>
/// <param name="Name">Display name for the server (required, max 100 characters).</param>
public record CreateServerRequest(string Name);
