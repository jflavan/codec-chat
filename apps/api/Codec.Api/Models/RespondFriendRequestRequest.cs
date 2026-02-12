namespace Codec.Api.Models;

/// <summary>
/// Request body for responding to a friend request (accept or decline).
/// </summary>
public record RespondFriendRequestRequest(string Action);
