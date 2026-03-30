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
public class ChatHub(IUserService userService, CodecDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<ChatHub> logger, Services.VoiceCallTimeoutService callTimeoutService, PresenceTracker presenceTracker, IPermissionResolverService permissionResolver) : Hub
{
    /// <summary>
    /// Called when a client connects. Automatically joins the user-scoped group
    /// (<c>user-{userId}</c>) and all server-scoped groups (<c>server-{serverId}</c>)
    /// so the client receives real-time membership events.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
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

        // Register presence
        var presenceStatus = presenceTracker.Connect(appUser.Id, Context.ConnectionId);

        // Save to DB
        var presenceState = new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            Status = presenceStatus,
            ConnectionId = Context.ConnectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        };
        db.PresenceStates.Add(presenceState);
        await db.SaveChangesAsync();

        // Broadcast online status to all user's servers (serverIds already fetched above)
        var presencePayload = new { userId = appUser.Id.ToString(), status = presenceStatus.ToString().ToLowerInvariant() };
        var presenceBroadcastTasks = serverIds.Select(serverId =>
            Clients.Group($"server-{serverId}").SendAsync("UserPresenceChanged", presencePayload));

        // Also broadcast to friends/DM contacts
        var friendUserIds = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Where(f => f.RequesterId == appUser.Id || f.RecipientId == appUser.Id)
            .Select(f => f.RequesterId == appUser.Id ? f.RecipientId : f.RequesterId)
            .Distinct()
            .ToListAsync();
        var friendPresenceTasks = friendUserIds.Select(friendId =>
            Clients.Group($"user-{friendId}").SendAsync("UserPresenceChanged", presencePayload));

        await Task.WhenAll(presenceBroadcastTasks.Concat(friendPresenceTasks));

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
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        if (!Guid.TryParse(channelId, out var channelGuid))
            throw new HubException("Invalid channel ID.");
        var canView = await permissionResolver.HasChannelPermissionAsync(channelGuid, appUser.Id, Permission.ViewChannels);
        if (!canView)
            throw new HubException("Missing permission: ViewChannels");
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

    /* ═══════════════════ DM Voice Calls ═══════════════════ */

    /// <summary>
    /// Initiates a DM voice call. Creates a VoiceCall record, sends IncomingCall to the
    /// recipient, and starts a 30-second ringing timeout.
    /// </summary>
    public async Task<object> StartCall(string dmChannelId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        if (!Guid.TryParse(dmChannelId, out var dmChannelGuid))
            throw new HubException("Invalid DM channel ID.");

        // Verify caller is a member of this DM channel.
        var membership = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.DmChannelId == dmChannelGuid && m.UserId == appUser.Id)
            .FirstOrDefaultAsync();

        if (membership is null)
            throw new HubException("Not a member of this DM channel.");

        // Find the other participant.
        var recipientMembership = await db.DmChannelMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.DmChannelId == dmChannelGuid && m.UserId != appUser.Id)
            .FirstOrDefaultAsync();

        if (recipientMembership?.User is null)
            throw new HubException("Recipient not found.");

        var recipientUser = recipientMembership.User;

        // Check for call collision: existing Ringing call between these two users.
        var existingCall = await db.VoiceCalls
            .FirstOrDefaultAsync(c => c.DmChannelId == dmChannelGuid
                && c.Status == VoiceCallStatus.Ringing);

        if (existingCall is not null)
            throw new HubException("There is already an active call on this conversation.");

        // Check if caller already has an active call.
        var callerActiveCall = await db.VoiceCalls
            .FirstOrDefaultAsync(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
                && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

        if (callerActiveCall is not null)
            throw new HubException("You are already in a call.");

        // Check if recipient already has an active call.
        var recipientActiveCall = await db.VoiceCalls
            .FirstOrDefaultAsync(c => (c.CallerUserId == recipientUser.Id || c.RecipientUserId == recipientUser.Id)
                && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

        if (recipientActiveCall is not null)
            throw new HubException("Recipient is already in a call.");

        // Leave any existing server voice channel.
        var existingVoiceState = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existingVoiceState is not null)
            await LeaveVoiceChannelInternal(appUser, existingVoiceState);

        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = dmChannelGuid,
            CallerUserId = appUser.Id,
            RecipientUserId = recipientUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };

        db.VoiceCalls.Add(call);
        await db.SaveChangesAsync();

        // Notify recipient.
        await Clients.Group($"user-{recipientUser.Id}").SendAsync("IncomingCall", new
        {
            callId = call.Id,
            dmChannelId,
            callerUserId = appUser.Id,
            callerDisplayName = appUser.EffectiveDisplayName,
            callerAvatarUrl = appUser.CustomAvatarPath ?? appUser.AvatarUrl
        });

        // Start 30-second timeout.
        callTimeoutService.StartTimeout(call.Id, appUser.Id, recipientUser.Id, dmChannelGuid);

        return new
        {
            callId = call.Id,
            recipientUserId = recipientUser.Id,
            recipientDisplayName = recipientUser.Nickname ?? recipientUser.DisplayName,
            recipientAvatarUrl = recipientUser.CustomAvatarPath ?? recipientUser.AvatarUrl
        };
    }

    /// <summary>
    /// Accepts an incoming DM voice call. Sets up the SFU room and transports for the
    /// recipient, then notifies the caller to set up their transports.
    /// </summary>
    public async Task<object> AcceptCall(string callId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        if (!Guid.TryParse(callId, out var callGuid))
            throw new HubException("Invalid call ID.");

        var call = await db.VoiceCalls.FirstOrDefaultAsync(c => c.Id == callGuid);
        if (call is null)
            throw new HubException("Call not found.");

        if (call.RecipientUserId != appUser.Id)
            throw new HubException("You are not the recipient of this call.");

        if (call.Status != VoiceCallStatus.Ringing)
            return new { alreadyHandled = true }; // Idempotent

        // Cancel the ringing timeout.
        callTimeoutService.CancelTimeout(call.Id);

        // Leave any existing voice session for the recipient.
        var existingVoiceState = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existingVoiceState is not null)
            await LeaveVoiceChannelInternal(appUser, existingVoiceState);

        call.Status = VoiceCallStatus.Active;
        call.AnsweredAt = DateTimeOffset.UtcNow;

        // Create SFU room and transports for the recipient.
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        var roomId = $"call-{call.Id}";
        JsonElement routerRtpCapabilities, sendTransport, recvTransport;
        try
        {
            routerRtpCapabilities = await GetOrCreateSfuRoomAsync(sfuApiUrl, roomId);
            sendTransport = await CreateSfuTransportAsync(sfuApiUrl, roomId, Context.ConnectionId, "send");
            recvTransport = await CreateSfuTransportAsync(sfuApiUrl, roomId, Context.ConnectionId, "recv");
        }
        catch
        {
            try
            {
                using var cleanupClient = httpClientFactory.CreateClient("sfu");
                await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{roomId}/participants/{Context.ConnectionId}");
            }
            catch { /* best-effort */ }
            throw;
        }

        // Persist VoiceState for recipient.
        var recipientVoiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            DmChannelId = call.DmChannelId,
            ConnectionId = Context.ConnectionId,
            ParticipantId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(recipientVoiceState);
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{roomId}");

        // Generate TURN credentials.
        var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";
        var turnSecret = config["Voice:TurnSecret"] ?? "";
        object? iceServers = null;
        if (!string.IsNullOrWhiteSpace(turnSecret))
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var turnUsername = $"{expiry}:{appUser.Id}";
            var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
            var msgBytes = Encoding.UTF8.GetBytes(turnUsername);
            using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
            var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
            iceServers = new[] { new { urls = new[] { turnServerUrl }, username = turnUsername, credential } };
        }

        // Notify the caller so they can also set up their WebRTC connection.
        await Clients.Group($"user-{call.CallerUserId}").SendAsync("CallAccepted", new
        {
            callId = call.Id,
            dmChannelId = call.DmChannelId,
            roomId
        });

        return new
        {
            callId = call.Id,
            roomId,
            routerRtpCapabilities,
            sendTransportOptions = sendTransport,
            recvTransportOptions = recvTransport,
            iceServers
        };
    }

    /// <summary>
    /// Called by the caller after receiving CallAccepted to create their SFU transports.
    /// </summary>
    public async Task<object> SetupCallTransports(string callId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        if (!Guid.TryParse(callId, out var callGuid))
            throw new HubException("Invalid call ID.");

        var call = await db.VoiceCalls.AsNoTracking().FirstOrDefaultAsync(c => c.Id == callGuid);
        if (call is null || call.CallerUserId != appUser.Id)
            throw new HubException("Call not found.");

        if (call.Status != VoiceCallStatus.Active)
            throw new HubException("Call is not active.");

        // Leave any existing voice session.
        var existingVoiceState = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existingVoiceState is not null)
            await LeaveVoiceChannelInternal(appUser, existingVoiceState);

        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        var roomId = $"call-{call.Id}";
        JsonElement routerRtpCapabilities, sendTransport, recvTransport;
        try
        {
            routerRtpCapabilities = await GetOrCreateSfuRoomAsync(sfuApiUrl, roomId);
            sendTransport = await CreateSfuTransportAsync(sfuApiUrl, roomId, Context.ConnectionId, "send");
            recvTransport = await CreateSfuTransportAsync(sfuApiUrl, roomId, Context.ConnectionId, "recv");
        }
        catch
        {
            try
            {
                using var cleanupClient = httpClientFactory.CreateClient("sfu");
                await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{roomId}/participants/{Context.ConnectionId}");
            }
            catch { /* best-effort */ }
            throw;
        }

        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            DmChannelId = call.DmChannelId,
            ConnectionId = Context.ConnectionId,
            ParticipantId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(voiceState);
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{roomId}");

        // Generate TURN credentials.
        var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";
        var turnSecret = config["Voice:TurnSecret"] ?? "";
        object? iceServers = null;
        if (!string.IsNullOrWhiteSpace(turnSecret))
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var turnUsername = $"{expiry}:{appUser.Id}";
            var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
            var msgBytes = Encoding.UTF8.GetBytes(turnUsername);
            using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
            var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
            iceServers = new[] { new { urls = new[] { turnServerUrl }, username = turnUsername, credential } };
        }

        // Get other participant (recipient) to check for their producer.
        var otherVoiceState = await db.VoiceStates
            .AsNoTracking()
            .Where(vs => vs.DmChannelId == call.DmChannelId && vs.UserId != appUser.Id)
            .Select(vs => new
            {
                userId = vs.UserId,
                displayName = vs.User!.Nickname ?? vs.User.DisplayName,
                avatarUrl = vs.User.CustomAvatarPath ?? vs.User.AvatarUrl,
                vs.IsMuted,
                vs.IsDeafened,
                participantId = vs.ParticipantId,
                producerId = vs.ProducerId,
                videoProducerId = vs.VideoProducerId,
                screenProducerId = vs.ScreenProducerId,
                vs.IsVideoEnabled,
                vs.IsScreenSharing
            })
            .ToListAsync();

        return new
        {
            routerRtpCapabilities,
            sendTransportOptions = sendTransport,
            recvTransportOptions = recvTransport,
            members = otherVoiceState,
            iceServers
        };
    }

    /// <summary>
    /// Declines an incoming call. Ends it as Declined and notifies the caller.
    /// </summary>
    public async Task DeclineCall(string callId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        if (!Guid.TryParse(callId, out var callGuid))
            throw new HubException("Invalid call ID.");

        var call = await db.VoiceCalls.FirstOrDefaultAsync(c => c.Id == callGuid);
        if (call is null) return; // Already handled
        if (call.RecipientUserId != appUser.Id)
            throw new HubException("You are not the recipient of this call.");
        if (call.Status != VoiceCallStatus.Ringing) return; // Idempotent

        callTimeoutService.CancelTimeout(call.Id);

        call.Status = VoiceCallStatus.Ended;
        call.EndReason = VoiceCallEndReason.Declined;
        call.EndedAt = DateTimeOffset.UtcNow;

        // Persist "Missed call" system message.
        var callerUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == call.CallerUserId);
        var systemMessage = new DirectMessage
        {
            Id = Guid.NewGuid(),
            DmChannelId = call.DmChannelId,
            AuthorUserId = call.CallerUserId,
            AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
            Body = "missed",
            MessageType = MessageType.VoiceCallEvent,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DirectMessages.Add(systemMessage);
        await db.SaveChangesAsync();

        await Clients.Group($"user-{call.CallerUserId}").SendAsync("CallDeclined", new
        {
            callId = call.Id,
            dmChannelId = call.DmChannelId
        });

        // Broadcast system message as ReceiveDm.
        var msgPayload = new
        {
            systemMessage.Id, systemMessage.DmChannelId, systemMessage.AuthorUserId,
            systemMessage.AuthorName, systemMessage.Body,
            imageUrl = (string?)null, systemMessage.CreatedAt,
            editedAt = (DateTimeOffset?)null,
            authorAvatarUrl = callerUser.CustomAvatarPath ?? callerUser.AvatarUrl,
            linkPreviews = Array.Empty<object>(),
            replyContext = (object?)null,
            messageType = (int)systemMessage.MessageType
        };
        await Clients.Group($"user-{call.CallerUserId}").SendAsync("ReceiveDm", msgPayload);
        await Clients.Group($"user-{call.RecipientUserId}").SendAsync("ReceiveDm", msgPayload);
    }

    /// <summary>
    /// Ends an active call. Either party can call this. Cleans up SFU resources,
    /// removes VoiceState, and persists a system message with the call duration.
    /// </summary>
    public async Task EndCall()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        var call = await db.VoiceCalls
            .FirstOrDefaultAsync(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
                && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

        if (call is null) return; // No active call

        await EndCallInternal(call, appUser.Id);
    }

    /// <summary>
    /// Internal helper to end a call with full cleanup. Used by EndCall and OnDisconnectedAsync.
    /// </summary>
    private async Task EndCallInternal(VoiceCall call, Guid initiatingUserId)
    {
        callTimeoutService.CancelTimeout(call.Id);

        var wasActive = call.Status == VoiceCallStatus.Active;
        call.Status = VoiceCallStatus.Ended;
        call.EndedAt = DateTimeOffset.UtcNow;

        if (wasActive)
        {
            call.EndReason = VoiceCallEndReason.Completed;
        }
        else
        {
            // Was still ringing — treat as missed
            call.EndReason = VoiceCallEndReason.Missed;
        }

        // Calculate duration for system message.
        var durationSeconds = wasActive && call.AnsweredAt.HasValue
            ? (int)(call.EndedAt.Value - call.AnsweredAt.Value).TotalSeconds
            : 0;

        var body = wasActive ? $"call:{durationSeconds}" : "missed";

        var callerUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == call.CallerUserId);
        var systemMessage = new DirectMessage
        {
            Id = Guid.NewGuid(),
            DmChannelId = call.DmChannelId,
            AuthorUserId = call.CallerUserId,
            AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
            Body = body,
            MessageType = MessageType.VoiceCallEvent,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DirectMessages.Add(systemMessage);

        // Clean up VoiceState for both participants.
        var voiceStates = await db.VoiceStates
            .Where(vs => vs.DmChannelId == call.DmChannelId)
            .ToListAsync();

        var roomId = $"call-{call.Id}";
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";

        foreach (var vs in voiceStates)
        {
            db.VoiceStates.Remove(vs);
            await Groups.RemoveFromGroupAsync(vs.ConnectionId, $"voice-{roomId}");

            // Clean up SFU participant.
            try
            {
                using var client = httpClientFactory.CreateClient("sfu");
                await client.DeleteAsync($"{sfuApiUrl}/rooms/{roomId}/participants/{vs.ParticipantId}");
            }
            catch { /* best-effort */ }
        }

        await db.SaveChangesAsync();

        // Notify the other party.
        var otherUserId = call.CallerUserId == initiatingUserId ? call.RecipientUserId : call.CallerUserId;
        await Clients.Group($"user-{otherUserId}").SendAsync("CallEnded", new
        {
            callId = call.Id,
            dmChannelId = call.DmChannelId,
            endReason = wasActive ? "completed" : "missed",
            durationSeconds = wasActive ? durationSeconds : (int?)null
        });

        // Broadcast system message.
        var msgPayload = new
        {
            systemMessage.Id, systemMessage.DmChannelId, systemMessage.AuthorUserId,
            systemMessage.AuthorName, systemMessage.Body,
            imageUrl = (string?)null, systemMessage.CreatedAt,
            editedAt = (DateTimeOffset?)null,
            authorAvatarUrl = callerUser.CustomAvatarPath ?? callerUser.AvatarUrl,
            linkPreviews = Array.Empty<object>(),
            replyContext = (object?)null,
            messageType = (int)systemMessage.MessageType
        };
        await Clients.Group($"user-{call.CallerUserId}").SendAsync("ReceiveDm", msgPayload);
        await Clients.Group($"user-{call.RecipientUserId}").SendAsync("ReceiveDm", msgPayload);
    }

    /* ═══════════════════ Voice ═══════════════════ */

    /// <summary>
    /// Joins a voice channel. Calls the SFU first — if that fails nothing is written to
    /// the DB and no events are sent. Returns router RTP capabilities, transport options,
    /// and the current members (including their producerIds for immediate consumption).
    /// </summary>
    public async Task<object> JoinVoiceChannel(string channelId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

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

        var canConnect = await permissionResolver.HasChannelPermissionAsync(channelGuid, appUser.Id, Permission.Connect);
        if (!canConnect)
            throw new HubException("Missing permission: Connect");

        // Clean up any existing voice session first (user switching channels).
        var existing = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existing is not null)
            await LeaveVoiceChannelInternal(appUser, existing);

        // Call the SFU before touching the DB. If any step fails, clean up any
        // resources already created so they don't leak in the SFU room.
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        JsonElement routerRtpCapabilities, sendTransport, recvTransport;
        try
        {
            routerRtpCapabilities = await GetOrCreateSfuRoomAsync(sfuApiUrl, channelId);
            sendTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "send");
            recvTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "recv");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach SFU at {SfuApiUrl} for channel {ChannelId}", sfuApiUrl, channelId);
            try
            {
                using var cleanupClient = httpClientFactory.CreateClient("sfu");
                await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}/participants/{Context.ConnectionId}");
            }
            catch { /* SFU cleanup is best-effort */ }
            throw new HubException("Voice server is unavailable. Please try again later.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error setting up SFU transports for channel {ChannelId}", channelId);
            try
            {
                using var cleanupClient = httpClientFactory.CreateClient("sfu");
                await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}/participants/{Context.ConnectionId}");
            }
            catch { /* SFU cleanup is best-effort */ }
            throw new HubException("Failed to set up voice connection. Please try again later.");
        }

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
            // connection (e.g. two browser tabs). Best-effort: clean up the SFU transports
            // that were already created so they don't leak in the SFU room.
            try
            {
                using var cleanupClient = httpClientFactory.CreateClient("sfu");
                await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}/participants/{Context.ConnectionId}");
            }
            catch
            {
                // SFU cleanup is best-effort; suppress any errors.
            }

            throw new HubException("You are already in a voice channel on another connection.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{channelId}");

        // Include producerIds so the new participant can immediately consume existing media.
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
                producerId = vs.ProducerId,
                videoProducerId = vs.VideoProducerId,
                screenProducerId = vs.ScreenProducerId,
                vs.IsVideoEnabled,
                vs.IsScreenSharing
            })
            .ToListAsync();

        var joiningUser = new
        {
            channelId,
            userId = appUser.Id,
            displayName = appUser.EffectiveDisplayName,
            avatarUrl = appUser.CustomAvatarPath ?? appUser.AvatarUrl,
            participantId = Context.ConnectionId
        };

        // Notify all server members so the sidebar voice member list updates for everyone,
        // not just participants already in the voice channel.
        var serverId = channel.ServerId.ToString();
        await Clients.Group($"server-{serverId}").SendAsync("UserJoinedVoice", joiningUser);

        // Generate TURN credentials so the client can relay through the TURN server
        // when direct UDP is blocked by NAT/firewall.
        var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";
        var turnSecret = config["Voice:TurnSecret"] ?? "";
        object? iceServers = null;
        if (!string.IsNullOrWhiteSpace(turnSecret))
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var turnUsername = $"{expiry}:{appUser.Id}";
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(turnSecret);
            var msgBytes = System.Text.Encoding.UTF8.GetBytes(turnUsername);
            using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
            var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
            iceServers = new[]
            {
                new { urls = new[] { turnServerUrl }, username = turnUsername, credential }
            };
        }

        return new
        {
            routerRtpCapabilities,
            sendTransportOptions = sendTransport,
            recvTransportOptions = recvTransport,
            members,
            iceServers
        };
    }

    /// <summary>
    /// Connects a WebRTC transport with the DTLS parameters provided by the client.
    /// The caller's active channel is looked up from their VoiceState.
    /// </summary>
    public async Task ConnectTransport(string transportId, JsonElement dtlsParameters)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null)
            throw new HubException("Not currently in a voice session.");

        var roomId = await GetSfuRoomIdAsync(voiceState);
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, dtlsParameters });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{roomId}/transports/{transportId}/connect", content);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a mediasoup Producer, persists the producerId so late joiners can consume
    /// this participant, and notifies other participants to create a Consumer.
    /// </summary>
    /// <param name="transportId">The send transport ID.</param>
    /// <param name="rtpParameters">RTP parameters for the new producer.</param>
    /// <param name="label">Producer label: "audio" (default), "video", or "screen".</param>
    public async Task<object> Produce(string transportId, JsonElement rtpParameters, string? label = null)
    {
        label ??= "audio";
        if (label is not ("audio" or "video" or "screen"))
            throw new HubException("Invalid producer label. Must be 'audio', 'video', or 'screen'.");

        var kind = label == "audio" ? "audio" : "video";

        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null)
            throw new HubException("Not currently in a voice session.");

        // Check Speak permission for audio producers on server voice channels.
        if (label == "audio" && voiceState.ChannelId.HasValue)
        {
            var canSpeak = await permissionResolver.HasChannelPermissionAsync(voiceState.ChannelId.Value, appUser.Id, Permission.Speak);
            if (!canSpeak)
                throw new HubException("Missing permission: Speak");
        }

        var roomId = await GetSfuRoomIdAsync(voiceState);
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, kind, rtpParameters, label });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{roomId}/transports/{transportId}/produce", content);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var producerId = result.GetProperty("producerId").GetString()!;

        switch (label)
        {
            case "audio":
                voiceState.ProducerId = producerId;
                break;
            case "video":
                voiceState.VideoProducerId = producerId;
                voiceState.IsVideoEnabled = true;
                break;
            case "screen":
                voiceState.ScreenProducerId = producerId;
                voiceState.IsScreenSharing = true;
                break;
        }
        await db.SaveChangesAsync();

        await Clients.OthersInGroup($"voice-{roomId}").SendAsync("NewProducer", new
        {
            channelId = voiceState.ChannelId?.ToString(),
            userId = appUser.Id,
            participantId = Context.ConnectionId,
            producerId,
            label
        });

        return new { producerId };
    }

    /// <summary>
    /// Stops a video or screen share producer. Closes the SFU producer, clears the
    /// persisted ID, and notifies other participants.
    /// </summary>
    public async Task StopProducing(string label)
    {
        if (label is not ("video" or "screen"))
            throw new HubException("Can only stop 'video' or 'screen' producers.");

        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null) return;

        var roomId = await GetSfuRoomIdAsync(voiceState);
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";

        // Close the producer in the SFU
        try
        {
            using var client = httpClientFactory.CreateClient("sfu");
            await client.DeleteAsync($"{sfuApiUrl}/rooms/{roomId}/participants/{voiceState.ParticipantId}/producers/{label}");
        }
        catch { /* best-effort */ }

        var producerId = label == "video" ? voiceState.VideoProducerId : voiceState.ScreenProducerId;

        if (label == "video")
        {
            voiceState.VideoProducerId = null;
            voiceState.IsVideoEnabled = false;
        }
        else
        {
            voiceState.ScreenProducerId = null;
            voiceState.IsScreenSharing = false;
        }
        await db.SaveChangesAsync();

        if (producerId is not null)
        {
            await Clients.OthersInGroup($"voice-{roomId}").SendAsync("ProducerClosed", new
            {
                channelId = voiceState.ChannelId?.ToString(),
                userId = appUser.Id,
                participantId = Context.ConnectionId,
                producerId,
                label
            });
        }
    }

    /// <summary>
    /// Creates a mediasoup Consumer so the calling client can receive a specific producer's audio.
    /// The caller's active channel is looked up from their VoiceState.
    /// </summary>
    public async Task<object> Consume(string producerId, string recvTransportId, JsonElement rtpCapabilities)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null)
            throw new HubException("Not currently in a voice session.");

        var roomId = await GetSfuRoomIdAsync(voiceState);
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        using var client = httpClientFactory.CreateClient("sfu");
        var body = JsonSerializer.Serialize(new { participantId = voiceState.ParticipantId, producerId, transportId = recvTransportId, rtpCapabilities });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"{sfuApiUrl}/rooms/{roomId}/consumers", content);
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Broadcasts a mute/deafen state change to other participants in the voice channel or DM call.
    /// </summary>
    public async Task UpdateVoiceState(bool isMuted, bool isDeafened)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null) return; // Not in voice; ignore silently.

        voiceState.IsMuted = isMuted;
        voiceState.IsDeafened = isDeafened;
        await db.SaveChangesAsync();

        if (voiceState.ChannelId.HasValue)
        {
            var channelId = voiceState.ChannelId.ToString()!;
            // Broadcast to all server members so sidebar mute/deafen icons stay in sync.
            var serverId = await db.Channels.AsNoTracking()
                .Where(c => c.Id == voiceState.ChannelId.Value)
                .Select(c => (Guid?)c.ServerId)
                .FirstOrDefaultAsync();

            var targetGroup = serverId.HasValue ? $"server-{serverId}" : $"voice-{channelId}";
            await Clients.OthersInGroup(targetGroup).SendAsync("VoiceStateUpdated", new
            {
                channelId,
                userId = appUser.Id,
                isMuted,
                isDeafened
            });
        }
        else if (voiceState.DmChannelId.HasValue)
        {
            // DM call — broadcast to the voice room group.
            var roomId = await GetSfuRoomIdAsync(voiceState);
            await Clients.OthersInGroup($"voice-{roomId}").SendAsync("VoiceStateUpdated", new
            {
                channelId = (string?)null,
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
        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is not null)
            await LeaveVoiceChannelInternal(appUser, voiceState);
    }

    /// <summary>
    /// Cleans up voice state and call state when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

            // Update presence on disconnect
            var (previousPresence, currentPresence, remainingConnections) = presenceTracker.Disconnect(Context.ConnectionId);

            // Remove this connection's DB row
            await db.PresenceStates
                .Where(ps => ps.ConnectionId == Context.ConnectionId)
                .ExecuteDeleteAsync();

            if (previousPresence != currentPresence)
            {
                if (currentPresence == PresenceStatus.Offline)
                {
                    await db.PresenceStates
                        .Where(ps => ps.UserId == appUser.Id)
                        .ExecuteDeleteAsync();
                }

                var presencePayload = new { userId = appUser.Id.ToString(), status = currentPresence.ToString().ToLowerInvariant() };
                var disconnectServerIds = await db.ServerMembers
                    .AsNoTracking()
                    .Where(sm => sm.UserId == appUser.Id)
                    .Select(sm => sm.ServerId)
                    .ToListAsync();
                foreach (var serverId in disconnectServerIds)
                {
                    await Clients.Group($"server-{serverId}").SendAsync("UserPresenceChanged", presencePayload);
                }

                var disconnectFriendIds = await db.Friendships
                    .Where(f => f.RequesterId == appUser.Id || f.RecipientId == appUser.Id)
                    .Select(f => f.RequesterId == appUser.Id ? f.RecipientId : f.RequesterId)
                    .Distinct()
                    .ToListAsync();
                foreach (var friendId in disconnectFriendIds)
                {
                    await Clients.Group($"user-{friendId}").SendAsync("UserPresenceChanged", presencePayload);
                }
            }

            var voiceState = await db.VoiceStates
                .FirstOrDefaultAsync(vs => vs.ConnectionId == Context.ConnectionId);

            if (voiceState is not null)
                await LeaveVoiceChannelInternal(appUser, voiceState);

            // Check for ringing or active calls involving this user.
            var activeCall = await db.VoiceCalls
                .FirstOrDefaultAsync(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
                    && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

            if (activeCall is not null)
                await EndCallInternal(activeCall, appUser.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during voice/call state cleanup on disconnect for connection {ConnectionId}. Attempting raw DB fallback.", Context.ConnectionId);
            var stale = await db.VoiceStates
                .FirstOrDefaultAsync(vs => vs.ConnectionId == Context.ConnectionId);
            if (stale is not null)
            {
                var staleUserId = stale.UserId;
                var staleChannelId = stale.ChannelId?.ToString();
                var staleServerId = stale.ChannelId.HasValue
                    ? await db.Channels.AsNoTracking()
                        .Where(c => c.Id == stale.ChannelId.Value)
                        .Select(c => (Guid?)c.ServerId)
                        .FirstOrDefaultAsync()
                    : null;

                db.VoiceStates.Remove(stale);
                await db.SaveChangesAsync();

                if (staleChannelId is not null && staleServerId.HasValue)
                {
                    try
                    {
                        await Clients.Group($"server-{staleServerId}").SendAsync("UserLeftVoice", new
                        {
                            channelId = staleChannelId,
                            userId = stale.UserId,
                            participantId = stale.ParticipantId
                        });
                    }
                    catch { /* best-effort */ }
                }

                // Best-effort cleanup of any active/ringing call for this user.
                try
                {
                    var staleCall = await db.VoiceCalls
                        .FirstOrDefaultAsync(c => (c.CallerUserId == staleUserId || c.RecipientUserId == staleUserId)
                            && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));
                    if (staleCall is not null)
                    {
                        callTimeoutService.CancelTimeout(staleCall.Id);
                        var wasActive = staleCall.Status == VoiceCallStatus.Active;
                        staleCall.Status = VoiceCallStatus.Ended;
                        staleCall.EndedAt = DateTimeOffset.UtcNow;
                        staleCall.EndReason = wasActive
                            ? VoiceCallEndReason.Completed
                            : VoiceCallEndReason.Missed;
                        await db.SaveChangesAsync();
                    }
                }
                catch { /* best-effort */ }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Heartbeat(bool isActive)
    {
        var change = presenceTracker.Heartbeat(Context.ConnectionId, isActive);
        if (change is null) return;

        var (userId, previous, current) = change.Value;

        // Update DB
        await db.PresenceStates
            .Where(ps => ps.ConnectionId == Context.ConnectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ps => ps.Status, current)
                .SetProperty(ps => ps.LastHeartbeatAt, DateTimeOffset.UtcNow)
                .SetProperty(ps => ps.LastActiveAt, ps => isActive ? DateTimeOffset.UtcNow : ps.LastActiveAt));

        // Broadcast
        var payload = new { userId = userId.ToString(), status = current.ToString().ToLowerInvariant() };
        var serverIds = await db.ServerMembers
            .AsNoTracking()
            .Where(sm => sm.UserId == userId)
            .Select(sm => sm.ServerId)
            .ToListAsync();
        foreach (var serverId in serverIds)
        {
            await Clients.Group($"server-{serverId}").SendAsync("UserPresenceChanged", payload);
        }

        var friendUserIds = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Where(f => f.RequesterId == userId || f.RecipientId == userId)
            .Select(f => f.RequesterId == userId ? f.RecipientId : f.RequesterId)
            .Distinct()
            .ToListAsync();
        foreach (var friendId in friendUserIds)
        {
            await Clients.Group($"user-{friendId}").SendAsync("UserPresenceChanged", payload);
        }
    }

    private async Task LeaveVoiceChannelInternal(Models.User appUser, VoiceState voiceState)
    {
        var channelId = voiceState.ChannelId?.ToString();
        string? roomId = null;

        Guid? serverId = null;
        if (voiceState.ChannelId.HasValue)
        {
            serverId = await db.Channels.AsNoTracking()
                .Where(c => c.Id == voiceState.ChannelId.Value)
                .Select(c => (Guid?)c.ServerId)
                .FirstOrDefaultAsync();
            roomId = channelId;
        }
        else if (voiceState.DmChannelId.HasValue)
        {
            var call = await db.VoiceCalls
                .AsNoTracking()
                .Where(c => c.DmChannelId == voiceState.DmChannelId.Value
                    && (c.Status == VoiceCallStatus.Active || c.Status == VoiceCallStatus.Ringing))
                .FirstOrDefaultAsync();
            if (call is not null)
                roomId = $"call-{call.Id}";
        }

        db.VoiceStates.Remove(voiceState);
        await db.SaveChangesAsync();

        if (roomId is not null)
        {
            await Groups.RemoveFromGroupAsync(voiceState.ConnectionId, $"voice-{roomId}");

            if (serverId.HasValue)
            {
                await Clients.Group($"server-{serverId}").SendAsync("UserLeftVoice", new
                {
                    channelId,
                    userId = appUser.Id,
                    participantId = voiceState.ParticipantId
                });
            }

            // Remove from SFU.
            var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
            try
            {
                using var client = httpClientFactory.CreateClient("sfu");
                await client.DeleteAsync($"{sfuApiUrl}/rooms/{roomId}/participants/{voiceState.ParticipantId}");
            }
            catch { /* best-effort */ }
        }
    }

    /* ── Room ID resolver ── */

    /// <summary>
    /// Resolves the SFU room ID for the given voice state.
    /// Server voice channels use the channel ID; DM calls use "call-{callId}".
    /// </summary>
    private async Task<string> GetSfuRoomIdAsync(VoiceState voiceState)
    {
        if (voiceState.ChannelId.HasValue)
            return voiceState.ChannelId.Value.ToString();

        if (voiceState.DmChannelId.HasValue)
        {
            var call = await db.VoiceCalls
                .AsNoTracking()
                .Where(c => c.DmChannelId == voiceState.DmChannelId.Value
                    && c.Status == VoiceCallStatus.Active)
                .OrderByDescending(c => c.AnsweredAt)
                .FirstOrDefaultAsync();

            if (call is not null)
                return $"call-{call.Id}";
        }

        throw new HubException("Not currently in a voice session.");
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
