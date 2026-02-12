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
public class ChannelsController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> chatHub) : ControllerBase
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
                message.CreatedAt,
                message.ChannelId
            })
            .ToListAsync();

        return Ok(messages);
    }

    /// <summary>
    /// Posts a new message to a channel. Requires server membership.
    /// </summary>
    [HttpPost("{channelId:guid}/messages")]
    public async Task<IActionResult> PostMessage(Guid channelId, [FromBody] CreateMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "Message body is required." });
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

        var authorName = string.IsNullOrWhiteSpace(appUser.DisplayName)
            ? "Unknown"
            : appUser.DisplayName;

        var message = new Message
        {
            ChannelId = channelId,
            AuthorUserId = appUser.Id,
            AuthorName = authorName,
            Body = request.Body.Trim()
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var payload = new
        {
            message.Id,
            message.AuthorName,
            message.AuthorUserId,
            message.Body,
            message.CreatedAt,
            message.ChannelId
        };

        await chatHub.Clients.Group(channelId.ToString()).SendAsync("ReceiveMessage", payload);

        return Created($"/channels/{channelId}/messages/{message.Id}", payload);
    }
}
