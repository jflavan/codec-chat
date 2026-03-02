# Voice Feature Beta Hardening Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix all 11 critical issues identified in the comprehensive voice code review so the feature is ready for beta testing.

**Architecture:** The voice feature spans 4 layers: ASP.NET Core API (SignalR hub + REST), Node.js SFU (mediasoup), SvelteKit frontend (mediasoup-client), and Azure infrastructure (Bicep + docker-compose). Fixes are grouped by layer and can be parallelized across layers.

**Tech Stack:** C# / .NET 10, TypeScript / SvelteKit / Svelte 5, Node.js / Express / mediasoup, Bicep / docker-compose

---

## Task 1: Fix TURN credential HMAC algorithm (API)

**Problem:** `VoiceController.cs` uses `HMACSHA256` but coturn's default is HMAC-SHA1. The coturn config does specify `sha256` (coturn 4.6.0+), but this is fragile. The safer approach: switch the API to SHA-1 for maximum coturn compatibility and remove the `sha256` flag from coturn config.

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/VoiceController.cs:112-115`
- Modify: `infra/voice/docker-compose.yml:61`

**Step 1: Change HMAC to SHA-1 in VoiceController**

In `apps/api/Codec.Api/Controllers/VoiceController.cs`, replace lines 112-115:

```csharp
// BEFORE:
var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
var msgBytes = Encoding.UTF8.GetBytes(username);
using var hmac = new HMACSHA256(keyBytes);
var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
```

With:

```csharp
// AFTER:
var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
var msgBytes = Encoding.UTF8.GetBytes(username);
using var hmac = new HMACSHA1(keyBytes);
var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
```

Also update the XML doc comment on line 93-95 to remove the `--sha256` coturn requirement:

```csharp
/// Issues short-lived TURN credentials using HMAC-SHA1 time-limited authentication.
/// The secret never leaves the server; clients receive a username + credential pair valid for 1 hour.
/// Compatible with coturn's default lt-cred-mech + use-auth-secret configuration.
```

**Step 2: Remove `sha256` from coturn config**

In `infra/voice/docker-compose.yml`, remove line 61 (`'sha256' \`) and update the comment on line 42:

```yaml
# Must match the HMAC algorithm used by the API (HMAC-SHA1, coturn's default).
```

**Step 3: Verify API builds**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`
Expected: Build succeeded, 0 errors.

---

## Task 2: Validate TurnSecret at startup + fix empty-string check (API)

**Problem:** Empty `TurnSecret` (`""`) passes the `?? throw` null check. Config is only validated at request time instead of startup.

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/VoiceController.cs:100-101`
- Modify: `apps/api/Codec.Api/Program.cs` (after line 101, before `builder.Services.AddHealthChecks()`)

**Step 1: Fix empty-string check in VoiceController**

Replace line 100-101:

```csharp
// BEFORE:
var turnSecret = config["Voice:TurnSecret"]
    ?? throw new InvalidOperationException("Voice:TurnSecret is required.");
```

With:

```csharp
// AFTER:
var turnSecret = config["Voice:TurnSecret"];
if (string.IsNullOrWhiteSpace(turnSecret))
    throw new InvalidOperationException("Voice:TurnSecret is required.");
```

**Step 2: Add startup validation in Program.cs**

After the SFU HttpClient registration (after line 101 in Program.cs), add:

```csharp
// Validate voice configuration at startup in non-development environments.
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["Voice:TurnSecret"]))
        throw new InvalidOperationException("Voice:TurnSecret must be configured in production.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["Voice:SfuInternalKey"]))
        throw new InvalidOperationException("Voice:SfuInternalKey must be configured in production.");
}
```

**Step 3: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`

---

## Task 3: Clean up voice sessions on channel deletion (API)

**Problem:** `DeleteChannel` in `ServersController.cs` doesn't clean up VoiceState rows, SFU participants, or broadcast `UserLeftVoice`.

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs:795-851`

**Step 1: Add voice cleanup before channel deletion**

The `ServersController` needs access to `IHttpClientFactory` and `IConfiguration` for SFU cleanup. Check if they're already injected; if not, add them to the constructor.

In the `DeleteChannel` method, insert voice cleanup between the authorization check and the existing cascade-delete code (after line 822, before line 824 `// Delete associated data`):

```csharp
// Clean up active voice sessions before deleting the channel.
if (channel.Type == ChannelType.Voice)
{
    var voiceStates = await db.VoiceStates
        .Where(vs => vs.ChannelId == channelId)
        .ToListAsync();

    if (voiceStates.Count > 0)
    {
        // Broadcast UserLeftVoice for each participant before removing state.
        foreach (var vs in voiceStates)
        {
            await hub.Clients.Group($"server-{serverId}").SendAsync("UserLeftVoice", new
            {
                channelId = channelId.ToString(),
                userId = vs.UserId,
                participantId = vs.ParticipantId
            });
        }

        db.VoiceStates.RemoveRange(voiceStates);

        // Clean up SFU room (closes all transports/producers/consumers).
        var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
        try
        {
            using var sfuClient = httpClientFactory.CreateClient("sfu");
            await sfuClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}");
        }
        catch
        {
            // SFU cleanup is best-effort; channel deletion proceeds regardless.
        }
    }
}
```

**Step 2: Ensure IHttpClientFactory and IConfiguration are injected**

Check the `ServersController` constructor. If `IHttpClientFactory` or `IConfiguration` are not already injected, add them as primary constructor parameters.

**Step 3: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`

---

## Task 4: Clean up SFU resources on partial join failure (API)

**Problem:** If the send transport is created but the recv transport fails, the send transport leaks in the SFU.

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs:171-176`

**Step 1: Wrap SFU calls in try-catch with cleanup**

Replace the three SFU calls (around lines 171-176) with:

```csharp
// Call the SFU before touching the DB. If any step fails, clean up any
// resources already created so they don't leak in the SFU room.
JsonElement routerRtpCapabilities, sendTransport, recvTransport;
try
{
    routerRtpCapabilities = await GetOrCreateSfuRoomAsync(sfuApiUrl, channelId);
    sendTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "send");
    recvTransport = await CreateSfuTransportAsync(sfuApiUrl, channelId, Context.ConnectionId, "recv");
}
catch
{
    // Best-effort cleanup of any SFU resources already created for this participant.
    try
    {
        using var cleanupClient = httpClientFactory.CreateClient("sfu");
        await cleanupClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}/participants/{Context.ConnectionId}");
    }
    catch { /* SFU cleanup is best-effort */ }
    throw;
}
```

**Step 2: Verify build**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`

---

## Task 5: Add TCP fallback to SFU transport config (SFU)

**Problem:** UDP-only transport means users behind restrictive firewalls can't connect.

**Files:**
- Modify: `apps/sfu/src/worker.ts:21-26`

**Step 1: Add TCP listenInfo**

Replace the `WEBRTC_TRANSPORT_OPTIONS`:

```typescript
export const WEBRTC_TRANSPORT_OPTIONS = {
  listenInfos: [
    { protocol: 'udp' as const, ip: '0.0.0.0', announcedAddress: ANNOUNCED_IP },
    { protocol: 'tcp' as const, ip: '0.0.0.0', announcedAddress: ANNOUNCED_IP },
  ],
  initialAvailableOutgoingBitrate: 1_000_000,
  minimumAvailableOutgoingBitrate: 600_000,
};
```

Also remove the `maxSctpMessageSize` line (SCTP is unused for audio-only).

**Step 2: Verify TypeScript compiles**

Run: `cd apps/sfu && npx tsc --noEmit`

---

## Task 6: Add Express error handler middleware (SFU)

**Problem:** Unhandled mediasoup errors expose stack traces to the API caller.

**Files:**
- Modify: `apps/sfu/src/index.ts` (after line 53, `app.use(createRoomRouter(worker))`)
- Modify: `apps/sfu/src/rooms.ts` — add input validation to connect/produce/consume endpoints

**Step 1: Add global error handler in index.ts**

After `app.use(createRoomRouter(worker));` (line 53), add:

```typescript
// Global error handler — prevent mediasoup internals from leaking to callers.
app.use((err: Error, _req: express.Request, res: express.Response, _next: express.NextFunction) => {
  console.error('Unhandled error:', err.message);
  res.status(500).json({ error: 'Internal server error' });
});
```

**Step 2: Add input validation to rooms.ts endpoints**

In the `/transports/:transportId/connect` handler (line 88), add after `req.body` destructuring:

```typescript
if (!participantId || !dtlsParameters) {
  return res.status(400).json({ error: 'participantId and dtlsParameters are required' });
}
```

In the `/transports/:transportId/produce` handler (line 111), add after destructuring:

```typescript
if (!participantId || !kind || !rtpParameters) {
  return res.status(400).json({ error: 'participantId, kind, and rtpParameters are required' });
}
if (kind !== 'audio') {
  return res.status(400).json({ error: 'Only audio producers are supported' });
}
```

In the `/consumers` handler (line 133), add after destructuring:

```typescript
if (!producerId || !transportId || !rtpCapabilities || !participantId) {
  return res.status(400).json({ error: 'producerId, transportId, rtpCapabilities, and participantId are required' });
}
```

**Step 3: Add resource cleanup on overwrite in rooms.ts**

In the transports handler (lines 73-77), close previous transports before overwriting:

```typescript
if (direction === 'send') {
  participant.sendTransport?.close();
  participant.sendTransport = transport;
} else {
  participant.recvTransport?.close();
  participant.recvTransport = transport;
}
```

In the produce handler (line 124), close previous producer:

```typescript
participant.producer?.close();
const producer = await participant.sendTransport.produce({ kind, rtpParameters });
```

Fix consumer event handlers (lines 171-172) to clean up Map references:

```typescript
consumer.on('transportclose', () => {
  consumer.close();
  consumerParticipant.consumers.delete(consumer.id);
});
consumer.on('producerclose', () => {
  consumer.close();
  consumerParticipant.consumers.delete(consumer.id);
});
```

And for the producer (line 127):

```typescript
producer.on('transportclose', () => {
  producer.close();
  participant.producer = undefined;
});
```

**Step 4: Verify build**

Run: `cd apps/sfu && npx tsc --noEmit`

---

## Task 7: Add beforeunload handler for voice cleanup (Frontend)

**Problem:** Closing the browser tab leaves mic active and ghost members until SignalR timeout.

**Files:**
- Modify: `apps/web/src/lib/services/voice-service.ts` (add sync cleanup method)
- Modify: `apps/web/src/routes/+page.svelte` (add beforeunload listener)

**Step 1: Add synchronous cleanup method to VoiceService**

Add to `VoiceService` class in `voice-service.ts`, after the `leave()` method:

```typescript
/** Synchronous cleanup for beforeunload — stops mic tracks immediately. */
teardownSync(): void {
  if (this.localStream) {
    for (const track of this.localStream.getTracks()) {
      track.stop();
    }
    this.localStream = null;
  }
  this.sendTransport?.close();
  this.sendTransport = null;
  this.recvTransport?.close();
  this.recvTransport = null;
  this.producer = null;
  this.consumers.clear();
  this.consumedProducerIds.clear();
  this.device = null;
}
```

**Step 2: Add beforeunload handler in +page.svelte**

Replace the `onMount` block (lines 24-36) with:

```typescript
onMount(() => {
  if (!googleClientId) {
    app.error = 'Missing PUBLIC_GOOGLE_CLIENT_ID.';
    app.isInitialLoading = false;
    return;
  }
  if (!apiBaseUrl) {
    app.error = 'Missing PUBLIC_API_BASE_URL.';
    app.isInitialLoading = false;
    return;
  }
  app.init();

  const handleBeforeUnload = () => {
    // Synchronous: stop mic tracks immediately so the browser mic indicator clears.
    app.voice.teardownSync();
  };
  window.addEventListener('beforeunload', handleBeforeUnload);

  return () => {
    window.removeEventListener('beforeunload', handleBeforeUnload);
  };
});
```

Note: `app.voice` is the `VoiceService` instance. Verify it is accessible — check if `voice` is a public property on `AppState`. If it's private, add a public getter or make the `teardownSync` call through `AppState`.

**Step 3: Verify frontend builds**

Run: `cd apps/web && npm run check`

---

## Task 8: Wire up TURN credentials for ICE servers (Frontend + API)

**Problem:** `getTurnCredentials()` exists in the API client but is never called. Users behind NAT cannot connect.

**Files:**
- Modify: `apps/api/Codec.Api/Hubs/ChatHub.cs` (return TURN config in JoinVoiceChannel response)
- Modify: `apps/web/src/lib/services/voice-service.ts:51,65` (inject iceServers into transport creation)

**Approach:** The cleanest fix is to include TURN/ICE server config in the `JoinVoiceChannel` SignalR response (server-side), then the frontend injects it into transport options. This avoids a separate HTTP round-trip.

**Step 1: Include ICE servers in JoinVoiceChannel response (API)**

In `ChatHub.cs`, inside `JoinVoiceChannel`, before the `return new { ... }` block, generate TURN credentials:

```csharp
// Generate TURN credentials for the joining client so they can relay through
// the TURN server when direct UDP is blocked.
var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";
var turnSecret = config["Voice:TurnSecret"] ?? "";
object? iceServers = null;
if (!string.IsNullOrWhiteSpace(turnSecret))
{
    var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
    var username = $"{expiry}:{appUser.Id}";
    var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
    var msgBytes = Encoding.UTF8.GetBytes(username);
    using var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
    var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
    iceServers = new[]
    {
        new { urls = new[] { turnServerUrl }, username, credential }
    };
}
```

Then include it in the return value:

```csharp
return new
{
    routerRtpCapabilities,
    sendTransportOptions = sendTransport,
    recvTransportOptions = recvTransport,
    members,
    iceServers
};
```

**Step 2: Inject iceServers in VoiceService.join() (Frontend)**

In `voice-service.ts`, after extracting the hub result (around line 44), add:

```typescript
const iceServers = (result as any).iceServers as { urls: string[]; username: string; credential: string }[] | undefined;
```

When creating both transports, inject iceServers:

```typescript
const sendTransport = this.device.createSendTransport({
  ...sendTransportOptions,
  ...(iceServers ? { iceServers } : {}),
});

// ... and similarly for recv:
const recvTransport = this.device.createRecvTransport({
  ...recvTransportOptions,
  ...(iceServers ? { iceServers } : {}),
});
```

**Step 3: Verify both build**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`
Run: `cd apps/web && npm run check`

---

## Task 9: Handle voice teardown on SignalR reconnection/close (Frontend)

**Problem:** SignalR disconnect/reconnect doesn't tear down voice. Users get one-way audio or broken state.

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts:1537-1558`

**Step 1: Add voice teardown to onReconnecting and onClose handlers**

Replace the `onReconnecting` handler (lines 1537-1543):

```typescript
onReconnecting: () => {
  this.isHubConnected = false;
  // Tear down voice immediately — the SignalR group membership is lost,
  // so the voice session cannot recover without a full re-join.
  if (this.activeVoiceChannelId) {
    const channelId = this.activeVoiceChannelId;
    this.voice.leave();
    this._cleanupRemoteAudio();
    this.activeVoiceChannelId = null;
    this.isMuted = false;
    this.isDeafened = false;
    this.setTransientError('Voice disconnected due to network interruption.');
  }
  if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
  this.reconnectTimer = setTimeout(() => {
    if (!this.isHubConnected) window.location.reload();
  }, 5000);
},
```

The `onClose` handler (lines 1551-1558) already reloads on error, which will clean up voice. But add voice teardown before the reload decision:

```typescript
onClose: (error) => {
  this.isHubConnected = false;
  if (this.reconnectTimer) {
    clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }
  // Clean up voice session before potential reload.
  if (this.activeVoiceChannelId) {
    this.voice.leave();
    this._cleanupRemoteAudio();
    this.activeVoiceChannelId = null;
    this.isMuted = false;
    this.isDeafened = false;
  }
  if (error) window.location.reload();
},
```

**Step 2: Verify frontend builds**

Run: `cd apps/web && npm run check`

---

## Task 10: Add coturn `external-ip` for correct TURN relay address (Infra)

**Problem:** coturn without `external-ip` may advertise the VM's private IP (10.1.0.x) in relay allocations, making TURN unusable for internet clients.

**Files:**
- Modify: `infra/voice/docker-compose.yml:48-63`

**Step 1: Add external-ip to coturn config**

In the coturn entrypoint script, add `external-ip` using the existing `ANNOUNCED_IP` env var. In the `printf` block, add after `'listening-port=3478'`:

```
"external-ip=${ANNOUNCED_IP}" \
```

Also pass `ANNOUNCED_IP` to the coturn service's environment:

```yaml
coturn:
  # ...
  environment:
    TURN_SECRET: "${TURN_SECRET}"
    ANNOUNCED_IP: "${ANNOUNCED_IP}"
```

The full coturn printf block should become:

```
printf '%s\n' \
  'lt-cred-mech' \
  'use-auth-secret' \
  "static-auth-secret=${TURN_SECRET}" \
  'realm=codec-chat.com' \
  'listening-port=3478' \
  "external-ip=${ANNOUNCED_IP}" \
  'min-port=49152' \
  'max-port=49200' \
  'no-tls' \
  'no-dtls' \
  'log-file=stdout' \
  > /tmp/turnserver.conf
exec turnserver -c /tmp/turnserver.conf
```

(Note: `sha256` line is also removed per Task 1.)

---

## Task 11: Add SFU ANNOUNCED_IP production validation (SFU)

**Problem:** SFU defaults to `127.0.0.1` for ANNOUNCED_IP. In Docker, this means ICE candidates are unreachable.

**Files:**
- Modify: `apps/sfu/src/worker.ts:14-15`

**Step 1: Add production validation**

Replace lines 14-15:

```typescript
/** Announced IP for ICE candidates. Must be the public IP in production. */
const ANNOUNCED_IP = process.env.ANNOUNCED_IP ?? '127.0.0.1';

if (process.env.NODE_ENV === 'production' && ANNOUNCED_IP === '127.0.0.1') {
  console.error('FATAL: ANNOUNCED_IP must be set to the public IP in production. Refusing to start.');
  process.exit(1);
}
```

---

## Task 12: Commit and push all changes

**Step 1: Build verification**

Run: `cd apps/api/Codec.Api && dotnet build --nologo`
Run: `cd apps/web && npm run check`
Run: `cd apps/sfu && npx tsc --noEmit`

All must pass with 0 errors.

**Step 2: Commit**

Stage only the modified voice files. Commit with:

```
fix(voice): harden voice feature for beta release

- Switch TURN credential HMAC from SHA-256 to SHA-1 for coturn compat
- Validate TurnSecret/SfuInternalKey at startup in production
- Clean up voice sessions on channel deletion (SFU + DB + broadcast)
- Fix SFU transport leak on partial join failure
- Add TCP fallback to SFU WebRTC transport config
- Add Express error handler to prevent mediasoup stack trace leaks
- Add input validation and resource cleanup to SFU endpoints
- Add beforeunload handler to stop mic on tab close
- Wire up TURN credentials in voice join flow for NAT traversal
- Tear down voice session on SignalR disconnect/reconnect
- Add coturn external-ip for correct relay address advertisement
- Add ANNOUNCED_IP production validation to SFU
```

**Step 3: Push**

`git push`
