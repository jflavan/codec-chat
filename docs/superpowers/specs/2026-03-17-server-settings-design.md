# Server Settings Enhancement — Design Spec

## Overview

Enhance the server settings experience with six features: invite management tab, server descriptions, channel descriptions/topics, channel categories with drag-and-drop reordering, audit log, and per-server/per-channel notification mute preferences.

## Data Model

### New Entities

**ChannelCategory**

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| ServerId | Guid | FK → Server, ON DELETE CASCADE |
| Name | string | max 50 chars |
| Position | int | sort order within server |

**AuditLogEntry**

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| ServerId | Guid | FK → Server, ON DELETE CASCADE |
| ActorUserId | Guid? | FK → User, nullable, ON DELETE SET NULL |
| Action | enum | see Action enum below |
| TargetType | string? | e.g. "Channel", "User", "Invite", "Emoji", "Message" |
| TargetId | string? | ID of the affected entity |
| Details | string? | human-readable context, e.g. "Renamed #general to #lobby" |
| CreatedAt | DateTimeOffset | |

Index on `(ServerId, CreatedAt DESC)`. When `ActorUserId` is null (user deleted), UI displays "Deleted User".

**Action enum values:** `ServerRenamed`, `ServerDescriptionChanged`, `ServerIconChanged`, `ServerDeleted`, `ChannelCreated`, `ChannelRenamed`, `ChannelDescriptionChanged`, `ChannelDeleted`, `ChannelPurged`, `ChannelMoved`, `CategoryCreated`, `CategoryRenamed`, `CategoryDeleted`, `MemberKicked`, `MemberRoleChanged`, `InviteCreated`, `InviteRevoked`, `EmojiUploaded`, `EmojiRenamed`, `EmojiDeleted`, `MessageDeletedByAdmin`.

**ChannelNotificationOverride**

| Field | Type | Constraints |
|-------|------|-------------|
| UserId | Guid | Composite PK, FK → User, ON DELETE CASCADE |
| ChannelId | Guid | Composite PK, FK → Channel, ON DELETE CASCADE |
| IsMuted | bool | |

Composite PK on `(UserId, ChannelId)`. No surrogate Id — matches the `ServerMember` pattern.

### Modified Entities

**Server**
- Add `Description` (string?, max 256 chars)

**Channel**
- Add `Description` (string?, max 256 chars)
- Add `CategoryId` (Guid?, FK → ChannelCategory, nullable — uncategorized channels have null)
- Add `Position` (int, default 0) — sort order within category

**ServerMember**
- Add `IsMuted` (bool, default false) — server-level mute

### Cleanup

`AuditLogCleanupService` (BackgroundService) runs daily, deletes entries older than 90 days. Same pattern as existing `RefreshTokenCleanupService`.

## EF Core Migration

Single migration covering all schema changes:

- `Server.Description` (nullable string, max 256)
- `Channel.Description` (nullable string, max 256)
- `Channel.CategoryId` (nullable FK → ChannelCategory)
- `Channel.Position` (int, default 0)
- `ChannelCategory` table
- `AuditLogEntry` table with `(ServerId, CreatedAt DESC)` index
- `ServerMember.IsMuted` (bool, default false)
- `ChannelNotificationOverride` table with `(UserId, ChannelId)` composite PK

### Cascade delete behaviors

- `ChannelCategory` → ON DELETE CASCADE from Server (server deleted = categories deleted)
- `AuditLogEntry` → ON DELETE CASCADE from Server; ON DELETE SET NULL from User (preserve history when user deleted)
- `ChannelNotificationOverride` → ON DELETE CASCADE from both User and Channel
- `Channel.CategoryId` → ON DELETE SET NULL from ChannelCategory (category deleted = channels become uncategorized)

## API Endpoints

### Server description

- `PATCH /servers/{serverId}` — extend existing endpoint to accept optional `description` field alongside `name`. Owner/Admin/GlobalAdmin.
- `GET /servers` — include `description` in server list response.

### Channel description & categories

- `PATCH /servers/{serverId}/channels/{channelId}` — extend to accept optional `description` field. Owner/Admin/GlobalAdmin.
- `POST /servers/{serverId}/categories` — create category (Name). Returns 201. Owner/Admin/GlobalAdmin.
- `PATCH /servers/{serverId}/categories/{categoryId}` — rename category. Owner/Admin/GlobalAdmin.
- `DELETE /servers/{serverId}/categories/{categoryId}` — delete category. Channels in it become uncategorized (CategoryId → null). Owner/Admin/GlobalAdmin.
- `PUT /servers/{serverId}/channel-order` — bulk update channel positions and category assignments. Body: `{ channels: [{ channelId, categoryId?, position }] }`. Owner/Admin/GlobalAdmin. Broadcasts `ChannelOrderChanged` via SignalR.
- `PUT /servers/{serverId}/category-order` — bulk update category positions. Body: `{ categories: [{ categoryId, position }] }`. Owner/Admin/GlobalAdmin. Broadcasts `CategoryOrderChanged` via SignalR.

### Invite management

Existing endpoints, no changes needed:
- `POST /servers/{serverId}/invites`
- `GET /servers/{serverId}/invites`
- `DELETE /servers/{serverId}/invites/{inviteId}`

### Audit log

- `GET /servers/{serverId}/audit-log?before={cursor}&limit={n}` — paginated, newest first. Owner/Admin/GlobalAdmin. Returns `{ hasMore, entries }`. Each entry includes actor display name and avatar.

### Notification preferences

- `PUT /servers/{serverId}/mute` — toggle server-level mute. Body: `{ isMuted: bool }`. Any member.
- `PUT /servers/{serverId}/channels/{channelId}/mute` — toggle channel-level mute. Body: `{ isMuted: bool }`. Any member. Routed under `ServersController` to keep server-scoped operations together.
- `GET /servers/{serverId}/notification-preferences` — returns `{ serverMuted, channelOverrides: [{ channelId, isMuted }] }`. Any member.

### New SignalR Events

- `ChannelOrderChanged { serverId }` — clients reload channel list
- `CategoryOrderChanged { serverId }` — clients reload categories
- `CategoryCreated { serverId, categoryId, name, position }`
- `CategoryRenamed { serverId, categoryId, name }`
- `CategoryDeleted { serverId, categoryId }`
- `ServerDescriptionChanged { serverId, description }`
- `ChannelDescriptionChanged { serverId, channelId, description }`

Audit log entries are written inline in each controller action to keep the action and its context together.

## Request & Response DTOs

### Request DTOs (C#)

- `UpdateServerRequest` — extend existing: `{ Name?: string, Description?: string }` (at least one required)
- `UpdateChannelRequest` — extend existing: `{ Name?: string, Description?: string }` (at least one required)
- `CreateCategoryRequest` — `{ Name: string }` (required, max 50 chars)
- `RenameCategoryRequest` — `{ Name: string }` (required, max 50 chars)
- `UpdateChannelOrderRequest` — `{ Channels: [{ ChannelId: Guid, CategoryId?: Guid, Position: int }] }` (must include ALL channels in server; omitted channels are rejected with 400)
- `UpdateCategoryOrderRequest` — `{ Categories: [{ CategoryId: Guid, Position: int }] }`
- `MuteRequest` — `{ IsMuted: bool }`

### Response DTOs (C#)

- `AuditLogEntryResponse` — `{ Id, Action, TargetType?, TargetId?, Details?, CreatedAt, Actor: { UserId?, DisplayName, AvatarUrl? } }` (Actor.DisplayName = "Deleted User" when ActorUserId is null)
- `PaginatedAuditLog` — `{ HasMore: bool, Entries: AuditLogEntryResponse[] }`
- `NotificationPreferencesResponse` — `{ ServerMuted: bool, ChannelOverrides: [{ ChannelId, IsMuted }] }`

### Frontend Types (TypeScript in `models.ts`)

```typescript
interface ChannelCategory {
  id: string;
  serverId: string;
  name: string;
  position: number;
}

interface AuditLogEntry {
  id: string;
  action: string;
  targetType?: string;
  targetId?: string;
  details?: string;
  createdAt: string;
  actor: { userId?: string; displayName: string; avatarUrl?: string };
}

interface NotificationPreferences {
  serverMuted: boolean;
  channelOverrides: { channelId: string; isMuted: boolean }[];
}
```

Existing types to extend:
- `MemberServer` — add `description?: string`
- `Channel` (in channel list responses) — add `description?: string`, `categoryId?: string`, `position: number`

### Frontend State (`AppState`)

- `serverSettingsCategory` type: extend from `'general' | 'emojis' | 'members'` to `'general' | 'channels' | 'invites' | 'emojis' | 'members' | 'audit-log'`

## Frontend — Settings Modal

### Tab structure

Current: General, Emojis, Members.
New: **General, Channels, Invites, Emojis, Members, Audit Log**.

### General tab (simplified)

- Server icon upload/remove (existing)
- Server name edit (existing)
- **Server description** — textarea below the name field, 256 char limit with counter, saves on blur or Enter
- Danger zone: delete server (existing)

### Channels tab (new, extracted from General)

- Category management: create/rename/delete categories
- Channel list grouped by category (uncategorized channels at top)
- Drag-and-drop reordering within and across categories using `svelte-dnd-action`
- Inline channel rename (existing behavior, moved here)
- Channel description edit — expandable textarea per channel, 256 char limit
- Channel type indicator (existing)
- Delete/purge buttons (existing)

### Invites tab (new, replaces sidebar panel)

- Create invite form: optional expiration dropdown (1h, 6h, 12h, 24h, 7d, never), optional max uses input
- Active invites table: code, created by, uses/max, expires, revoke button
- Copy invite link button per row

### Audit Log tab (new)

- Scrollable list of events, newest first
- Each entry: actor avatar + name, action description, timestamp (relative)
- Infinite scroll pagination (same pattern as message loading)
- No filtering in v1 — chronological feed only

### Members tab

Unchanged — existing promote/demote/kick functionality stays as-is.

## Frontend — Outside Settings Modal

### Channel sidebar

- Channels grouped by category with collapsible headers (click category name to collapse/expand)
- Uncategorized channels appear at the top, above any categories
- Within each category, channels sorted by `position`, then text before voice
- Collapse state stored in `localStorage` per server using key `codec:category-collapse:{serverId}` (JSON array of collapsed category IDs)

### Chat area header

- **Server description**: muted subtitle text below the server name. Truncated with ellipsis after ~80 chars, full text on hover tooltip. Admins/Owners see a pencil icon on hover to edit inline.
- **Channel description/topic**: displayed to the right of the channel name, separated by a vertical divider. Truncated with tooltip for long text. Admins/Owners see a pencil icon on hover to edit inline.

### Notification mute controls

- **Server-level**: right-click context menu on server icon in the server rail → "Mute Server" toggle. Muted servers show a crossed-out bell badge on their icon.
- **Channel-level**: right-click context menu on channel name in sidebar → "Mute Channel" toggle. Muted channels show their name in dimmed text.
- Muted servers/channels suppress unread count badges and bold channel names. @mentions in muted channels are still delivered but not highlighted.

### Invite panel removal

- Delete `InvitePanel.svelte`
- Remove the invite button from `ChannelSidebar.svelte` header
- Remove `showInvitePanel` from `AppState`
- Settings modal Invites tab is the only way to manage invites

## Permissions

| Action | Owner | Admin | Member | GlobalAdmin |
|--------|-------|-------|--------|-------------|
| Edit server description | Yes | Yes | No | Yes |
| Edit channel description | Yes | Yes | No | Yes |
| Create/rename/delete categories | Yes | Yes | No | Yes |
| Reorder channels | Yes | Yes | No | Yes |
| Create/revoke invites | Yes | Yes | No | Yes |
| View audit log | Yes | Yes | No | Yes |
| Mute server (own preference) | Yes | Yes | Yes | Yes |
| Mute channel (own preference) | Yes | Yes | Yes | Yes |

## New Dependencies

- `svelte-dnd-action` — Svelte-native drag-and-drop library for channel reordering
