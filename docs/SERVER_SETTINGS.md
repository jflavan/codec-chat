# Server Settings Feature

## Overview
The Server Settings feature provides a centralized UI for server Owners and Admins to manage server configuration. It covers server name and description, channel management with categories and descriptions, invite management, member role management, audit log viewing, and notification preferences. The feature follows the existing modal design patterns established by the User Settings feature.

## Access
- **Location:** Gear icon (⚙) button in the channel sidebar header
- **Permissions:** Only visible to users with Owner or Admin role in the current server, or users with the Global Admin role (Global Admin can access settings for any server, even if not a member)
- **Shortcut:** Click the gear icon or use ESC to close

## UI Components

### ServerSettingsModal
A full-screen modal overlay that displays server management options. Follows the same design patterns as UserSettingsModal:
- Semi-transparent dark backdrop (rgba(0, 0, 0, 0.85))
- Centered content panel (max-width: 740px)
- Close button (top-right corner)
- ESC key and backdrop click handlers
- Focus management (restore focus on close)

### Settings Tabs

The modal uses a sidebar with six tabs:

| Tab | Access |
|-----|--------|
| General | Owner, Admin, Global Admin |
| Channels | Owner, Admin, Global Admin |
| Invites | Owner, Admin, Global Admin |
| Emojis | Owner, Admin, Global Admin |
| Members | Owner, Admin, Global Admin |
| Audit Log | Owner, Admin, Global Admin |

### ServerSettings Content

#### 1. General Tab
- **Server Name Display:** Shows current server name with inline edit (100-character limit)
- **Server Description:** Optional description field (256-character limit); editable inline; shown in the chat area header for all members
- **Keyboard Shortcuts:** Enter to save, Escape to cancel
- **Real-time Sync:** Name changes broadcast via `ServerNameChanged`, description changes via `ServerDescriptionChanged`
- **Danger Zone (Owner or Global Admin):** Permanent server deletion with two-step confirmation; cascades to all channels, messages, reactions, invites, and members; broadcasts `ServerDeleted` to all members

#### 2. Channels Tab (`ServerChannels.svelte`)
- **Channel List:** All channels grouped by category (uncategorized first, then named categories)
- **Inline Rename:** Edit button next to any channel name (100-character limit)
- **Channel Description/Topic:** Optional per-channel description (256-character limit); edit button opens inline field; displayed in the chat area header
- **Category Assignment:** Drag-and-drop channels between categories and within categories to reorder; bulk position saved via `PUT /servers/{id}/channel-order`
- **Create Category:** "Add Category" button creates a new named category group
- **Rename/Delete Category:** Inline rename and delete controls per category header
- **Delete Channel:** Delete button with inline confirmation; cascade-deletes messages, reactions, and link previews
- **Real-time Sync:** Channel description changes broadcast via `ChannelDescriptionChanged`; category events via `CategoryCreated`, `CategoryRenamed`, `CategoryDeleted`; order changes via `ChannelOrderChanged`, `CategoryOrderChanged`

#### 3. Invites Tab (`ServerInvites.svelte`)
- **Invite List:** All active (non-expired) invite codes with creator name, expiry, use count, and max uses
- **Create Invite:** Configurable expiry (1h, 24h, 7d, 30d, never) and max uses (1, 5, 10, 25, 100, unlimited)
- **Revoke Invite:** Delete button removes the invite code immediately
- **Copy Link:** Click to copy the full invite URL to clipboard
- *(Moved from the channel sidebar InvitePanel; the sidebar no longer shows invite management)*

#### 4. Member Management Tab
- **Member List:** All server members with role badges
- **Role Management:** Promote/demote members (Owner freely; Admin can promote Members only)
- **Real-time:** Role changes broadcast via `MemberRoleChanged` SignalR event

#### 5. Audit Log Tab (`ServerAuditLog.svelte`)
- **Paginated Log:** Chronological list of server events (newest first), 50 entries per page
- **Entry Fields:** Timestamp, actor display name (or "System"), action type, target type, target display, and optional detail string
- **Tracked Action Types:** ServerCreated, ServerRenamed, ServerDescriptionChanged, ServerDeleted, ChannelCreated, ChannelRenamed, ChannelDescriptionChanged, ChannelDeleted, ChannelPurged, MemberJoined, MemberLeft, MemberKicked, MemberRoleChanged, InviteCreated, InviteRevoked, EmojiUploaded, EmojiRenamed, EmojiDeleted, MessageDeleted, CategoryCreated, CategoryDeleted
- **Auto-cleanup:** Entries older than 90 days are purged automatically by `AuditLogCleanupService`

#### 6. Notification Preferences
Notification mute is managed via right-click context menus rather than within the settings modal:
- **Server mute:** Right-click a server icon in the server rail → "Mute Server" / "Unmute Server"; stored in `ServerMember.IsMuted`
- **Channel mute:** Right-click a channel in the channel sidebar → "Mute Channel" / "Unmute Channel"; stored in `ChannelNotificationOverride.IsMuted`
- **Context Menu Component:** `ContextMenu.svelte` is a reusable component used for both server and channel context menus

## API Endpoints

### Server Management

#### Update Server (name and/or description)
```http
PATCH /servers/{serverId}
Authorization: Bearer {idToken}
Content-Type: application/json

{
  "name": "New Server Name",
  "description": "Optional server description (256 char max)"
}
```
- `name` and `description` are both optional; omit to leave unchanged
- Broadcasts `ServerNameChanged` and/or `ServerDescriptionChanged` via SignalR
- Requires Owner, Admin, or Global Admin role

#### Update Channel (name and/or description)
```http
PATCH /servers/{serverId}/channels/{channelId}
Authorization: Bearer {idToken}
Content-Type: application/json

{
  "name": "new-channel-name",
  "description": "Optional channel topic (256 char max)"
}
```
- `name` and `description` are both optional; omit to leave unchanged
- Broadcasts `ChannelNameChanged` and/or `ChannelDescriptionChanged` via SignalR
- Requires Owner, Admin, or Global Admin role

#### Delete Channel
```http
DELETE /servers/{serverId}/channels/{channelId}
Authorization: Bearer {idToken}
```
- Cascade-deletes all messages, reactions, and link previews
- Broadcasts `ChannelDeleted` to all server members
- Requires Owner, Admin, or Global Admin role

#### Delete Server
```http
DELETE /servers/{serverId}
Authorization: Bearer {idToken}
```
- Cascade-deletes all channels, messages, reactions, link previews, members, and invites
- Broadcasts `ServerDeleted` to all server members; clients navigate away automatically
- Requires Owner or Global Admin role

### Channel Categories

#### List Categories
```http
GET /servers/{serverId}/categories
Authorization: Bearer {idToken}
```
Returns all categories for the server ordered by position.

#### Create Category
```http
POST /servers/{serverId}/categories
Authorization: Bearer {idToken}
Content-Type: application/json

{ "name": "Category Name" }
```
Broadcasts `CategoryCreated` via SignalR. Requires Owner, Admin, or Global Admin.

#### Rename Category
```http
PATCH /servers/{serverId}/categories/{categoryId}
Authorization: Bearer {idToken}
Content-Type: application/json

{ "name": "New Name" }
```
Broadcasts `CategoryRenamed` via SignalR. Requires Owner, Admin, or Global Admin.

#### Delete Category
```http
DELETE /servers/{serverId}/categories/{categoryId}
Authorization: Bearer {idToken}
```
Channels in the deleted category become uncategorized. Broadcasts `CategoryDeleted` via SignalR. Requires Owner, Admin, or Global Admin.

#### Bulk Update Channel Order
```http
PUT /servers/{serverId}/channel-order
Authorization: Bearer {idToken}
Content-Type: application/json

[
  { "channelId": "guid", "position": 0, "categoryId": "guid-or-null" },
  { "channelId": "guid", "position": 1, "categoryId": "guid-or-null" }
]
```
Updates `Position` and `CategoryId` for all listed channels atomically. Broadcasts `ChannelOrderChanged` via SignalR.

#### Bulk Update Category Order
```http
PUT /servers/{serverId}/category-order
Authorization: Bearer {idToken}
Content-Type: application/json

[
  { "categoryId": "guid", "position": 0 },
  { "categoryId": "guid", "position": 1 }
]
```
Updates `Position` for all listed categories atomically. Broadcasts `CategoryOrderChanged` via SignalR.

### Audit Log

#### Get Audit Log
```http
GET /servers/{serverId}/audit-log?page=1&pageSize=50
Authorization: Bearer {idToken}
```
Returns paginated audit log entries (newest first). Default page size: 50.

**Response:**
```json
{
  "totalCount": 123,
  "entries": [
    {
      "id": "guid",
      "action": "ChannelCreated",
      "actorDisplayName": "Alice",
      "targetType": "Channel",
      "targetId": "guid",
      "targetDisplay": "#general",
      "details": null,
      "createdAt": "2026-03-17T12:00:00Z"
    }
  ]
}
```
Requires Owner, Admin, or Global Admin role.

### Notification Preferences

#### Get Notification Preferences
```http
GET /servers/{serverId}/notification-preferences
Authorization: Bearer {idToken}
```
Returns the current user's mute settings for the server and its channels.

**Response:**
```json
{
  "serverMuted": false,
  "channelOverrides": [
    { "channelId": "guid", "isMuted": true }
  ]
}
```

#### Toggle Server Mute
```http
PUT /servers/{serverId}/mute
Authorization: Bearer {idToken}
Content-Type: application/json

{ "isMuted": true }
```
Updates `ServerMember.IsMuted` for the current user. Requires membership.

#### Toggle Channel Mute
```http
PUT /servers/{serverId}/channels/{channelId}/mute
Authorization: Bearer {idToken}
Content-Type: application/json

{ "isMuted": true }
```
Creates or updates a `ChannelNotificationOverride` record for the current user and channel. Requires membership.

## Frontend State Management

### App State Properties
```typescript
serverSettingsOpen: boolean              // Modal visibility
serverSettingsTab: string                // Active tab ('general' | 'channels' | 'invites' | 'emojis' | 'members' | 'audit-log')
isUpdatingServerName: boolean            // Loading state for server name update
isUpdatingServerDescription: boolean     // Loading state for server description update
isUpdatingChannelName: boolean           // Loading state for channel name update
isGlobalAdmin: boolean                   // Whether current user has global admin role
canDeleteServer: boolean                 // Derived: isGlobalAdmin || isOwner
canDeleteChannel: boolean                // Derived: isGlobalAdmin || isOwner || isAdmin
categories: ChannelCategory[]            // Ordered list of categories for the current server
notificationPreferences: NotificationPreferences | null  // Current mute settings
```

### App State Methods
```typescript
openServerSettings(tab?: string): void           // Open the server settings modal, optionally on a specific tab
closeServerSettings(): void                      // Close the server settings modal
updateServerName(name: string): Promise<void>
updateServerDescription(description: string): Promise<void>
updateChannelName(channelId: string, name: string): Promise<void>
updateChannelDescription(channelId: string, description: string): Promise<void>
createCategory(name: string): Promise<void>
renameCategory(categoryId: string, name: string): Promise<void>
deleteCategory(categoryId: string): Promise<void>
updateChannelOrder(updates: ChannelOrderUpdate[]): Promise<void>
updateCategoryOrder(updates: CategoryOrderUpdate[]): Promise<void>
deleteServer(serverId: string): Promise<void>
deleteChannel(serverId: string, channelId: string): Promise<void>
muteServer(serverId: string, isMuted: boolean): Promise<void>
muteChannel(serverId: string, channelId: string, isMuted: boolean): Promise<void>
loadNotificationPreferences(serverId: string): Promise<void>
```

### SignalR Event Handlers
```typescript
onServerNameChanged(event): void
onServerDescriptionChanged(event): void
onChannelNameChanged(event): void
onChannelDescriptionChanged(event): void
onServerDeleted(event): void
onChannelDeleted(event): void
onCategoryCreated(event): void
onCategoryRenamed(event): void
onCategoryDeleted(event): void
onChannelOrderChanged(event): void
onCategoryOrderChanged(event): void
```

## Real-time Updates

### Server Changes
- `ServerNameChanged` — broadcast to server group; all clients update their server list and sidebar header
- `ServerDescriptionChanged` — broadcast to server group; all clients update the chat area header description

### Channel Changes
- `ChannelNameChanged` — broadcast to server group; all clients update the channel sidebar and chat area header
- `ChannelDescriptionChanged` — broadcast to server group; all clients update the chat area header topic
- `ChannelDeleted` — broadcast to server group; clients remove the channel from local state and navigate if active

### Category and Order Changes
- `CategoryCreated` — broadcast to server group; clients append the new category to their sidebar
- `CategoryRenamed` — broadcast to server group; clients update the category header label
- `CategoryDeleted` — broadcast to server group; clients remove the category; affected channels become uncategorized
- `ChannelOrderChanged` — broadcast to server group; clients reorder channels and reassign category IDs
- `CategoryOrderChanged` — broadcast to server group; clients reorder category groups in the sidebar

### Server/Channel Deletion
- `ServerDeleted` — broadcast to server group; all clients remove the server and navigate to Home
- `ChannelDeleted` — broadcast to server group; clients remove the channel from their list

## Mobile Responsive Design

The Server Settings modal adapts to smaller screens (< 900px):
- Full-width content area
- Reduced padding (24px horizontal vs 32px on desktop)
- Stacked button layout in forms
- Touch-friendly button sizes
- Optimized for portrait and landscape orientations

## Accessibility

### Keyboard Navigation
- **ESC:** Close modal and return focus to trigger element
- **Enter:** Submit inline forms (when in edit mode)
- **Tab:** Navigate between interactive elements

### ARIA Labels
- Modal has `role="dialog"` and `aria-label="Server Settings"`
- Close button has `aria-label="Close settings"`
- All form inputs have associated labels
- Member count section provides context for screen readers

### Focus Management
- Focus is captured within the modal when open
- Previous focus element is restored when modal closes
- Keyboard focus is visible on all interactive elements

## Security Considerations

### Authorization
- All endpoints enforce role-based access control (RBAC)
- Only Owners, Admins, and Global Admins can access server settings
- Frontend hides the settings gear icon for non-admin members (shown for Global Admin)
- Backend validates permissions on every request
- Global Admin bypasses membership and role checks for all server settings operations

### Input Validation
- Server and channel names are trimmed and validated
- Maximum length enforced (100 characters)
- Empty names are rejected
- SQL injection protection via parameterized queries

### SSRF Protection
- Not applicable (no URL inputs in this feature)

## Future Enhancements

Potential expansions of the Server Settings feature:
1. **Permission System:** Granular permissions per role
2. **Server Templates:** Create and apply server templates
3. **Notification badge suppression:** Actually suppress unread/mention badges for muted servers/channels in the UI

## Related Documentation

- [Architecture](ARCHITECTURE.md) - System design and API endpoints
- [Features](FEATURES.md) - Complete feature list
- [User Settings](USER_SETTINGS.md) - Similar modal pattern for user preferences
- [Design](DESIGN.md) - UI/UX design specification
