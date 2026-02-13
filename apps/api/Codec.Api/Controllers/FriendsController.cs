using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages friend requests and friendships between users.
/// </summary>
[ApiController]
[Authorize]
[Route("friends")]
public class FriendsController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub, IAvatarService avatarService) : ControllerBase
{
    /// <summary>
    /// Lists the current user's confirmed friends.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        var friendships = await db.Friendships
            .AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == appUser.Id || f.RecipientId == appUser.Id))
            .Select(f => new
            {
                f.Id,
                f.RequesterId,
                f.RecipientId,
                f.UpdatedAt,
                RequesterDisplayName = f.Requester!.DisplayName,
                RequesterNickname = f.Requester.Nickname,
                RequesterAvatarUrl = f.Requester.AvatarUrl,
                RequesterCustomAvatarPath = f.Requester.CustomAvatarPath,
                RecipientDisplayName = f.Recipient!.DisplayName,
                RecipientNickname = f.Recipient.Nickname,
                RecipientAvatarUrl = f.Recipient.AvatarUrl,
                RecipientCustomAvatarPath = f.Recipient.CustomAvatarPath
            })
            .ToListAsync();

        var result = friendships.Select(f =>
        {
            var isRequester = f.RequesterId == appUser.Id;
            var friendId = isRequester ? f.RecipientId : f.RequesterId;
            var friendNickname = isRequester ? f.RecipientNickname : f.RequesterNickname;
            var friendGoogleName = isRequester ? f.RecipientDisplayName : f.RequesterDisplayName;
            var friendName = string.IsNullOrWhiteSpace(friendNickname) ? friendGoogleName : friendNickname;
            var friendGoogleAvatar = isRequester ? f.RecipientAvatarUrl : f.RequesterAvatarUrl;
            var friendCustomPath = isRequester ? f.RecipientCustomAvatarPath : f.RequesterCustomAvatarPath;

            return new
            {
                FriendshipId = f.Id,
                User = new
                {
                    Id = friendId,
                    DisplayName = friendName,
                    AvatarUrl = avatarService.ResolveUrl(friendCustomPath) ?? friendGoogleAvatar
                },
                Since = f.UpdatedAt
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Removes an existing friend. Deletes the friendship record.
    /// </summary>
    [HttpDelete("{friendshipId:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid friendshipId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.Status == FriendshipStatus.Accepted);

        if (friendship is null)
        {
            return NotFound(new { error = "Friendship not found." });
        }

        if (friendship.RequesterId != appUser.Id && friendship.RecipientId != appUser.Id)
        {
            return StatusCode(403, new { error = "You are not a participant in this friendship." });
        }

        var otherUserId = friendship.RequesterId == appUser.Id
            ? friendship.RecipientId
            : friendship.RequesterId;

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync();

        await chatHub.Clients.Group($"user-{otherUserId}")
            .SendAsync("FriendRemoved", new { FriendshipId = friendshipId, UserId = appUser.Id });

        return NoContent();
    }

    /// <summary>
    /// Sends a friend request to another user.
    /// </summary>
    [HttpPost("requests")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (request.RecipientUserId == appUser.Id)
        {
            return BadRequest(new { error = "You cannot send a friend request to yourself." });
        }

        var recipient = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.RecipientUserId);

        if (recipient is null)
        {
            return NotFound(new { error = "Recipient user not found." });
        }

        // Check for existing friendship in either direction.
        var existing = await db.Friendships
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == appUser.Id && f.RecipientId == request.RecipientUserId) ||
                (f.RequesterId == request.RecipientUserId && f.RecipientId == appUser.Id));

        if (existing is not null)
        {
            return Conflict(new { error = "A friendship or pending request already exists between these users." });
        }

        var friendship = new Friendship
        {
            RequesterId = appUser.Id,
            RecipientId = request.RecipientUserId
        };

        db.Friendships.Add(friendship);
        await db.SaveChangesAsync();

        var requesterAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;
        var recipientAvatarUrl = avatarService.ResolveUrl(recipient.CustomAvatarPath) ?? recipient.AvatarUrl;
        var requesterName = userService.GetEffectiveDisplayName(appUser);
        var recipientName = string.IsNullOrWhiteSpace(recipient.Nickname) ? recipient.DisplayName : recipient.Nickname;

        var payload = new
        {
            friendship.Id,
            Requester = new { appUser.Id, DisplayName = requesterName, AvatarUrl = requesterAvatarUrl },
            Recipient = new { Id = recipient.Id, DisplayName = recipientName, AvatarUrl = recipientAvatarUrl },
            Status = friendship.Status.ToString(),
            friendship.CreatedAt
        };

        await chatHub.Clients.Group($"user-{request.RecipientUserId}")
            .SendAsync("FriendRequestReceived", new
            {
                RequestId = friendship.Id,
                Requester = new { appUser.Id, DisplayName = requesterName, AvatarUrl = requesterAvatarUrl },
                friendship.CreatedAt
            });

        return Created($"/friends/requests/{friendship.Id}", payload);
    }

    /// <summary>
    /// Lists pending friend requests for the current user.
    /// </summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests([FromQuery] string? direction)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        IQueryable<Friendship> query = db.Friendships
            .AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending);

        if (string.Equals(direction, "sent", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(f => f.RequesterId == appUser.Id);
        }
        else
        {
            // Default to received.
            query = query.Where(f => f.RecipientId == appUser.Id);
        }

        var requests = await query
            .Select(f => new
            {
                f.Id,
                f.RequesterId,
                f.RecipientId,
                f.CreatedAt,
                RequesterDisplayName = f.Requester!.DisplayName,
                RequesterNickname = f.Requester.Nickname,
                RequesterAvatarUrl = f.Requester.AvatarUrl,
                RequesterCustomAvatarPath = f.Requester.CustomAvatarPath,
                RecipientDisplayName = f.Recipient!.DisplayName,
                RecipientNickname = f.Recipient.Nickname,
                RecipientAvatarUrl = f.Recipient.AvatarUrl,
                RecipientCustomAvatarPath = f.Recipient.CustomAvatarPath
            })
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        var result = requests.Select(f => new
        {
            f.Id,
            Requester = new
            {
                Id = f.RequesterId,
                DisplayName = string.IsNullOrWhiteSpace(f.RequesterNickname) ? f.RequesterDisplayName : f.RequesterNickname,
                AvatarUrl = avatarService.ResolveUrl(f.RequesterCustomAvatarPath) ?? f.RequesterAvatarUrl
            },
            Recipient = new
            {
                Id = f.RecipientId,
                DisplayName = string.IsNullOrWhiteSpace(f.RecipientNickname) ? f.RecipientDisplayName : f.RecipientNickname,
                AvatarUrl = avatarService.ResolveUrl(f.RecipientCustomAvatarPath) ?? f.RecipientAvatarUrl
            },
            Status = "Pending",
            f.CreatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Responds to a friend request (accept or decline). Only the recipient may respond.
    /// </summary>
    [HttpPut("requests/{requestId:guid}")]
    public async Task<IActionResult> RespondToRequest(Guid requestId, [FromBody] RespondFriendRequestRequest request)
    {
        if (!string.Equals(request.Action, "accept", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Action, "decline", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Action must be 'accept' or 'decline'." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        var friendship = await db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Recipient)
            .FirstOrDefaultAsync(f => f.Id == requestId && f.Status == FriendshipStatus.Pending);

        if (friendship is null)
        {
            return NotFound(new { error = "Friend request not found or is not pending." });
        }

        if (friendship.RecipientId != appUser.Id)
        {
            return StatusCode(403, new { error = "Only the recipient can respond to this request." });
        }

        var isAccept = string.Equals(request.Action, "accept", StringComparison.OrdinalIgnoreCase);
        friendship.Status = isAccept ? FriendshipStatus.Accepted : FriendshipStatus.Declined;
        friendship.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        var requester = friendship.Requester!;
        var recipient = friendship.Recipient!;
        var requesterAvatarUrl = avatarService.ResolveUrl(requester.CustomAvatarPath) ?? requester.AvatarUrl;
        var recipientAvatarUrl = avatarService.ResolveUrl(recipient.CustomAvatarPath) ?? recipient.AvatarUrl;
        var requesterEffectiveName = string.IsNullOrWhiteSpace(requester.Nickname) ? requester.DisplayName : requester.Nickname;
        var recipientEffectiveName = string.IsNullOrWhiteSpace(recipient.Nickname) ? recipient.DisplayName : recipient.Nickname;

        if (isAccept)
        {
            await chatHub.Clients.Group($"user-{friendship.RequesterId}")
                .SendAsync("FriendRequestAccepted", new
                {
                    FriendshipId = friendship.Id,
                    User = new { Id = appUser.Id, DisplayName = recipientEffectiveName, AvatarUrl = recipientAvatarUrl },
                    Since = friendship.UpdatedAt
                });
        }
        else
        {
            await chatHub.Clients.Group($"user-{friendship.RequesterId}")
                .SendAsync("FriendRequestDeclined", new { RequestId = friendship.Id });
        }

        return Ok(new
        {
            friendship.Id,
            Requester = new { Id = requester.Id, DisplayName = requesterEffectiveName, AvatarUrl = requesterAvatarUrl },
            Recipient = new { Id = recipient.Id, DisplayName = recipientEffectiveName, AvatarUrl = recipientAvatarUrl },
            Status = friendship.Status.ToString(),
            friendship.CreatedAt,
            friendship.UpdatedAt
        });
    }

    /// <summary>
    /// Cancels a pending friend request. Only the requester may cancel.
    /// </summary>
    [HttpDelete("requests/{requestId:guid}")]
    public async Task<IActionResult> CancelRequest(Guid requestId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == requestId && f.Status == FriendshipStatus.Pending);

        if (friendship is null)
        {
            return NotFound(new { error = "Friend request not found or is not pending." });
        }

        if (friendship.RequesterId != appUser.Id)
        {
            return StatusCode(403, new { error = "Only the requester can cancel this request." });
        }

        var recipientId = friendship.RecipientId;
        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync();

        await chatHub.Clients.Group($"user-{recipientId}")
            .SendAsync("FriendRequestCancelled", new { RequestId = requestId });

        return NoContent();
    }
}
