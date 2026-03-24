using System.Text.RegularExpressions;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Codec.Api.Filters;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages messages within channels.
/// </summary>
[ApiController]
[Authorize]
[RequireEmailVerified]
[Route("channels")]
public partial class ChannelsController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub, IAvatarService avatarService, IServiceScopeFactory scopeFactory, MessageCacheService messageCache, PushNotificationService? pushService = null) : ControllerBase
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    /// <summary>
    /// Returns messages for a channel, ordered by creation time (ascending).
    /// Supports cursor-based pagination via the <c>before</c> and <c>limit</c> query parameters.
    /// When <c>before</c> is supplied, only messages created before that timestamp are returned.
    /// The response includes a <c>hasMore</c> flag indicating whether older messages exist.
    /// Requires server membership.
    /// </summary>
    [HttpGet("{channelId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid channelId, [FromQuery] DateTimeOffset? before = null, [FromQuery] Guid? around = null, [FromQuery] int limit = 100)
    {
        // Clamp limit to a safe range to prevent abuse.
        limit = Math.Clamp(limit, 1, 200);

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        // Cache-first path for paginated history (not around-mode, not latest page).
        // Skip caching the latest page (before == null) since new messages invalidate it immediately.
        if (!around.HasValue && before.HasValue)
        {
            var cached = await messageCache.GetMessagesAsync(channelId, before, limit);
            if (cached is not null)
            {
                return Content(cached, "application/json");
            }
        }

        // --- Around-message mode: load messages centered on a target message ---
        if (around.HasValue)
        {
            var targetMessage = await db.Messages
                .AsNoTracking()
                .Where(m => m.Id == around.Value && m.ChannelId == channelId)
                .Select(m => new
                {
                    m.Id,
                    m.AuthorName,
                    m.AuthorUserId,
                    m.Body,
                    m.ImageUrl,
                    m.CreatedAt,
                    m.EditedAt,
                    m.ChannelId,
                    m.ReplyToMessageId,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                    m.MessageType
                })
                .FirstOrDefaultAsync();

            if (targetMessage is null)
            {
                return NotFound(new { error = "Message not found." });
            }

            var half = limit / 2;

            var beforeMessages = await db.Messages
                .AsNoTracking()
                .Where(m => m.ChannelId == channelId && m.CreatedAt < targetMessage.CreatedAt)
                .OrderByDescending(m => m.CreatedAt)
                .Take(half)
                .Select(m => new
                {
                    m.Id,
                    m.AuthorName,
                    m.AuthorUserId,
                    m.Body,
                    m.ImageUrl,
                    m.CreatedAt,
                    m.EditedAt,
                    m.ChannelId,
                    m.ReplyToMessageId,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                    m.MessageType
                })
                .ToListAsync();

            beforeMessages.Reverse();

            var afterMessages = await db.Messages
                .AsNoTracking()
                .Where(m => m.ChannelId == channelId && m.CreatedAt > targetMessage.CreatedAt)
                .OrderBy(m => m.CreatedAt)
                .Take(half)
                .Select(m => new
                {
                    m.Id,
                    m.AuthorName,
                    m.AuthorUserId,
                    m.Body,
                    m.ImageUrl,
                    m.CreatedAt,
                    m.EditedAt,
                    m.ChannelId,
                    m.ReplyToMessageId,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                    m.MessageType
                })
                .ToListAsync();

            var hasMoreBefore = beforeMessages.Count == half;
            var hasMoreAfter = afterMessages.Count == half;

            var allMessages = beforeMessages
                .Append(targetMessage)
                .Concat(afterMessages)
                .ToList();

            var aroundMessageIds = allMessages.Select(m => m.Id).ToArray();

            // Batch-load reactions.
            var aroundReactionLookup = new Dictionary<Guid, IReadOnlyList<ReactionSummary>>();
            if (aroundMessageIds.Length > 0)
            {
                var reactions = await db.Reactions
                    .AsNoTracking()
                    .Where(reaction => reaction.MessageId != null && aroundMessageIds.Contains(reaction.MessageId.Value))
                    .ToListAsync();

                aroundReactionLookup = reactions
                    .GroupBy(reaction => reaction.MessageId!.Value)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<ReactionSummary>)group
                            .GroupBy(reaction => reaction.Emoji)
                            .Select(emojiGroup => new ReactionSummary(
                                emojiGroup.Key,
                                emojiGroup.Count(),
                                emojiGroup.Select(reaction => reaction.UserId).ToList()))
                            .ToList());
            }

            // Batch-load link previews.
            var aroundLinkPreviewLookup = new Dictionary<Guid, IReadOnlyList<LinkPreviewDto>>();
            if (aroundMessageIds.Length > 0)
            {
                var linkPreviews = await db.LinkPreviews
                    .AsNoTracking()
                    .Where(lp => lp.MessageId != null && aroundMessageIds.Contains(lp.MessageId.Value)
                        && lp.Status == LinkPreviewStatus.Success)
                    .ToListAsync();

                aroundLinkPreviewLookup = linkPreviews
                    .GroupBy(lp => lp.MessageId!.Value)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<LinkPreviewDto>)group
                            .Select(lp => new LinkPreviewDto(lp.Url, lp.Title, lp.Description, lp.ImageUrl, lp.SiteName, lp.CanonicalUrl))
                            .ToList());
            }

            // Batch-load mentions.
            var aroundMentionsByMessage = allMessages.ToDictionary(m => m.Id, m => ParseMentionUserIds(m.Body));
            var aroundAllMentionedIds = aroundMentionsByMessage.Values
                .SelectMany(ids => ids)
                .Distinct()
                .ToList();

            var aroundMentionUserLookup = new Dictionary<Guid, MentionDto>();
            if (aroundAllMentionedIds.Count > 0)
            {
                aroundMentionUserLookup = await db.Users
                    .AsNoTracking()
                    .Where(u => aroundAllMentionedIds.Contains(u.Id))
                    .ToDictionaryAsync(
                        u => u.Id,
                        u => new MentionDto(u.Id, string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname));
            }

            // Batch-load reply context.
            var aroundReplyToIds = allMessages
                .Where(m => m.ReplyToMessageId.HasValue)
                .Select(m => m.ReplyToMessageId!.Value)
                .Distinct()
                .ToList();

            var aroundReplyContextLookup = new Dictionary<Guid, ReplyContextDto>();
            if (aroundReplyToIds.Count > 0)
            {
                var parentMessages = await db.Messages
                    .AsNoTracking()
                    .Where(m => aroundReplyToIds.Contains(m.Id))
                    .Select(m => new
                    {
                        m.Id,
                        m.AuthorName,
                        m.AuthorUserId,
                        AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                        AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                        m.Body
                    })
                    .ToListAsync();

                aroundReplyContextLookup = parentMessages.ToDictionary(
                    p => p.Id,
                    p => new ReplyContextDto(
                        p.Id,
                        p.AuthorName,
                        avatarService.ResolveUrl(p.AuthorCustomAvatarPath) ?? p.AuthorGoogleAvatarUrl,
                        p.AuthorUserId,
                        p.Body.Length > 100 ? p.Body[..100] : p.Body,
                        false));
            }

            var aroundResponse = allMessages.Select(message =>
            {
                var mentionIds = aroundMentionsByMessage.TryGetValue(message.Id, out var cached) ? cached : [];
                var mentions = mentionIds
                    .Where(id => aroundMentionUserLookup.ContainsKey(id))
                    .Select(id => aroundMentionUserLookup[id])
                    .ToList();

                return new
                {
                    message.Id,
                    message.AuthorName,
                    message.AuthorUserId,
                    message.Body,
                    message.ImageUrl,
                    message.CreatedAt,
                    message.EditedAt,
                    message.ChannelId,
                    AuthorAvatarUrl = avatarService.ResolveUrl(message.AuthorCustomAvatarPath) ?? message.AuthorGoogleAvatarUrl,
                    Reactions = aroundReactionLookup.TryGetValue(message.Id, out var reactions)
                        ? reactions
                        : Array.Empty<ReactionSummary>(),
                    LinkPreviews = aroundLinkPreviewLookup.TryGetValue(message.Id, out var previews)
                        ? previews
                        : Array.Empty<LinkPreviewDto>(),
                    Mentions = (IReadOnlyList<MentionDto>)mentions,
                    ReplyContext = message.ReplyToMessageId.HasValue
                        ? aroundReplyContextLookup.TryGetValue(message.ReplyToMessageId.Value, out var ctx)
                            ? ctx
                            : new ReplyContextDto(message.ReplyToMessageId.Value, string.Empty, null, null, string.Empty, true)
                        : (ReplyContextDto?)null,
                    MessageType = (int)message.MessageType
                };
            });

            return Ok(new { hasMoreBefore, hasMoreAfter, messages = aroundResponse });
        }

        var query = db.Messages
            .AsNoTracking()
            .Where(message => message.ChannelId == channelId);

        if (before.HasValue)
        {
            query = query.Where(message => message.CreatedAt < before.Value);
        }

        // Fetch one extra row to determine if more messages exist beyond this page.
        var messages = await query
            .OrderByDescending(message => message.CreatedAt)
            .Take(limit + 1)
            .Select(message => new
            {
                message.Id,
                message.AuthorName,
                message.AuthorUserId,
                message.Body,
                message.ImageUrl,
                message.CreatedAt,
                message.EditedAt,
                message.ChannelId,
                message.ReplyToMessageId,
                AuthorCustomAvatarPath = message.AuthorUser != null ? message.AuthorUser.CustomAvatarPath : null,
                AuthorGoogleAvatarUrl = message.AuthorUser != null ? message.AuthorUser.AvatarUrl : null,
                message.MessageType
            })
            .ToListAsync();

        var hasMore = messages.Count > limit;
        if (hasMore)
        {
            messages = messages.Take(limit).ToList();
        }

        // Reverse to chronological order (oldest first) for the client.
        messages.Reverse();

        var messageIds = messages.Select(message => message.Id).ToArray();
        var reactionLookup = new Dictionary<Guid, IReadOnlyList<ReactionSummary>>();

        if (messageIds.Length > 0)
        {
            var reactions = await db.Reactions
                .AsNoTracking()
                .Where(reaction => reaction.MessageId != null && messageIds.Contains(reaction.MessageId.Value))
                .ToListAsync();

            reactionLookup = reactions
                .GroupBy(reaction => reaction.MessageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ReactionSummary>)group
                        .GroupBy(reaction => reaction.Emoji)
                        .Select(emojiGroup => new ReactionSummary(
                            emojiGroup.Key,
                            emojiGroup.Count(),
                            emojiGroup.Select(reaction => reaction.UserId).ToList()))
                        .ToList());
        }

        // Load link previews for all messages in a single query.
        var linkPreviewLookup = new Dictionary<Guid, IReadOnlyList<LinkPreviewDto>>();
        if (messageIds.Length > 0)
        {
            var linkPreviews = await db.LinkPreviews
                .AsNoTracking()
                .Where(lp => lp.MessageId != null && messageIds.Contains(lp.MessageId.Value)
                    && lp.Status == LinkPreviewStatus.Success)
                .ToListAsync();

            linkPreviewLookup = linkPreviews
                .GroupBy(lp => lp.MessageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<LinkPreviewDto>)group
                        .Select(lp => new LinkPreviewDto(lp.Url, lp.Title, lp.Description, lp.ImageUrl, lp.SiteName, lp.CanonicalUrl))
                        .ToList());
        }

        // Resolve mentions for all messages in a single batch query.
        // Parse once and cache per message to avoid redundant regex work in the response projection.
        var mentionsByMessage = messages.ToDictionary(m => m.Id, m => ParseMentionUserIds(m.Body));
        var allMentionedIds = mentionsByMessage.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();

        var mentionUserLookup = new Dictionary<Guid, MentionDto>();
        if (allMentionedIds.Count > 0)
        {
            mentionUserLookup = await db.Users
                .AsNoTracking()
                .Where(u => allMentionedIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => new MentionDto(u.Id, string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname));
        }

        // Batch-load referenced parent messages for reply context.
        var replyToIds = messages
            .Where(m => m.ReplyToMessageId.HasValue)
            .Select(m => m.ReplyToMessageId!.Value)
            .Distinct()
            .ToList();

        var replyContextLookup = new Dictionary<Guid, ReplyContextDto>();
        if (replyToIds.Count > 0)
        {
            var parentMessages = await db.Messages
                .AsNoTracking()
                .Where(m => replyToIds.Contains(m.Id))
                .Select(m => new
                {
                    m.Id,
                    m.AuthorName,
                    m.AuthorUserId,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                    m.Body
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

        var response = messages.Select(message =>
        {
            var mentionIds = mentionsByMessage.TryGetValue(message.Id, out var cached) ? cached : [];
            var mentions = mentionIds
                .Where(id => mentionUserLookup.ContainsKey(id))
                .Select(id => mentionUserLookup[id])
                .ToList();

            return new
            {
                message.Id,
                message.AuthorName,
                message.AuthorUserId,
                message.Body,
                message.ImageUrl,
                message.CreatedAt,
                message.EditedAt,
                message.ChannelId,
                AuthorAvatarUrl = avatarService.ResolveUrl(message.AuthorCustomAvatarPath) ?? message.AuthorGoogleAvatarUrl,
                Reactions = reactionLookup.TryGetValue(message.Id, out var reactions)
                    ? reactions
                    : Array.Empty<ReactionSummary>(),
                LinkPreviews = linkPreviewLookup.TryGetValue(message.Id, out var previews)
                    ? previews
                    : Array.Empty<LinkPreviewDto>(),
                Mentions = (IReadOnlyList<MentionDto>)mentions,
                ReplyContext = message.ReplyToMessageId.HasValue
                    ? replyContextLookup.TryGetValue(message.ReplyToMessageId.Value, out var ctx)
                        ? ctx
                        : new ReplyContextDto(message.ReplyToMessageId.Value, string.Empty, null, null, string.Empty, true)
                    : (ReplyContextDto?)null,
                MessageType = (int)message.MessageType
            };
        });

        var result = new { hasMore, messages = response };

        // Cache paginated history pages (not the latest page, which is invalidated too frequently).
        if (before.HasValue)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(result, CamelCaseJsonOptions);
            await messageCache.SetMessagesAsync(channelId, before, limit, json);
            return Content(json, "application/json");
        }

        return Ok(result);
    }

    /// <summary>
    /// Posts a new message to a channel. Requires server membership.
    /// </summary>
    [HttpPost("{channelId:guid}/messages")]
    public async Task<IActionResult> PostMessage(Guid channelId, [FromBody] CreateMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest(new { error = "Message body or image is required." });
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var authorName = userService.GetEffectiveDisplayName(appUser);
        if (string.IsNullOrWhiteSpace(authorName))
        {
            authorName = "Unknown";
        }

        // Validate reply-to reference if provided.
        ReplyContextDto? replyContext = null;
        if (request.ReplyToMessageId.HasValue)
        {
            var replyTarget = await db.Messages
                .AsNoTracking()
                .Where(m => m.Id == request.ReplyToMessageId.Value)
                .Select(m => new { m.Id, m.ChannelId, m.AuthorName, m.AuthorUserId, m.Body,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null })
                .FirstOrDefaultAsync();

            if (replyTarget is null)
            {
                return BadRequest(new { error = "The message being replied to does not exist." });
            }

            if (replyTarget.ChannelId != channelId)
            {
                return BadRequest(new { error = "The message being replied to is in a different channel." });
            }

            replyContext = new ReplyContextDto(
                replyTarget.Id,
                replyTarget.AuthorName,
                avatarService.ResolveUrl(replyTarget.AuthorCustomAvatarPath) ?? replyTarget.AuthorGoogleAvatarUrl,
                replyTarget.AuthorUserId,
                replyTarget.Body.Length > 100 ? replyTarget.Body[..100] : replyTarget.Body,
                false);
        }

        var message = new Message
        {
            ChannelId = channelId,
            AuthorUserId = appUser.Id,
            AuthorName = authorName,
            Body = request.Body?.Trim() ?? string.Empty,
            ImageUrl = request.ImageUrl,
            ReplyToMessageId = request.ReplyToMessageId
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var authorAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;

        // Resolve mention tokens in the message body.
        var mentionIds = ParseMentionUserIds(message.Body);
        var mentions = new List<MentionDto>();
        if (mentionIds.Count > 0)
        {
            mentions = await db.Users
                .AsNoTracking()
                .Where(u => mentionIds.Contains(u.Id))
                .Select(u => new MentionDto(u.Id, string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname))
                .ToListAsync();
        }

        var payload = new
        {
            message.Id,
            message.AuthorName,
            message.AuthorUserId,
            message.Body,
            message.ImageUrl,
            message.CreatedAt,
            message.EditedAt,
            message.ChannelId,
            AuthorAvatarUrl = authorAvatarUrl,
            Reactions = Array.Empty<object>(),
            LinkPreviews = Array.Empty<object>(),
            Mentions = (IReadOnlyList<MentionDto>)mentions,
            ReplyContext = replyContext,
            MessageType = (int)message.MessageType
        };

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ReceiveMessage", payload);
        await messageCache.InvalidateChannelAsync(channelId);

        // Notify each mentioned user who is a member of this server.
        var notifiedUserIds = new HashSet<Guid> { appUser.Id }; // skip author

        foreach (var mention in mentions)
        {
            if (notifiedUserIds.Contains(mention.UserId)) continue;

            var isMentionedMember = await userService.IsMemberAsync(channel.ServerId, mention.UserId);
            if (isMentionedMember)
            {
                await chatHub.Clients.Group($"user-{mention.UserId}").SendAsync("MentionReceived", new
                {
                    message.Id,
                    message.ChannelId,
                    channel.ServerId,
                    AuthorName = message.AuthorName,
                    message.Body
                });
                notifiedUserIds.Add(mention.UserId);
            }
        }

        // Handle @here: notify all server members who haven't been individually mentioned.
        if (message.Body.Contains("<@here>", StringComparison.OrdinalIgnoreCase))
        {
            var serverMemberIds = await db.ServerMembers
                .AsNoTracking()
                .Where(sm => sm.ServerId == channel.ServerId)
                .Select(sm => sm.UserId)
                .ToListAsync();

            foreach (var memberId in serverMemberIds)
            {
                if (notifiedUserIds.Contains(memberId)) continue;
                notifiedUserIds.Add(memberId);

                await chatHub.Clients.Group($"user-{memberId}").SendAsync("MentionReceived", new
                {
                    message.Id,
                    message.ChannelId,
                    channel.ServerId,
                    AuthorName = message.AuthorName,
                    message.Body
                });
            }
        }

        // Send push notifications to all mentioned users (excluding the author).
        if (pushService is not null && notifiedUserIds.Count > 1) // > 1 because author is always in the set
        {
            var truncatedBody = message.Body.Length > 200 ? message.Body[..200] + "…" : message.Body;
            var mentionedIds = notifiedUserIds.Where(id => id != appUser.Id);
            _ = pushService.SendToUsersAsync(mentionedIds, new PushPayload
            {
                Type = "mention",
                Title = $"{message.AuthorName} mentioned you in #{channel.Name}",
                Body = truncatedBody,
                Tag = $"mention-{channel.Id}",
                Url = "/"
            });
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

                var previews = new List<LinkPreview>();
                foreach (var url in urls)
                {
                    var result = await previewService.FetchMetadataAsync(url);
                    var preview = new LinkPreview
                    {
                        MessageId = messageId,
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
                        preview.Status = LinkPreviewStatus.Success;
                    }
                    else
                    {
                        preview.Status = LinkPreviewStatus.Failed;
                    }

                    previews.Add(preview);
                }

                scopedDb.LinkPreviews.AddRange(previews);
                await scopedDb.SaveChangesAsync();

                // Invalidate message cache so subsequent reads include link previews.
                await messageCache.InvalidateChannelAsync(channelId);

                var successPreviews = previews
                    .Where(p => p.Status == LinkPreviewStatus.Success)
                    .Select(p => new LinkPreviewDto(p.Url, p.Title, p.Description, p.ImageUrl, p.SiteName, p.CanonicalUrl))
                    .ToList();

                if (successPreviews.Count > 0)
                {
                    await hub.Clients.Group(channelId.ToString()).SendAsync("LinkPreviewsReady", new
                    {
                        MessageId = messageId,
                        ChannelId = channelId,
                        LinkPreviews = successPreviews
                    });
                }
            }
            catch
            {
                // Link preview failures must never affect message delivery.
            }
        });

        return Created($"/channels/{channelId}/messages/{message.Id}", payload);
    }

    /// <summary>
    /// Deletes a message. Only the author of the message can delete it. Requires server membership.
    /// Cascade-deletes associated reactions and link previews. Replies referencing this message
    /// will have their <c>ReplyToMessageId</c> set to <c>null</c> automatically.
    /// </summary>
    [HttpDelete("{channelId:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid channelId, Guid messageId, [FromServices] AuditService audit)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChannelId == channelId);
        if (message is null)
        {
            return NotFound(new { error = "Message not found." });
        }

        if (message.AuthorUserId != appUser.Id && !appUser.IsGlobalAdmin)
        {
            throw new Codec.Api.Services.Exceptions.ForbiddenException("You can only delete your own messages.");
        }

        var isAdminDelete = message.AuthorUserId != appUser.Id;
        var authorName = message.AuthorName;
        var channelName = await db.Channels
            .AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

        db.Messages.Remove(message);
        if (isAdminDelete)
        {
            audit.Log(channel.ServerId, appUser.Id, AuditAction.MessageDeletedByAdmin,
                targetType: "Message", targetId: messageId.ToString(),
                details: $"Message by {authorName} in #{channelName}");
        }
        await db.SaveChangesAsync();

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("MessageDeleted", new
        {
            MessageId = messageId,
            ChannelId = channelId
        });
        await messageCache.InvalidateChannelAsync(channelId);

        return NoContent();
    }

    /// <summary>
    /// Deletes all messages in a channel. Requires global admin.
    /// Cascade-deletes associated reactions and link previews.
    /// </summary>
    [HttpDelete("{channelId:guid}/messages")]
    public async Task<IActionResult> PurgeChannelMessages(Guid channelId, [FromServices] AuditService audit)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        if (!appUser.IsGlobalAdmin)
        {
            throw new Codec.Api.Services.Exceptions.ForbiddenException();
        }

        await db.LinkPreviews
            .Where(lp => lp.Message!.ChannelId == channelId)
            .ExecuteDeleteAsync();

        await db.Reactions
            .Where(r => r.Message!.ChannelId == channelId)
            .ExecuteDeleteAsync();

        await db.Messages
            .Where(m => m.ChannelId == channelId)
            .ExecuteDeleteAsync();

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ChannelPurged", new
        {
            ChannelId = channelId
        });
        await messageCache.InvalidateChannelAsync(channelId);

        audit.Log(channel.ServerId, appUser.Id, AuditAction.ChannelPurged,
            targetType: "Channel", targetId: channelId.ToString(),
            details: channel.Name);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Edits a message body. Only the author of the message can edit it. Requires server membership.
    /// Sets the <c>EditedAt</c> timestamp to the current UTC time.
    /// </summary>
    [HttpPut("{channelId:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> EditMessage(Guid channelId, Guid messageId, [FromBody] EditMessageRequest request)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChannelId == channelId);
        if (message is null)
        {
            return NotFound(new { error = "Message not found." });
        }

        if (message.AuthorUserId != appUser.Id)
        {
            throw new Codec.Api.Services.Exceptions.ForbiddenException("You can only edit your own messages.");
        }

        message.Body = request.Body.Trim();
        message.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("MessageEdited", new
        {
            MessageId = messageId,
            ChannelId = channelId,
            Body = message.Body,
            EditedAt = message.EditedAt
        });
        await messageCache.InvalidateChannelAsync(channelId);

        return Ok(new { message.Id, message.Body, message.EditedAt });
    }

    /// <summary>
    /// Toggles an emoji reaction on a message. If the user has already reacted with
    /// the given emoji, it is removed; otherwise it is added. Requires server membership.
    /// </summary>
    [HttpPost("{channelId:guid}/messages/{messageId:guid}/reactions")]
    public async Task<IActionResult> ToggleReaction(Guid channelId, Guid messageId, [FromBody] ToggleReactionRequest request)
    {
        var emoji = request.Emoji.Trim();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var messageExists = await db.Messages.AnyAsync(m => m.Id == messageId && m.ChannelId == channelId);
        if (!messageExists)
        {
            return NotFound(new { error = "Message not found." });
        }

        var existing = await db.Reactions.FirstOrDefaultAsync(
            r => r.MessageId == messageId && r.UserId == appUser.Id && r.Emoji == emoji);

        string action;
        if (existing is not null)
        {
            db.Reactions.Remove(existing);
            action = "removed";
        }
        else
        {
            db.Reactions.Add(new Reaction
            {
                MessageId = messageId,
                UserId = appUser.Id,
                Emoji = emoji
            });
            action = "added";
        }

        await db.SaveChangesAsync();

        // Build updated reaction summary for this message.
        var updatedReactions = await db.Reactions
            .AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .GroupBy(r => r.Emoji)
            .Select(g => new
            {
                Emoji = g.Key,
                Count = g.Count(),
                UserIds = g.Select(r => r.UserId).ToList()
            })
            .ToListAsync();

        var reactionPayload = new
        {
            MessageId = messageId,
            ChannelId = channelId,
            Reactions = updatedReactions
        };

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ReactionUpdated", reactionPayload);
        await messageCache.InvalidateChannelAsync(channelId);

        return Ok(new { action, reactions = updatedReactions });
    }

    /// <summary>
    /// List pinned messages for a channel, ordered by most recently pinned.
    /// </summary>
    [HttpGet("{channelId:guid}/pins")]
    public async Task<IActionResult> GetPinnedMessages(Guid channelId)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return NotFound(new { error = "Channel not found." });

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var pins = await db.PinnedMessages
            .AsNoTracking()
            .Where(p => p.ChannelId == channelId)
            .OrderByDescending(p => p.PinnedAt)
            .Include(p => p.Message)
                .ThenInclude(m => m!.AuthorUser)
            .Include(p => p.PinnedByUser)
            .ToListAsync();

        var pinMessageIds = pins.Select(p => p.MessageId).ToArray();

        var reactionLookup = new Dictionary<Guid, IReadOnlyList<ReactionSummary>>();
        if (pinMessageIds.Length > 0)
        {
            var reactions = await db.Reactions.AsNoTracking()
                .Where(r => r.MessageId != null && pinMessageIds.Contains(r.MessageId.Value))
                .ToListAsync();
            reactionLookup = reactions
                .GroupBy(r => r.MessageId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<ReactionSummary>)g
                        .GroupBy(r => r.Emoji)
                        .Select(eg => new ReactionSummary(eg.Key, eg.Count(), eg.Select(r => r.UserId).ToList()))
                        .ToList());
        }

        var linkPreviewLookup = new Dictionary<Guid, IReadOnlyList<LinkPreviewDto>>();
        if (pinMessageIds.Length > 0)
        {
            var linkPreviews = await db.LinkPreviews.AsNoTracking()
                .Where(lp => lp.MessageId != null && pinMessageIds.Contains(lp.MessageId.Value)
                    && lp.Status == LinkPreviewStatus.Success)
                .ToListAsync();
            linkPreviewLookup = linkPreviews
                .GroupBy(lp => lp.MessageId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<LinkPreviewDto>)g
                        .Select(lp => new LinkPreviewDto(lp.Url, lp.Title, lp.Description, lp.ImageUrl, lp.SiteName, lp.CanonicalUrl))
                        .ToList());
        }

        var result = pins.Select(p =>
        {
            var msg = p.Message!;
            return new
            {
                messageId = p.MessageId,
                channelId = p.ChannelId,
                pinnedBy = new
                {
                    userId = p.PinnedByUserId,
                    displayName = p.PinnedByUser is not null ? userService.GetEffectiveDisplayName(p.PinnedByUser) : "Unknown"
                },
                pinnedAt = p.PinnedAt,
                message = new
                {
                    id = msg.Id,
                    authorName = msg.AuthorName,
                    authorUserId = msg.AuthorUserId,
                    body = msg.Body,
                    imageUrl = msg.ImageUrl,
                    createdAt = msg.CreatedAt,
                    editedAt = msg.EditedAt,
                    channelId = msg.ChannelId,
                    authorAvatarUrl = avatarService.ResolveUrl(msg.AuthorUser?.CustomAvatarPath) ?? msg.AuthorUser?.AvatarUrl,
                    reactions = reactionLookup.TryGetValue(msg.Id, out var reactions) ? reactions : Array.Empty<ReactionSummary>(),
                    linkPreviews = linkPreviewLookup.TryGetValue(msg.Id, out var previews) ? previews : Array.Empty<LinkPreviewDto>(),
                    mentions = Array.Empty<object>(),
                    replyContext = (object?)null,
                    messageType = (int)msg.MessageType
                }
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Pin a message. Requires Owner/Admin role or GlobalAdmin.
    /// </summary>
    [HttpPost("{channelId:guid}/pins/{messageId:guid}")]
    public async Task<IActionResult> PinMessage(Guid channelId, Guid messageId, [FromServices] AuditService audit)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return NotFound(new { error = "Channel not found." });

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId && m.ChannelId == channelId);
        if (message is null) return NotFound(new { error = "Message not found." });

        var alreadyPinned = await db.PinnedMessages.AnyAsync(p => p.ChannelId == channelId && p.MessageId == messageId);
        if (alreadyPinned) return BadRequest(new { error = "Message is already pinned." });

        var pinCount = await db.PinnedMessages.CountAsync(p => p.ChannelId == channelId);
        if (pinCount >= 50) return BadRequest(new { error = "This channel has reached the maximum of 50 pinned messages." });

        var pin = new PinnedMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            ChannelId = channelId,
            PinnedByUserId = appUser.Id,
            PinnedAt = DateTimeOffset.UtcNow
        };
        db.PinnedMessages.Add(pin);

        var authorName = userService.GetEffectiveDisplayName(appUser);
        var systemMessage = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorUserId = null,
            AuthorName = "System",
            Body = $"{authorName} pinned a message to this channel.",
            MessageType = MessageType.PinNotification,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Messages.Add(systemMessage);

        audit.Log(channel.ServerId, appUser.Id, AuditAction.MessagePinned, "Message", messageId.ToString(),
            $"Pinned message {messageId} in #{channel.Name}");

        await db.SaveChangesAsync();

        var displayName = userService.GetEffectiveDisplayName(appUser);
        await chatHub.Clients.Group($"channel-{channelId}").SendAsync("MessagePinned", new
        {
            messageId,
            channelId,
            pinnedBy = new { userId = appUser.Id, displayName },
            pinnedAt = pin.PinnedAt
        });

        await chatHub.Clients.Group($"channel-{channelId}").SendAsync("ReceiveMessage", new
        {
            id = systemMessage.Id,
            channelId,
            authorUserId = (Guid?)null,
            authorName = "System",
            authorAvatarUrl = (string?)null,
            body = systemMessage.Body,
            imageUrl = (string?)null,
            createdAt = systemMessage.CreatedAt,
            editedAt = (DateTimeOffset?)null,
            reactions = Array.Empty<object>(),
            linkPreviews = Array.Empty<object>(),
            mentions = Array.Empty<object>(),
            replyContext = (object?)null,
            messageType = (int)MessageType.PinNotification
        });

        await messageCache.InvalidateChannelAsync(channelId);

        return Created($"/channels/{channelId}/pins/{messageId}", new
        {
            messageId,
            channelId,
            pinnedBy = new { userId = appUser.Id, displayName },
            pinnedAt = pin.PinnedAt
        });
    }

    /// <summary>
    /// Unpin a message. Requires Owner/Admin role or GlobalAdmin.
    /// </summary>
    [HttpDelete("{channelId:guid}/pins/{messageId:guid}")]
    public async Task<IActionResult> UnpinMessage(Guid channelId, Guid messageId, [FromServices] AuditService audit)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return NotFound(new { error = "Channel not found." });

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var pin = await db.PinnedMessages.FirstOrDefaultAsync(p => p.ChannelId == channelId && p.MessageId == messageId);
        if (pin is null) return NotFound(new { error = "Message is not pinned." });

        db.PinnedMessages.Remove(pin);

        audit.Log(channel.ServerId, appUser.Id, AuditAction.MessageUnpinned, "Message", messageId.ToString(),
            $"Unpinned message {messageId} in #{channel.Name}");

        await db.SaveChangesAsync();

        var displayName = userService.GetEffectiveDisplayName(appUser);
        await chatHub.Clients.Group($"channel-{channelId}").SendAsync("MessageUnpinned", new
        {
            messageId,
            channelId,
            unpinnedBy = new { userId = appUser.Id, displayName }
        });

        return NoContent();
    }

    /// <summary>
    /// Parses <c>&lt;@guid&gt;</c> mention tokens from a message body and returns
    /// the distinct set of referenced user IDs.
    /// </summary>
    private static List<Guid> ParseMentionUserIds(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var matches = MentionRegex().Matches(body);
        var ids = new HashSet<Guid>();
        foreach (Match match in matches)
        {
            if (Guid.TryParse(match.Groups[1].Value, out var userId))
            {
                ids.Add(userId);
            }
        }
        return [.. ids];
    }

    [GeneratedRegex(@"<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>")]
    private static partial Regex MentionRegex();

    private sealed record ReactionSummary(string Emoji, int Count, IReadOnlyList<Guid> UserIds);
    private sealed record LinkPreviewDto(string Url, string? Title, string? Description, string? ImageUrl, string? SiteName, string? CanonicalUrl);
    private sealed record MentionDto(Guid UserId, string DisplayName);

    /// <summary>
    /// Lightweight reply context describing the original message being replied to.
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
