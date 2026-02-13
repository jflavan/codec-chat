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
public class ChannelsController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub, IAvatarService avatarService, IServiceScopeFactory scopeFactory) : ControllerBase
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

        var response = messages.Select(message => new
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
                : Array.Empty<LinkPreviewDto>()
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

        var message = new Message
        {
            ChannelId = channelId,
            AuthorUserId = appUser.Id,
            AuthorName = authorName,
            Body = request.Body?.Trim() ?? string.Empty,
            ImageUrl = request.ImageUrl
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var authorAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;

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
            LinkPreviews = Array.Empty<object>()
        };

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ReceiveMessage", payload);

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

    private sealed record ReactionSummary(string Emoji, int Count, IReadOnlyList<Guid> UserIds);
    private sealed record LinkPreviewDto(string Url, string? Title, string? Description, string? ImageUrl, string? SiteName, string? CanonicalUrl);
}
