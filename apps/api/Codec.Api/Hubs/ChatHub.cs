using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Codec.Api.Hubs;

/// <summary>
/// SignalR hub for real-time chat features including message delivery, typing indicators,
/// and friend-related events. Clients join channel-scoped and user-scoped groups.
/// </summary>
[Authorize]
public class ChatHub(IUserService userService) : Hub
{
    /// <summary>
    /// Called when a client connects. Automatically joins the user-scoped group
    /// (<c>user-{userId}</c>) so they receive friend-related real-time events.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{appUser.Id}");
        await base.OnConnectedAsync();
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
}
