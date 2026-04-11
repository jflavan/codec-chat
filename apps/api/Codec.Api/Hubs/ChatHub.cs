using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Livekit.Server.Sdk.Dotnet;
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
public class ChatHub(IUserService userService, CodecDbContext db, IConfiguration config, ILogger<ChatHub> logger, Services.VoiceCallTimeoutService callTimeoutService, PresenceTracker presenceTracker, IPermissionResolverService permissionResolver, MetricsCounterService metricsCounter) : Hub
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
        if (!Guid.TryParse(serverId, out var serverGuid))
            throw new HubException("Invalid server ID.");

        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);

        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverGuid);
        if (!serverExists)
            throw new HubException("Server not found.");

        var isMember = appUser.IsGlobalAdmin || await db.ServerMembers.AsNoTracking()
            .AnyAsync(m => m.ServerId == serverGuid && m.UserId == appUser.Id);
        if (!isMember)
            throw new HubException("Not a member of this server.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"server-{serverGuid}");
    }

    /// <summary>
    /// Removes the caller from a server-scoped group.
    /// Called after being kicked or leaving a server.
    /// </summary>
    public async Task LeaveServer(string serverId)
    {
        if (!Guid.TryParse(serverId, out var serverGuid))
            throw new HubException("Invalid server ID.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"server-{serverGuid}");
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

        // Verify the user is actually a member of the server this channel belongs to
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelGuid);
        if (channel is null)
            throw new HubException("Channel not found.");
        var isMember = appUser.IsGlobalAdmin || await db.ServerMembers.AsNoTracking()
            .AnyAsync(m => m.ServerId == channel.ServerId && m.UserId == appUser.Id);
        if (!isMember)
            throw new HubException("Not a member of this server.");

        if (!appUser.IsGlobalAdmin)
        {
            var canView = await permissionResolver.HasChannelPermissionAsync(channelGuid, appUser.Id, Permission.ViewChannels);
            if (!canView)
                throw new HubException("Missing permission: ViewChannels");
        }
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
        if (!Guid.TryParse(channelId, out _))
            throw new HubException("Invalid channel ID.");
        var safeName = Truncate(displayName, 100);
        await Clients.OthersInGroup(channelId).SendAsync("UserTyping", channelId, safeName);
    }

    /// <summary>
    /// Clears the typing indicator for other users in the channel.
    /// </summary>
    public async Task StopTyping(string channelId, string displayName)
    {
        if (!Guid.TryParse(channelId, out _))
            throw new HubException("Invalid channel ID.");
        var safeName = Truncate(displayName, 100);
        await Clients.OthersInGroup(channelId).SendAsync("UserStoppedTyping", channelId, safeName);
    }

    /// <summary>
    /// Adds the caller to a DM channel group for receiving real-time messages.
    /// </summary>
    public async Task JoinDmChannel(string dmChannelId)
    {
        if (!Guid.TryParse(dmChannelId, out var dmChannelGuid))
            throw new HubException("Invalid DM channel ID.");

        var (appUser, _) = await userService.GetOrCreateUserAsync(Context.User!);
        var isMember = await db.DmChannelMembers.AsNoTracking()
            .AnyAsync(m => m.DmChannelId == dmChannelGuid && m.UserId == appUser.Id);
        if (!isMember)
            throw new HubException("Not a member of this DM channel.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"dm-{dmChannelGuid}");
    }

    /// <summary>
    /// Removes the caller from a DM channel group.
    /// </summary>
    public async Task LeaveDmChannel(string dmChannelId)
    {
        if (!Guid.TryParse(dmChannelId, out var dmChannelGuid))
            throw new HubException("Invalid DM channel ID.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"dm-{dmChannelGuid}");
    }

    /// <summary>
    /// Broadcasts a typing indicator to the other participant in a DM conversation.
    /// </summary>
    public async Task StartDmTyping(string dmChannelId, string displayName)
    {
        if (!Guid.TryParse(dmChannelId, out _))
            throw new HubException("Invalid DM channel ID.");
        var safeName = Truncate(displayName, 100);
        await Clients.OthersInGroup($"dm-{dmChannelId}")
            .SendAsync("DmTyping", dmChannelId, safeName);
    }

    /// <summary>
    /// Clears the typing indicator in a DM conversation.
    /// </summary>
    public async Task StopDmTyping(string dmChannelId, string displayName)
    {
        if (!Guid.TryParse(dmChannelId, out _))
            throw new HubException("Invalid DM channel ID.");
        var safeName = Truncate(displayName, 100);
        await Clients.OthersInGroup($"dm-{dmChannelId}")
            .SendAsync("DmStoppedTyping", dmChannelId, safeName);
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
    /// Accepts an incoming DM voice call. Creates a VoiceState for the recipient,
    /// generates a LiveKit token, and notifies the caller.
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

        var roomName = $"call-{call.Id}";

        // Persist VoiceState for recipient.
        var recipientVoiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            DmChannelId = call.DmChannelId,
            ConnectionId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(recipientVoiceState);
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{roomName}");

        // Generate LiveKit token for the recipient.
        var token = GenerateLiveKitToken(appUser.Id.ToString(), appUser.EffectiveDisplayName, roomName);

        // Notify the caller so they can also connect to LiveKit.
        await Clients.Group($"user-{call.CallerUserId}").SendAsync("CallAccepted", new
        {
            callId = call.Id,
            dmChannelId = call.DmChannelId,
            roomName
        });

        return new
        {
            callId = call.Id,
            roomName,
            token
        };
    }

    /// <summary>
    /// Called by the caller after receiving CallAccepted to get their LiveKit token.
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

        var roomName = $"call-{call.Id}";

        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            DmChannelId = call.DmChannelId,
            ConnectionId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(voiceState);
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{roomName}");

        var token = GenerateLiveKitToken(appUser.Id.ToString(), appUser.EffectiveDisplayName, roomName);

        return new
        {
            roomName,
            token
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
        metricsCounter.IncrementMessages();
    }

    /// <summary>
    /// Ends an active call. Either party can call this. Removes VoiceState
    /// records and persists a system message with the call duration.
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

        var roomName = $"call-{call.Id}";

        foreach (var vs in voiceStates)
        {
            db.VoiceStates.Remove(vs);
            await Groups.RemoveFromGroupAsync(vs.ConnectionId, $"voice-{roomName}");
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
        metricsCounter.IncrementMessages();
    }

    /* ═══════════════════ Voice ═══════════════════ */

    /// <summary>
    /// Joins a voice channel. Persists VoiceState, broadcasts join event to the server,
    /// and returns a LiveKit token for direct WebRTC connection.
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

        if (!appUser.IsGlobalAdmin)
        {
            var canConnect = await permissionResolver.HasChannelPermissionAsync(channelGuid, appUser.Id, Permission.Connect);
            if (!canConnect)
                throw new HubException("Missing permission: Connect");
        }

        // Clean up any existing voice session first (user switching channels).
        var existing = await db.VoiceStates.FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);
        if (existing is not null)
            await LeaveVoiceChannelInternal(appUser, existing);

        // Persist voice state and join the SignalR group.
        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            ChannelId = channelGuid,
            ConnectionId = Context.ConnectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.VoiceStates.Add(voiceState);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new HubException("You are already in a voice channel on another connection.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{channelId}");

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
                vs.IsVideoEnabled,
                vs.IsScreenSharing
            })
            .ToListAsync();

        var joiningUser = new
        {
            channelId,
            userId = appUser.Id,
            displayName = appUser.EffectiveDisplayName,
            avatarUrl = appUser.CustomAvatarPath ?? appUser.AvatarUrl
        };

        // Notify all server members so the sidebar voice member list updates for everyone.
        var serverId = channel.ServerId.ToString();
        await Clients.Group($"server-{serverId}").SendAsync("UserJoinedVoice", joiningUser);

        // Generate LiveKit token for the client to connect directly.
        var roomName = channelId;
        var token = GenerateLiveKitToken(appUser.Id.ToString(), appUser.EffectiveDisplayName, roomName);

        return new
        {
            token,
            roomName,
            members
        };
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
            var roomName = await GetLiveKitRoomNameAsync(voiceState);
            await Clients.OthersInGroup($"voice-{roomName}").SendAsync("VoiceStateUpdated", new
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
                            userId = stale.UserId
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
        string? roomName = null;

        Guid? serverId = null;
        if (voiceState.ChannelId.HasValue)
        {
            serverId = await db.Channels.AsNoTracking()
                .Where(c => c.Id == voiceState.ChannelId.Value)
                .Select(c => (Guid?)c.ServerId)
                .FirstOrDefaultAsync();
            roomName = channelId;
        }
        else if (voiceState.DmChannelId.HasValue)
        {
            var call = await db.VoiceCalls
                .AsNoTracking()
                .Where(c => c.DmChannelId == voiceState.DmChannelId.Value
                    && (c.Status == VoiceCallStatus.Active || c.Status == VoiceCallStatus.Ringing))
                .FirstOrDefaultAsync();
            if (call is not null)
                roomName = $"call-{call.Id}";
        }

        db.VoiceStates.Remove(voiceState);
        await db.SaveChangesAsync();

        if (roomName is not null)
        {
            await Groups.RemoveFromGroupAsync(voiceState.ConnectionId, $"voice-{roomName}");

            if (serverId.HasValue)
            {
                await Clients.Group($"server-{serverId}").SendAsync("UserLeftVoice", new
                {
                    channelId,
                    userId = appUser.Id
                });
            }
        }
    }

    /* ── LiveKit helpers ── */

    /// <summary>
    /// Resolves the LiveKit room name for the given voice state.
    /// Server voice channels use the channel ID; DM calls use "call-{callId}".
    /// </summary>
    private async Task<string> GetLiveKitRoomNameAsync(VoiceState voiceState)
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

    /// <summary>
    /// Generates a LiveKit access token for the given user and room.
    /// </summary>
    private string GenerateLiveKitToken(string userId, string displayName, string roomName)
    {
        var apiKey = config["LiveKit:ApiKey"] ?? "devkey";
        var apiSecret = config["LiveKit:ApiSecret"] ?? "secret";

        var token = new AccessToken(apiKey, apiSecret)
            .WithIdentity(userId)
            .WithName(displayName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanPublish = true,
                CanSubscribe = true
            })
            .WithTtl(TimeSpan.FromHours(1));

        return token.ToJwt();
    }

    private static string Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= maxLength ? value : value[..maxLength];
}
