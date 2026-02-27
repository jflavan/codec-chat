using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Hubs;

/// <summary>
/// SignalR hub for real-time chat features including message delivery, typing indicators,
/// friend-related events, and WebRTC voice signaling.
/// Clients join channel-scoped, user-scoped, server-scoped, and voice-scoped groups.
/// </summary>
[Authorize]
public class ChatHub(IUserService userService, CodecDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory) : Hub
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

        List<Guid> serverIds;
        if (appUser.IsGlobalAdmin)
        {
            serverIds = await db.Servers
                .AsNoTracking()
                .Select(s => s.Id)
                .ToListAsync();
        }
        else
        {
            serverIds = await db.ServerMembers
                .AsNoTracking()
                .Where(m => m.UserId == appUser.Id)
                .Select(m => m.ServerId)
                .ToListAsync();
        }

        var groupJoinTasks = serverIds
            .Select(serverId => Groups.AddToGroupAsync(Context.ConnectionId, $"server-{serverId}"));

        await Task.WhenAll(groupJoinTasks);
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

    /* ═══════════════════ Voice ═══════════════════ */

    /// <summary>
    /// Joins a voice channel. Calls the SFU first — if that fails nothing is written to
    /// the DB and no events are sent. Returns router RTP capabilities, transport options,
    /// and the current members (including their producerIds for immediate consumption).
    /// </summary>
    public async Task<object> JoinVoiceChannel(string channelId)
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);

        if (!Guid.TryParse(channelId, out var channelGuid))
            throw new HubException("Invalid channel ID.");

        var channel = await db.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelGuid && c.Type == ChannelType.Voice);

        if (channel is null)
            throw new HubException("Voice channel not found.");

        var isMember = appUser.IsGlobalAdmin || await db.ServerMembers
            .AsNoTracking()
            .AnyAsync(m => m.ServerId == channel.ServerId && m.UserId == appUser.Id);

        if (!isMember)
            throw new HubException("Not a member of this server.");

        // Clean up any existing voice session first (user switching channels).
        var existing = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existing is not null)
            await LeaveVoiceChannelInternal(appUser, existing);

        // Call the SFU before touching the DB. If the SFU is unavailable we throw here
        // with no state change — no stale VoiceState row, no spurious UserJoinedVoice event.
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        var routerRtpCapabilities = await GetOrCreateSfuRoomAsync(sfuApiUrl, channelId);
        var sendTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "send");
        var recvTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "recv");

        // SFU is ready — persist voice state and join the SignalR group.
        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            ChannelId = channelGuid,
            ConnectionId = Context.ConnectionId,
            ParticipantId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(voiceState);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index on UserId fired — user already has an active session on another
            // connection (e.g. two browser tabs). Reject cleanly rather than letting an
            // unhandled exception surface as a generic internal error.
            throw new HubException("You are already in a voice channel on another connection.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{channelId}");

        // Include producerId so the new participant can immediately consume existing audio.
        var members = await db.VoiceStates
            .AsNoTracking()
            .Where(vs => vs.ChannelId == channelGuid && vs.UserId != appUser.Id)
            .Select(vs => new
            {
                userId = vs.UserId,
                displayName = vs.User!.Nickname ?? vs.User.DisplayName,
                avatarUrl = vs.User.CustomAvatarPath ?? vs.User.AvatarUrl,
                vs.IsMuted,
                vs.IsDeafened,
                participantId = vs.ParticipantId,
                producerId = vs.ProducerId
            })
            .ToListAsync();

        await Clients.OthersInGroup($"voice-{channelId}").SendAsync("UserJoinedVoice", new
        {
            channelId,
            userId = appUser.Id,
            displayName = appUser.EffectiveDisplayName,
            avatarUrl = appUser.CustomAvatarPath ?? appUser.AvatarUrl,
            participantId = Context.ConnectionId
        });

        return new
        {
            routerRtpCapabilities,
            sendTransportOptions = sendTransport,
            recvTransportOptions = recvTransport,
            members
        };
    }

    /// <summary>
    /// Connects a WebRTC transport with the DTLS parameters provided by the client.
    /// The caller's active channel is looked up from their VoiceState.
    /// </summary>
    public async Task ConnectTransport(string transportId, JsonElement dtlsParameters)
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState?.ChannelId is null)
            throw new HubException("Not currently in a voice channel.");

        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        // Include participantId so the SFU can verify this transport belongs to the caller.
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, dtlsParameters });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{voiceState.ChannelId}/transports/{transportId}/connect", content);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a mediasoup Producer, persists the producerId so late joiners can consume
    /// this participant, and notifies other participants to create a Consumer.
    /// </summary>
    public async Task<object> Produce(string transportId, JsonElement rtpParameters)
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState?.ChannelId is null)
            throw new HubException("Not currently in a voice channel.");

        var channelId = voiceState.ChannelId.ToString()!;
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        // Include participantId so the SFU can verify this transport belongs to the caller.
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, kind = "audio", rtpParameters });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{channelId}/transports/{transportId}/produce", content);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var producerId = result.GetProperty("producerId").GetString()!;

        // Persist so late joiners returned by JoinVoiceChannel can consume this participant.
        voiceState.ProducerId = producerId;
        await db.SaveChangesAsync();

        await Clients.OthersInGroup($"voice-{channelId}").SendAsync("NewProducer", new
        {
            channelId,
            userId = appUser.Id,
            participantId = Context.ConnectionId,
            producerId
        });

        return new { producerId };
    }

    /// <summary>
    /// Creates a mediasoup Consumer so the calling client can receive a specific producer's audio.
    /// The caller's active channel is looked up from their VoiceState.
    /// </summary>
    public async Task<object> Consume(string producerId, string recvTransportId, JsonElement rtpCapabilities)
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState?.ChannelId is null)
            throw new HubException("Not currently in a voice channel.");

        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        // Include participantId so the SFU can verify the recv transport belongs to the caller.
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, producerId, transportId = recvTransportId, rtpCapabilities });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{voiceState.ChannelId}/consumers", content);
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Broadcasts a mute/deafen state change to other participants in the voice channel.
    /// The caller's active channel is looked up from their VoiceState.
    /// </summary>
    public async Task UpdateVoiceState(bool isMuted, bool isDeafened)
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null) return; // Not in voice; ignore silently.

        var channelId = voiceState.ChannelId?.ToString();
        voiceState.IsMuted = isMuted;
        voiceState.IsDeafened = isDeafened;
        await db.SaveChangesAsync();

        if (channelId is not null)
        {
            await Clients.OthersInGroup($"voice-{channelId}").SendAsync("VoiceStateUpdated", new
            {
                channelId,
                userId = appUser.Id,
                isMuted,
                isDeafened
            });
        }
    }

    /// <summary>
    /// Removes the calling client from their active voice channel.
    /// </summary>
    public async Task LeaveVoiceChannel()
    {
        var appUser = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is not null)
            await LeaveVoiceChannelInternal(appUser, voiceState);
    }

    /// <summary>
    /// Cleans up voice state when a client disconnects (tab close, network drop, etc.).
    /// Wrapped in try-catch so a transient failure (e.g. DB unavailable, expired token) during
    /// user lookup cannot prevent the voice state row from being removed.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var voiceState = await db.VoiceStates
                .FirstOrDefaultAsync(vs => vs.ConnectionId == Context.ConnectionId);

            if (voiceState is not null)
            {
                var appUser = await userService.GetOrCreateUserAsync(Context.User!);
                await LeaveVoiceChannelInternal(appUser, voiceState);
            }
        }
        catch
        {
            // Best-effort cleanup. If we can't look up the user, attempt a raw DB delete
            // using only the ConnectionId so the stale row doesn't linger indefinitely.
            var stale = await db.VoiceStates
                .FirstOrDefaultAsync(vs => vs.ConnectionId == Context.ConnectionId);
            if (stale is not null)
            {
                db.VoiceStates.Remove(stale);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveVoiceChannelInternal(Models.User appUser, VoiceState voiceState)
    {
        var channelId = voiceState.ChannelId?.ToString();

        db.VoiceStates.Remove(voiceState);
        await db.SaveChangesAsync();

        if (channelId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"voice-{channelId}");

            await Clients.Group($"voice-{channelId}").SendAsync("UserLeftVoice", new
            {
                channelId,
                userId = appUser.Id,
                participantId = voiceState.ParticipantId
            });

            // Remove the participant from the SFU room.
            var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
            try
            {
                using var client = httpClientFactory.CreateClient("sfu");
                await client.DeleteAsync(
                    $"{sfuApiUrl}/rooms/{channelId}/participants/{voiceState.ParticipantId}");
            }
            catch
            {
                // SFU may be unavailable; voice state is already cleaned up in DB.
            }
        }
    }

    /* ── SFU helpers ── */

    private async Task<JsonElement> GetOrCreateSfuRoomAsync(string sfuApiUrl, string channelId)
    {
        using var client = httpClientFactory.CreateClient("sfu");
        var resp = await client.PostAsync($"{sfuApiUrl}/rooms/{channelId}", null);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("routerRtpCapabilities");
    }

    private async Task<JsonElement> CreateSfuTransportAsync(string sfuApiUrl, string channelId, string participantId, string direction)
    {
        using var client = httpClientFactory.CreateClient("sfu");
        var body = JsonSerializer.Serialize(new { participantId, direction });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync($"{sfuApiUrl}/rooms/{channelId}/transports", content);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }
}
