namespace Codec.Api.Models;

/// <summary>
/// Request body for sending a friend request.
/// </summary>
public record SendFriendRequestRequest(Guid RecipientUserId);
