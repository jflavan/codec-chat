# Server Settings Feature

## Overview
The Server Settings feature provides a centralized UI for server Owners and Admins to manage server configuration, including server name, channel names, and viewing member statistics. The feature follows the existing modal design patterns established by the User Settings feature.

## Access
- **Location:** Gear icon (⚙) button in the channel sidebar header
- **Permissions:** Only visible to users with Owner or Admin role in the current server
- **Shortcut:** Click the gear icon or use ESC to close

## UI Components

### ServerSettingsModal
A full-screen modal overlay that displays server management options. Follows the same design patterns as UserSettingsModal:
- Semi-transparent dark backdrop (rgba(0, 0, 0, 0.85))
- Centered content panel (max-width: 740px)
- Close button (top-right corner)
- ESC key and backdrop click handlers
- Focus management (restore focus on close)

### ServerSettings Content
The modal contains three main sections:

#### 1. Server Overview
- **Server Name Display:** Shows current server name
- **Edit Mode:** Click "Edit" button to enter inline edit mode
- **Name Input:** Text input with 100-character limit
- **Actions:** Save/Cancel buttons
- **Keyboard Shortcuts:**
  - Enter: Save changes
  - Escape: Cancel edit mode
- **Real-time Sync:** Changes broadcast to all server members via SignalR

#### 2. Channel Management
- **Channel List:** Displays all channels in the server with # prefix
- **Inline Rename:** Click "Edit" button next to any channel name
- **Name Input:** Text input with 100-character limit
- **Actions:** Save/Cancel buttons per channel
- **Keyboard Shortcuts:**
  - Enter: Save changes
  - Escape: Cancel edit mode
- **Real-time Sync:** Changes broadcast to all server members via SignalR

#### 3. Member Management
- **Member Count:** Displays total number of server members
- **Reference:** Links to the Members sidebar for detailed member management
- **Note:** Kicking members is handled through the existing Members sidebar UI

## API Endpoints

### Update Server Name
```http
PATCH /servers/{serverId}
Authorization: Bearer {idToken}
Content-Type: application/json

{
  "name": "New Server Name"
}
```

**Response:**
```json
{
  "id": "guid",
  "name": "New Server Name"
}
```

**Authorization:**
- Requires Owner or Admin role
- Returns 403 Forbidden for non-admin members
- Returns 404 Not Found if server doesn't exist

**Validation:**
- Name is required (non-empty after trimming)
- Maximum 100 characters
- Leading/trailing whitespace is trimmed

**SignalR Event:**
```json
{
  "event": "ServerNameChanged",
  "serverId": "guid",
  "name": "New Server Name"
}
```

### Update Channel Name
```http
PATCH /servers/{serverId}/channels/{channelId}
Authorization: Bearer {idToken}
Content-Type: application/json

{
  "name": "new-channel-name"
}
```

**Response:**
```json
{
  "id": "guid",
  "name": "new-channel-name",
  "serverId": "guid"
}
```

**Authorization:**
- Requires Owner or Admin role
- Returns 403 Forbidden for non-admin members
- Returns 404 Not Found if server or channel doesn't exist

**Validation:**
- Name is required (non-empty after trimming)
- Maximum 100 characters
- Leading/trailing whitespace is trimmed

**SignalR Event:**
```json
{
  "event": "ChannelNameChanged",
  "serverId": "guid",
  "channelId": "guid",
  "name": "new-channel-name"
}
```

## Frontend State Management

### App State Properties
```typescript
serverSettingsOpen: boolean          // Modal visibility
isUpdatingServerName: boolean        // Loading state for server name update
isUpdatingChannelName: boolean       // Loading state for channel name update
```

### App State Methods
```typescript
openServerSettings(): void           // Open the server settings modal
closeServerSettings(): void          // Close the server settings modal
updateServerName(name: string): Promise<void>      // Update server name
updateChannelName(channelId: string, name: string): Promise<void>  // Update channel name
```

### SignalR Event Handlers
```typescript
onServerNameChanged(event: ServerNameChangedEvent): void
onChannelNameChanged(event: ChannelNameChangedEvent): void
```

These handlers update the local state to reflect name changes in real-time, ensuring all connected clients see the updates immediately.

## Real-time Updates

### Server Name Changes
When a server name is updated:
1. API validates the request and updates the database
2. API broadcasts `ServerNameChanged` event via SignalR to all members in the server group
3. All connected clients receive the event and update their local server list
4. The change is reflected immediately in:
   - Server sidebar (if visible)
   - Channel sidebar header
   - Browser title (if the server is currently selected)

### Channel Name Changes
When a channel name is updated:
1. API validates the request and updates the database
2. API broadcasts `ChannelNameChanged` event via SignalR to all members in the server group
3. All connected clients receive the event and update their local channel list
4. The change is reflected immediately in:
   - Channel list in the channel sidebar
   - Chat area header (if the channel is currently selected)
   - Browser title (if the channel is currently selected)

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
- Only Owners and Admins can access server settings
- Frontend hides the settings gear icon for non-admin members
- Backend validates permissions on every request

### Input Validation
- Server and channel names are trimmed and validated
- Maximum length enforced (100 characters)
- Empty names are rejected
- SQL injection protection via parameterized queries

### SSRF Protection
- Not applicable (no URL inputs in this feature)

## Future Enhancements

Potential expansions of the Server Settings feature:
1. **Server Description:** Add a description field for servers
2. **Server Icon:** Upload custom server icons/avatars
3. **Channel Ordering:** Drag-and-drop to reorder channels
4. **Channel Deletion:** Delete channels (with confirmation)
5. **Channel Categories:** Group channels into categories
6. **Permission System:** Granular permissions per role
7. **Audit Log:** View history of server changes
8. **Member Role Management:** Promote/demote members from settings
9. **Server Deletion:** Delete entire server (with confirmation and ownership transfer)
10. **Server Templates:** Create and apply server templates

## Related Documentation

- [Architecture](ARCHITECTURE.md) - System design and API endpoints
- [Features](FEATURES.md) - Complete feature list
- [User Settings](USER_SETTINGS.md) - Similar modal pattern for user preferences
- [Design](DESIGN.md) - UI/UX design specification
