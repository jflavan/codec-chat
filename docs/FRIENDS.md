# Friends Feature Specification

This document describes the **Friends** feature for Codec — a social relationship system that allows users to connect with one another, manage friend requests, and view their friends list.

## Overview

The Friends system introduces user-to-user relationships outside the context of servers and channels. Users can send friend requests, accept or decline incoming requests, remove existing friends, and browse their friends list. The Friends feature lays the groundwork for [Direct Messages](DIRECT_MESSAGES.md), presence indicators, and other social features.

## Goals

- Allow users to form 1-on-1 social connections independent of server membership
- Provide a clear, low-friction flow for sending, accepting, and declining friend requests
- Surface a friends list in the UI that serves as a launch point for direct messages
- Respect user privacy — only mutual (accepted) friendships grant access to DMs and online status

## Terminology

| Term | Definition |
|------|-----------|
| **Friend Request** | A pending invitation from one user (requester) to another (recipient) to become friends |
| **Friendship** | A confirmed, mutual relationship between two users |
| **Requester** | The user who initiates a friend request |
| **Recipient** | The user who receives a friend request |
| **Friends List** | The UI panel that displays a user's confirmed friends and pending requests |

## User Stories

### Sending a Friend Request
> As an authenticated user, I want to send a friend request to another user so that I can connect with them outside of servers.

### Accepting a Friend Request
> As an authenticated user, I want to accept a pending friend request so that the requester and I become friends.

### Declining a Friend Request
> As an authenticated user, I want to decline a pending friend request so that the requester is not added to my friends list.

### Cancelling a Sent Friend Request
> As an authenticated user, I want to cancel a friend request I previously sent so that the recipient no longer sees it as pending.

### Removing a Friend
> As an authenticated user, I want to remove an existing friend so that they are no longer on my friends list.

### Viewing My Friends List
> As an authenticated user, I want to see my complete friends list so that I can start a conversation or view a friend's profile.

### Viewing Pending Requests
> As an authenticated user, I want to see incoming and outgoing pending friend requests so that I can act on them.

## Data Model

### Friendship Entity

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid (PK) | Unique friendship record identifier |
| `RequesterId` | Guid (FK → User) | The user who sent the friend request |
| `RecipientId` | Guid (FK → User) | The user who received the friend request |
| `Status` | FriendshipStatus (enum) | Current state of the relationship |
| `CreatedAt` | DateTimeOffset | When the request was sent |
| `UpdatedAt` | DateTimeOffset | When the status last changed |

### FriendshipStatus Enum

```
Pending   = 0   // Request sent, awaiting response
Accepted  = 1   // Both users are friends
Declined  = 2   // Recipient declined the request
```

### Constraints

- **Unique pair:** Only one `Friendship` row may exist for a given pair of users regardless of direction. Before inserting, check both `(RequesterId, RecipientId)` and `(RecipientId, RequesterId)`.
- **No self-friendship:** `RequesterId` must differ from `RecipientId`.

### Schema Diagram

```
┌─────────────┐          ┌───────────────┐          ┌─────────────┐
│   User      │          │  Friendship   │          │   User      │
│ (Requester) │          │───────────────│          │ (Recipient) │
│─────────────│          │ Id (PK)       │          │─────────────│
│ Id ─────────│─────────►│ RequesterId   │          │ Id ─────────│
│ DisplayName │          │ RecipientId   │◄─────────│ DisplayName │
│ Email       │          │ Status        │          │ Email       │
│ AvatarUrl   │          │ CreatedAt     │          │ AvatarUrl   │
└─────────────┘          │ UpdatedAt     │          └─────────────┘
                         └───────────────┘
```

### Indexes

```
IX_Friendship_RequesterId            — fast lookup of requests sent by a user
IX_Friendship_RecipientId            — fast lookup of requests received by a user
IX_Friendship_RequesterId_RecipientId (unique) — enforce one relationship per pair
```

## API Endpoints

All endpoints require authentication (`Authorization: Bearer <token>`).

### Send Friend Request

```
POST /friends/requests
```

**Request Body:**
```json
{
  "recipientUserId": "guid"
}
```

**Success Response:** `201 Created`
```json
{
  "id": "guid",
  "requester": { "id": "guid", "displayName": "Alice", "avatarUrl": "..." },
  "recipient": { "id": "guid", "displayName": "Bob", "avatarUrl": "..." },
  "status": "Pending",
  "createdAt": "2026-02-12T09:00:00Z"
}
```

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Recipient is the current user (self-request) |
| `404 Not Found` | Recipient user does not exist |
| `409 Conflict` | A friendship or pending request already exists between these users |

### List Friend Requests (Pending)

```
GET /friends/requests?direction=received|sent
```

**Query Parameters:**
| Parameter | Required | Description |
|-----------|----------|-------------|
| `direction` | No | Filter by `received` (default) or `sent` |

**Success Response:** `200 OK`
```json
[
  {
    "id": "guid",
    "requester": { "id": "guid", "displayName": "Alice", "avatarUrl": "..." },
    "recipient": { "id": "guid", "displayName": "Bob", "avatarUrl": "..." },
    "status": "Pending",
    "createdAt": "2026-02-12T09:00:00Z"
  }
]
```

### Respond to a Friend Request

```
PUT /friends/requests/{requestId}
```

**Request Body:**
```json
{
  "action": "accept" | "decline"
}
```

**Success Response:** `200 OK` — returns the updated friendship object with new status.

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Invalid action value |
| `403 Forbidden` | Current user is not the recipient of this request |
| `404 Not Found` | Request does not exist or is not in Pending status |

### Cancel a Sent Friend Request

```
DELETE /friends/requests/{requestId}
```

**Success Response:** `204 No Content`

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | Current user is not the requester |
| `404 Not Found` | Request does not exist or is not in Pending status |

### List Friends

```
GET /friends
```

**Success Response:** `200 OK`
```json
[
  {
    "friendshipId": "guid",
    "user": { "id": "guid", "displayName": "Bob", "avatarUrl": "..." },
    "since": "2026-02-12T09:05:00Z"
  }
]
```

Returns the *other* user in each accepted friendship, along with the date the friendship was established.

### Remove a Friend

```
DELETE /friends/{friendshipId}
```

**Success Response:** `204 No Content`

**Error Responses:**
| Status | Condition |
|--------|-----------|
| `403 Forbidden` | Current user is not a participant in this friendship |
| `404 Not Found` | Friendship does not exist or is not in Accepted status |

## Real-time Events (SignalR)

Friend-related events are delivered to the affected user(s) via a **user-scoped SignalR group** (e.g., `user-{userId}`). This is separate from channel-scoped groups used for messaging.

| Event | Payload | Delivered To |
|-------|---------|-------------|
| `FriendRequestReceived` | `{ requestId, requester: { id, displayName, avatarUrl }, createdAt }` | Recipient |
| `FriendRequestAccepted` | `{ friendshipId, user: { id, displayName, avatarUrl }, since }` | Requester |
| `FriendRequestDeclined` | `{ requestId }` | Requester |
| `FriendRequestCancelled` | `{ requestId }` | Recipient |
| `FriendRemoved` | `{ friendshipId, userId }` | The other participant |

## UI Design

### Friends Panel Location

The Friends panel is accessed from the **Home** button at the top of the server sidebar (the icon rail). When no server is selected, the middle sidebar displays the Friends panel instead of a channel list.

### Friends Panel Sections

#### Tab Bar
Three tabs at the top of the Friends panel:
1. **All Friends** — shows all confirmed friends
2. **Pending** — shows incoming and outgoing pending requests
3. **Add Friend** — provides a search/input field to send a new request

#### All Friends Tab
- List of confirmed friends, each showing: avatar (32px), display name, and friendship date
- Click a friend to open a direct message conversation (see [DIRECT_MESSAGES.md](DIRECT_MESSAGES.md))
- Right-click or overflow menu: "Remove Friend"

#### Pending Tab
- **Incoming Requests** section: shows requests awaiting the user's response with Accept (✓) and Decline (✕) buttons
- **Outgoing Requests** section: shows requests the user has sent with a Cancel (✕) button
- Each entry shows: avatar (32px), display name, and request date

#### Add Friend Tab
- Text input field: "Enter a username or email…"
- Search results appear below as the user types (debounced, 300ms)
- Each result shows: avatar, display name, email, and a "Send Request" button
- Disabled button with "Pending" label if a request already exists
- Disabled button with "Friends" label if already friends

### Styling

All components follow the existing CODEC CRT phosphor-green theme defined in [THEME.md](THEME.md) and [DESIGN.md](DESIGN.md):
- Tab bar uses `--bg-secondary` background with `--accent` active indicator
- Friend list items use the same card pattern as member items in `MembersSidebar`
- Accept/Send buttons use `--accent` (primary style), Decline/Cancel/Remove use `--danger`
- Hover states follow existing component conventions (150–200ms transitions)

## Acceptance Criteria

### AC-1: Send a Friend Request
- [ ] An authenticated user can send a friend request to another user by user ID or via search
- [ ] The API returns `201 Created` with the new pending friendship
- [ ] The API returns `409 Conflict` if a friendship or pending request already exists
- [ ] The API returns `400 Bad Request` if the user attempts to friend themselves
- [ ] The API returns `404 Not Found` if the recipient does not exist
- [ ] The recipient receives a real-time `FriendRequestReceived` event via SignalR

### AC-2: Accept a Friend Request
- [ ] The recipient of a pending request can accept it
- [ ] Accepting changes the friendship status from `Pending` to `Accepted`
- [ ] The requester receives a real-time `FriendRequestAccepted` event via SignalR
- [ ] Both users now appear in each other's friends list

### AC-3: Decline a Friend Request
- [ ] The recipient of a pending request can decline it
- [ ] Declining changes the friendship status to `Declined`
- [ ] The requester receives a real-time `FriendRequestDeclined` event via SignalR
- [ ] The declined request no longer appears in either user's pending list

### AC-4: Cancel a Sent Friend Request
- [ ] The requester can cancel a pending request they sent
- [ ] Cancelling deletes the friendship record
- [ ] The recipient receives a real-time `FriendRequestCancelled` event via SignalR
- [ ] The cancelled request no longer appears in either user's pending list

### AC-5: View Friends List
- [ ] An authenticated user can view all confirmed friends
- [ ] Each entry displays the friend's avatar, display name, and friendship date
- [ ] The list updates in real-time when friendships are added or removed

### AC-6: View Pending Requests
- [ ] An authenticated user can view incoming pending requests
- [ ] An authenticated user can view outgoing pending requests
- [ ] Each entry displays the other user's avatar, display name, and request date
- [ ] Incoming requests show Accept and Decline action buttons
- [ ] Outgoing requests show a Cancel action button

### AC-7: Remove a Friend
- [ ] An authenticated user can remove a confirmed friend
- [ ] Removing deletes the friendship record
- [ ] The removed friend receives a real-time `FriendRemoved` event via SignalR
- [ ] The removed user no longer appears in either user's friends list

### AC-8: Add Friend Search
- [ ] The Add Friend tab provides a search input for finding users
- [ ] Search results appear after a debounced 300ms pause in typing
- [ ] Results show avatar, display name, and a contextual action button
- [ ] The action button reflects current relationship status (Send Request / Pending / Friends)

### AC-9: Friends Panel UI
- [ ] The Friends panel is accessible from the Home icon in the server sidebar
- [ ] The panel displays three tabs: All Friends, Pending, Add Friend
- [ ] All components follow the CODEC CRT phosphor-green theme
- [ ] Styles are consistent with existing components (MembersSidebar, MemberItem)

### AC-10: Database Integrity
- [ ] Only one friendship record exists between any two users
- [ ] Self-friendships are prevented at the database and API level
- [ ] Friendship records include proper foreign keys to the User table
- [ ] The `Friendship` table has appropriate indexes for query performance

## Dependencies

- **Prerequisite:** None — the Friends feature can be built on top of the existing User model
- **Enables:** [Direct Messages](DIRECT_MESSAGES.md) (friends list serves as the primary entry point for starting DM conversations)
- **Related:** Presence indicators (online/offline status of friends) — a future feature that builds on the friends list

## Migration Plan

A single EF Core migration (`AddFriendships`) will:
1. Create the `Friendships` table with all columns, foreign keys, and indexes
2. Seed no initial data (friendships are user-generated)

## Task Breakdown

### API
- [ ] Create `Friendship` entity and `FriendshipStatus` enum in `Models/`
- [ ] Add `Friendships` DbSet to `CodecDbContext`
- [ ] Configure entity relationships, unique constraint, and indexes in `OnModelCreating`
- [ ] Create and apply EF Core migration (`AddFriendships`)
- [ ] Create `FriendsController` with all endpoints
- [ ] Add user search endpoint (`GET /users/search?q=...`) for the Add Friend flow
- [ ] Add user-scoped SignalR group support to `ChatHub` (join `user-{userId}` group on connect)
- [ ] Broadcast friend-related events via SignalR

### Web
- [ ] Add `Friendship`, `FriendRequest`, and `FriendshipStatus` types to `models.ts`
- [ ] Add friend-related API methods to `ApiClient`
- [ ] Add friend-related SignalR event handlers to `ChatHubService`
- [ ] Add friends state management to `AppState`
- [ ] Create `FriendsPanel.svelte` component with tab navigation
- [ ] Create `FriendsList.svelte` (All Friends tab)
- [ ] Create `PendingRequests.svelte` (Pending tab)
- [ ] Create `AddFriend.svelte` (Add Friend tab with search)
- [ ] Wire Home icon in `ServerSidebar` to display the Friends panel

### Documentation
- [ ] Update `ARCHITECTURE.md` with new endpoints and SignalR events
- [ ] Update `DATA.md` with Friendship entity and schema diagram
- [ ] Update `FEATURES.md` to track Friends feature progress
- [ ] Update `DESIGN.md` with Friends panel UI specification
- [ ] Update `PLAN.md` with Friends task breakdown

## Open Questions

1. **Blocked users:** Should there be a "Block" action that prevents future friend requests? (Deferred to a future moderation feature.)
2. **Request expiration:** Should pending friend requests expire after a period of time? (Not for MVP — revisit if request spam becomes an issue.)
3. **Mutual server requirement:** Should users need to share a server to send friend requests, or can any user request any other user? (Recommendation: any user can request any other, matching Discord's behavior.)
