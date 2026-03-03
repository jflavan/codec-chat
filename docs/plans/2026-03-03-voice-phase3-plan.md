# Voice Phase 3: DM Voice Calls — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Discord-style 1:1 voice calls initiated from DM conversations, with ringing/accept/decline, system messages, and call duration display.

**Architecture:** Extend the existing SignalR hub with call signaling methods (StartCall, AcceptCall, DeclineCall, EndCall). Reuse the mediasoup SFU for audio transport using `call-{callId}` room IDs. Track call state in a new `VoiceCall` entity. Persist call events as `DirectMessage` records with a `MessageType` discriminator. Frontend adds incoming call overlay, DM call header, and modifies VoiceConnectedBar.

**Tech Stack:** C# 14 / .NET 10 / EF Core / SignalR (backend), Svelte 5 / TypeScript / mediasoup-client (frontend)

**Design doc:** `docs/plans/2026-03-03-voice-phase3-design.md`

---

### Task 1: Data Model — VoiceCall entity and enums

**Files:**
- Create: `apps/api/Codec.Api/Models/VoiceCall.cs`
- Create: `apps/api/Codec.Api/Models/VoiceCallStatus.cs`
- Create: `apps/api/Codec.Api/Models/VoiceCallEndReason.cs`
- Create: `apps/api/Codec.Api/Models/MessageType.cs`
- Modify: `apps/api/Codec.Api/Models/DirectMessage.cs`
- Modify: `apps/api/Codec.Api/Models/VoiceState.cs`
- Modify: `apps/api/Codec.Api/Data/CodecDbContext.cs`

**Step 1: Create VoiceCallStatus enum**

```csharp
// apps/api/Codec.Api/Models/VoiceCallStatus.cs
namespace Codec.Api.Models;

public enum VoiceCallStatus
{
    Ringing = 0,
    Active = 1,
    Ended = 2
}
```

**Step 2: Create VoiceCallEndReason enum**

```csharp
// apps/api/Codec.Api/Models/VoiceCallEndReason.cs
namespace Codec.Api.Models;

public enum VoiceCallEndReason
{
    Answered = 0,
    Missed = 1,
    Declined = 2
}
```

**Step 3: Create MessageType enum**

```csharp
// apps/api/Codec.Api/Models/MessageType.cs
namespace Codec.Api.Models;

public enum MessageType
{
    Regular = 0,
    VoiceCallEvent = 1
}
```

**Step 4: Create VoiceCall entity**

```csharp
// apps/api/Codec.Api/Models/VoiceCall.cs
namespace Codec.Api.Models;

/// <summary>
/// Tracks a DM voice call from initiation through completion.
/// </summary>
public class VoiceCall
{
    public Guid Id { get; set; }
    public Guid DmChannelId { get; set; }
    public DmChannel? DmChannel { get; set; }
    public Guid CallerUserId { get; set; }
    public User? CallerUser { get; set; }
    public Guid RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }
    public VoiceCallStatus Status { get; set; }
    public VoiceCallEndReason? EndReason { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
```

**Step 5: Add MessageType to DirectMessage**

In `apps/api/Codec.Api/Models/DirectMessage.cs`, add after the `Body` property:

```csharp
public MessageType MessageType { get; set; } = MessageType.Regular;
```

**Step 6: Add DmChannelId to VoiceState**

In `apps/api/Codec.Api/Models/VoiceState.cs`, add after the `Channel` property:

```csharp
/// <summary>The DM channel for a direct voice call. Null for server voice channels.</summary>
public Guid? DmChannelId { get; set; }
public DmChannel? DmChannel { get; set; }
```

**Step 7: Register VoiceCall in CodecDbContext**

In `apps/api/Codec.Api/Data/CodecDbContext.cs`:

Add DbSet:
```csharp
public DbSet<VoiceCall> VoiceCalls => Set<VoiceCall>();
```

Add to `OnModelCreating` (after VoiceState section):
```csharp
// VoiceCall relationships and indexes.
modelBuilder.Entity<VoiceCall>()
    .HasOne(vc => vc.DmChannel)
    .WithMany()
    .HasForeignKey(vc => vc.DmChannelId)
    .OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<VoiceCall>()
    .HasOne(vc => vc.CallerUser)
    .WithMany()
    .HasForeignKey(vc => vc.CallerUserId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<VoiceCall>()
    .HasOne(vc => vc.RecipientUser)
    .WithMany()
    .HasForeignKey(vc => vc.RecipientUserId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<VoiceCall>()
    .HasIndex(vc => vc.DmChannelId);

modelBuilder.Entity<VoiceCall>()
    .HasIndex(vc => vc.CallerUserId);

modelBuilder.Entity<VoiceCall>()
    .HasIndex(vc => vc.RecipientUserId);

// VoiceState → DmChannel relationship for direct calls.
modelBuilder.Entity<VoiceState>()
    .HasOne(vs => vs.DmChannel)
    .WithMany()
    .HasForeignKey(vs => vs.DmChannelId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<VoiceState>()
    .HasIndex(vs => vs.DmChannelId);
```

**Step 8: Create EF Core migration**

Run:
```bash
cd apps/api/Codec.Api && dotnet ef migrations add AddVoiceCalls
```

**Step 9: Build to verify**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeded

**Step 10: Commit**

```bash
git add apps/api/
git commit -m "feat(voice): add VoiceCall entity, MessageType enum, DmChannelId on VoiceState"
```

---

### Task 2: Call Timeout Infrastructure — Background service and timer dictionary

**Files:**
- Create: `apps/api/Codec.Api/Services/VoiceCallTimeoutService.cs`
- Modify: `apps/api/Codec.Api/Program.cs`

**Step 1: Create VoiceCallTimeoutService**

This service manages per-call 30-second timers and a background cleanup loop for stale calls after API restart.

```csharp
// apps/api/Codec.Api/Services/VoiceCallTimeoutService.cs
using System.Collections.Concurrent;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Codec.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

/// <summary>
/// Manages 30-second call ringing timeouts and periodic cleanup of stale calls.
/// </summary>
public class VoiceCallTimeoutService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ChatHub> hubContext,
    ILogger<VoiceCallTimeoutService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _callTimers = new();

    /// <summary>
    /// Starts a 30-second timeout for a ringing call. If the call is still Ringing
    /// when the timer fires, it is ended as Missed and both parties are notified.
    /// </summary>
    public void StartTimeout(Guid callId, Guid callerUserId, Guid recipientUserId, Guid dmChannelId)
    {
        var cts = new CancellationTokenSource();
        _callTimers[callId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // Call was answered/declined/ended before timeout
            }
            finally
            {
                _callTimers.TryRemove(callId, out _);
            }

            await HandleTimeout(callId, callerUserId, recipientUserId, dmChannelId);
        });
    }

    /// <summary>Cancels the timeout for a call (called on accept/decline/end).</summary>
    public void CancelTimeout(Guid callId)
    {
        if (_callTimers.TryRemove(callId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task HandleTimeout(Guid callId, Guid callerUserId, Guid recipientUserId, Guid dmChannelId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

            var call = await db.VoiceCalls.FirstOrDefaultAsync(c => c.Id == callId);
            if (call is null || call.Status != VoiceCallStatus.Ringing) return;

            call.Status = VoiceCallStatus.Ended;
            call.EndReason = VoiceCallEndReason.Missed;
            call.EndedAt = DateTimeOffset.UtcNow;

            // Persist system message
            var callerUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == callerUserId);
            db.DirectMessages.Add(new DirectMessage
            {
                Id = Guid.NewGuid(),
                DmChannelId = dmChannelId,
                AuthorUserId = callerUserId,
                AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
                Body = "missed",
                MessageType = MessageType.VoiceCallEvent,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();

            var missedPayload = new { callId, dmChannelId };
            await hubContext.Clients.Group($"user-{callerUserId}").SendAsync("CallMissed", missedPayload);
            await hubContext.Clients.Group($"user-{recipientUserId}").SendAsync("CallMissed", missedPayload);

            // Also broadcast as ReceiveDm so the system message appears in chat
            var systemMsg = await db.DirectMessages
                .AsNoTracking()
                .Where(m => m.DmChannelId == dmChannelId && m.MessageType == MessageType.VoiceCallEvent)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id, m.DmChannelId, m.AuthorUserId, m.AuthorName, m.Body,
                    imageUrl = (string?)null, m.CreatedAt, editedAt = (DateTimeOffset?)null,
                    authorAvatarUrl = m.AuthorUser!.CustomAvatarPath ?? m.AuthorUser.AvatarUrl,
                    linkPreviews = new object[0],
                    replyContext = (object?)null,
                    messageType = (int)m.MessageType
                })
                .FirstAsync();

            await hubContext.Clients.Group($"user-{callerUserId}").SendAsync("ReceiveDm", systemMsg);
            await hubContext.Clients.Group($"user-{recipientUserId}").SendAsync("ReceiveDm", systemMsg);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle call timeout for call {CallId}", callId);
        }
    }

    /// <summary>Background loop: every 60s, clean up stale Ringing calls older than 30s.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
                var staleCalls = await db.VoiceCalls
                    .Where(c => c.Status == VoiceCallStatus.Ringing && c.StartedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var call in staleCalls)
                {
                    call.Status = VoiceCallStatus.Ended;
                    call.EndReason = VoiceCallEndReason.Missed;
                    call.EndedAt = DateTimeOffset.UtcNow;

                    var callerUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == call.CallerUserId, stoppingToken);
                    if (callerUser is not null)
                    {
                        db.DirectMessages.Add(new DirectMessage
                        {
                            Id = Guid.NewGuid(),
                            DmChannelId = call.DmChannelId,
                            AuthorUserId = call.CallerUserId,
                            AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
                            Body = "missed",
                            MessageType = MessageType.VoiceCallEvent,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }

                    var payload = new { callId = call.Id, dmChannelId = call.DmChannelId };
                    await hubContext.Clients.Group($"user-{call.CallerUserId}").SendAsync("CallMissed", payload, stoppingToken);
                    await hubContext.Clients.Group($"user-{call.RecipientUserId}").SendAsync("CallMissed", payload, stoppingToken);
                }

                if (staleCalls.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Cleaned up {Count} stale ringing call(s)", staleCalls.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during stale call cleanup");
            }
        }
    }
}
```

**Step 2: Register in Program.cs**

Find the services registration section and add:
```csharp
builder.Services.AddSingleton<VoiceCallTimeoutService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceCallTimeoutService>());
```

**Step 3: Build to verify**

Run: `cd apps/api/Codec.Api && dotnet build`

**Step 4: Commit**

```bash
git add apps/api/
git commit -m "feat(voice): add VoiceCallTimeoutService for 30s ringing timeout and stale cleanup"
```

---

### Task 3: SignalR Hub — Call signaling methods

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs`

This is the core task. Add `StartCall`, `AcceptCall`, `DeclineCall`, `EndCall` methods and update `ConnectTransport`, `Produce`, `Consume`, `UpdateVoiceState`, `LeaveVoiceChannelInternal`, and `OnDisconnectedAsync` to handle DM calls.

**Step 1: Add VoiceCallTimeoutService to ChatHub constructor**

Update the constructor to inject `VoiceCallTimeoutService`:

```csharp
public class ChatHub(
    IUserService userService,
    CodecDbContext db,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    ILogger<ChatHub> logger,
    Services.VoiceCallTimeoutService callTimeoutService) : Hub
```

**Step 2: Add StartCall method**

Add after the `/* ═══════════════════ Voice ═══════════════════ */` section, before `JoinVoiceChannel`:

```csharp
/// <summary>
/// Initiates a DM voice call. Creates a VoiceCall record, sends IncomingCall to the
/// recipient, and starts a 30-second ringing timeout.
/// </summary>
public async Task<object> StartCall(string dmChannelId)
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);

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
```

**Step 3: Add AcceptCall method**

```csharp
/// <summary>
/// Accepts an incoming DM voice call. Sets up the SFU room and transports for both
/// parties, then returns WebRTC setup data to the recipient and sends it to the caller.
/// </summary>
public async Task<object> AcceptCall(string callId)
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);

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
    call.EndReason = VoiceCallEndReason.Answered;

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
    // The caller gets the same room but creates their own transports.
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
```

**Step 4: Add SetupCallTransports hub method (for the caller after acceptance)**

```csharp
/// <summary>
/// Called by the caller after receiving CallAccepted to create their SFU transports.
/// </summary>
public async Task<object> SetupCallTransports(string callId)
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);

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
            producerId = vs.ProducerId
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
```

**Step 5: Add DeclineCall method**

```csharp
/// <summary>
/// Declines an incoming call. Ends it as Missed and notifies the caller.
/// </summary>
public async Task DeclineCall(string callId)
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);

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
```

**Step 6: Add EndCall method**

```csharp
/// <summary>
/// Ends an active call. Either party can call this. Cleans up SFU resources,
/// removes VoiceState, and persists a system message with the call duration.
/// </summary>
public async Task EndCall()
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);

    var call = await db.VoiceCalls
        .FirstOrDefaultAsync(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
            && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

    if (call is null) return; // No active call

    callTimeoutService.CancelTimeout(call.Id);

    var wasActive = call.Status == VoiceCallStatus.Active;
    call.Status = VoiceCallStatus.Ended;
    call.EndedAt = DateTimeOffset.UtcNow;

    if (!wasActive)
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
    var otherUserId = call.CallerUserId == appUser.Id ? call.RecipientUserId : call.CallerUserId;
    await Clients.Group($"user-{otherUserId}").SendAsync("CallEnded", new
    {
        callId = call.Id,
        dmChannelId = call.DmChannelId,
        endReason = wasActive ? "answered" : "missed",
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
```

**Step 7: Update ConnectTransport, Produce, and Consume to support DM calls**

The current implementations use `voiceState.ChannelId` as the SFU room ID. For DM calls, `ChannelId` is null and `DmChannelId` is set. Update these methods to resolve the room ID from either source.

Add a private helper method:

```csharp
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
```

Update `ConnectTransport` — replace the channel null check and room ID:

```csharp
public async Task ConnectTransport(string transportId, JsonElement dtlsParameters)
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);
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
    var resp = await client.PostAsync($"{sfuApiUrl}/rooms/{roomId}/transports/{transportId}/connect", content);
    resp.EnsureSuccessStatusCode();
}
```

Update `Produce` similarly — use `GetSfuRoomIdAsync`, and broadcast `NewProducer` to `voice-{roomId}`.

Update `Consume` similarly — use `GetSfuRoomIdAsync`.

Update `UpdateVoiceState` — handle DM channel case by broadcasting to `voice-{roomId}` when `ChannelId` is null but `DmChannelId` is set.

**Step 8: Update OnDisconnectedAsync for DM calls**

In `OnDisconnectedAsync`, after the existing voice state cleanup, add:

```csharp
// Check for ringing or active calls involving this user.
try
{
    var appUser = await userService.GetOrCreateUserAsync(Context.User!);
    var activeCall = await db.VoiceCalls
        .FirstOrDefaultAsync(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
            && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active));

    if (activeCall is not null)
    {
        // For ringing calls, end immediately as missed.
        // For active calls, end with duration.
        // (Reuse EndCall logic but internal)
        callTimeoutService.CancelTimeout(activeCall.Id);
        // ... (similar to EndCall logic)
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error cleaning up call state on disconnect for {ConnectionId}", Context.ConnectionId);
}
```

**Step 9: Update LeaveVoiceChannelInternal for DM calls**

Add handling for when `voiceState.DmChannelId` is set:

```csharp
private async Task LeaveVoiceChannelInternal(Models.User appUser, VoiceState voiceState)
{
    var channelId = voiceState.ChannelId?.ToString();
    string? roomId = null;

    // Determine the broadcast target and SFU room ID.
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
```

**Step 10: Build to verify**

Run: `cd apps/api/Codec.Api && dotnet build`

**Step 11: Commit**

```bash
git add apps/api/
git commit -m "feat(voice): add StartCall, AcceptCall, DeclineCall, EndCall hub methods with DM call support"
```

---

### Task 4: REST Endpoint — GET /voice/active-call

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/VoiceController.cs`

**Step 1: Add GetActiveCall endpoint**

```csharp
/// <summary>
/// Returns the caller's active or ringing VoiceCall, if any.
/// Used on page load/reconnect to restore call state.
/// </summary>
[HttpGet("active-call")]
public async Task<IActionResult> GetActiveCall()
{
    var appUser = await userService.GetOrCreateUserAsync(User);

    var call = await db.VoiceCalls
        .AsNoTracking()
        .Where(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
            && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active))
        .Select(c => new
        {
            c.Id,
            c.DmChannelId,
            c.CallerUserId,
            c.RecipientUserId,
            status = c.Status.ToString().ToLowerInvariant(),
            c.StartedAt,
            c.AnsweredAt
        })
        .FirstOrDefaultAsync();

    if (call is null)
        return NoContent();

    // Include the other party's display info.
    var otherUserId = call.CallerUserId == appUser.Id ? call.RecipientUserId : call.CallerUserId;
    var otherUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == otherUserId);

    return Ok(new
    {
        call.Id,
        call.DmChannelId,
        call.CallerUserId,
        call.RecipientUserId,
        call.status,
        call.StartedAt,
        call.AnsweredAt,
        otherUserId,
        otherDisplayName = otherUser.Nickname ?? otherUser.DisplayName,
        otherAvatarUrl = otherUser.CustomAvatarPath ?? otherUser.AvatarUrl
    });
}
```

**Step 2: Build and commit**

```bash
cd apps/api/Codec.Api && dotnet build
git add apps/api/ && git commit -m "feat(voice): add GET /voice/active-call endpoint for reconnect recovery"
```

---

### Task 5: Frontend Types and API Client

**Files:**
- Modify: `apps/web/src/lib/types/models.ts`
- Modify: `apps/web/src/lib/api/client.ts`

**Step 1: Add call-related types to models.ts**

Add at the end of the file:

```typescript
/** DM voice call status. */
export type VoiceCallStatus = 'ringing' | 'active' | 'ended';

/** Active or ringing call returned by GET /voice/active-call. */
export type ActiveCallResponse = {
	id: string;
	dmChannelId: string;
	callerUserId: string;
	recipientUserId: string;
	status: VoiceCallStatus;
	startedAt: string;
	answeredAt?: string | null;
	otherUserId: string;
	otherDisplayName: string;
	otherAvatarUrl?: string | null;
};

/** Message type discriminator. */
export type MessageType = 'regular' | 'voiceCallEvent';
```

Add `messageType` to the `DirectMessage` type:

```typescript
export type DirectMessage = {
	// ... existing fields ...
	messageType?: number; // 0 = regular, 1 = voiceCallEvent
};
```

**Step 2: Add getActiveCall to ApiClient**

In `apps/web/src/lib/api/client.ts`, add:

```typescript
async getActiveCall(token: string): Promise<ActiveCallResponse | null> {
	try {
		return await this.request<ActiveCallResponse>('GET', '/voice/active-call', token);
	} catch {
		return null; // 204 No Content or error
	}
}
```

**Step 3: Commit**

```bash
git add apps/web/src/lib/types/ apps/web/src/lib/api/
git commit -m "feat(voice): add call types and getActiveCall API method"
```

---

### Task 6: ChatHubService — Call signaling methods and events

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts`

**Step 1: Add call event types**

Add after existing event types:

```typescript
export type IncomingCallEvent = {
	callId: string;
	dmChannelId: string;
	callerUserId: string;
	callerDisplayName: string;
	callerAvatarUrl?: string | null;
};

export type CallAcceptedEvent = {
	callId: string;
	dmChannelId: string;
	roomId: string;
};

export type CallDeclinedEvent = {
	callId: string;
	dmChannelId: string;
};

export type CallEndedEvent = {
	callId: string;
	dmChannelId: string;
	endReason: string;
	durationSeconds?: number | null;
};

export type CallMissedEvent = {
	callId: string;
	dmChannelId: string;
};
```

**Step 2: Add callback types to SignalRCallbacks**

```typescript
onIncomingCall?: (event: IncomingCallEvent) => void;
onCallAccepted?: (event: CallAcceptedEvent) => void;
onCallDeclined?: (event: CallDeclinedEvent) => void;
onCallEnded?: (event: CallEndedEvent) => void;
onCallMissed?: (event: CallMissedEvent) => void;
```

**Step 3: Register event handlers in start()**

Add after existing event registrations:

```typescript
if (callbacks.onIncomingCall) {
	connection.on('IncomingCall', callbacks.onIncomingCall);
}
if (callbacks.onCallAccepted) {
	connection.on('CallAccepted', callbacks.onCallAccepted);
}
if (callbacks.onCallDeclined) {
	connection.on('CallDeclined', callbacks.onCallDeclined);
}
if (callbacks.onCallEnded) {
	connection.on('CallEnded', callbacks.onCallEnded);
}
if (callbacks.onCallMissed) {
	connection.on('CallMissed', callbacks.onCallMissed);
}
```

**Step 4: Add hub invocation methods**

Add in the Voice section:

```typescript
async startCall(dmChannelId: string): Promise<{
	callId: string;
	recipientUserId: string;
	recipientDisplayName: string;
	recipientAvatarUrl?: string | null;
}> {
	if (!this.isConnected) throw new Error('Hub not connected');
	return this.connection!.invoke('StartCall', dmChannelId);
}

async acceptCall(callId: string): Promise<{
	callId: string;
	roomId: string;
	routerRtpCapabilities: object;
	sendTransportOptions: object;
	recvTransportOptions: object;
	iceServers?: object[];
	alreadyHandled?: boolean;
}> {
	if (!this.isConnected) throw new Error('Hub not connected');
	return this.connection!.invoke('AcceptCall', callId);
}

async setupCallTransports(callId: string): Promise<{
	routerRtpCapabilities: object;
	sendTransportOptions: object;
	recvTransportOptions: object;
	members: object[];
	iceServers?: object[];
}> {
	if (!this.isConnected) throw new Error('Hub not connected');
	return this.connection!.invoke('SetupCallTransports', callId);
}

async declineCall(callId: string): Promise<void> {
	if (!this.isConnected) throw new Error('Hub not connected');
	await this.connection!.invoke('DeclineCall', callId);
}

async endCall(): Promise<void> {
	if (this.isConnected) {
		await this.connection!.invoke('EndCall').catch(() => {});
	}
}
```

**Step 5: Commit**

```bash
git add apps/web/src/lib/services/chat-hub.ts
git commit -m "feat(voice): add call signaling methods and events to ChatHubService"
```

---

### Task 7: VoiceService — joinWithOptions method

**Files:**
- Modify: `apps/web/src/lib/services/voice-service.ts`

**Step 1: Add joinWithOptions method**

Add after the `join()` method. This lets the caller/recipient set up WebRTC with pre-fetched transport options instead of calling `hub.joinVoiceChannel()`:

```typescript
/**
 * Join a voice session with pre-fetched transport options (for DM calls).
 * Skips the hub.joinVoiceChannel() call since transports are already created.
 */
async joinWithOptions(
	options: {
		routerRtpCapabilities: object;
		sendTransportOptions: object;
		recvTransportOptions: object;
		members?: (VoiceChannelMember & { producerId?: string })[];
		iceServers?: { urls: string[]; username: string; credential: string }[];
	},
	hub: ChatHubService,
	callbacks: VoiceServiceCallbacks
): Promise<VoiceChannelMember[]> {
	this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
	const audioTrack = this.localStream.getAudioTracks()[0];

	this.device = new Device();
	const routerRtpCapabilities = options.routerRtpCapabilities as RtpCapabilities;
	const sendTransportOptions = options.sendTransportOptions as TransportOptions;
	const recvTransportOptions = options.recvTransportOptions as TransportOptions;
	const members = (options.members ?? []) as (VoiceChannelMember & { producerId?: string })[];
	const iceServers = options.iceServers;

	await this.device.load({ routerRtpCapabilities });

	const sendTransport = this.device.createSendTransport({
		...sendTransportOptions,
		...(iceServers ? { iceServers } : {}),
	});
	this.sendTransport = sendTransport;
	sendTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
		hub.connectTransport(sendTransport.id, dtlsParameters).then(callback).catch(errback);
	});
	sendTransport.on('produce', ({ kind, rtpParameters }, callback, errback) => {
		hub.produce(sendTransport.id, rtpParameters)
			.then((producerId) => callback({ id: producerId }))
			.catch(errback);
	});

	const recvTransport = this.device.createRecvTransport({
		...recvTransportOptions,
		...(iceServers ? { iceServers } : {}),
	});
	this.recvTransport = recvTransport;
	recvTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
		hub.connectTransport(recvTransport.id, dtlsParameters).then(callback).catch(errback);
	});

	this.producer = await sendTransport.produce({ track: audioTrack });

	for (const member of members) {
		if (member.producerId) {
			await this.consumeProducer(member.producerId, member.participantId, hub, callbacks);
		}
	}

	return members;
}
```

**Step 2: Commit**

```bash
git add apps/web/src/lib/services/voice-service.ts
git commit -m "feat(voice): add joinWithOptions to VoiceService for DM call WebRTC setup"
```

---

### Task 8: AppState — Call state and methods

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add call state properties**

Add after the existing voice state properties:

```typescript
/* ───── calls ───── */
activeCall = $state<{
	callId: string;
	dmChannelId: string;
	otherUserId: string;
	otherDisplayName: string;
	otherAvatarUrl?: string | null;
	status: 'ringing' | 'active';
	startedAt: string;
	answeredAt?: string;
} | null>(null);

incomingCall = $state<{
	callId: string;
	dmChannelId: string;
	callerUserId: string;
	callerDisplayName: string;
	callerAvatarUrl?: string | null;
} | null>(null);
```

**Step 2: Add startCall method**

```typescript
async startCall(dmChannelId: string): Promise<void> {
	if (this.activeCall || this.incomingCall) return;

	// Leave any active server voice channel.
	if (this.activeVoiceChannelId) {
		await this.leaveVoiceChannel();
	}

	try {
		const result = await this.hub.startCall(dmChannelId);
		this.activeCall = {
			callId: result.callId,
			dmChannelId,
			otherUserId: result.recipientUserId,
			otherDisplayName: result.recipientDisplayName,
			otherAvatarUrl: result.recipientAvatarUrl,
			status: 'ringing',
			startedAt: new Date().toISOString(),
		};
	} catch (e) {
		this.setError(e);
	}
}
```

**Step 3: Add acceptCall method**

```typescript
async acceptCall(callId: string): Promise<void> {
	if (!this.incomingCall || this.incomingCall.callId !== callId) return;

	// Leave any active server voice channel.
	if (this.activeVoiceChannelId) {
		await this.leaveVoiceChannel();
	}

	const caller = this.incomingCall;
	this.incomingCall = null;

	try {
		const result = await this.hub.acceptCall(callId);

		if ('alreadyHandled' in result && result.alreadyHandled) return;

		this.activeCall = {
			callId,
			dmChannelId: caller.dmChannelId,
			otherUserId: caller.callerUserId,
			otherDisplayName: caller.callerDisplayName,
			otherAvatarUrl: caller.callerAvatarUrl,
			status: 'active',
			startedAt: new Date().toISOString(),
			answeredAt: new Date().toISOString(),
		};

		// Set up WebRTC with the transport options from AcceptCall.
		const members = await this.voice.joinWithOptions(
			{
				routerRtpCapabilities: result.routerRtpCapabilities,
				sendTransportOptions: result.sendTransportOptions,
				recvTransportOptions: result.recvTransportOptions,
				iceServers: result.iceServers as any,
			},
			this.hub,
			{
				onNewTrack: (pid, track) => {
					this._attachRemoteAudio(pid, caller.callerUserId, track);
				},
				onTrackEnded: (pid) => this._detachRemoteAudio(pid),
			}
		);

		this.isMuted = false;
		this.isDeafened = false;

		if (this.voiceInputMode === 'push-to-talk') {
			this.voice.setMuted(true);
			this.isPttActive = false;
			this._registerPttListeners();
		}
	} catch (e) {
		console.error('[Voice] Failed to accept call:', e);
		this.activeCall = null;
		try { await this.hub.endCall(); } catch { /* ignore */ }
		await this.voice.leave();
		this._cleanupRemoteAudio();
		this.setError(e);
	}
}
```

**Step 4: Add declineCall method**

```typescript
async declineCall(callId: string): Promise<void> {
	if (!this.incomingCall || this.incomingCall.callId !== callId) return;
	this.incomingCall = null;

	try {
		await this.hub.declineCall(callId);
	} catch {
		// ignore
	}
}
```

**Step 5: Add endCall method**

```typescript
async endCall(): Promise<void> {
	if (!this.activeCall) return;
	this.activeCall = null;

	try {
		await this.hub.endCall();
	} catch {
		// ignore
	}

	await this.voice.leave();
	this._cleanupRemoteAudio();
	this.isMuted = false;
	this.isDeafened = false;
	this.isPttActive = false;
	this._removePttListeners();
}
```

**Step 6: Add checkActiveCall method (for reconnect)**

```typescript
async checkActiveCall(): Promise<void> {
	if (!this.token) return;
	const call = await this.api.getActiveCall(this.token);
	if (!call) return;

	if (call.status === 'ringing') {
		if (call.callerUserId === this.me?.user.id) {
			// We initiated the call — restore ringing state.
			this.activeCall = {
				callId: call.id,
				dmChannelId: call.dmChannelId,
				otherUserId: call.otherUserId,
				otherDisplayName: call.otherDisplayName,
				otherAvatarUrl: call.otherAvatarUrl,
				status: 'ringing',
				startedAt: call.startedAt,
			};
		} else {
			// We are the recipient — show incoming call.
			this.incomingCall = {
				callId: call.id,
				dmChannelId: call.dmChannelId,
				callerUserId: call.otherUserId,
				callerDisplayName: call.otherDisplayName,
				callerAvatarUrl: call.otherAvatarUrl,
			};
		}
	}
	// Active calls after refresh can't be rejoined without re-establishing WebRTC,
	// so just end them cleanly.
	if (call.status === 'active') {
		try { await this.hub.endCall(); } catch { /* ignore */ }
	}
}
```

**Step 7: Wire up call event callbacks in the hub start() call**

In the `init()` or wherever `hub.start()` is called with callbacks, add:

```typescript
onIncomingCall: (event) => {
	// Don't show incoming call if we already have one or are in a call.
	if (this.activeCall || this.incomingCall) return;
	this.incomingCall = event;
},
onCallAccepted: async (event) => {
	if (!this.activeCall || this.activeCall.callId !== event.callId) return;

	try {
		// Caller: set up our transports now.
		const transportResult = await this.hub.setupCallTransports(event.callId);

		this.activeCall = { ...this.activeCall, status: 'active', answeredAt: new Date().toISOString() };

		const members = await this.voice.joinWithOptions(
			{
				routerRtpCapabilities: transportResult.routerRtpCapabilities,
				sendTransportOptions: transportResult.sendTransportOptions,
				recvTransportOptions: transportResult.recvTransportOptions,
				members: transportResult.members as any,
				iceServers: transportResult.iceServers as any,
			},
			this.hub,
			{
				onNewTrack: (pid, track) => {
					this._attachRemoteAudio(pid, this.activeCall!.otherUserId, track);
				},
				onTrackEnded: (pid) => this._detachRemoteAudio(pid),
			}
		);

		this.isMuted = false;
		this.isDeafened = false;

		if (this.voiceInputMode === 'push-to-talk') {
			this.voice.setMuted(true);
			this.isPttActive = false;
			this._registerPttListeners();
		}
	} catch (e) {
		console.error('[Voice] Failed to set up call as caller:', e);
		this.activeCall = null;
		await this.voice.leave();
		this._cleanupRemoteAudio();
		this.setError(e);
	}
},
onCallDeclined: (event) => {
	if (this.activeCall?.callId === event.callId) {
		this.activeCall = null;
	}
},
onCallEnded: (event) => {
	if (this.activeCall?.callId === event.callId) {
		this.activeCall = null;
		this.voice.leave();
		this._cleanupRemoteAudio();
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this._removePttListeners();
	}
},
onCallMissed: (event) => {
	if (this.activeCall?.callId === event.callId) {
		this.activeCall = null;
	}
	if (this.incomingCall?.callId === event.callId) {
		this.incomingCall = null;
	}
},
```

**Step 8: Call checkActiveCall on reconnect**

In the `onReconnected` callback, add:
```typescript
await this.checkActiveCall();
```

**Step 9: Commit**

```bash
git add apps/web/src/lib/state/ apps/web/src/lib/types/ apps/web/src/lib/api/
git commit -m "feat(voice): add call state management and event handlers to AppState"
```

---

### Task 9: IncomingCallOverlay Component

**Files:**
- Create: `apps/web/src/lib/components/voice/IncomingCallOverlay.svelte`
- Modify: `apps/web/src/routes/+page.svelte`

**Step 1: Create IncomingCallOverlay.svelte**

A global overlay that appears when `app.incomingCall` is set. Shows caller info, accept/decline buttons, and a ring animation.

```svelte
<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let ringAudio: HTMLAudioElement | null = null;

	onMount(() => {
		// Play a simple ring tone using oscillator (no external audio file needed).
		// Could be replaced with an audio file later.
		try {
			const ctx = new AudioContext();
			const osc = ctx.createOscillator();
			const gain = ctx.createGain();
			osc.type = 'sine';
			osc.frequency.value = 440;
			gain.gain.value = 0.1;
			osc.connect(gain);
			gain.connect(ctx.destination);
			osc.start();
			// Pulse ring: 1s on, 2s off
			const interval = setInterval(() => {
				gain.gain.value = gain.gain.value > 0 ? 0 : 0.1;
			}, 1000);
			ringAudio = { stop: () => { osc.stop(); ctx.close(); clearInterval(interval); } } as any;
		} catch {
			// Audio not available
		}
	});

	onDestroy(() => {
		(ringAudio as any)?.stop?.();
	});

	function accept() {
		if (app.incomingCall) {
			app.acceptCall(app.incomingCall.callId);
		}
	}

	function decline() {
		if (app.incomingCall) {
			app.declineCall(app.incomingCall.callId);
		}
	}
</script>

{#if app.incomingCall}
	<div class="call-overlay" role="alertdialog" aria-label="Incoming voice call">
		<div class="call-card">
			{#if app.incomingCall.callerAvatarUrl}
				<img class="caller-avatar" src={app.incomingCall.callerAvatarUrl} alt="" />
			{:else}
				<div class="caller-avatar-placeholder">
					{app.incomingCall.callerDisplayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<div class="caller-name">{app.incomingCall.callerDisplayName}</div>
			<div class="call-label">Incoming Voice Call</div>
			<div class="call-actions">
				<button class="call-btn accept-btn" onclick={accept} aria-label="Accept call">
					<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
						<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
					</svg>
				</button>
				<button class="call-btn decline-btn" onclick={decline} aria-label="Decline call">
					<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
						<path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08a.956.956 0 0 1-.01-1.36c3.36-3.13 7.53-4.96 11.72-4.96s8.36 1.83 11.72 4.96c.18.18.29.42.29.68 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28a11.27 11.27 0 0 0-2.67-1.85.996.996 0 0 1-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/>
					</svg>
				</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.call-overlay {
		position: fixed;
		inset: 0;
		z-index: 200;
		display: grid;
		place-items: center;
		background: rgba(0, 0, 0, 0.7);
	}
	.call-card {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 12px;
		padding: 32px 40px;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		min-width: 280px;
	}
	.caller-avatar, .caller-avatar-placeholder {
		width: 80px;
		height: 80px;
		border-radius: 50%;
		object-fit: cover;
	}
	.caller-avatar-placeholder {
		background: var(--bg-tertiary);
		display: grid;
		place-items: center;
		font-size: 32px;
		color: var(--text-muted);
		font-weight: 600;
	}
	.caller-name {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
	}
	.call-label {
		font-size: 13px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.05em;
		animation: pulse 2s ease-in-out infinite;
	}
	.call-actions {
		display: flex;
		gap: 24px;
		margin-top: 12px;
	}
	.call-btn {
		width: 56px;
		height: 56px;
		border-radius: 50%;
		border: none;
		cursor: pointer;
		display: grid;
		place-items: center;
		transition: filter 150ms ease;
	}
	.call-btn:hover { filter: brightness(1.15); }
	.accept-btn { background: var(--success); color: #000; }
	.decline-btn { background: var(--danger); color: #fff; }
	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50% { opacity: 0.5; }
	}
</style>
```

**Step 2: Add to +page.svelte**

Import and render `IncomingCallOverlay` alongside other global overlays. Add after the existing modal components:

```svelte
<IncomingCallOverlay />
```

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/voice/ apps/web/src/routes/
git commit -m "feat(voice): add IncomingCallOverlay component"
```

---

### Task 10: DmChatArea — Call button and system messages

**Files:**
- Modify: `apps/web/src/lib/components/dm/DmChatArea.svelte`

**Step 1: Add call button to the DM header**

In the `<header class="dm-header">` section, after `<div class="dm-header-left">...</div>`, add:

```svelte
<div class="dm-header-right">
	<button
		class="call-btn-header"
		disabled={!!app.activeCall || !!app.incomingCall}
		onclick={() => { if (app.activeDmChannelId) app.startCall(app.activeDmChannelId); }}
		aria-label="Start voice call"
		title="Start voice call"
	>
		<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
			<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
		</svg>
	</button>
</div>
```

**Step 2: Add system message rendering in the message feed**

In the `{#each app.dmMessages as message}` block, add a conditional for voice call events before the regular message article:

```svelte
{#if message.messageType === 1}
	<div class="system-message voice-call-event">
		<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" class="call-event-icon" class:missed={message.body === 'missed'}>
			<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
		</svg>
		<span class="call-event-text">
			{#if message.body === 'missed'}
				Missed voice call
			{:else if message.body?.startsWith('call:')}
				{@const secs = parseInt(message.body.split(':')[1] ?? '0')}
				Voice call — {Math.floor(secs / 60)}m {secs % 60}s
			{:else}
				Voice call
			{/if}
		</span>
		<time class="call-event-time">{formatTime(message.createdAt)}</time>
	</div>
{:else}
	<!-- existing message rendering -->
{/if}
```

**Step 3: Add styles**

```css
.dm-header-right {
	margin-left: auto;
	display: flex;
	align-items: center;
}
.call-btn-header {
	background: none;
	border: none;
	color: var(--text-muted);
	cursor: pointer;
	padding: 6px;
	border-radius: 4px;
	display: grid;
	place-items: center;
	transition: color 150ms ease, background 150ms ease;
}
.call-btn-header:hover:not(:disabled) {
	color: var(--text-normal);
	background: var(--bg-message-hover);
}
.call-btn-header:disabled {
	opacity: 0.4;
	cursor: not-allowed;
}
.system-message.voice-call-event {
	display: flex;
	align-items: center;
	justify-content: center;
	gap: 8px;
	padding: 8px 16px;
	color: var(--text-muted);
	font-size: 13px;
}
.call-event-icon {
	color: var(--success);
	flex-shrink: 0;
}
.call-event-icon.missed {
	color: var(--danger);
}
.call-event-time {
	font-size: 11px;
	color: var(--text-dim);
}
```

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/dm/
git commit -m "feat(voice): add call button and system messages to DmChatArea"
```

---

### Task 11: DmCallHeader Component

**Files:**
- Create: `apps/web/src/lib/components/voice/DmCallHeader.svelte`

**Step 1: Create the component**

Shows during an active DM call: other party info, call duration timer, mute/deafen/hang-up controls. During ringing: shows "Calling..." with cancel.

```svelte
<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let elapsed = $state(0);
	let timer: ReturnType<typeof setInterval> | null = null;

	onMount(() => {
		if (app.activeCall?.status === 'active' && app.activeCall.answeredAt) {
			const start = new Date(app.activeCall.answeredAt).getTime();
			timer = setInterval(() => {
				elapsed = Math.floor((Date.now() - start) / 1000);
			}, 1000);
		}
	});

	// Start timer when call becomes active.
	$effect(() => {
		if (app.activeCall?.status === 'active' && app.activeCall.answeredAt && !timer) {
			const start = new Date(app.activeCall.answeredAt).getTime();
			timer = setInterval(() => {
				elapsed = Math.floor((Date.now() - start) / 1000);
			}, 1000);
		}
		if (!app.activeCall && timer) {
			clearInterval(timer);
			timer = null;
			elapsed = 0;
		}
	});

	onDestroy(() => {
		if (timer) clearInterval(timer);
	});

	const formattedTime = $derived(() => {
		const m = Math.floor(elapsed / 60);
		const s = elapsed % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	});
</script>

{#if app.activeCall}
	<div class="dm-call-header" role="status" aria-label="Voice call in progress">
		<div class="call-info">
			{#if app.activeCall.otherAvatarUrl}
				<img class="call-avatar" src={app.activeCall.otherAvatarUrl} alt="" />
			{:else}
				<div class="call-avatar-placeholder">
					{app.activeCall.otherDisplayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<div class="call-details">
				<span class="call-name">{app.activeCall.otherDisplayName}</span>
				{#if app.activeCall.status === 'ringing'}
					<span class="call-status ringing">Calling...</span>
				{:else}
					<span class="call-status active">{formattedTime()}</span>
				{/if}
			</div>
		</div>
		<div class="call-controls">
			{#if app.activeCall.status === 'active'}
				<button
					class="ctl-btn"
					class:active={app.isMuted}
					onclick={() => app.toggleMute()}
					aria-label={app.isMuted ? 'Unmute' : 'Mute'}
				>
					{#if app.isMuted}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/></svg>
					{:else}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/></svg>
					{/if}
				</button>
				<button
					class="ctl-btn"
					class:active={app.isDeafened}
					onclick={() => app.toggleDeafen()}
					aria-label={app.isDeafened ? 'Undeafen' : 'Deafen'}
				>
					{#if app.isDeafened}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1C7.03 1 3 5.03 3 10v3c0 1.1.9 2 2 2h1v-5H5c0-3.87 3.13-7 7-7s7 3.13 7 7h-1v5h1c1.1 0 2-.9 2-2v-3c0-4.97-4.03-9-9-9zm-1 14h2v1h-2zm5.5 1.5h-1.01L16 21H8l-.49-4.5H6.5c-.28 0-.5-.22-.5-.5v-4c0-.28.22-.5.5-.5h11c.28 0 .5.22.5.5v4c0 .28-.22.5-.5.5z"/></svg>
					{:else}
						<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1c-4.97 0-9 4.03-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2c0-3.87 3.13-7 7-7s7 3.13 7 7v2h-4v8h3c1.66 0 3-1.34 3-3v-7c0-4.97-4.03-9-9-9z"/></svg>
					{/if}
				</button>
			{/if}
			<button
				class="ctl-btn end-btn"
				onclick={() => app.endCall()}
				aria-label={app.activeCall.status === 'ringing' ? 'Cancel call' : 'End call'}
			>
				<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 9c-1.6 0-3.15.25-4.6.72v3.1c0 .39-.23.74-.56.9-.98.49-1.87 1.12-2.66 1.85-.18.18-.43.28-.7.28-.28 0-.53-.11-.71-.29L.29 13.08a.956.956 0 0 1-.01-1.36c3.36-3.13 7.53-4.96 11.72-4.96s8.36 1.83 11.72 4.96c.18.18.29.42.29.68 0 .28-.11.53-.29.71l-2.48 2.48c-.18.18-.43.29-.71.29-.27 0-.52-.11-.7-.28a11.27 11.27 0 0 0-2.67-1.85.996.996 0 0 1-.56-.9v-3.1C15.15 9.25 13.6 9 12 9z"/></svg>
			</button>
		</div>
	</div>
{/if}

<style>
	.dm-call-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 16px;
		background: var(--bg-tertiary);
		border-bottom: 1px solid var(--border);
	}
	.call-info {
		display: flex;
		align-items: center;
		gap: 10px;
	}
	.call-avatar, .call-avatar-placeholder {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		object-fit: cover;
	}
	.call-avatar-placeholder {
		background: var(--bg-primary);
		display: grid;
		place-items: center;
		font-size: 14px;
		color: var(--text-muted);
		font-weight: 600;
	}
	.call-name {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
	}
	.call-details {
		display: flex;
		flex-direction: column;
	}
	.call-status {
		font-size: 12px;
	}
	.call-status.ringing {
		color: var(--warn);
		animation: pulse 1.5s ease-in-out infinite;
	}
	.call-status.active {
		color: var(--success);
		font-variant-numeric: tabular-nums;
	}
	.call-controls {
		display: flex;
		gap: 4px;
	}
	.ctl-btn {
		background: none;
		border: none;
		padding: 6px;
		cursor: pointer;
		color: var(--text-muted);
		border-radius: 4px;
		display: grid;
		place-items: center;
		min-width: 32px;
		min-height: 32px;
		transition: background 150ms ease, color 150ms ease;
	}
	.ctl-btn:hover { background: var(--bg-message-hover); color: var(--text-normal); }
	.ctl-btn.active { color: var(--danger); }
	.end-btn:hover { background: var(--danger); color: #fff; }
	@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
</style>
```

**Step 2: Integrate into DmChatArea**

Import and render `DmCallHeader` at the top of the DM chat area, between the header and the body, when there's an active call for the current DM:

```svelte
{#if app.activeCall && app.activeCall.dmChannelId === app.activeDmChannelId}
	<DmCallHeader />
{/if}
```

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/voice/ apps/web/src/lib/components/dm/
git commit -m "feat(voice): add DmCallHeader component with timer and controls"
```

---

### Task 12: VoiceConnectedBar — DM call support

**Files:**
- Modify: `apps/web/src/lib/components/channel-sidebar/VoiceConnectedBar.svelte`

**Step 1: Update to show DM call info**

Update the component to handle both server voice channels and DM calls:

Replace the `channelName` derived with:

```typescript
const label = $derived(() => {
	if (app.activeCall) {
		return app.activeCall.otherDisplayName;
	}
	return app.channels.find((c) => c.id === app.activeVoiceChannelId)?.name ?? 'Voice';
});

const isInCall = $derived(!!app.activeCall);
```

Update the template: show the bar when either `activeVoiceChannelId` or `activeCall` is set. When in a DM call, show "In call with {name}" instead of "# {channelName}". Use `app.endCall()` for the leave button when in a call.

**Step 2: Update the condition for showing the bar**

In the parent component that renders VoiceConnectedBar, ensure it shows when `app.activeVoiceChannelId || app.activeCall`.

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/channel-sidebar/
git commit -m "feat(voice): update VoiceConnectedBar to support DM calls"
```

---

### Task 13: Integration and edge case handling

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs` (OnDisconnectedAsync refinement)
- Modify: `apps/web/src/lib/state/app-state.svelte.ts` (teardown, beforeunload)

**Step 1: Refine OnDisconnectedAsync for call cleanup**

Ensure that when a user disconnects:
- If they have a Ringing call (as caller or recipient), end it as Missed
- If they have an Active call, end it with duration and clean up SFU

**Step 2: Update teardownVoiceSync for calls**

In `teardownVoiceSync()`, also clear `activeCall` and `incomingCall`.

**Step 3: Update beforeunload handler**

In `+page.svelte`, the existing `beforeunload` handler calls `app.teardownVoiceSync()`. Ensure the hub `EndCall` is sent synchronously (or best-effort) — the server `OnDisconnectedAsync` handles the actual cleanup.

**Step 4: Build both projects**

```bash
cd apps/api/Codec.Api && dotnet build
cd apps/web && npm run check
```

**Step 5: Commit**

```bash
git add apps/api/ apps/web/
git commit -m "feat(voice): handle disconnect edge cases for DM calls"
```

---

### Task 14: Documentation Update

**Files:**
- Modify: `docs/plans/2026-03-03-voice-phase3-design.md` (mark as implemented)
- Modify: `VOICE.md` attachment or `docs/` (update Phase 3 status)

**Step 1: Update VOICE.md Phase 3 status**

Change Phase 3 from `📋 Planned` to `✅ Complete`.

**Step 2: Commit**

```bash
git add docs/ .context/
git commit -m "docs: mark voice Phase 3 as complete"
```
