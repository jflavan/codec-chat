# Voice Channels & Calls Feature Specification

This document describes the **Voice Channels and Calls** feature for Codec — real-time voice communication through persistent server voice channels and direct 1-on-1 calls from DMs.

## Overview

Codec offers two main ways to communicate by voice: **voice channels** within servers and **direct calls** between users in DMs.

**Voice channels** are persistent audio rooms that exist within a server. Unlike text channels, they don't store message history — they're live spaces users join and leave at will. Clicking a voice channel immediately connects the user; there is no ringing. Users simply drop in and out as they please. Each server can have multiple voice channels, often organized by purpose (e.g., "General," "Gaming," "Music").

**Direct calls** are initiated from a DM conversation. These function like traditional phone calls — the recipient gets a ring notification and can accept or decline. Once connected, the same audio controls apply.

Both voice channels and calls share common capabilities: mute/unmute, deafen/undeafen, per-user volume adjustment, noise suppression (powered by Krisp), and push-to-talk as an alternative to open-mic voice activation.

## Goals

- Enable real-time voice communication through persistent voice channels within servers
- Enable 1-on-1 voice calls between users from existing DM conversations
- Provide familiar audio controls (mute, deafen, per-user volume adjustment)
- Support noise suppression and push-to-talk as voice input modes
- Reuse existing real-time infrastructure (SignalR for signaling) and extend it with WebRTC for peer-to-peer audio streaming
- Deliver a seamless, low-latency audio experience consistent with the existing CODEC CRT visual theme

## Terminology

| Term | Definition |
|------|-----------|
| **Voice Channel** | A persistent audio room within a server that users can freely join and leave; no message history is stored |
| **Direct Call** | A 1-on-1 voice call initiated from a DM conversation with ring/accept/decline semantics |
| **Voice State** | The current voice status of a user: which voice channel or call they are in, mute/deafen state |
| **Mute** | Disabling the user's microphone so others cannot hear them |
| **Deafen** | Disabling the user's audio output so they hear nothing (implicitly mutes as well) |
| **Push-to-Talk (PTT)** | A voice activation mode where the microphone is only active while a designated key is held |
| **Voice Activity Detection (VAD)** | The default open-mic mode where audio is transmitted whenever the user speaks |
| **Noise Suppression** | AI-powered background noise filtering (powered by Krisp) |
| **Signaling** | The process of exchanging WebRTC connection metadata (SDP offers/answers, ICE candidates) between peers via SignalR |
| **SFU (Selective Forwarding Unit)** | A media server that receives audio streams from each participant and forwards them selectively, used for voice channels with 3+ participants |
| **Peer-to-Peer (P2P)** | Direct WebRTC connection between two users, used for direct calls and small voice channels |
| **ICE Candidate** | Network address information exchanged during WebRTC connection setup |
| **TURN Server** | A relay server used when direct P2P connections cannot be established (e.g., behind restrictive firewalls/NATs) |
| **STUN Server** | A server that helps discover the public IP address of a client behind a NAT |

## Architecture

### Technology Choices

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Audio transport | WebRTC (`RTCPeerConnection`) | Industry standard for real-time audio/video in browsers; low-latency, peer-to-peer capable |
| Signaling | SignalR (existing hub) | Already in use for chat; reliable WebSocket channel for exchanging SDP offers/answers and ICE candidates |
| NAT traversal | STUN/TURN servers | Required for WebRTC connectivity across NATs and firewalls |
| Audio processing | Web Audio API + Krisp SDK | Noise suppression, gain control, and push-to-talk gating |
| Voice channels (3+ users) | SFU media server | Scales better than full-mesh P2P for group voice; each client sends one stream, receives N-1 |
| Direct calls (1-on-1) | P2P via WebRTC | Lowest latency for two-party calls; no media server needed |
| Codec | Opus | WebRTC default audio codec; excellent quality at low bitrates (48 kHz, variable bitrate) |

### Connection Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Voice Channels (3+ users)                    │
│                                                                     │
│   User A ──audio──►┌─────────┐──audio──► User B                    │
│   User B ──audio──►│   SFU   │──audio──► User A                    │
│   User C ──audio──►│  Server │──audio──► User A                    │
│                    └─────────┘──audio──► User B                    │
│                         ▲                                           │
│                         │ signaling via SignalR                     │
│                         ▼                                           │
│                    ┌─────────┐                                     │
│                    │ ASP.NET │                                     │
│                    │   API   │                                     │
│                    └─────────┘                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                        Direct Calls (1-on-1)                        │
│                                                                     │
│   Caller ◄────── P2P WebRTC Audio ──────► Callee                   │
│      │                                       │                      │
│      └──── signaling via SignalR ─────────────┘                     │
│                         │                                           │
│                    ┌─────────┐                                     │
│                    │ ASP.NET │                                     │
│                    │   API   │                                     │
│                    └─────────┘                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Signaling Flow (WebRTC via SignalR)

All WebRTC session negotiation travels through the existing SignalR hub. No audio data passes through the API server (except when relayed via TURN).

**Voice Channel Join:**

```
User A (joining)                SignalR Hub               Existing Users (B, C)
     │                              │                              │
     │── JoinVoiceChannel ────────►│                              │
     │                              │── UserJoinedVoice ─────────►│
     │                              │                              │
     │◄─ VoiceChannelMembers ──────│  (list of current members)   │
     │                              │                              │
     │── SendWebRtcOffer ─────────►│── ReceiveWebRtcOffer ──────►│  (to each peer)
     │                              │                              │
     │◄─ ReceiveWebRtcAnswer ─────│◄─ SendWebRtcAnswer ─────────│  (from each peer)
     │                              │                              │
     │◄─► IceCandidate ───────────│──► IceCandidate ────────────►│  (bidirectional)
     │                              │                              │
     │  ◄══════════ P2P Audio (or via SFU) ══════════►           │
```

**Direct Call:**

```
Caller                         SignalR Hub                    Callee
  │                                │                             │
  │── StartDirectCall ───────────►│                             │
  │                                │── IncomingCall ───────────►│
  │                                │                             │
  │                                │◄─ AcceptCall ──────────────│
  │◄─ CallAccepted ──────────────│                             │
  │                                │                             │
  │── SendWebRtcOffer ──────────►│── ReceiveWebRtcOffer ─────►│
  │◄─ ReceiveWebRtcAnswer ──────│◄─ SendWebRtcAnswer ────────│
  │◄─► IceCandidate ────────────│──► IceCandidate ───────────►│
  │                                │                             │
  │  ◄══════════════ P2P Audio ════════════════►               │
```

## User Stories

### Voice Channels

#### Joining a Voice Channel
> As a server member, I want to click on a voice channel to immediately connect and start talking with anyone else in that channel.

#### Leaving a Voice Channel
> As a user in a voice channel, I want to disconnect at any time by clicking a disconnect button or by joining a different voice channel.

#### Seeing Who's in a Voice Channel
> As a server member, I want to see which users are currently in each voice channel so I know who I'll be talking to before joining.

#### Creating a Voice Channel
> As a server Owner or Admin, I want to create voice channels within my server so members have dedicated spaces for voice communication.

### Direct Calls

#### Starting a Direct Call
> As a user in a DM conversation, I want to call the other user so that we can have a voice conversation.

#### Receiving a Direct Call
> As a user, I want to receive a ring notification when someone calls me so that I can accept or decline the call.

#### Declining a Direct Call
> As a user receiving a call, I want to decline the call so that I'm not forced to join.

#### Ending a Direct Call
> As a user in a call, I want to hang up at any time to end the conversation.

### Audio Controls (Both)

#### Muting Microphone
> As a voice user, I want to mute my microphone so that others don't hear me when I'm not speaking.

#### Deafening Audio
> As a voice user, I want to deafen myself so I hear nothing, which also mutes my microphone.

#### Adjusting User Volume
> As a voice user, I want to adjust the volume of individual users so I can hear everyone at a comfortable level.

#### Toggling Noise Suppression
> As a voice user, I want to toggle noise suppression on or off to filter out background noise from my microphone.

#### Using Push-to-Talk
> As a voice user, I want to use push-to-talk mode so that my microphone is only active while I hold a designated key.

## Data Model

### Channel Entity (Extended)

The existing `Channel` entity is extended with a `Type` discriminator to distinguish text and voice channels.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Existing — unique channel identifier |
| `ServerId` | Guid (FK → Server) | Existing — reference to the parent server |
| `Name` | string | Existing — channel display name |
| `Type` | ChannelType (enum) | **New** — `Text` (default, 0) or `Voice` (1) |
| `CreatedAt` | DateTimeOffset | Existing — when the channel was created |

### ChannelType Enum

```csharp
public enum ChannelType
{
    Text = 0,
    Voice = 1
}
```

### VoiceState Entity

Tracks the real-time voice status of each connected user. Rows are created when a user joins a voice channel or call and deleted when they disconnect.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique voice state identifier |
| `UserId` | Guid (FK → User) | The connected user |
| `ChannelId` | Guid? (FK → Channel, nullable) | The voice channel the user is in (null for direct calls) |
| `DmChannelId` | Guid? (FK → DmChannel, nullable) | The DM channel for direct calls (null for voice channels) |
| `IsMuted` | bool | Whether the user's microphone is muted |
| `IsDeafened` | bool | Whether the user has deafened themselves |
| `SessionId` | string | SignalR connection ID for routing signaling messages |
| `JoinedAt` | DateTimeOffset | When the user connected |

### DirectCallState Entity

Tracks active and recent direct calls between users in DM conversations.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique call identifier |
| `DmChannelId` | Guid (FK → DmChannel) | The DM channel this call belongs to |
| `CallerUserId` | Guid (FK → User) | The user who initiated the call |
| `RecipientUserId` | Guid (FK → User) | The user being called |
| `Status` | CallStatus (enum) | Current call status |
| `StartedAt` | DateTimeOffset | When the call was initiated |
| `AnsweredAt` | DateTimeOffset? | When the call was accepted (null if not yet answered) |
| `EndedAt` | DateTimeOffset? | When the call ended (null if still active) |

### CallStatus Enum

```csharp
public enum CallStatus
{
    Ringing = 0,
    Active = 1,
    Ended = 2,
    Declined = 3,
    Missed = 4,
    Cancelled = 5
}
```

### Constraints

- **One voice connection per user:** A user can only be in one voice channel or call at a time. Joining a new voice channel automatically disconnects from the current one.
- **ChannelId XOR DmChannelId:** Exactly one of `ChannelId` or `DmChannelId` must be non-null in a `VoiceState` record.
- **Voice channel membership:** Only server members can join a server's voice channels.
- **Direct call prerequisite:** Direct calls require an existing DM channel (and thus an accepted friendship).
- **Call uniqueness:** Only one active call can exist per DM channel at a time.

### Schema Diagram

```
┌─────────────┐       ┌─────────────────┐       ┌─────────────────┐
│   Server    │       │    Channel      │       │   VoiceState    │
│─────────────│       │─────────────────│       │─────────────────│
│ Id ─────────│──────►│ ServerId (FK)   │       │ Id (PK)         │
│ Name        │       │ Id (PK) ────────│──────►│ ChannelId (FK?) │
└─────────────┘       │ Name            │       │ UserId (FK) ────│──► User
                      │ Type (NEW)      │       │ DmChannelId(FK?)│──► DmChannel
                      │ CreatedAt       │       │ IsMuted         │
                      └─────────────────┘       │ IsDeafened      │
                                                │ SessionId       │
                                                │ JoinedAt        │
                                                └─────────────────┘

┌─────────────┐       ┌─────────────────────┐
│  DmChannel  │       │  DirectCallState    │
│─────────────│       │─────────────────────│
│ Id ─────────│──────►│ DmChannelId (FK)    │
│ CreatedAt   │       │ Id (PK)             │
└─────────────┘       │ CallerUserId (FK)   │──► User
                      │ RecipientUserId(FK) │──► User
                      │ Status              │
                      │ StartedAt           │
                      │ AnsweredAt          │
                      │ EndedAt             │
                      └─────────────────────┘
```

### Indexes

```
IX_VoiceState_UserId                   — fast lookup of a user's current voice state (enforce one-at-a-time)
IX_VoiceState_ChannelId                — fast lookup of users in a voice channel
IX_VoiceState_DmChannelId              — fast lookup of users in a direct call
IX_DirectCallState_DmChannelId         — fast lookup of calls for a DM channel
IX_DirectCallState_Status              — filter active/ringing calls
IX_Channel_ServerId_Type               — fast lookup of voice channels per server
```

## API Endpoints

All endpoints require authentication (`Authorization: Bearer <token>`).

### Voice Channels

#### Create a Voice Channel

```
POST /servers/{serverId}/channels
```

Extended to accept a `type` field. The existing channel creation endpoint is reused with an optional type discriminator.

**Request Body:**
```json
{
  "name": "General Voice",
  "type": "voice"
}
```

**Success Response:** `201 Created`
```json
{
  "id": "guid",
  "serverId": "guid",
  "name": "General Voice",
  "type": "voice",
  "createdAt": "2026-02-16T10:00:00Z"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Channel name is empty or exceeds maximum length |
| `403 Forbidden` | User is not an Owner or Admin of the server |
| `404 Not Found` | Server does not exist |

#### Get Voice Channel Members

```
GET /channels/{channelId}/voice-states
```

Returns the list of users currently connected to a voice channel, including their mute/deafen state.

**Success Response:** `200 OK`
```json
[
  {
    "userId": "guid",
    "displayName": "Alice",
    "avatarUrl": "https://...",
    "isMuted": false,
    "isDeafened": false,
    "joinedAt": "2026-02-16T10:05:00Z"
  }
]
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | User is not a member of the server |
| `404 Not Found` | Channel does not exist or is not a voice channel |

### Direct Calls

#### Start a Direct Call

```
POST /dm/{dmChannelId}/calls
```

Initiates a call to the other participant in a DM channel. The recipient receives an `IncomingCall` SignalR event.

**Request Body:** _(empty)_

**Success Response:** `201 Created`
```json
{
  "id": "guid",
  "dmChannelId": "guid",
  "callerUserId": "guid",
  "recipientUserId": "guid",
  "status": "ringing",
  "startedAt": "2026-02-16T10:10:00Z"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | User is not a participant in the DM channel |
| `404 Not Found` | DM channel does not exist |
| `409 Conflict` | A call is already active or ringing in this DM channel |

#### Accept a Direct Call

```
POST /dm/{dmChannelId}/calls/{callId}/accept
```

**Success Response:** `200 OK`
```json
{
  "id": "guid",
  "status": "active",
  "answeredAt": "2026-02-16T10:10:15Z"
}
```

**Side Effects:**
- Broadcasts `CallAccepted` event to the caller via SignalR
- Creates `VoiceState` records for both participants

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | User is not the call recipient |
| `404 Not Found` | Call does not exist |
| `409 Conflict` | Call is no longer in `Ringing` status |

#### Decline a Direct Call

```
POST /dm/{dmChannelId}/calls/{callId}/decline
```

**Success Response:** `200 OK`
```json
{
  "id": "guid",
  "status": "declined",
  "endedAt": "2026-02-16T10:10:20Z"
}
```

**Side Effects:**
- Broadcasts `CallDeclined` event to the caller via SignalR

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | User is not the call recipient |
| `404 Not Found` | Call does not exist |
| `409 Conflict` | Call is no longer in `Ringing` status |

#### End a Direct Call

```
POST /dm/{dmChannelId}/calls/{callId}/end
```

Either participant can end an active call. Also used by the caller to cancel a ringing call.

**Success Response:** `200 OK`
```json
{
  "id": "guid",
  "status": "ended",
  "endedAt": "2026-02-16T10:30:00Z"
}
```

**Side Effects:**
- Broadcasts `CallEnded` event to the other participant via SignalR
- Deletes `VoiceState` records for both participants

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | User is not a participant in the call |
| `404 Not Found` | Call does not exist |
| `409 Conflict` | Call is already ended/declined/missed |

### Voice State Updates

#### Update Voice State

```
PATCH /voice/state
```

Updates the current user's mute/deafen state. The change is broadcast to other participants.

**Request Body:**
```json
{
  "isMuted": true,
  "isDeafened": false
}
```

**Success Response:** `200 OK`
```json
{
  "userId": "guid",
  "isMuted": true,
  "isDeafened": false
}
```

**Side Effects:**
- Broadcasts `VoiceStateUpdated` event to all other participants in the same voice channel or call

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | User is not currently in a voice channel or call |

## Real-time Events (SignalR)

Voice events are delivered through the existing SignalR hub (`/hubs/chat`). Signaling messages are targeted to specific users or voice channel groups.

### Client → Server Methods

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinVoiceChannel` | `channelId: string` | Join a voice channel; server validates membership and returns current members |
| `LeaveVoiceChannel` | `channelId: string` | Disconnect from the current voice channel |
| `StartDirectCall` | `dmChannelId: string` | Initiate a call to the other DM participant |
| `AcceptCall` | `callId: string` | Accept an incoming direct call |
| `DeclineCall` | `callId: string` | Decline an incoming direct call |
| `EndCall` | `callId: string` | End or cancel an active/ringing call |
| `SendWebRtcOffer` | `targetUserId: string, sdp: string` | Send an SDP offer to a specific peer |
| `SendWebRtcAnswer` | `targetUserId: string, sdp: string` | Send an SDP answer to a specific peer |
| `SendIceCandidate` | `targetUserId: string, candidate: string` | Send an ICE candidate to a specific peer |
| `UpdateVoiceState` | `isMuted: bool, isDeafened: bool` | Update mute/deafen state and broadcast to peers |

### Server → Client Events

| Event | Payload | Delivered To |
|-------|---------|-------------|
| `VoiceChannelMembers` | `{ channelId, members: VoiceStateSummary[] }` | The joining user |
| `UserJoinedVoice` | `{ channelId, userId, displayName, avatarUrl }` | All other users in the voice channel |
| `UserLeftVoice` | `{ channelId, userId }` | All other users in the voice channel |
| `VoiceStateUpdated` | `{ userId, isMuted, isDeafened }` | All other participants (channel or call) |
| `IncomingCall` | `{ callId, dmChannelId, callerUserId, callerName, callerAvatar }` | The call recipient (via user-scoped group) |
| `CallAccepted` | `{ callId, dmChannelId }` | The caller |
| `CallDeclined` | `{ callId, dmChannelId }` | The caller |
| `CallEnded` | `{ callId, dmChannelId, endedByUserId }` | The other participant |
| `CallMissed` | `{ callId, dmChannelId }` | The caller (after ring timeout) |
| `ReceiveWebRtcOffer` | `{ fromUserId, sdp }` | The target peer |
| `ReceiveWebRtcAnswer` | `{ fromUserId, sdp }` | The target peer |
| `ReceiveIceCandidate` | `{ fromUserId, candidate }` | The target peer |

### SignalR Groups for Voice

| Group Name | Scope | Used For |
|------------|-------|----------|
| `voice-{channelId}` | Voice channel | Broadcasting join/leave/state updates to all channel participants |
| `call-{callId}` | Direct call | Broadcasting call lifecycle events and signaling between call participants |
| `user-{userId}` | User (existing) | Delivering incoming call notifications and missed call events |

## UI Design

### Voice Channels in Channel Sidebar

Voice channels appear in the channel sidebar alongside text channels, visually distinguished by a speaker icon instead of the `#` hash icon.

```
┌──────────────────────────────┐
│  My Server              ⚙   │
│──────────────────────────────│
│  TEXT CHANNELS               │
│  # general                   │
│  # random                    │
│──────────────────────────────│
│  VOICE CHANNELS              │
│  🔊 General Voice            │
│     ├─ 🟢 Alice (muted 🔇)  │
│     └─ 🟢 Bob               │
│  🔊 Gaming                   │
│  🔊 Music                    │
│──────────────────────────────│
│  [avatar] Alice     ⚙ ⏻     │
└──────────────────────────────┘
```

**Voice Channel Entry:**
- Speaker icon (🔊) replaces `#` — uses `currentColor` SVG, 20px
- Channel name in `--text-normal`, bold when active
- Hover: `--bg-message-hover` background
- Click: immediately joins the voice channel (no navigation needed)

**Connected Users (Nested Below Voice Channel):**
- Indented list of users currently in the channel
- Each user: avatar (20px circular) + display name (13px, `--text-muted`)
- Mute/deafen icons shown inline when applicable (🔇 muted, 🔕 deafened)
- Updated in real-time via `UserJoinedVoice` / `UserLeftVoice` / `VoiceStateUpdated` events

### Voice Connected Bar

When the user is connected to a voice channel or call, a persistent "voice connected" bar appears above the User Panel at the bottom of the channel sidebar.

```
┌──────────────────────────────┐
│  🔊 General Voice            │
│  Connected • 00:12:34        │
│  [🎤 Mute] [🎧 Deafen] [📞 Disconnect] │
└──────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Container | `--bg-secondary` background, `--border` top border, 8px padding |
| Channel/call name | 13px, 600 weight, `--accent` color, clickable (navigates to the voice channel) |
| Status text | 12px, `--text-muted`, "Connected" + elapsed time |
| Mute button (🎤) | 32×32px, `--text-normal` default, `--danger` when muted, toggle on click |
| Deafen button (🎧) | 32×32px, `--text-normal` default, `--danger` when deafened, toggle on click |
| Disconnect button (📞) | 32×32px, `--danger` color, ends voice session on click |

### Direct Call UI

#### Initiating a Call

A phone icon button appears in the DM chat header. Clicking it initiates a call.

```
┌──────────────────────────────────────────────────┐
│  [avatar] Bob                           [📞 Call] │
│──────────────────────────────────────────────────│
│                                                    │
│              (DM message feed)                     │
│                                                    │
└──────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Call button | 32×32px, `--text-muted` default, `--accent` on hover, in DM header |

#### Outgoing Call (Ringing)

When a call is initiated, a call overlay appears in the DM chat area.

```
┌──────────────────────────────────────────────────┐
│                                                    │
│              [avatar 80px]                         │
│              Bob                                   │
│              Ringing...                            │
│                                                    │
│              [🔴 Cancel]                           │
│                                                    │
└──────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Overlay | Centered in DM chat area, `--bg-secondary` background, 16px border-radius |
| Avatar | 80px circular |
| Name | 18px, 600 weight, `--text-header` |
| Status | 14px, `--text-muted`, animated ellipsis |
| Cancel button | `--danger` background, white text, circular, 56px |

#### Incoming Call (Ring Notification)

When receiving a call, an incoming call notification appears as an overlay.

```
┌──────────────────────────────────────────────────┐
│  ┌────────────────────────────────────────────┐   │
│  │  [avatar 48px]  Alice is calling...        │   │
│  │                 [🟢 Accept] [🔴 Decline]   │   │
│  └────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Notification | Fixed position, top-right of viewport, `--bg-secondary` background, `--border` border, 12px border-radius, z-index 100 |
| Avatar | 48px circular |
| Caller name | 15px, 600 weight, `--text-header` |
| "is calling..." | 13px, `--text-muted` |
| Accept button | `--success` background, white phone icon, 44×44px, circular |
| Decline button | `--danger` background, white phone icon, 44×44px, circular |
| Ring timeout | 30 seconds — notification dismissed, call marked as missed |
| Audio | Browser notification sound (if permitted) |

#### Active Call

During an active call, a compact call interface appears in the DM chat area.

```
┌──────────────────────────────────────────────────┐
│                                                    │
│        [avatar 64px]        [avatar 64px]         │
│        Alice (You)          Bob                    │
│        🎤 Speaking          🔇 Muted               │
│                                                    │
│        Connected • 05:23                           │
│                                                    │
│  [🎤 Mute] [🎧 Deafen] [🔇 Noise] [🔴 End Call]  │
│                                                    │
└──────────────────────────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Call area | Centered in DM chat area, replaces message feed during active call |
| Avatars | 64px circular, green ring when speaking (voice activity indicator) |
| Speaking indicator | Pulsing `--accent` ring around avatar when user's audio is active |
| Timer | 14px, `--text-muted`, `MM:SS` format |
| Control buttons | 44×44px each, `--bg-message-hover` background, icon `--text-normal`, toggle state changes icon color to `--danger` |
| Noise suppression toggle | 🔇 icon, `--accent` when active, `--text-normal` when off |
| End Call button | `--danger` background, white phone-down icon, wider (80×44px) |

### Audio Controls Panel

A user context menu (right-click on a user in a voice channel or during a call) provides per-user volume control.

```
┌─────────────────────────────┐
│  User Volume                │
│  ──────●──────── 100%       │
│                             │
│  [Mute User]               │
└─────────────────────────────┘
```

| Element | Style |
|---------|-------|
| Context menu | `--bg-secondary` background, `--border` border, 8px border-radius, 160px min-width |
| Volume slider | Range input, `--accent` track fill, 0–200% range, default 100% |
| Mute user | `--text-normal` text, toggles local mute for that user |

### Voice Settings (User Settings Extension)

A new "Voice & Audio" category is added to the User Settings modal.

```
┌──────────────────────────────────────────────────────────────┐
│  ⚙ User Settings                                        ✕   │
│──────────────────────────────────────────────────────────────│
│  │ 👤 My Profile    │                                       │
│  │ 🔒 My Account    │  VOICE & AUDIO                        │
│  │ 🎤 Voice & Audio │                                       │
│  │                  │  Input Device                          │
│  │                  │  [Default ▾]                           │
│  │                  │                                        │
│  │                  │  Output Device                         │
│  │                  │  [Default ▾]                           │
│  │                  │                                        │
│  │                  │  Input Mode                            │
│  │                  │  (●) Voice Activity  ( ) Push to Talk  │
│  │                  │                                        │
│  │                  │  Push-to-Talk Shortcut                 │
│  │                  │  [Click to record keybind]             │
│  │                  │                                        │
│  │                  │  Noise Suppression (Krisp)             │
│  │                  │  [━━━━━━━━━━○] On                      │
│  │                  │                                        │
│  │                  │  Input Sensitivity                     │
│  │                  │  ──────●──────── -40 dB                │
│  │                  │  [Live meter ████████░░░░░]            │
│  │                  │                                        │
│  └──────────────────┘                                        │
└──────────────────────────────────────────────────────────────┘
```

| Setting | Type | Description |
|---------|------|-------------|
| Input Device | Dropdown | Select microphone from available devices (via `navigator.mediaDevices.enumerateDevices()`) |
| Output Device | Dropdown | Select speaker/headphones output device |
| Input Mode | Radio | Choose between Voice Activity Detection (open mic) and Push-to-Talk |
| Push-to-Talk Shortcut | Keybind recorder | Capture the key combination for PTT (default: unset) |
| Noise Suppression | Toggle | Enable/disable Krisp-powered noise filtering |
| Input Sensitivity | Slider + live meter | Threshold for voice activity detection; shows real-time microphone input level |

Voice settings are persisted to `localStorage` on the client. They are not stored server-side.

### Styling

All voice components follow the existing CODEC CRT phosphor-green theme:
- Voice connected bar uses the same `--bg-secondary` background and spacing as the User Panel
- Voice channel entries follow the same card pattern as text channel entries with speaker icon instead of `#`
- Call overlays use `--bg-secondary` with rounded corners consistent with existing modal/overlay patterns
- Control buttons use the same sizing and hover states as existing action buttons
- The speaking indicator ring uses `--accent` with a pulse animation (respects `prefers-reduced-motion`)

## Audio Processing Pipeline

### Client-Side Audio Flow

```
Microphone Input
      │
      ▼
┌─────────────────┐
│  Web Audio API   │
│  AudioContext    │
│  ├─ Gain Node   │  ◄── Input sensitivity / volume control
│  ├─ Analyser    │  ◄── Voice activity detection (VAD) meter
│  ├─ Krisp Node  │  ◄── Noise suppression (when enabled)
│  └─ PTT Gate    │  ◄── Push-to-talk mute gate (when PTT mode active)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  RTCPeerConn    │
│  addTrack()     │  ──── Opus-encoded audio ────► Peer(s) / SFU
└─────────────────┘
```

### Voice Activity Detection

- Uses the Web Audio `AnalyserNode` to measure input volume in real-time
- When volume exceeds the sensitivity threshold, the user's speaking state is set to `true`
- Speaking state is broadcast to peers for UI updates (green ring around avatar)
- A 300ms grace period prevents flickering between speaking/silent states

### Push-to-Talk Implementation

- Listens for `keydown` / `keyup` events on the configured PTT key
- When PTT key is held: unmutes the audio track on the `RTCPeerConnection`
- When PTT key is released: mutes the audio track
- Visual indicator shows PTT key state in the voice connected bar
- A 200ms release delay prevents audio clipping at the end of speech

## Acceptance Criteria

### AC-1: Create a Voice Channel
- [ ] A server Owner or Admin can create a voice channel with a name
- [ ] The channel type is persisted as `Voice` in the database
- [ ] Voice channels appear in the channel sidebar under a "VOICE CHANNELS" section header
- [ ] Voice channels display a speaker icon (🔊) instead of `#`
- [ ] The API returns `403 Forbidden` if the user is not an Owner or Admin

### AC-2: Join a Voice Channel
- [ ] Clicking a voice channel immediately connects the user (no ringing)
- [ ] The user's browser requests microphone permission on first join
- [ ] The user appears in the voice channel's connected users list
- [ ] Other users in the channel receive a `UserJoinedVoice` event
- [ ] WebRTC peer connections are established with existing channel members
- [ ] The "voice connected" bar appears above the User Panel

### AC-3: Leave a Voice Channel
- [ ] Clicking the disconnect button disconnects the user
- [ ] The user is removed from the voice channel's connected users list
- [ ] Other users receive a `UserLeftVoice` event
- [ ] WebRTC peer connections are closed cleanly
- [ ] Joining a different voice channel automatically leaves the current one

### AC-4: See Voice Channel Members
- [ ] Connected users are displayed nested below each voice channel in the sidebar
- [ ] Each user shows avatar, display name, and mute/deafen indicators
- [ ] The list updates in real-time as users join, leave, or change voice state

### AC-5: Start a Direct Call
- [ ] A phone icon button is visible in the DM chat header
- [ ] Clicking the button initiates a call and shows the outgoing call overlay
- [ ] The recipient receives an `IncomingCall` notification
- [ ] The API returns `409 Conflict` if a call is already active or ringing

### AC-6: Receive and Accept a Direct Call
- [ ] An incoming call notification appears with the caller's avatar and name
- [ ] Clicking "Accept" establishes a P2P WebRTC connection
- [ ] Both users see the active call interface
- [ ] The call timer starts counting from the moment the call is accepted

### AC-7: Decline a Direct Call
- [ ] Clicking "Decline" dismisses the notification
- [ ] The caller receives a `CallDeclined` event and their overlay updates
- [ ] The call is recorded with status `Declined`

### AC-8: End a Direct Call
- [ ] Either participant can end the call by clicking the "End Call" button
- [ ] Both participants' call UI is dismissed
- [ ] WebRTC connections are closed cleanly
- [ ] The call is recorded with status `Ended` and `EndedAt` timestamp

### AC-9: Missed Call
- [ ] If the recipient does not answer within 30 seconds, the call is marked as `Missed`
- [ ] The caller receives a `CallMissed` event
- [ ] The outgoing call overlay is dismissed

### AC-10: Mute and Unmute
- [ ] Clicking the mute button toggles the microphone on/off
- [ ] The mute state is reflected in the user's avatar indicator (mute icon)
- [ ] Other participants receive a `VoiceStateUpdated` event
- [ ] The audio track is disabled/enabled on the WebRTC connection

### AC-11: Deafen and Undeafen
- [ ] Clicking the deafen button toggles audio output on/off
- [ ] Deafening also mutes the user's microphone
- [ ] Undeafening restores the previous mute state
- [ ] The deafen state is reflected in the user's avatar indicator
- [ ] Other participants receive a `VoiceStateUpdated` event

### AC-12: Per-User Volume Adjustment
- [ ] Right-clicking a user in a voice channel or call shows a volume slider
- [ ] The slider adjusts the local volume for that user (0–200%)
- [ ] Volume adjustments are local-only (not broadcast to others)
- [ ] A "Mute User" option completely silences that user locally

### AC-13: Noise Suppression
- [ ] A noise suppression toggle is available in the call controls and Voice & Audio settings
- [ ] When enabled, background noise is filtered from the user's microphone input
- [ ] The setting persists across sessions via `localStorage`

### AC-14: Push-to-Talk
- [ ] Users can switch between Voice Activity and Push-to-Talk input modes in settings
- [ ] In PTT mode, the microphone is only active while the configured key is held
- [ ] A keybind recorder allows setting the PTT key
- [ ] The PTT key state is visually indicated in the voice connected bar
- [ ] The setting persists across sessions via `localStorage`

### AC-15: Voice & Audio Settings
- [ ] A "Voice & Audio" category appears in the User Settings modal
- [ ] Users can select input and output audio devices from dropdown lists
- [ ] An input sensitivity slider with a live audio level meter is provided
- [ ] All voice settings persist to `localStorage`

### AC-16: WebRTC Connection Resilience
- [ ] ICE candidate exchange completes successfully through SignalR
- [ ] TURN relay is used when direct P2P connection fails
- [ ] Audio quality adapts to available bandwidth (Opus bitrate adjustment)
- [ ] Temporary network interruptions are handled gracefully with automatic reconnection attempts

## Dependencies

- **Prerequisite:** Existing Channel and Server infrastructure for voice channels
- **Prerequisite:** Existing DM channel infrastructure for direct calls (see [DIRECT_MESSAGES.md](DIRECT_MESSAGES.md))
- **Prerequisite:** Existing SignalR hub for signaling (see [ARCHITECTURE.md](ARCHITECTURE.md))
- **External:** STUN/TURN server infrastructure for NAT traversal (e.g., Twilio STUN/TURN, Metered, or self-hosted coturn)
- **External:** Krisp SDK for noise suppression (or alternative: RNNoise as open-source fallback)
- **External (future):** SFU media server for voice channels with many participants (e.g., mediasoup, Janus, or LiveKit)
- **Reuses:** Existing User Panel, channel sidebar, DM chat area components
- **Reuses:** Existing User Settings modal with new "Voice & Audio" category
- **Related:** Presence indicators (future) — showing online/offline status complements voice state

## Migration Plan

A single EF Core migration (`AddVoiceChannels`) will:
1. Add `Type` column to the `Channels` table with a default value of `Text` (0) for all existing channels
2. Create the `VoiceStates` table with foreign keys and unique constraint on `UserId`
3. Create the `DirectCallStates` table with foreign keys
4. Add all required indexes
5. Add a composite index on `Channels` (`ServerId`, `Type`) for efficient voice channel queries

**Data safety:** The migration is additive-only. No existing data is modified or deleted. All existing channels default to `Type = Text`.

## Task Breakdown

### Phase 1: Foundation (Voice Channels — Join/Leave)

#### API
- [ ] Add `ChannelType` enum (`Text = 0`, `Voice = 1`) in `Models/`
- [ ] Add `Type` property to `Channel` entity with default `Text`
- [ ] Create `VoiceState` entity in `Models/`
- [ ] Add `DbSet<VoiceState>` to `CodecDbContext` and configure relationships, keys, and indexes
- [ ] Create and apply EF Core migration (`AddVoiceChannels`)
- [ ] Extend `POST /servers/{serverId}/channels` to accept optional `type` field
- [ ] Create `GET /channels/{channelId}/voice-states` endpoint
- [ ] Add voice SignalR hub methods: `JoinVoiceChannel`, `LeaveVoiceChannel`
- [ ] Broadcast `UserJoinedVoice`, `UserLeftVoice`, `VoiceChannelMembers` events
- [ ] Add `UpdateVoiceState` hub method for mute/deafen broadcast
- [ ] Create `PATCH /voice/state` endpoint for mute/deafen state persistence
- [ ] Clean up stale `VoiceState` records on SignalR disconnect (`OnDisconnectedAsync`)

#### Web
- [ ] Add `ChannelType`, `VoiceState` types to `models.ts`
- [ ] Add voice-related API methods to `ApiClient`
- [ ] Add WebRTC signaling event handlers to `ChatHubService` (`SendWebRtcOffer`, `SendWebRtcAnswer`, `SendIceCandidate`, receive counterparts)
- [ ] Add voice state management to `AppState` (current voice channel, connected users, mute/deafen)
- [ ] Create `VoiceService` class for WebRTC peer connection management, audio track handling, and ICE candidate exchange
- [ ] Update `ChannelSidebar.svelte` to display voice channels with speaker icon and connected users
- [ ] Create `VoiceConnectedBar.svelte` (mute, deafen, disconnect controls + channel name + timer)
- [ ] Implement microphone permission request flow on first voice channel join
- [ ] Wire click on voice channel → join voice channel (SignalR + WebRTC)

### Phase 2: Direct Calls

#### API
- [ ] Create `DirectCallState` entity and `CallStatus` enum in `Models/`
- [ ] Add `DbSet<DirectCallState>` to `CodecDbContext` and configure relationships
- [ ] Create and apply EF Core migration (`AddDirectCalls`)
- [ ] Create `POST /dm/{dmChannelId}/calls` (start call)
- [ ] Create `POST /dm/{dmChannelId}/calls/{callId}/accept`
- [ ] Create `POST /dm/{dmChannelId}/calls/{callId}/decline`
- [ ] Create `POST /dm/{dmChannelId}/calls/{callId}/end`
- [ ] Add call SignalR hub methods: `StartDirectCall`, `AcceptCall`, `DeclineCall`, `EndCall`
- [ ] Broadcast `IncomingCall`, `CallAccepted`, `CallDeclined`, `CallEnded`, `CallMissed` events
- [ ] Implement 30-second ring timeout with `CallMissed` status transition

#### Web
- [ ] Add `DirectCallState`, `CallStatus` types to `models.ts`
- [ ] Add call-related API methods to `ApiClient`
- [ ] Add call lifecycle SignalR event handlers to `ChatHubService`
- [ ] Add call state management to `AppState` (active call, incoming call notification)
- [ ] Add phone icon button to `DmChatArea.svelte` header
- [ ] Create `OutgoingCallOverlay.svelte` (ringing state with cancel button)
- [ ] Create `IncomingCallNotification.svelte` (accept/decline with ring sound)
- [ ] Create `ActiveCallView.svelte` (avatars, speaking indicators, controls, timer)
- [ ] Wire call lifecycle (start → ring → accept/decline → active → end)

### Phase 3: Audio Controls & Settings

#### Web
- [ ] Implement per-user volume control via Web Audio `GainNode` on received audio tracks
- [ ] Create user volume context menu (right-click on user in voice channel / call)
- [ ] Add "Voice & Audio" category to User Settings modal
- [ ] Create `VoiceSettings.svelte` with input/output device selection, input mode, sensitivity slider
- [ ] Implement push-to-talk keybind recorder and PTT gate logic
- [ ] Integrate Krisp SDK (or RNNoise fallback) for noise suppression toggle
- [ ] Implement voice activity detection with `AnalyserNode` and speaking indicator
- [ ] Persist voice settings to `localStorage`

### Phase 4: Polish & Resilience

- [ ] Handle WebRTC connection failures with retry logic
- [ ] Implement TURN server fallback configuration
- [ ] Handle browser tab close / navigation (clean up voice state via `beforeunload`)
- [ ] Add `prefers-reduced-motion` support for speaking indicator animations
- [ ] Add accessibility labels and keyboard navigation for all voice controls
- [ ] Add mobile-responsive voice UI (compact controls, touch-friendly buttons)
- [ ] Add call duration display formatting (MM:SS / HH:MM:SS)

### Documentation
- [ ] Update `ARCHITECTURE.md` with voice architecture, WebRTC signaling flow, and new SignalR events
- [ ] Update `DATA.md` with `VoiceState`, `DirectCallState` entities and schema diagram
- [ ] Update `FEATURES.md` to track Voice Channels & Calls feature progress
- [ ] Update `DESIGN.md` with voice channel sidebar, call overlays, and voice connected bar UI specs

## Open Questions

- **SFU provider:** Which SFU should be used for voice channels with many participants? (Options: mediasoup, Janus, LiveKit, Cloudflare Calls. Recommendation: start with P2P for ≤4 users, add SFU for 5+.)
- **STUN/TURN provider:** Should Codec self-host coturn or use a managed service? (Recommendation: start with free STUN servers like Google's `stun.l.google.com:19302` + a managed TURN provider like Metered or Twilio for relay.)
- **Krisp licensing:** Krisp SDK requires a commercial license. Should Codec use the open-source RNNoise library as an initial alternative? (Recommendation: start with RNNoise via a WebAssembly module, evaluate Krisp for a future premium tier.)
- **Voice channel user limit:** Should voice channels have a configurable maximum user limit? (Recommendation: defer — allow unlimited initially, add configurable limits in a later iteration.)
- **Call history:** Should ended calls be surfaced in the DM conversation as system messages (e.g., "Missed call from Alice at 3:45 PM")? (Recommendation: yes, add as a follow-up after core call functionality is implemented.)
- **Screen sharing:** Should screen sharing be bundled with voice channels and calls? (Recommendation: defer to a separate feature spec — it introduces video tracks and significantly increases scope.)
- **Mobile browser support:** WebRTC works in mobile browsers but push-to-talk is limited. Should mobile use voice activity only? (Recommendation: yes, default to voice activity on mobile; push-to-talk is desktop-only.)
- **Echo cancellation:** WebRTC includes built-in acoustic echo cancellation (AEC). Is additional processing needed? (Recommendation: rely on browser-native AEC initially; monitor user feedback.)
