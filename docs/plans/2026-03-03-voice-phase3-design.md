# Voice Phase 3: Direct Voice Calls from DMs

## Overview

Add Discord-style 1:1 voice calls initiated from DM conversations. Clicking a call button rings the recipient with accept/decline UI and a 30-second timeout. Answered calls show duration; missed/declined calls show "Missed voice call" as a system message in the DM chat. Users can only be in one voice session at a time (server voice channel OR DM call).

## Data Model

### New: VoiceCall Entity

```csharp
public class VoiceCall
{
    public Guid Id { get; set; }
    public Guid DmChannelId { get; set; }
    public Guid CallerUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public VoiceCallStatus Status { get; set; }       // Ringing, Active, Ended
    public VoiceCallEndReason? EndReason { get; set; } // Answered, Missed, Declined
    public DateTimeOffset StartedAt { get; set; }      // When call initiated
    public DateTimeOffset? AnsweredAt { get; set; }     // When recipient accepted
    public DateTimeOffset? EndedAt { get; set; }        // When call ended
}

public enum VoiceCallStatus { Ringing = 0, Active = 1, Ended = 2 }
public enum VoiceCallEndReason { Answered = 0, Missed = 1, Declined = 2 }
```

### Modified: VoiceState

Add nullable `DmChannelId` (FK to `DmChannels`). A voice session is either in a server channel (`ChannelId`) or a DM channel (`DmChannelId`), never both.

### Modified: DirectMessage

Add `MessageType` property (`Regular = 0`, `VoiceCallEvent = 1`). For voice call events, `Body` stores `"missed"` or `"call:{durationSeconds}"`. The frontend renders these as styled system messages.

### Migration

One EF Core migration:
- Create `VoiceCalls` table with FKs to `DmChannels` and `Users`
- Add `DmChannelId` column to `VoiceStates` (nullable FK)
- Add `MessageType` column to `DirectMessages` (default 0)

## SignalR Signaling

### Hub Methods (client -> server)

| Method | Parameters | Description |
|--------|-----------|-------------|
| `StartCall` | `dmChannelId` | Create VoiceCall (Ringing), send `IncomingCall` to recipient, start 30s timer |
| `AcceptCall` | `callId` | Set Active, create SFU room (`call-{callId}`), return transport options to both |
| `DeclineCall` | `callId` | End as Declined, notify caller, persist "Missed call" system message |
| `EndCall` | — | Either party hangs up. Cleanup SFU, notify other, persist system message |

### Events (server -> client)

| Event | Payload | Description |
|-------|---------|-------------|
| `IncomingCall` | `{ callId, dmChannelId, callerUserId, callerDisplayName, callerAvatarUrl }` | Triggers ringing UI on recipient |
| `CallAccepted` | `{ callId, dmChannelId, routerRtpCapabilities, sendTransportOptions, recvTransportOptions, iceServers }` | Caller begins WebRTC setup |
| `CallDeclined` | `{ callId, dmChannelId }` | Recipient declined |
| `CallEnded` | `{ callId, dmChannelId, endReason, durationSeconds? }` | Other party hung up or call ended |
| `CallMissed` | `{ callId, dmChannelId }` | 30s timeout expired |

### Timeout Mechanism

- `ConcurrentDictionary<Guid, CancellationTokenSource>` keyed by call ID
- `StartCall` schedules `Task.Delay(30s)` — if still Ringing, ends as Missed
- Accept/decline/end cancels the timer
- Background cleanup job runs every 60s to catch stale Ringing calls (e.g., after API restart)

### SFU Room ID Scheme

DM calls use `call-{callId}` as the SFU room ID (vs `{channelId}` for server voice channels). No SFU code changes needed.

## REST API

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/voice/active-call` | Returns active VoiceCall (Ringing or Active) for the caller, or 204. Used on page load/reconnect. |

## Edge Cases

**Call collision (A calls B while B calls A):** Check for existing Ringing call between the pair before creating a new one. Return error; client surfaces "They're already calling you."

**Recipient already in voice:** Call still rings. On accept, server runs `LeaveVoiceChannelInternal` for the recipient first. Caller leaves any active voice session when initiating the call (before ringing starts).

**Caller disconnects during ringing:** `OnDisconnectedAsync` finds Ringing call where user is caller, ends as Missed, notifies recipient.

**Recipient disconnects during ringing:** Same — finds Ringing call where user is recipient, ends as Missed, notifies caller.

**Either party disconnects during active call:** 10-second grace period before ending. If the user reconnects within 10s, they can rejoin via `GET /voice/active-call`. Otherwise, call ends and system message is persisted.

**Page refresh during active call:** On reconnect, client calls `GET /voice/active-call`. If call is still Active and other party is connected, offer rejoin. If ended during refresh, show as ended.

**Double-accept/double-decline:** Check status is still Ringing before proceeding. If already Active or Ended, return silently (idempotent).

**Calling offline user:** Call rings server-side (VoiceCall created). If recipient connects within 30s, they receive `IncomingCall`. Otherwise, timeout fires as Missed.

**Stale call cleanup:** Background `IHostedService` runs every 60s, finds Ringing calls older than 30s and Active calls with no VoiceState for either participant, ends them appropriately.

## Frontend

### New State (AppState)

```typescript
activeCall = $state<ActiveCall | null>(null);
incomingCall = $state<IncomingCall | null>(null);

type ActiveCall = {
  callId: string;
  dmChannelId: string;
  otherUserId: string;
  otherDisplayName: string;
  otherAvatarUrl?: string | null;
  status: 'ringing' | 'active';
  startedAt: string;
  answeredAt?: string;
};

type IncomingCall = {
  callId: string;
  dmChannelId: string;
  callerUserId: string;
  callerDisplayName: string;
  callerAvatarUrl?: string | null;
};
```

### New AppState Methods

- `startCall(dmChannelId)` — Leave active voice, invoke hub, set activeCall with ringing status
- `acceptCall(callId)` — Invoke hub, receive transport options, run VoiceService join, set active
- `declineCall(callId)` — Invoke hub, clear incomingCall
- `endCall()` — Invoke hub, VoiceService leave, clear activeCall
- `checkActiveCall()` — On reconnect, hit GET /voice/active-call, restore or cleanup

### VoiceService Changes

New `joinWithOptions(options, callbacks)` method that accepts pre-fetched transport options (from `CallAccepted` event) and goes straight to Device/transport setup, skipping the `hub.joinVoiceChannel()` call.

### New Components

1. **`IncomingCallOverlay.svelte`** — Global overlay when `incomingCall` is set. Shows caller info, accept/decline buttons, plays ring sound. Auto-dismisses on cancel/miss.

2. **`DmCallHeader.svelte`** — Replaces standard DM header during active call. Shows other party info, call duration timer, mute/deafen/hang-up. During ringing: "Calling..." with cancel button.

### Modified Components

3. **`DmChatArea.svelte`** — Add call button to header. Render `messageType === 'voiceCallEvent'` as centered system messages with phone icon.

4. **`VoiceConnectedBar.svelte`** — When `activeCall` is set, show "In call with {name}" instead of channel name. Same controls.

### Audio Feedback

- Incoming ring: looping audio while `incomingCall` is set
- Outgoing ringback: looping audio while `activeCall.status === 'ringing'`
- Both stop on accept/decline/timeout/cancel
