# Direct Messages Feature Specification

This document describes the **Direct Messages (DMs)** feature for Codec — a private, 1-on-1 messaging system that allows users to have real-time conversations outside of server channels.

## Overview

Direct Messages extend Codec's existing messaging system with private, 1-on-1 conversations between two users. DMs live outside the server/channel hierarchy and are accessible from the Home screen alongside the [Friends](FRIENDS.md) panel. The DM system reuses the existing real-time infrastructure (SignalR) and follows the same message format and UI patterns as server channel messages.

## Goals

- Enable private, real-time 1-on-1 conversations between any two users who are friends
- Provide a familiar messaging experience consistent with server channel messaging (message feed, typing indicators, reactions)
- Organize DM conversations in a dedicated sidebar accessible from the Home screen
- Persist DM message history with the same reliability as channel messages

## Terminology

| Term | Definition |
|------|-----------|
| **Direct Message (DM)** | A private message between two users, outside of any server |
| **DM Conversation** | A persistent 1-on-1 chat thread between two specific users |
| **DM Channel** | The underlying data entity that represents a DM conversation (a special type of channel not attached to a server) |
| **DM List** | The sidebar that shows the user's active DM conversations |
| **Participant** | One of the two users in a DM conversation |

## User Stories

### Starting a DM Conversation
> As an authenticated user, I want to start a direct message conversation with a friend so that I can communicate privately.

### Sending a Direct Message
> As a participant in a DM conversation, I want to send a message so that the other participant can read it in real time.

### Viewing DM Conversations
> As an authenticated user, I want to see a list of my DM conversations so that I can resume previous conversations.

### Receiving a Direct Message
> As an authenticated user, I want to receive DM notifications and see new messages in real time so that I don't miss private conversations.

### Viewing DM Message History
> As a participant in a DM conversation, I want to scroll through past messages so that I can reference earlier parts of the conversation.

### Closing a DM Conversation
> As an authenticated user, I want to close a DM conversation from my list so that I can declutter my sidebar without deleting messages.

## Data Model

### DmChannel Entity

A DM conversation is modeled as a special channel that is **not** associated with any server.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique DM channel identifier |
| `CreatedAt` | DateTimeOffset | When the conversation was started |

### DmChannelMember Entity

Join table linking users to DM channels.

| Column | Type | Description |
|--------|------|-------------|
| `DmChannelId` | Guid (PK, FK → DmChannel) | Reference to DM channel |
| `UserId` | Guid (PK, FK → User) | Reference to participant |
| `IsOpen` | bool | Whether this conversation appears in the user's DM sidebar (default: `true`) |
| `JoinedAt` | DateTimeOffset | When the user was added to the conversation |

### DirectMessage Entity

Individual messages within a DM conversation. Follows the same shape as the existing `Message` entity.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique message identifier |
| `DmChannelId` | Guid (FK → DmChannel) | Reference to DM channel |
| `AuthorUserId` | Guid (FK → User) | Reference to message author |
| `AuthorName` | string | Display name snapshot (denormalized) |
| `Body` | string | Message content (plain text) |
| `CreatedAt` | DateTimeOffset | Message timestamp |

### Constraints

- **Exactly two members:** Each `DmChannel` must have exactly two `DmChannelMember` entries.
- **Unique pair:** Only one `DmChannel` may exist for a given pair of users. Enforced by application logic (query before insert).
- **Friendship required:** A DM conversation can only be created between users who have an accepted friendship (see [FRIENDS.md](FRIENDS.md)).

### Schema Diagram

```
┌─────────────┐       ┌─────────────────┐       ┌─────────────┐
│   User      │       │ DmChannelMember │       │  DmChannel  │
│─────────────│       │─────────────────│       │─────────────│
│ Id ─────────│──────►│ UserId (PK,FK)  │       │ Id ─────────│
│ DisplayName │       │ DmChannelId     │◄──────│ CreatedAt   │
│ AvatarUrl   │       │ IsOpen          │       └──────┬──────┘
└─────────────┘       │ JoinedAt        │              │
                      └─────────────────┘              │
                                                       │
                      ┌─────────────────┐              │
                      │ DirectMessage   │              │
                      │─────────────────│              │
                      │ Id (PK)         │              │
                      │ DmChannelId (FK)│──────────────┘
                      │ AuthorUserId(FK)│──────► User
                      │ AuthorName      │
                      │ Body            │
                      │ CreatedAt       │
                      └─────────────────┘
```

### Indexes

```
IX_DmChannelMember_UserId               — fast lookup of a user's DM conversations
IX_DmChannelMember_DmChannelId          — fast lookup of members in a DM channel
IX_DirectMessage_DmChannelId            — fast retrieval of messages in a conversation
IX_DirectMessage_AuthorUserId           — fast lookup of messages by author
```

## API Endpoints

All endpoints require authentication (`Authorization: Bearer <token>`).

### Start or Resume a DM Conversation

```
POST /dm/channels
```

Creates a new DM channel between the current user and the specified user, or returns the existing channel if one already exists.

**Request Body:**
```json
{
  "recipientUserId": "guid"
}
```

**Success Response:** `200 OK` (existing) or `201 Created` (new)
```json
{
  "id": "guid",
  "participant": { "id": "guid", "displayName": "Bob", "avatarUrl": "..." },
  "lastMessage": { "body": "Hey!", "authorName": "Alice", "createdAt": "..." } | null,
  "createdAt": "2026-02-12T09:00:00Z"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Recipient is the current user |
| `403 Forbidden` | Users are not friends |
| `404 Not Found` | Recipient user does not exist |

### List DM Conversations

```
GET /dm/channels
```

Returns all DM conversations the current user is a participant in where `IsOpen = true`, ordered by most recent message.

**Success Response:** `200 OK`
```json
[
  {
    "id": "guid",
    "participant": { "id": "guid", "displayName": "Bob", "avatarUrl": "..." },
    "lastMessage": { "body": "See you later!", "authorName": "Bob", "createdAt": "..." },
    "createdAt": "2026-02-12T09:00:00Z"
  }
]
```

### Get DM Messages

```
GET /dm/channels/{channelId}/messages?before={timestamp}&limit={count}
```

Returns messages in a DM conversation, ordered newest-first with cursor-based pagination.

**Query Parameters:**
| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `before` | No | now | Return messages before this ISO 8601 timestamp |
| `limit` | No | 50 | Maximum number of messages to return (max 100) |

**Success Response:** `200 OK`
```json
[
  {
    "id": "guid",
    "authorUserId": "guid",
    "authorName": "Alice",
    "body": "Hello!",
    "createdAt": "2026-02-12T09:00:00Z"
  }
]
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | Current user is not a participant |
| `404 Not Found` | DM channel does not exist |

### Send a Direct Message

```
POST /dm/channels/{channelId}/messages
```

**Request Body:**
```json
{
  "body": "Hello!"
}
```

**Success Response:** `201 Created`
```json
{
  "id": "guid",
  "authorUserId": "guid",
  "authorName": "Alice",
  "body": "Hello!",
  "createdAt": "2026-02-12T09:05:00Z"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Message body is empty or exceeds maximum length |
| `403 Forbidden` | Current user is not a participant |
| `404 Not Found` | DM channel does not exist |

**Side Effects:**
- Broadcasts `ReceiveDm` event to the other participant via SignalR
- Sets `IsOpen = true` for both participants (reopens closed conversations)

### Close a DM Conversation

```
DELETE /dm/channels/{channelId}
```

Hides the conversation from the current user's DM list by setting `IsOpen = false`. Does **not** delete messages or the channel — the conversation can be reopened by either participant sending a new message.

**Success Response:** `204 No Content`

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | Current user is not a participant |
| `404 Not Found` | DM channel does not exist |

## Real-time Events (SignalR)

DM events are delivered via user-scoped SignalR groups (`user-{userId}`), consistent with the approach defined in [FRIENDS.md](FRIENDS.md).

### Client → Server Methods

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinDmChannel` | `dmChannelId: string` | Join a DM channel group for real-time events |
| `LeaveDmChannel` | `dmChannelId: string` | Leave a DM channel group |
| `StartDmTyping` | `dmChannelId: string, displayName: string` | Broadcast typing indicator to DM partner |
| `StopDmTyping` | `dmChannelId: string, displayName: string` | Clear typing indicator |

### Server → Client Events

| Event | Payload | Delivered To |
|-------|---------|-------------|
| `ReceiveDm` | `{ id, dmChannelId, authorUserId, authorName, body, createdAt }` | The other participant (via user-scoped group) |
| `DmTyping` | `{ dmChannelId, displayName }` | The other participant |
| `DmStoppedTyping` | `{ dmChannelId, displayName }` | The other participant |
| `DmConversationOpened` | `{ dmChannelId, participant: { id, displayName, avatarUrl } }` | Current user (when a new DM is received and `IsOpen` was `false`) |

## UI Design

### Navigation

When the user clicks the **Home** icon at the top of the server sidebar:
- The **channel sidebar** is replaced with two panels:
  1. **DM Conversations list** (upper section) — active DM conversations sorted by most recent message
  2. **Friends panel** (tabs: All Friends, Pending, Add Friend) — as defined in [FRIENDS.md](FRIENDS.md)
- The **main content area** shows the selected DM conversation's message feed, or a welcome/empty state if no conversation is selected

### DM Conversations List

- Each entry shows: participant avatar (32px), participant display name, and a preview of the last message (truncated, `--text-muted`)
- Active (selected) conversation: `--bg-message-hover` background, `--text-header` name color
- Hover: `--bg-message-hover` background
- Right-click or overflow menu on each entry: "Close Conversation"
- Unread indicator: bold display name and accent-colored dot (future enhancement)

### DM Chat Area

The DM chat area reuses the existing chat components with minor adaptations:

- **Header:** Shows the other participant's avatar and display name (no `#` prefix, no server context)
- **Message Feed:** Identical to `MessageFeed.svelte` / `MessageItem.svelte` — avatar, author, timestamp, grouped messages, hover states
- **Composer:** Identical to `Composer.svelte` — placeholder text reads "Message @{displayName}"
- **Typing Indicator:** Identical to `TypingIndicator.svelte` — shows "{displayName} is typing…"
- **Reactions:** Supported on DM messages using the same `ReactionBar` component (future — can be deferred from initial implementation)

### Starting a DM from the Friends List

- Clicking a friend in the **All Friends** tab opens (or creates) a DM conversation with that friend
- The DM conversation is selected in the DM list and the chat area updates to show the conversation

### Styling

All components follow the existing CODEC CRT phosphor-green theme:
- DM list uses the same layout and spacing as the channel sidebar
- DM entries use the same card pattern as channel list items
- The chat area inherits all existing chat styles and components

## Acceptance Criteria

### AC-1: Start a DM Conversation
- [ ] An authenticated user can start a DM conversation with an accepted friend
- [ ] The API returns `201 Created` for a new conversation or `200 OK` for an existing one
- [ ] The API returns `403 Forbidden` if the users are not friends
- [ ] The API returns `400 Bad Request` if the user attempts to DM themselves
- [ ] The API returns `404 Not Found` if the recipient does not exist
- [ ] A new DM channel has exactly two members

### AC-2: Send a Direct Message
- [ ] A participant can send a message to a DM conversation
- [ ] The message is persisted and includes author attribution and timestamp
- [ ] The other participant receives the message in real time via `ReceiveDm` SignalR event
- [ ] Sending a message to a closed conversation reopens it for both participants (`IsOpen = true`)
- [ ] The API returns `400 Bad Request` for empty messages
- [ ] The API returns `403 Forbidden` if the user is not a participant

### AC-3: View DM Message History
- [ ] A participant can retrieve the message history for a DM conversation
- [ ] Messages are returned in chronological order with cursor-based pagination
- [ ] Each message includes id, author info, body, and timestamp
- [ ] The API returns `403 Forbidden` if the user is not a participant

### AC-4: List DM Conversations
- [ ] An authenticated user can view all open DM conversations
- [ ] Conversations are ordered by most recent message (newest first)
- [ ] Each entry shows the other participant's info and a preview of the last message
- [ ] Closed conversations (`IsOpen = false`) do not appear in the list

### AC-5: Close a DM Conversation
- [ ] A participant can close a DM conversation from their list
- [ ] Closing sets `IsOpen = false` for the current user only
- [ ] Messages are not deleted — the conversation can be reopened
- [ ] The other participant's list is not affected
- [ ] The conversation is re-opened automatically when a new message is sent by either participant

### AC-6: DM Typing Indicators
- [ ] Typing indicators appear in real time when the other participant is typing
- [ ] Typing indicators follow the same behavior as channel typing indicators (appear, timeout, disappear)
- [ ] The typing indicator shows "{displayName} is typing…"

### AC-7: DM Conversations List UI
- [ ] The DM list appears in the sidebar when the Home icon is selected
- [ ] Each entry shows participant avatar, name, and last message preview
- [ ] The selected conversation is visually highlighted
- [ ] A "Close Conversation" action is available for each entry

### AC-8: DM Chat Area UI
- [ ] The DM chat area shows the participant's avatar and name in the header
- [ ] Messages are displayed using the existing message feed components
- [ ] The composer placeholder reads "Message @{displayName}"
- [ ] The typing indicator is displayed below the message feed

### AC-9: Start DM from Friends List
- [ ] Clicking a friend in the All Friends tab opens a DM conversation with them
- [ ] If no conversation exists, one is created automatically
- [ ] The UI navigates to the new or existing conversation

### AC-10: Real-time Updates
- [ ] New DM messages appear instantly for both participants without page refresh
- [ ] The DM conversations list updates in real time (new message preview, reordering)
- [ ] Conversations that were closed are re-opened and appear in the list when a new message arrives

### AC-11: Database Integrity
- [ ] Each DM channel has exactly two members
- [ ] Only one DM channel exists between any pair of users
- [ ] DM messages have proper foreign keys to the DM channel and author
- [ ] Closing a conversation does not delete any data
- [ ] The `DmChannel`, `DmChannelMember`, and `DirectMessage` tables have appropriate indexes

## Dependencies

- **Prerequisite:** [Friends](FRIENDS.md) feature — DM conversations require an accepted friendship between users
- **Reuses:** Existing `MessageFeed`, `MessageItem`, `Composer`, `TypingIndicator`, `ReactionBar` components
- **Reuses:** Existing SignalR infrastructure with the addition of user-scoped groups and DM-specific hub methods
- **Related:** Presence indicators (future) — showing online/offline status in DM conversations

## Migration Plan

A single EF Core migration (`AddDirectMessages`) will:
1. Create the `DmChannels` table
2. Create the `DmChannelMembers` table with composite primary key and foreign keys
3. Create the `DirectMessages` table with foreign keys
4. Add all required indexes

## Task Breakdown

### API
- [ ] Create `DmChannel`, `DmChannelMember`, and `DirectMessage` entities in `Models/`
- [ ] Add DbSets to `CodecDbContext` and configure relationships, keys, and indexes
- [ ] Create and apply EF Core migration (`AddDirectMessages`)
- [ ] Create `DmController` with all endpoints (create/resume, list, send, close)
- [ ] Add friendship validation — verify accepted friendship before allowing DM creation
- [ ] Add DM-specific SignalR hub methods (`JoinDmChannel`, `LeaveDmChannel`, `StartDmTyping`, `StopDmTyping`)
- [ ] Broadcast `ReceiveDm`, `DmTyping`, `DmStoppedTyping`, and `DmConversationOpened` events via SignalR
- [ ] Re-open closed conversations when new messages are sent

### Web
- [ ] Add `DmChannel`, `DmConversation`, and `DirectMessage` types to `models.ts`
- [ ] Add DM-related API methods to `ApiClient`
- [ ] Add DM-related SignalR event handlers to `ChatHubService`
- [ ] Add DM state management to `AppState` (conversations list, active conversation, messages)
- [ ] Create `DmList.svelte` component (conversation sidebar entries)
- [ ] Create `DmChatArea.svelte` wrapper (adapts `ChatArea` components for DM context)
- [ ] Wire Home icon navigation to show DM list + Friends panel
- [ ] Wire friend click in `FriendsList.svelte` to open/create DM conversation
- [ ] Adapt `Composer.svelte` placeholder for DM context

### Documentation
- [ ] Update `ARCHITECTURE.md` with DM endpoints, SignalR events, and data model
- [ ] Update `DATA.md` with DM entities and schema diagram
- [ ] Update `FEATURES.md` to track Direct Messages feature progress
- [ ] Update `DESIGN.md` with DM UI specification
- [ ] Update `PLAN.md` with DM task breakdown

## Open Questions

- **Group DMs:** Should the system support group DMs (3+ participants)? (Deferred — the current design supports exactly 2 participants. The `DmChannelMember` join table makes future expansion to group DMs possible without schema changes.)
- **DM Reactions:** Should emoji reactions be supported on DM messages from the initial release? (Recommendation: defer to a follow-up iteration to reduce scope. The data model supports it, but the UI integration can come later.)
- **Unfriending behavior:** If two users are in a DM conversation and one removes the other as a friend, should the DM conversation remain accessible? (Recommendation: keep existing messages accessible as read-only, but prevent new messages until friendship is re-established.)
- **Message deletion:** ✅ Implemented — users can delete their own DM messages via the action bar. Cascade-deletes associated link previews and broadcasts `DmMessageDeleted` via SignalR for real-time removal.
- **Notifications:** Should DMs trigger push notifications or in-app badges? (Deferred to a future Notifications feature — for now, real-time delivery via SignalR is sufficient.)
