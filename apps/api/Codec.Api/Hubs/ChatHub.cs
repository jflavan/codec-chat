using Codec.Api.Data;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Hubs;

/// <summary>
/// SignalR hub for real-time chat features including message delivery, typing indicators,
/// and friend-related events. Clients join channel-scoped, user-scoped, and server-scoped groups.
/// </summary>
[Authorize]
public class ChatHub(IUserService userService, CodecDbContext db) : Hub
{
    /// <summary>
    /// Called when a client connects. Automatically joins the user-scoped group
    /// (<c>user-{userId}</c>) and all server-scoped groups (<c>server-{serverId}</c>)
    /// so the client receives real-time membership events.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{appUser.Id}");

        var serverIds = await db.ServerMembers
            .AsNoTracking()
            .Where(m => m.UserId == appUser.Id)
            .Select(m => m.ServerId)
            .ToListAsync();

        foreach (var serverId in serverIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"server-{serverId}");
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Adds the caller to a server-scoped group so they receive membership events.
    /// Called after joining a new server.
    /// </summary>
    public async Task JoinServer(string serverId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"server-{serverId}");
    }

    /// <summary>
    /// Removes the caller from a server-scoped group.
    /// Called after being kicked or leaving a server.
    /// </summary>
    public async Task LeaveServer(string serverId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"server-{serverId}");
    }
    /// <summary>
    /// Adds the caller to a SignalR group scoped to <paramref name="channelId"/>
    /// so they receive real-time messages for that channel.
    /// </summary>
    public async Task JoinChannel(string channelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, channelId);
    }

    /// <summary>
    /// Removes the caller from the channel group.
    /// </summary>
    public async Task LeaveChannel(string channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channelId);
    }

    /// <summary>
    /// Broadcasts a typing indicator to other users in the channel.
    /// </summary>
    public async Task StartTyping(string channelId, string displayName)
    {
        await Clients.OthersInGroup(channelId).SendAsync("UserTyping", channelId, displayName);
    }

    /// <summary>
    /// Clears the typing indicator for other users in the channel.
    /// </summary>
    public async Task StopTyping(string channelId, string displayName)
    {
        await Clients.OthersInGroup(channelId).SendAsync("UserStoppedTyping", channelId, displayName);
    }

    /// <summary>
    /// Adds the caller to a DM channel group for receiving real-time messages.
    /// </summary>
    public async Task JoinDmChannel(string dmChannelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"dm-{dmChannelId}");
    }

    /// <summary>
    /// Removes the caller from a DM channel group.
    /// </summary>
    public async Task LeaveDmChannel(string dmChannelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"dm-{dmChannelId}");
    }

    /// <summary>
    /// Broadcasts a typing indicator to the other participant in a DM conversation.
    /// </summary>
    public async Task StartDmTyping(string dmChannelId, string displayName)
    {
        await Clients.OthersInGroup($"dm-{dmChannelId}")
            .SendAsync("DmTyping", dmChannelId, displayName);
    }

    /// <summary>
    /// Clears the typing indicator in a DM conversation.
    /// </summary>
    public async Task StopDmTyping(string dmChannelId, string displayName)
    {
        await Clients.OthersInGroup($"dm-{dmChannelId}")
            .SendAsync("DmStoppedTyping", dmChannelId, displayName);
    }
}
