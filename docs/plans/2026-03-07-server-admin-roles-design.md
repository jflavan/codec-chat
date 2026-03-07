# Server Admin Role Management — Design

## Summary

Allow server owners and admins to promote members to admin (and demote them) through a new "Members" tab in the server settings modal.

## API

### New Endpoint

`PATCH /servers/{serverId}/members/{userId}/role`

**Request body:**

```json
{ "role": "Admin" | "Member" }
```

**Authorization rules:**

- Owner: can promote Members to Admin, demote Admins to Member
- Admin: can promote Members to Admin, cannot demote other Admins
- Nobody can change the Owner's role
- Nobody can change their own role
- Global admins bypass all checks

**Response:** updated member object (same shape as GET members response)

**Error cases:**

- 403 if caller lacks permission
- 404 if server or target member not found
- 400 if attempting to set role to Owner or change the Owner's role

### SignalR Event

`MemberRoleChanged` broadcast to `server-{serverId}` group.

Payload: `{ serverId, userId, newRole }`

## Frontend

### Server Settings — Members Tab

- New "Members" tab in `ServerSettingsSidebar`, visible only to Owner/Admin/GlobalAdmin
- New `ServerMembers.svelte` component rendered when the tab is active
- Displays all server members in a list: avatar, display name, current role
- Role actions per row:
  - **Owner row:** static "Owner" label, no action
  - **Current user's row:** static role label, no action
  - **Admin row (viewer is Owner/GlobalAdmin):** "Remove Admin" button with inline confirmation ("Are you sure?" replaces button briefly)
  - **Admin row (viewer is Admin):** no action (peer protection)
  - **Member row (viewer is Owner/Admin/GlobalAdmin):** "Make Admin" button, immediate (no confirmation)

### Member Sidebar — Role Badges

- Small "Owner" / "Admin" badge next to member names in the regular member sidebar (`MemberItem.svelte`)
- Subtle styling: muted color, small font size

### Real-time Updates

- `MemberRoleChanged` SignalR callback updates the member's role in `AppState`
- Member list badges and settings panel react automatically via Svelte `$derived` reactivity

## Scope Exclusions

- No new role types beyond the existing Owner/Admin/Member enum
- No granular per-permission system
- No audit log of role changes
