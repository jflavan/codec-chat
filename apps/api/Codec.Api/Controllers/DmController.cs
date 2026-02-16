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
/// Manages direct message conversations and messages between users.
/// </summary>
[ApiController]
[Authorize]
[Route("dm")]
public class DmController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub, IAvatarService avatarService, IServiceScopeFactory scopeFactory) : ControllerBase
{
    /// <summary>
    /// Creates a new DM channel between the current user and the specified recipient,
    /// or returns the existing channel if one already exists. Requires an accepted friendship.
    /// </summary>
    [HttpPost("channels")]
    public async Task<IActionResult> CreateOrResumeChannel([FromBody] CreateDmChannelRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (request.RecipientUserId == appUser.Id)
        {
            return BadRequest(new { error = "You cannot start a DM conversation with yourself." });
        }

        var recipient = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.RecipientUserId);

        if (recipient is null)
        {
            return NotFound(new { error = "Recipient user not found." });
        }

        // Verify accepted friendship between the two users.
        var areFriends = await db.Friendships.AsNoTracking().AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == appUser.Id && f.RecipientId == request.RecipientUserId) ||
             (f.RequesterId == request.RecipientUserId && f.RecipientId == appUser.Id)));

        if (!areFriends)
        {
            return StatusCode(403, new { error = "You must be friends to start a DM conversation." });
        }

        // Check for an existing DM channel between the two users.
        var existingChannelId = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.UserId == appUser.Id)
            .Select(m => m.DmChannelId)
            .Intersect(
                db.DmChannelMembers
                    .AsNoTracking()
                    .Where(m => m.UserId == request.RecipientUserId)
                    .Select(m => m.DmChannelId)
            )
            .FirstOrDefaultAsync();

        var recipientEffectiveName = string.IsNullOrWhiteSpace(recipient.Nickname) ? recipient.DisplayName : recipient.Nickname;

        if (existingChannelId != Guid.Empty)
        {
            // Re-open the conversation for the current user if it was closed.
            var myMembership = await db.DmChannelMembers
                .FirstOrDefaultAsync(m => m.DmChannelId == existingChannelId && m.UserId == appUser.Id);

            if (myMembership is not null && !myMembership.IsOpen)
            {
                myMembership.IsOpen = true;
                await db.SaveChangesAsync();
            }

            var existingChannel = await db.DmChannels.AsNoTracking()
                .FirstAsync(c => c.Id == existingChannelId);

            var recipientAvatarUrl = avatarService.ResolveUrl(recipient.CustomAvatarPath) ?? recipient.AvatarUrl;

            return Ok(new
            {
                existingChannel.Id,
                Participant = new { Id = recipient.Id, DisplayName = recipientEffectiveName, AvatarUrl = recipientAvatarUrl },
                existingChannel.CreatedAt
            });
        }

        // Create a new DM channel with both members.
        var dmChannel = new DmChannel();

        db.DmChannels.Add(dmChannel);
        db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = appUser.Id });
        db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = request.RecipientUserId });

        await db.SaveChangesAsync();

        var avatarUrl = avatarService.ResolveUrl(recipient.CustomAvatarPath) ?? recipient.AvatarUrl;

        var payload = new
        {
            dmChannel.Id,
            Participant = new { Id = recipient.Id, DisplayName = recipientEffectiveName, AvatarUrl = avatarUrl },
            dmChannel.CreatedAt
        };

        // Notify the recipient that a DM conversation was opened.
        var appUserAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;
        var appUserEffectiveName = userService.GetEffectiveDisplayName(appUser);
        await chatHub.Clients.Group($"user-{request.RecipientUserId}")
            .SendAsync("DmConversationOpened", new
            {
                DmChannelId = dmChannel.Id,
                Participant = new { appUser.Id, DisplayName = appUserEffectiveName, AvatarUrl = appUserAvatarUrl }
            });

        return Created($"/dm/channels/{dmChannel.Id}", payload);
    }

    /// <summary>
    /// Lists the current user's open DM conversations, ordered by most recent message.
    /// </summary>
    [HttpGet("channels")]
    public async Task<IActionResult> ListChannels()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        // Get DM channel IDs where the current user has IsOpen = true.
        var openChannelIds = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.UserId == appUser.Id && m.IsOpen)
            .Select(m => m.DmChannelId)
            .ToListAsync();

        if (openChannelIds.Count == 0)
        {
            return Ok(Array.Empty<object>());
        }

        // Get the other participant for each channel.
        var otherMembers = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => openChannelIds.Contains(m.DmChannelId) && m.UserId != appUser.Id)
            .Select(m => new
            {
                m.DmChannelId,
                m.User!.Id,
                m.User.DisplayName,
                Nickname = m.User.Nickname,
                m.User.AvatarUrl,
                m.User.CustomAvatarPath
            })
            .ToListAsync();

        // Get the latest message per channel for sorting and preview.
        var latestMessages = await db.DirectMessages
            .AsNoTracking()
            .Where(m => openChannelIds.Contains(m.DmChannelId))
            .GroupBy(m => m.DmChannelId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToListAsync();

        var latestMessageLookup = latestMessages.ToDictionary(m => m.DmChannelId);

        // Get channel creation dates for channels with no messages.
        var channelDates = await db.DmChannels
            .AsNoTracking()
            .Where(c => openChannelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.CreatedAt);

        var conversations = otherMembers.Select(om =>
        {
            latestMessageLookup.TryGetValue(om.DmChannelId, out var lastMsg);
            var effectiveName = string.IsNullOrWhiteSpace(om.Nickname) ? om.DisplayName : om.Nickname;
            return new
            {
                Id = om.DmChannelId,
                Participant = new
                {
                    om.Id,
                    DisplayName = effectiveName,
                    AvatarUrl = avatarService.ResolveUrl(om.CustomAvatarPath) ?? om.AvatarUrl
                },
                LastMessage = lastMsg is not null
                    ? new { lastMsg.AuthorName, lastMsg.Body, lastMsg.CreatedAt }
                    : null,
                SortDate = lastMsg?.CreatedAt ?? channelDates.GetValueOrDefault(om.DmChannelId)
            };
        })
        .OrderByDescending(c => c.SortDate)
        .ToList();

        return Ok(conversations);
    }

    /// <summary>
    /// Returns messages in a DM conversation, ordered by creation time.
    /// Supports cursor-based pagination with <c>before</c> and <c>limit</c> parameters.
    /// </summary>
    [HttpGet("channels/{channelId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid channelId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);

        var appUser = await userService.GetOrCreateUserAsync(User);

        var isMember = await db.DmChannelMembers.AsNoTracking()
            .AnyAsync(m => m.DmChannelId == channelId && m.UserId == appUser.Id);

        if (!isMember)
        {
            var channelExists = await db.DmChannels.AsNoTracking().AnyAsync(c => c.Id == channelId);
            return channelExists
                ? StatusCode(403, new { error = "You are not a participant in this conversation." })
                : NotFound(new { error = "DM channel not found." });
        }

        var query = db.DirectMessages
            .AsNoTracking()
            .Where(m => m.DmChannelId == channelId);

        if (before.HasValue)
        {
            query = query.Where(m => m.CreatedAt < before.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit + 1)
            .Select(m => new
            {
                m.Id,
                m.DmChannelId,
                m.AuthorUserId,
                m.AuthorName,
                m.Body,
                m.ImageUrl,
                m.CreatedAt,
                m.EditedAt,
                m.ReplyToDirectMessageId,
                AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null
            })
            .ToListAsync();

        var hasMore = messages.Count > limit;
        if (hasMore)
        {
            messages = messages.Take(limit).ToList();
        }

        // Reverse to chronological order for the client.
        messages.Reverse();

        // Load link previews for the returned messages.
        var messageIds = messages.Select(m => m.Id).ToArray();
        var linkPreviewLookup = new Dictionary<Guid, IReadOnlyList<LinkPreviewDto>>();
        if (messageIds.Length > 0)
        {
            var linkPreviews = await db.LinkPreviews
                .AsNoTracking()
                .Where(lp => lp.DirectMessageId != null && messageIds.Contains(lp.DirectMessageId.Value)
                    && lp.Status == Models.LinkPreviewStatus.Success)
                .ToListAsync();

            linkPreviewLookup = linkPreviews
                .GroupBy(lp => lp.DirectMessageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<LinkPreviewDto>)group
                        .Select(lp => new LinkPreviewDto(lp.Url, lp.Title, lp.Description, lp.ImageUrl, lp.SiteName, lp.CanonicalUrl))
                        .ToList());
        }

        // Batch-load referenced parent DMs for reply context.
        var replyToIds = messages
            .Where(m => m.ReplyToDirectMessageId.HasValue)
            .Select(m => m.ReplyToDirectMessageId!.Value)
            .Distinct()
            .ToList();

        var replyContextLookup = new Dictionary<Guid, ReplyContextDto>();
        if (replyToIds.Count > 0)
        {
            var parentMessages = await db.DirectMessages
                .AsNoTracking()
                .Where(dm => replyToIds.Contains(dm.Id))
                .Select(dm => new
                {
                    dm.Id,
                    dm.AuthorName,
                    dm.AuthorUserId,
                    AuthorCustomAvatarPath = dm.AuthorUser != null ? dm.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = dm.AuthorUser != null ? dm.AuthorUser.AvatarUrl : null,
                    dm.Body
                })
                .ToListAsync();

            replyContextLookup = parentMessages.ToDictionary(
                p => p.Id,
                p => new ReplyContextDto(
                    p.Id,
                    p.AuthorName,
                    avatarService.ResolveUrl(p.AuthorCustomAvatarPath) ?? p.AuthorGoogleAvatarUrl,
                    p.AuthorUserId,
                    p.Body.Length > 100 ? p.Body[..100] : p.Body,
                    false));
        }

        var response = messages.Select(m => new
        {
            m.Id,
            m.DmChannelId,
            m.AuthorUserId,
            m.AuthorName,
            m.Body,
            m.ImageUrl,
            m.CreatedAt,
            m.EditedAt,
            AuthorAvatarUrl = avatarService.ResolveUrl(m.AuthorCustomAvatarPath) ?? m.AuthorGoogleAvatarUrl,
            LinkPreviews = linkPreviewLookup.TryGetValue(m.Id, out var previews)
                ? previews
                : Array.Empty<LinkPreviewDto>(),
            ReplyContext = m.ReplyToDirectMessageId.HasValue
                ? replyContextLookup.TryGetValue(m.ReplyToDirectMessageId.Value, out var ctx)
                    ? ctx
                    : new ReplyContextDto(m.ReplyToDirectMessageId.Value, string.Empty, null, null, string.Empty, true)
                : (ReplyContextDto?)null
        });

        return Ok(new { hasMore, messages = response });
    }

    /// <summary>
    /// Sends a direct message to a DM conversation. Re-opens the conversation
    /// for both participants if either had closed it.
    /// </summary>
    [HttpPost("channels/{channelId:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid channelId, [FromBody] CreateMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest(new { error = "Message body or image is required." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        var members = await db.DmChannelMembers
            .Where(m => m.DmChannelId == channelId)
            .ToListAsync();

        if (members.Count == 0)
        {
            return NotFound(new { error = "DM channel not found." });
        }

        var myMembership = members.FirstOrDefault(m => m.UserId == appUser.Id);
        if (myMembership is null)
        {
            return StatusCode(403, new { error = "You are not a participant in this conversation." });
        }

        var authorName = userService.GetEffectiveDisplayName(appUser);
        if (string.IsNullOrWhiteSpace(authorName))
        {
            authorName = "Unknown";
        }

        // Validate reply-to reference if provided.
        ReplyContextDto? replyContext = null;
        if (request.ReplyToDirectMessageId.HasValue)
        {
            var replyTarget = await db.DirectMessages
                .AsNoTracking()
                .Where(dm => dm.Id == request.ReplyToDirectMessageId.Value)
                .Select(dm => new { dm.Id, dm.DmChannelId, dm.AuthorName, dm.AuthorUserId, dm.Body,
                    AuthorCustomAvatarPath = dm.AuthorUser != null ? dm.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = dm.AuthorUser != null ? dm.AuthorUser.AvatarUrl : null })
                .FirstOrDefaultAsync();

            if (replyTarget is null)
            {
                return BadRequest(new { error = "The message being replied to does not exist." });
            }

            if (replyTarget.DmChannelId != channelId)
            {
                return BadRequest(new { error = "The message being replied to is in a different DM channel." });
            }

            replyContext = new ReplyContextDto(
                replyTarget.Id,
                replyTarget.AuthorName,
                avatarService.ResolveUrl(replyTarget.AuthorCustomAvatarPath) ?? replyTarget.AuthorGoogleAvatarUrl,
                replyTarget.AuthorUserId,
                replyTarget.Body.Length > 100 ? replyTarget.Body[..100] : replyTarget.Body,
                false);
        }

        var message = new DirectMessage
        {
            DmChannelId = channelId,
            AuthorUserId = appUser.Id,
            AuthorName = authorName,
            Body = request.Body?.Trim() ?? string.Empty,
            ImageUrl = request.ImageUrl,
            ReplyToDirectMessageId = request.ReplyToDirectMessageId
        };

        db.DirectMessages.Add(message);

        // Re-open the conversation for both participants.
        foreach (var member in members)
        {
            if (!member.IsOpen)
            {
                member.IsOpen = true;
            }
        }

        await db.SaveChangesAsync();

        var authorAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;

        var payload = new
        {
            message.Id,
            message.DmChannelId,
            message.AuthorUserId,
            message.AuthorName,
            message.Body,
            message.ImageUrl,
            message.CreatedAt,
            message.EditedAt,
            AuthorAvatarUrl = authorAvatarUrl,
            LinkPreviews = Array.Empty<object>(),
            ReplyContext = replyContext
        };

        // Broadcast to the other participant via their user-scoped group.
        var otherMember = members.First(m => m.UserId != appUser.Id);
        await chatHub.Clients.Group($"user-{otherMember.UserId}")
            .SendAsync("ReceiveDm", payload);

        // Also broadcast to the DM channel group for the sender's own open tabs.
        await chatHub.Clients.Group($"dm-{channelId}")
            .SendAsync("ReceiveDm", payload);

        // If the other participant's conversation was just re-opened, notify them.
        if (!members.First(m => m.UserId == otherMember.UserId).IsOpen)
        {
            // Already re-opened above, but we check original state. Since we already
            // set IsOpen = true for all members above, this notification is sent
            // via the ReceiveDm event — the client will handle re-opening from there.
        }

        // Fire-and-forget: extract URLs and fetch link previews in the background.
        var messageId = message.Id;
        var messageBody = message.Body;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
                var previewService = scope.ServiceProvider.GetRequiredService<ILinkPreviewService>();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();

                var urls = previewService.ExtractUrls(messageBody);
                if (urls.Count == 0) return;

                var previews = new List<Models.LinkPreview>();
                foreach (var url in urls)
                {
                    var result = await previewService.FetchMetadataAsync(url);
                    var preview = new Models.LinkPreview
                    {
                        DirectMessageId = messageId,
                        Url = url,
                        FetchedAt = DateTimeOffset.UtcNow
                    };

                    if (result is not null)
                    {
                        preview.Title = result.Title;
                        preview.Description = result.Description;
                        preview.ImageUrl = result.ImageUrl;
                        preview.SiteName = result.SiteName;
                        preview.CanonicalUrl = result.CanonicalUrl;
                        preview.Status = Models.LinkPreviewStatus.Success;
                    }
                    else
                    {
                        preview.Status = Models.LinkPreviewStatus.Failed;
                    }

                    previews.Add(preview);
                }

                scopedDb.LinkPreviews.AddRange(previews);
                await scopedDb.SaveChangesAsync();

                var successPreviews = previews
                    .Where(p => p.Status == Models.LinkPreviewStatus.Success)
                    .Select(p => new LinkPreviewDto(p.Url, p.Title, p.Description, p.ImageUrl, p.SiteName, p.CanonicalUrl))
                    .ToList();

                if (successPreviews.Count > 0)
                {
                    var previewPayload = new
                    {
                        MessageId = messageId,
                        DmChannelId = channelId,
                        LinkPreviews = successPreviews
                    };

                    // Broadcast to both the DM channel group and the other user's personal group.
                    await hub.Clients.Group($"dm-{channelId}").SendAsync("LinkPreviewsReady", previewPayload);
                    await hub.Clients.Group($"user-{otherMember.UserId}").SendAsync("LinkPreviewsReady", previewPayload);
                }
            }
            catch
            {
                // Link preview failures must never affect message delivery.
            }
        });

        return Created($"/dm/channels/{channelId}/messages/{message.Id}", payload);
    }

    /// <summary>
    /// Edits a direct message body. Only the author of the message can edit it.
    /// Sets the <c>EditedAt</c> timestamp to the current UTC time.
    /// </summary>
    [HttpPut("channels/{channelId:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> EditMessage(Guid channelId, Guid messageId, [FromBody] EditMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "Message body is required." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        var isMember = await db.DmChannelMembers.AsNoTracking()
            .AnyAsync(m => m.DmChannelId == channelId && m.UserId == appUser.Id);

        if (!isMember)
        {
            var channelExists = await db.DmChannels.AsNoTracking().AnyAsync(c => c.Id == channelId);
            return channelExists
                ? StatusCode(403, new { error = "You are not a participant in this conversation." })
                : NotFound(new { error = "DM channel not found." });
        }

        var message = await db.DirectMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.DmChannelId == channelId);
        if (message is null)
        {
            return NotFound(new { error = "Message not found." });
        }

        if (message.AuthorUserId != appUser.Id)
        {
            return StatusCode(403, new { error = "You can only edit your own messages." });
        }

        message.Body = request.Body.Trim();
        message.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var otherUserId = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.DmChannelId == channelId && m.UserId != appUser.Id)
            .Select(m => m.UserId)
            .FirstOrDefaultAsync();

        var editPayload = new
        {
            MessageId = messageId,
            DmChannelId = channelId,
            Body = message.Body,
            EditedAt = message.EditedAt
        };

        await chatHub.Clients.Group($"dm-{channelId}").SendAsync("DmMessageEdited", editPayload);

        if (otherUserId != Guid.Empty)
        {
            await chatHub.Clients.Group($"user-{otherUserId}").SendAsync("DmMessageEdited", editPayload);
        }

        return Ok(new { message.Id, message.Body, message.EditedAt });
    }

    /// <summary>
    /// Deletes a direct message. Only the author of the message can delete it.
    /// Cascade-deletes associated link previews. Replies referencing this message
    /// will have their <c>ReplyToDirectMessageId</c> set to <c>null</c> automatically.
    /// </summary>
    [HttpDelete("channels/{channelId:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid channelId, Guid messageId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        var isMember = await db.DmChannelMembers.AsNoTracking()
            .AnyAsync(m => m.DmChannelId == channelId && m.UserId == appUser.Id);

        if (!isMember)
        {
            var channelExists = await db.DmChannels.AsNoTracking().AnyAsync(c => c.Id == channelId);
            return channelExists
                ? StatusCode(403, new { error = "You are not a participant in this conversation." })
                : NotFound(new { error = "DM channel not found." });
        }

        var message = await db.DirectMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.DmChannelId == channelId);
        if (message is null)
        {
            return NotFound(new { error = "Message not found." });
        }

        if (message.AuthorUserId != appUser.Id)
        {
            return StatusCode(403, new { error = "You can only delete your own messages." });
        }

        var otherUserId = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.DmChannelId == channelId && m.UserId != appUser.Id)
            .Select(m => m.UserId)
            .FirstOrDefaultAsync();

        db.DirectMessages.Remove(message);
        await db.SaveChangesAsync();

        var deletePayload = new
        {
            MessageId = messageId,
            DmChannelId = channelId
        };

        await chatHub.Clients.Group($"dm-{channelId}").SendAsync("DmMessageDeleted", deletePayload);

        if (otherUserId != Guid.Empty)
        {
            await chatHub.Clients.Group($"user-{otherUserId}").SendAsync("DmMessageDeleted", deletePayload);
        }

        return NoContent();
    }

    /// <summary>
    /// Closes a DM conversation for the current user by setting <c>IsOpen = false</c>.
    /// Messages are preserved — the conversation can be re-opened.
    /// </summary>
    [HttpDelete("channels/{channelId:guid}")]
    public async Task<IActionResult> CloseChannel(Guid channelId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        var membership = await db.DmChannelMembers
            .FirstOrDefaultAsync(m => m.DmChannelId == channelId && m.UserId == appUser.Id);

        if (membership is null)
        {
            var channelExists = await db.DmChannels.AsNoTracking().AnyAsync(c => c.Id == channelId);
            return channelExists
                ? StatusCode(403, new { error = "You are not a participant in this conversation." })
                : NotFound(new { error = "DM channel not found." });
        }

        membership.IsOpen = false;
        await db.SaveChangesAsync();

        return NoContent();
    }

    private sealed record LinkPreviewDto(string Url, string? Title, string? Description, string? ImageUrl, string? SiteName, string? CanonicalUrl);

    /// <summary>
    /// Lightweight reply context describing the original DM being replied to.
    /// </summary>
    private sealed record ReplyContextDto(
        Guid MessageId,
        string AuthorName,
        string? AuthorAvatarUrl,
        Guid? AuthorUserId,
        string BodyPreview,
        bool IsDeleted
    );
}
