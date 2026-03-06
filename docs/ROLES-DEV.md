# Roles — Developer Reference

See [ROLES.md](ROLES.md) for the user-facing permission matrix.

## Data Model

**`ServerRole` enum** (`apps/api/Codec.Api/Models/ServerRole.cs`):

| Value | Name |
|---|---|
| 0 | Owner |
| 1 | Admin |
| 2 | Member |

**`ServerMember`** (`apps/api/Codec.Api/Models/ServerMember.cs`) — join table linking users to servers. The `Role` field defaults to `ServerRole.Member`. Primary key is (`ServerId`, `UserId`).

**`User.IsGlobalAdmin`** (`apps/api/Codec.Api/Models/User.cs:28`) — platform-wide admin flag, independent of server membership. Set at startup for the user matching `GlobalAdmin:Email` in config.

## Enforcement Patterns

### Backend (ServersController)

All role-gated endpoints follow this pattern in `apps/api/Codec.Api/Controllers/ServersController.cs`:

1. Check `appUser.IsGlobalAdmin` — if true, skip membership check.
2. Look up `ServerMember` for the current user and server.
3. Return `Forbid()` if no membership or insufficient role.

```csharp
if (!appUser.IsGlobalAdmin)
{
    var membership = await db.ServerMembers
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

    if (membership is null)
        return Forbid();

    if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        return Forbid();
}
```

**Admin kick restriction:** Admins cannot kick other Admins. The controller checks `callerRole is ServerRole.Admin && targetMembership.Role is ServerRole.Admin` and returns `Forbid()`.

### Frontend (AppState)

`apps/web/src/lib/state/app-state.svelte.ts` (lines 196-220) exposes derived permission flags:

| Property | Allowed Roles |
|---|---|
| `canManageChannels` | Owner, Admin, Global Admin |
| `canKickMembers` | Owner, Admin, Global Admin |
| `canManageInvites` | Owner, Admin, Global Admin |
| `canDeleteServer` | Owner, Global Admin |
| `canDeleteChannel` | Owner, Admin, Global Admin |

Components use these flags to show/hide UI elements (e.g., kick buttons in `MemberItem.svelte`, channel management controls).

### SignalR (ChatHub)

`apps/api/Codec.Api/Hubs/ChatHub.cs` — on connect, Global Admins are automatically added to all server groups so they receive real-time events for every server.

## Key Files

| Area | File |
|---|---|
| Role enum | `apps/api/Codec.Api/Models/ServerRole.cs` |
| Membership model | `apps/api/Codec.Api/Models/ServerMember.cs` |
| Global admin flag | `apps/api/Codec.Api/Models/User.cs` |
| Permission checks | `apps/api/Codec.Api/Controllers/ServersController.cs` |
| Real-time groups | `apps/api/Codec.Api/Hubs/ChatHub.cs` |
| Seed data | `apps/api/Codec.Api/Data/SeedData.cs` |
| Frontend state | `apps/web/src/lib/state/app-state.svelte.ts` |
| Member list UI | `apps/web/src/lib/components/server/MembersSidebar.svelte` |
| Member item UI | `apps/web/src/lib/components/server/MemberItem.svelte` |

## Adding a New Role-Gated Feature

1. **Backend:** Add a permission check in the relevant controller following the pattern above. Check `IsGlobalAdmin` first, then membership role.
2. **Frontend state:** Add a `readonly canDoThing = $derived(...)` property in `AppState`.
3. **Frontend UI:** Gate the UI element with the new derived property.
4. **Documentation:** Update the permission matrix in `docs/ROLES.md`.
