using System.Text.RegularExpressions;
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
/// Manages messages within channels.
/// </summary>
[ApiController]
[Authorize]
[Route("channels")]
public partial class ChannelsController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub, IAvatarService avatarService, IServiceScopeFactory scopeFactory) : ControllerBase
{
    /// <summary>
    /// Returns messages for a channel, ordered by creation time. Requires server membership.
    /// </summary>
    [HttpGet("{channelId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid channelId)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(channel.ServerId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

        var messages = await db.Messages
            .AsNoTracking()
            .Where(message => message.ChannelId == channelId)
            .OrderBy(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.AuthorName,
                message.AuthorUserId,
                message.Body,
                message.ImageUrl,
                message.CreatedAt,
                message.ChannelId,
                message.ReplyToMessageId,
                AuthorCustomAvatarPath = message.AuthorUser != null ? message.AuthorUser.CustomAvatarPath : null,
                AuthorGoogleAvatarUrl = message.AuthorUser != null ? message.AuthorUser.AvatarUrl : null
            })
            .ToListAsync();

        var messageIds = messages.Select(message => message.Id).ToArray();
        var reactionLookup = new Dictionary<Guid, IReadOnlyList<ReactionSummary>>();

        if (messageIds.Length > 0)
        {
            var reactions = await db.Reactions
                .AsNoTracking()
                .Where(reaction => messageIds.Contains(reaction.MessageId))
                .ToListAsync();

            reactionLookup = reactions
                .GroupBy(reaction => reaction.MessageId)
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
        var allMentionedIds = messages
            .SelectMany(m => ParseMentionUserIds(m.Body))
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
            var mentionIds = ParseMentionUserIds(message.Body);
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
                    : (ReplyContextDto?)null
            };
        });

        return Ok(response);
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

        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(channel.ServerId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

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
            message.ChannelId,
            AuthorAvatarUrl = authorAvatarUrl,
            Reactions = Array.Empty<object>(),
            LinkPreviews = Array.Empty<object>(),
            Mentions = (IReadOnlyList<MentionDto>)mentions,
            ReplyContext = replyContext
        };

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ReceiveMessage", payload);

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
    public async Task<IActionResult> DeleteMessage(Guid channelId, Guid messageId)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(channel.ServerId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChannelId == channelId);
        if (message is null)
        {
            return NotFound(new { error = "Message not found." });
        }

        if (message.AuthorUserId != appUser.Id)
        {
            return StatusCode(403, new { error = "You can only delete your own messages." });
        }

        db.Messages.Remove(message);
        await db.SaveChangesAsync();

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("MessageDeleted", new
        {
            MessageId = messageId,
            ChannelId = channelId
        });

        return NoContent();
    }

    /// <summary>
    /// Toggles an emoji reaction on a message. If the user has already reacted with
    /// the given emoji, it is removed; otherwise it is added. Requires server membership.
    /// </summary>
    [HttpPost("{channelId:guid}/messages/{messageId:guid}/reactions")]
    public async Task<IActionResult> ToggleReaction(Guid channelId, Guid messageId, [FromBody] ToggleReactionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Emoji))
        {
            return BadRequest(new { error = "Emoji is required." });
        }

        var emoji = request.Emoji.Trim();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(channel.ServerId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

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

        return Ok(new { action, reactions = updatedReactions });
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
