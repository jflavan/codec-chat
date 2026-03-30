# Roles and Permissions — Discord-Style Design

## Overview

Full Discord-style roles and permissions system: multi-role assignment per member, granular permission editing on roles, and per-channel permission overrides with three-state (Allow / Neutral / Deny) control.

## Current State

The codebase has a `ServerRoleEntity` model with a `Permission` flags enum (24 permissions), a `RolesController` with CRUD endpoints, and hierarchy enforcement. However:

- Each member has exactly one role (`ServerMember.RoleId`)
- Permission editing is not exposed in the UI
- No per-channel permission overrides exist
- Enforcement is inconsistent — some actions check server-level permissions, others check role names or use `EnsureAdminAsync`

## Design Decisions

- **Server-level role permissions**: two-state (granted / not granted)
- **Channel-level overrides**: three-state (Allow / Neutral / Deny) — deny always wins
- **Multi-role**: members can hold multiple roles; permissions are OR'd across all roles
- **"Member" role**: stays as the default role assigned on join; it is a regular role in the multi-role list and **can be removed** from a member (they would lose those base permissions). This differs from Discord's `@everyone` which is implicit.
- **Permission resolution**: happens on the backend; the API returns effective permissions to the frontend

## Data Model

### New Tables

#### `ServerMemberRoles` (join table)

Replaces `ServerMember.RoleId`.

| Column | Type | Notes |
|--------|------|-------|
| `UserId` | Guid | Composite PK, FK to User |
| `RoleId` | Guid | Composite PK, FK to ServerRoleEntity |
| `AssignedAt` | DateTimeOffset | When the role was granted |

`(UserId, RoleId)` is sufficient for uniqueness since each role already belongs to a server.

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

- **`ServerMember`**: drop the `RoleId` column and `Role` navigation property. Role relationships move entirely to `ServerMemberRoles`. Add a `Roles` collection navigation property instead.
- **`ServerRoleEntity`**: update the `Members` navigation property from `List<ServerMember>` to `List<ServerMemberRole>` (the join entity). No schema changes to the table itself.

### Migration

1. Create `ServerMemberRoles` table
2. Create `ChannelPermissionOverrides` table
3. For each `ServerMember` with a `RoleId`, insert a row into `ServerMemberRoles`
4. Drop `RoleId` column from `ServerMember`
5. Remove the legacy `ServerRole` enum (`Models/ServerRole.cs`)

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

Dedicated service for computing effective permissions. Registered as scoped (caches resolved roles and permissions per HTTP request to avoid repeated DB hits).

```
PermissionResolverService:
    ResolveServerPermissions(serverId, userId) → Permission
    ResolveChannelPermissions(channelId, userId) → Permission
    HasPermission(channelId, userId, Permission) → bool
    HasServerPermission(serverId, userId, Permission) → bool
    GetHighestRolePosition(serverId, userId) → int
```

**Cache strategy**: on first call per request, load the member's role IDs + role entities in one query. Cache in a `Dictionary<(Guid serverId, Guid userId), List<ServerRoleEntity>>`. Channel overrides are loaded and cached per channel on first access.

### Replacing `UserService` Authorization Methods

The existing `UserService` authorization methods must be replaced:

| Current method | Replacement |
|---------------|-------------|
| `EnsureMemberAsync(serverId, userId)` | Keep as-is (membership check, no role needed) but remove `.Include(m => m.Role)` — load roles via `PermissionResolverService` instead |
| `EnsurePermissionAsync(serverId, userId, perm)` | `PermissionResolverService.HasServerPermission(serverId, userId, perm)` — aggregates across all roles |
| `EnsureAdminAsync(serverId, userId)` | `PermissionResolverService.HasServerPermission(serverId, userId, Permission.ManageServer)` |
| `EnsureOwnerAsync(serverId, userId)` | Keep as-is (checks `Server.OwnerId`, not role-based) |
| `GetPermissionsAsync(serverId, userId)` | `PermissionResolverService.ResolveServerPermissions(serverId, userId)` |
| `IsOwnerAsync(serverId, userId)` | Keep as-is |

Every call site using `EnsureAdminAsync` (27+ occurrences across controllers) must be migrated to use `PermissionResolverService.HasServerPermission` with the specific permission being checked (e.g., `ManageServer`, `ManageChannels`, `ManageEmojis`). This is an opportunity to tighten enforcement — many actions currently gated behind "admin" should check their specific permission flag.

### Replacing String-Based Role Checks

The codebase uses hardcoded string comparisons against role names. These must be replaced with permission-based checks:

| Current check | Location | Replacement |
|--------------|----------|-------------|
| `currentServerRole === 'Owner'` | `app-state.svelte.ts`, `ServerMembers.svelte` | Check `Server.OwnerId === currentUser.id` (ownership is not a role) |
| `currentServerRole === 'Admin'` | `app-state.svelte.ts` | `hasPermission(permissions, Permission.ManageServer)` |
| `member.role !== 'Owner'` | `ServerMembers.svelte` | `member.userId !== server.ownerId` |
| `member.role === 'Owner'` | `ServerMembers.svelte` | `member.userId === server.ownerId` |

The `currentServerRole` derived state (a single string) is removed. Replace with `currentServerPermissions` (already exists) and `isServerOwner` (new derived boolean).

### Replacing `ServerMember.Role` Navigation Queries

Every `.Include(m => m.Role)` query (30+ occurrences) must be updated:

**Pattern**: Replace `member.Role.Permissions` with an aggregation across the member's roles:
```csharp
// Before (single role)
var member = await db.ServerMembers
    .Include(m => m.Role)
    .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
var perms = member.Role.Permissions;

// After (multi-role)
var member = await db.ServerMembers
    .Include(m => m.MemberRoles).ThenInclude(mr => mr.Role)
    .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == userId);
var perms = member.MemberRoles.Aggregate(Permission.None, (acc, mr) => acc | mr.Role.Permissions);
```

However, most controller code should not do this directly — it should call `PermissionResolverService` instead. The Include pattern is only needed where full role data is returned in API responses (e.g., `GetMembers`).

### API Endpoints

#### Updated: Role management (`RolesController`)

- `PATCH /servers/{serverId}/roles/{roleId}` — now includes permission editing (bitmask update)

#### New: Channel overrides (on `ChannelsController`)

These go on the existing `ChannelsController` which already handles `/channels/{channelId}/...` routes:

- `GET /channels/{channelId}/overrides` — list all permission overrides for a channel
- `PUT /channels/{channelId}/overrides/{roleId}` — set Allow/Deny bitmask for a role on a channel
- `DELETE /channels/{channelId}/overrides/{roleId}` — remove a role's override from a channel

#### Updated: Member role management (`ServersController`)

Replace `PATCH /servers/{serverId}/members/{userId}/role` with:

- `PUT /servers/{serverId}/members/{userId}/roles` — set the full list of roles for a member
- `POST /servers/{serverId}/members/{userId}/roles/{roleId}` — add a single role
- `DELETE /servers/{serverId}/members/{userId}/roles/{roleId}` — remove a single role

#### Updated: Role deletion (`RolesController`)

`DELETE /servers/{serverId}/roles/{roleId}` — instead of reassigning members to the Member role via `member.RoleId`, simply remove the deleted role's entries from `ServerMemberRoles`. Members who lose their only role should have the "Member" role auto-assigned.

#### Hierarchy enforcement (multi-role)

"Can user A act on user B" compares the highest role position (lowest number) of each user. User A can only act on user B if A's highest role is above (lower position number than) B's highest role. Use `PermissionResolverService.GetHighestRolePosition()`.

### API Response Shape Changes

**`GET /servers` (GetMyServers)**: Currently returns `role: string` and `permissions: number` per server. Change to:
- `roles: MemberRole[]` — all assigned roles
- `permissions: number` — aggregated server-level permissions
- `isOwner: boolean` — whether the user owns this server

**`GET /servers/{serverId}/members` (GetMembers)**: Currently returns `role`, `rolePosition`, `roleColor`, `roleIsHoisted` as single values. Change to:
- `roles: MemberRole[]` — all assigned roles
- `permissions: number` — aggregated server-level permissions
- `displayRole: MemberRole | null` — highest role with a color (for display name coloring)
- `highestPosition: number` — for hierarchy comparison in the UI

### SignalR Events

New/updated events broadcast to `server-{serverId}`:
- `MemberRolesUpdated` — when a member's role list changes (replaces `MemberRoleChanged`)
- `ChannelOverrideUpdated` — when a channel permission override is set or removed

Update `WebhookEventType` enum (`Models/Webhook.cs`) and the frontend `WebhookEventType` (`models.ts`) to add `MemberRolesUpdated`. Keep `MemberRoleChanged` as a deprecated alias for backward compatibility with existing webhook consumers.

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
| Kick member | `KickMembers` (server) |
| Ban member | `BanMembers` (server) |

SignalR hub (`ChatHub`) must check channel-resolved permissions for:
- Joining a channel group (`ViewChannels`)
- Sending messages (`SendMessages`)
- Voice actions (`Connect`, `Speak`)

Channel visibility: `GET /servers/{serverId}/channels` filters out channels where the user lacks `ViewChannels`. SignalR broadcasting skips users who can't see a channel.

### Frontend Permission Constants

`Speak` (1 << 31), `MuteMembers` (1 << 32), and `DeafenMembers` (1 << 33) exceed the 32-bit bitwise range in JavaScript. These must use `2 ** N` (float) like `Administrator` already does:

```typescript
Speak: 2 ** 31,
MuteMembers: 2 ** 32,
DeafenMembers: 2 ** 33,
```

## Frontend Changes

### Types

Update `Member`:
```typescript
type Member = {
    // ...existing fields (userId, displayName, email, avatarUrl, joinedAt, etc.)
    roles: MemberRole[];
    permissions: number;
    displayRole?: MemberRole;  // highest role with color, for name coloring
    highestPosition: number;   // for hierarchy checks
};

type MemberRole = {
    id: string;
    name: string;
    color?: string | null;
    position: number;
    isSystemRole: boolean;
};
```

Update `MemberServer`:
```typescript
type MemberServer = {
    // ...existing fields
    roles: MemberRole[];       // replaces role: string | null
    permissions: number;
    isOwner: boolean;
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

- Remove `currentServerRole` (single string). Replace with:
  - `isServerOwner` — derived from `Server.OwnerId === me.user.id`
  - `currentServerPermissions` — already exists, now computed from aggregated roles
- New `channelOverrides` state loaded on demand in channel settings
- Existing permission helpers (`canManageChannels`, etc.) continue working — they read from `permissions`
- `canDeleteServer` changes from `currentServerRole === 'Owner'` to `isServerOwner`

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
- Hierarchy checks use `highestPosition` instead of single `rolePosition`

**New: `ChannelPermissions.svelte`** — channel override editor:
- Accessed from channel settings
- Lists all server roles with edit capability per role
- Three-state toggles: green check (Allow), gray dash (Neutral), red X (Deny)

**`ChannelSidebar.svelte`** — channel visibility:
- Filter channel list based on resolved permissions
- Hide channels where user lacks ViewChannels

**`ServerWebhooks.svelte`** — update `WebhookEventType` options to include `MemberRolesUpdated`.

**`ServerAuditLog.svelte`** — update audit action labels for role changes.

### Files Requiring Changes

**Backend:**
- `Models/ServerRole.cs` — delete (legacy enum)
- `Models/ServerMember.cs` — drop `RoleId`, `Role` nav; add `MemberRoles` collection
- `Models/ServerRoleEntity.cs` — update `Members` nav to `List<ServerMemberRole>`
- `Models/ServerMemberRole.cs` — new join entity
- `Models/ChannelPermissionOverride.cs` — new entity
- `Models/Permission.cs` — no changes
- `Models/Webhook.cs` — add `MemberRolesUpdated` event type
- `Models/UpdateMemberRoleRequest.cs` — replace with `UpdateMemberRolesRequest` (list of role IDs)
- `Data/CodecDbContext.cs` — new DbSets, relationship config, remove old `ServerMember.Role` config
- `Services/UserService.cs` — rewrite `EnsureMemberAsync` (remove Role include), deprecate `EnsurePermissionAsync`/`EnsureAdminAsync` in favor of `PermissionResolverService`
- `Services/PermissionResolverService.cs` — new service
- `Controllers/RolesController.cs` — update permission editing, update delete to use join table
- `Controllers/ServersController.cs` — new multi-role endpoints, update all `EnsureAdminAsync` calls, update response shapes
- `Controllers/ChannelsController.cs` — new override endpoints, channel visibility filtering
- `Controllers/MessagesController.cs` — use channel-resolved permissions
- `Hubs/ChatHub.cs` — channel-resolved permission checks, new events
- `Program.cs` — register `PermissionResolverService`
- New migration file

**Frontend:**
- `types/models.ts` — update Member, MemberServer, add MemberRole, ChannelPermissionOverride, fix Speak/MuteMembers/DeafenMembers constants
- `api/client.ts` — new endpoints for multi-role, channel overrides
- `state/app-state.svelte.ts` — remove `currentServerRole`, add `isServerOwner`, update role methods
- `services/chat-hub.ts` — update `MemberRoleChanged` → `MemberRolesUpdated` event
- `components/server-settings/ServerRoles.svelte` — permission editing UI
- `components/server-settings/ServerMembers.svelte` — multi-role tag list, hierarchy via `highestPosition`
- `components/server-settings/ServerWebhooks.svelte` — updated event types
- `components/server-settings/ServerAuditLog.svelte` — updated action labels
- `components/channel-sidebar/ChannelSidebar.svelte` — channel visibility filtering
- New: `components/channel-settings/ChannelPermissions.svelte`

## Testing Strategy

### Backend Unit Tests

`PermissionResolverService` — exhaustive coverage:
- Single role, multi-role OR combination
- Administrator and owner bypass
- Channel overrides: allow-only, deny-only, mixed, deny-wins-over-allow
- ViewChannels gate
- Hierarchy comparison with multi-role (highest position wins)

### Backend Integration Tests

- Role CRUD with permission editing
- Multi-role assignment: add/remove roles, verify effective permissions change
- Channel overrides: set overrides, verify per-channel resolution
- Enforcement: gated actions return 403 without permissions
- Migration: existing single-role data migrates correctly to join table
- Kick/ban enforcement uses `KickMembers`/`BanMembers` permissions specifically

### Frontend Tests

- Permission toggle components (two-state and three-state)
- Role tag list (add/remove)
- Channel visibility filtering
- Hierarchy enforcement in UI using `highestPosition`

### Manual Testing Checklist

- Create custom role with specific permissions, assign to member, verify only those actions work
- Add channel override denying SendMessages — verify member can't type in that channel but can in others
- Assign multiple roles — verify permissions combine (OR)
- Remove a role — verify real-time update via SignalR
- Delete a role — verify members who lose their only role get "Member" auto-assigned
- Verify server owner can do everything regardless of assigned roles
