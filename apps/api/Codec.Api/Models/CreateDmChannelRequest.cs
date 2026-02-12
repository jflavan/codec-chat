namespace Codec.Api.Models;

/// <summary>
/// Request body for starting or resuming a DM conversation.
/// </summary>
public record CreateDmChannelRequest(Guid RecipientUserId);
