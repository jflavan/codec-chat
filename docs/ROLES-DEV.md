# Roles — Developer Reference

See [ROLES.md](ROLES.md) for the user-facing permission matrix.

## Data Model

**`ServerRoleEntity`** (`apps/api/Codec.Api/Models/ServerRoleEntity.cs`) — custom or system role within a server. Properties: `Id`, `ServerId`, `Name`, `Color`, `Position`, `Permissions` (bitmask), `IsSystemRole`, `IsHoisted`, `IsMentionable`.

**`Permission`** (`apps/api/Codec.Api/Models/Permission.cs`) — `[Flags] enum` with granular permission flags stored as `long`. Use `PermissionExtensions.Has()` for checks (auto-grants on `Administrator`).

**`ServerMember`** (`apps/api/Codec.Api/Models/ServerMember.cs`) — join table linking users to servers. The `RoleId` field references a `ServerRoleEntity`. Primary key is (`ServerId`, `UserId`).

**`User.IsGlobalAdmin`** — platform-wide admin flag, independent of server membership.

## Enforcement Patterns

### Backend (UserService)

Permission checks go through `IUserService`:

```csharp
// Check specific permission
await userService.EnsurePermissionAsync(serverId, appUser.Id, Permission.ManageChannels, appUser.IsGlobalAdmin);

// Check admin-equivalent (Administrator flag)
await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

// Check server owner
await userService.EnsureOwnerAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);
```

All methods:
1. Check `isGlobalAdmin` — if true, bypass all checks.
2. Load `ServerMember` with its `Role` (Include).
3. Check `role.Permissions.Has(permission)` — `Administrator` auto-grants everything.
4. Throw `ForbiddenException` on failure.

**Hierarchy enforcement:** When assigning roles, check that the caller's role position is lower (higher rank) than the target role's position. Members cannot assign roles equal to or above their own.

### Frontend (ServerStore)

`apps/web/src/lib/state/server-store.svelte.ts` uses `hasPermission()` for derived flags:

```typescript
readonly canManageChannels = $derived(
    this.isGlobalAdmin || hasPermission(this.currentServerPermissions, Permission.ManageChannels)
);
```

The `currentServerPermissions` number comes from the API's `permissions` field on the server membership response.

### Role Management (RolesController)

`apps/api/Codec.Api/Controllers/RolesController.cs` provides CRUD endpoints:

| Method | Endpoint | Permission Required |
|---|---|---|
| GET | `/servers/{id}/roles` | Membership |
| POST | `/servers/{id}/roles` | ManageRoles |
| PATCH | `/servers/{id}/roles/{roleId}` | ManageRoles |
| DELETE | `/servers/{id}/roles/{roleId}` | ManageRoles |
| PUT | `/servers/{id}/roles/reorder` | ManageRoles |

### SignalR Events

| Event | Payload | When |
|---|---|---|
| `MemberRoleChanged` | `{ serverId, userId, newRole, permissions }` | Member's role changed |
| `RoleCreated` | `{ serverId, role: { id, name, color, position, permissions, ... } }` | New role created |
| `RoleUpdated` | `{ serverId, role: { ... } }` | Role properties changed |
| `RoleDeleted` | `{ serverId, roleId, roleName }` | Role deleted |
| `RolesReordered` | `{ serverId }` | Role positions changed |

## Key Files

| Area | File |
|---|---|
| Role entity | `apps/api/Codec.Api/Models/ServerRoleEntity.cs` |
| Permission flags | `apps/api/Codec.Api/Models/Permission.cs` |
| Membership model | `apps/api/Codec.Api/Models/ServerMember.cs` |
| Permission checks | `apps/api/Codec.Api/Services/UserService.cs` |
| Role CRUD | `apps/api/Codec.Api/Controllers/RolesController.cs` |
| Member management | `apps/api/Codec.Api/Controllers/ServersController.cs` |
| Seed data | `apps/api/Codec.Api/Data/SeedData.cs` |
| Migration | `apps/api/Codec.Api/Migrations/20260324223326_CustomRolesAndPermissions.cs` |
| Frontend types | `apps/web/src/lib/types/models.ts` |
| Frontend state | `apps/web/src/lib/state/server-store.svelte.ts` |
| Role mgmt UI | `apps/web/src/lib/components/server-settings/ServerRoles.svelte` |
| Member list | `apps/web/src/lib/components/members/MembersSidebar.svelte` |

## Adding a New Permission

1. **Backend:** Add a new flag to the `Permission` enum with the next available bit position.
2. **Backend:** Add it to `AdminDefaults` and/or `MemberDefaults` in `PermissionExtensions` if it should be granted by default.
3. **Backend:** Use `EnsurePermissionAsync(serverId, userId, Permission.NewFlag, isGlobalAdmin)` in your controller.
4. **Frontend:** Add the flag value to `Permission` in `apps/web/src/lib/types/models.ts`.
5. **Frontend:** Add a `canDoThing` derived property in `ServerStore` using `hasPermission()`.
6. **Documentation:** Update `docs/ROLES.md` permission table.
