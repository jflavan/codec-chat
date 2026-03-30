# Roles and Permissions — Discord-Style Design

## Overview

Full Discord-style roles and permissions system: multi-role assignment per member, granular permission editing on roles, and per-channel permission overrides with three-state (Allow / Neutral / Deny) control.

## Current State

The codebase has a `ServerRoleEntity` model with a `Permission` flags enum (24 permissions), a `RolesController` with CRUD endpoints, and hierarchy enforcement. However:

- Each member has exactly one role (`ServerMember.RoleId`)
- Permission editing is not exposed in the UI
- No per-channel permission overrides exist
- Enforcement is inconsistent — some actions check server-level permissions, others check role names

## Design Decisions

- **Server-level role permissions**: two-state (granted / not granted)
- **Channel-level overrides**: three-state (Allow / Neutral / Deny) — deny always wins
- **Multi-role**: members can hold multiple roles; permissions are OR'd across all roles
- **"Member" role**: stays as the default role assigned on join; not an implicit `@everyone` — it's a regular role in the multi-role list
- **Permission resolution**: happens on the backend; the API returns effective permissions to the frontend

## Data Model

### New Tables

#### `ServerMemberRoles` (join table)

Replaces `ServerMember.RoleId`.

| Column | Type | Notes |
|--------|------|-------|
| `ServerId` | Guid | Composite PK, FK to Server |
| `UserId` | Guid | Composite PK, FK to User |
| `RoleId` | Guid | Composite PK, FK to ServerRoleEntity |
| `AssignedAt` | DateTimeOffset | When the role was granted |

#### `ChannelPermissionOverrides`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | Guid | PK |
| `ChannelId` | Guid | FK to Channel |
| `RoleId` | Guid | FK to ServerRoleEntity |
| `Allow` | long | Permission bitmask — bits explicitly granted |
| `Deny` | long | Permission bitmask — bits explicitly denied |

Unique index on `(ChannelId, RoleId)`.

### Changes to Existing Tables

- **`ServerMember`**: drop the `RoleId` column. Role relationships move entirely to `ServerMemberRoles`.
- **`ServerRoleEntity`**: no schema changes. `Permissions` column stays as-is (two-state grant bitmask).

### Migration

1. Create `ServerMemberRoles` table
2. Create `ChannelPermissionOverrides` table
3. For each `ServerMember` with a `RoleId`, insert a row into `ServerMemberRoles`
4. Drop `RoleId` column from `ServerMember`
5. Remove the legacy `ServerRole` enum (`Models/ServerRole.cs`). Replace all references to `ServerRole.Owner` / `ServerRole.Admin` / `ServerRole.Member` with checks against `ServerRoleEntity.IsSystemRole` and `ServerRoleEntity.Name` or position-based hierarchy checks

## Permission Resolution Algorithm

```
function resolvePermissions(member, channel):
    // 1. Server owner bypasses everything
    if member is server owner → return ALL_PERMISSIONS

    // 2. Collect all member's roles (from ServerMemberRoles)
    roles = member's assigned roles

    // 3. Compute server-level permissions (OR together all role grants)
    serverPerms = None
    for each role in roles:
        serverPerms |= role.Permissions

    // 4. Administrator bypasses everything
    if serverPerms has Administrator → return ALL_PERMISSIONS

    // 5. Apply channel overrides
    channelAllow = None
    channelDeny = None
    for each role in roles:
        override = channel.overrides[role.Id]
        if override exists:
            channelAllow |= override.Allow
            channelDeny |= override.Deny

    // 6. Deny wins: apply allow first, then deny last
    effectivePerms = (serverPerms | channelAllow) & ~channelDeny

    // 7. ViewChannels gate: if can't view, deny everything
    if effectivePerms does NOT have ViewChannels → return None

    return effectivePerms
```

Key behaviors:
- Deny always wins over Allow at the channel level (deny applied last)
- Administrator bypasses channel overrides entirely
- Server owner bypasses everything
- ViewChannels gate: if the resolved permissions lack ViewChannels, the channel is invisible and all permissions are stripped

## Backend Changes

### New Service: `PermissionResolverService`

Dedicated service for computing effective permissions. Replaces direct `UserService.EnsurePermissionAsync` calls.

```
PermissionResolverService (scoped lifetime — caches per request):
    ResolveServerPermissions(serverId, userId) → Permission
    ResolveChannelPermissions(channelId, userId) → Permission
    HasPermission(channelId, userId, Permission) → bool
```

### API Endpoints

#### Updated: Role management (`RolesController`)

- `PATCH /servers/{serverId}/roles/{roleId}` — now includes permission editing (bitmask update)

#### New: Channel overrides

- `GET /channels/{channelId}/overrides` — list all permission overrides for a channel
- `PUT /channels/{channelId}/overrides/{roleId}` — set Allow/Deny bitmask for a role on a channel
- `DELETE /channels/{channelId}/overrides/{roleId}` — remove a role's override from a channel

#### Updated: Member role management (`ServersController`)

Replace `PATCH /servers/{serverId}/members/{userId}/role` with:

- `PUT /servers/{serverId}/members/{userId}/roles` — set the full list of roles for a member
- `POST /servers/{serverId}/members/{userId}/roles/{roleId}` — add a single role
- `DELETE /servers/{serverId}/members/{userId}/roles/{roleId}` — remove a single role

#### Hierarchy enforcement (multi-role)

"Can user A act on user B" compares the highest role position (lowest number) of each user. User A can only act on user B if A's highest role is above (lower position number than) B's highest role.

### SignalR Events

New events broadcast to `server-{serverId}`:
- `MemberRolesUpdated` — when a member's role list changes (replaces `MemberRoleChanged`)
- `ChannelOverrideUpdated` — when a channel permission override is set or removed

### Permission Enforcement Audit

These actions must resolve permissions against the specific channel (not just server-level):

| Action | Resolved permission |
|--------|-------------------|
| Send message | `SendMessages` (channel) |
| Pin message | `PinMessages` (channel) |
| Delete others' messages | `ManageMessages` (channel) |
| Upload file | `AttachFiles` (channel) |
| Add reaction | `AddReactions` (channel) |
| Join voice channel | `Connect` (channel) |

SignalR hub (`ChatHub`) must check channel-resolved permissions for:
- Joining a channel group (`ViewChannels`)
- Sending messages (`SendMessages`)
- Voice actions (`Connect`, `Speak`)

Channel visibility: `GET /servers/{serverId}/channels` filters out channels where the user lacks `ViewChannels`. SignalR broadcasting skips users who can't see a channel.

## Frontend Changes

### Types

Update `Member`:
```typescript
type Member = {
    // ...existing fields...
    roles: MemberRole[];
    permissions: number;
    displayRole?: MemberRole;  // highest role with color, for name coloring
};

type MemberRole = {
    id: string;
    name: string;
    color?: string | null;
    position: number;
    isSystemRole: boolean;
};
```

New:
```typescript
type ChannelPermissionOverride = {
    channelId: string;
    roleId: string;
    roleName: string;
    allow: number;
    deny: number;
};
```

### State (`AppState`)

- Replace single-role tracking with multi-role `memberRoles`
- New `channelOverrides` state loaded on demand in channel settings
- Existing permission helpers (`canManageChannels`, etc.) continue working — they read from `permissions` which the API computes

### Components

**`ServerRoles.svelte`** — add permission editing:
- Grid of permission flags with on/off toggles when editing a role
- Grouped by category: General, Membership, Messages, Voice
- System roles (Owner) show permissions as read-only
- Hierarchy enforcement: can only edit permissions you yourself have

**`ServerMembers.svelte`** — multi-role assignment:
- Replace single role dropdown with role tag list (colored pills)
- Click to add roles, click X to remove
- Show all assigned roles

**New: `ChannelPermissions.svelte`** — channel override editor:
- Accessed from channel settings
- Lists all server roles with edit capability per role
- Three-state toggles: green check (Allow), gray dash (Neutral), red X (Deny)

**`ChannelSidebar.svelte`** — channel visibility:
- Filter channel list based on resolved permissions
- Hide channels where user lacks ViewChannels

## Testing Strategy

### Backend Unit Tests

`PermissionResolverService` — exhaustive coverage:
- Single role, multi-role OR combination
- Administrator and owner bypass
- Channel overrides: allow-only, deny-only, mixed, deny-wins-over-allow
- ViewChannels gate
- Hierarchy comparison with multi-role

### Backend Integration Tests

- Role CRUD with permission editing
- Multi-role assignment: add/remove roles, verify effective permissions
- Channel overrides: set overrides, verify per-channel resolution
- Enforcement: gated actions return 403 without permissions
- Migration: existing single-role data migrates correctly

### Frontend Tests

- Permission toggle components (two-state and three-state)
- Role tag list (add/remove)
- Channel visibility filtering
- Hierarchy enforcement in UI

### Manual Testing Checklist

- Create custom role with specific permissions, assign to member, verify only those actions work
- Add channel override denying SendMessages — verify member can't type in that channel but can in others
- Assign multiple roles — verify permissions combine (OR)
- Remove a role — verify real-time update via SignalR
