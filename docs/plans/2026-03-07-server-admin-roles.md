# Server Admin Role Management — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow server owners/admins to promote and demote members via a new Members tab in server settings, with real-time updates and role badges in the member sidebar.

**Architecture:** New PATCH endpoint on ServersController for role changes, SignalR `MemberRoleChanged` event, new `ServerMembers.svelte` settings tab, and role badges on `MemberItem.svelte`. Follows existing patterns for kick/member management.

**Tech Stack:** ASP.NET Core 10, EF Core, SignalR, SvelteKit 5 (runes), TypeScript

---

### Task 1: API Endpoint — Request DTO

**Files:**
- Create: `apps/api/Codec.Api/Models/UpdateMemberRoleRequest.cs`

**Step 1: Create the request DTO**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class UpdateMemberRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
```

**Step 2: Build to verify it compiles**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Models/UpdateMemberRoleRequest.cs
git commit -m "feat(api): add UpdateMemberRoleRequest DTO"
```

---

### Task 2: API Endpoint — UpdateMemberRole action on ServersController

**Files:**
- Modify: `apps/api/Codec.Api/Controllers/ServersController.cs` (add new endpoint after KickMember, ~line 282)

**Step 1: Add the UpdateMemberRole endpoint**

Insert after the `KickMember` method (after line 282):

```csharp
/// <summary>
/// Changes a member's role within a server.
/// Owner can promote to Admin or demote to Member.
/// Admin can promote Members to Admin but cannot demote other Admins.
/// Nobody can change the Owner's role or their own role.
/// </summary>
[HttpPatch("{serverId:guid}/members/{targetUserId:guid}/role")]
public async Task<IActionResult> UpdateMemberRole(Guid serverId, Guid targetUserId, [FromBody] UpdateMemberRoleRequest request)
{
    if (!Enum.TryParse<ServerRole>(request.Role, ignoreCase: true, out var newRole)
        || newRole is ServerRole.Owner)
    {
        return BadRequest(new { error = "Role must be 'Admin' or 'Member'." });
    }

    var appUser = await userService.GetOrCreateUserAsync(User);
    var callerMembership = await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

    if (targetUserId == appUser.Id)
    {
        return BadRequest(new { error = "You cannot change your own role." });
    }

    var targetMembership = await db.ServerMembers
        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == targetUserId);

    if (targetMembership is null)
    {
        return NotFound(new { error = "User is not a member of this server." });
    }

    if (targetMembership.Role is ServerRole.Owner)
    {
        return BadRequest(new { error = "Cannot change the server owner's role." });
    }

    // Admins cannot demote other Admins (only Owner/GlobalAdmin can).
    if (callerMembership.Role is ServerRole.Admin
        && targetMembership.Role is ServerRole.Admin
        && newRole is ServerRole.Member)
    {
        return Forbid();
    }

    if (targetMembership.Role == newRole)
    {
        return Ok(new
        {
            targetMembership.UserId,
            Role = targetMembership.Role.ToString(),
            targetMembership.JoinedAt
        });
    }

    targetMembership.Role = newRole;
    await db.SaveChangesAsync();

    await hub.Clients.Group($"server-{serverId}").SendAsync("MemberRoleChanged", new
    {
        serverId,
        userId = targetUserId,
        newRole = newRole.ToString()
    });

    return Ok(new
    {
        targetMembership.UserId,
        Role = targetMembership.Role.ToString(),
        targetMembership.JoinedAt
    });
}
```

**Step 2: Build to verify it compiles**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add apps/api/Codec.Api/Controllers/ServersController.cs
git commit -m "feat(api): add PATCH endpoint to change member roles"
```

---

### Task 3: Frontend API Client — updateMemberRole method

**Files:**
- Modify: `apps/web/src/lib/api/client.ts` (add method after `kickMember`, ~line 506)

**Step 1: Add the API method**

Insert after `kickMember` method:

```typescript
/** Change a member's role in a server (requires Owner or Admin role). */
updateMemberRole(token: string, serverId: string, userId: string, role: string): Promise<Member> {
    return this.request<Member>(
        `${this.baseUrl}/servers/${encodeURIComponent(serverId)}/members/${encodeURIComponent(userId)}/role`,
        { method: 'PATCH', headers: this.headers(token, true), body: JSON.stringify({ role }) }
    );
}
```

**Step 2: Verify TypeScript compiles**

Run: `cd apps/web && npx tsc --noEmit`
Expected: No errors (or only pre-existing ones)

**Step 3: Commit**

```bash
git add apps/web/src/lib/api/client.ts
git commit -m "feat(web): add updateMemberRole API client method"
```

---

### Task 4: SignalR Event — MemberRoleChanged

**Files:**
- Modify: `apps/web/src/lib/services/chat-hub.ts` (add event type and callback registration)

**Step 1: Add event type**

After `MemberLeftEvent` type (~line 93), add:

```typescript
export type MemberRoleChangedEvent = {
    serverId: string;
    userId: string;
    newRole: string;
};
```

**Step 2: Add callback to SignalRCallbacks interface**

In the `SignalRCallbacks` type, after `onMemberLeft` (~line 225), add:

```typescript
onMemberRoleChanged?: (event: MemberRoleChangedEvent) => void;
```

**Step 3: Register the callback in the connect method**

After the `onMemberLeft` registration block (~line 321), add:

```typescript
if (callbacks.onMemberRoleChanged) {
    connection.on('MemberRoleChanged', callbacks.onMemberRoleChanged);
}
```

**Step 4: Verify TypeScript compiles**

Run: `cd apps/web && npx tsc --noEmit`
Expected: No errors

**Step 5: Commit**

```bash
git add apps/web/src/lib/services/chat-hub.ts
git commit -m "feat(web): add MemberRoleChanged SignalR event type and callback"
```

---

### Task 5: AppState — updateMemberRole action + SignalR handler

**Files:**
- Modify: `apps/web/src/lib/state/app-state.svelte.ts`

**Step 1: Add the updateMemberRole method**

After the `kickMember` method (~line 861), add:

```typescript
/** Change a member's role in the currently selected server. */
async updateMemberRole(userId: string, role: string): Promise<void> {
    if (!this.idToken || !this.selectedServerId) return;
    try {
        await this.api.updateMemberRole(this.idToken, this.selectedServerId, userId, role);
        await this.loadMembers(this.selectedServerId);
    } catch (e) {
        this.setError(e);
    }
}
```

**Step 2: Add the `canManageRoles` derived permission**

After the `canDeleteChannel` derived (~line 226), add:

```typescript
readonly canManageRoles = $derived(
    this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
);
```

**Step 3: Add the SignalR callback handler**

In the `connectHub` method, after the `onMemberLeft` callback (~line 2273), add:

```typescript
onMemberRoleChanged: (event) => {
    if (event.serverId === this.selectedServerId) {
        this.loadMembers(event.serverId);
    }
    // Update the caller's own role in the servers list if they were promoted/demoted
    if (event.userId === this.me?.user.id) {
        this.servers = this.servers.map((s) =>
            s.serverId === event.serverId ? { ...s, role: event.newRole } : s
        );
    }
},
```

**Step 4: Add `'members'` to the serverSettingsCategory type**

Find `serverSettingsCategory = $state<'general' | 'emojis'>('general');` (~line 126) and change to:

```typescript
serverSettingsCategory = $state<'general' | 'emojis' | 'members'>('general');
```

**Step 5: Verify TypeScript compiles**

Run: `cd apps/web && npx tsc --noEmit`
Expected: No errors

**Step 6: Commit**

```bash
git add apps/web/src/lib/state/app-state.svelte.ts
git commit -m "feat(web): add updateMemberRole state action, permission, and SignalR handler"
```

---

### Task 6: Server Settings — Members Tab in Sidebar

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte` (~line 12)

**Step 1: Add 'members' to the categories array**

In the `categories` derived, after the emojis push (~line 16), add:

```typescript
cats.push({ id: 'members', label: 'Members' });
```

And update the type to include `'members'`:

```typescript
const cats: { id: 'general' | 'emojis' | 'members'; label: string }[] = [
```

The members tab should be inside the same `if (isAdminOrOwner)` guard as emojis.

**Step 2: Verify it renders**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerSettingsSidebar.svelte
git commit -m "feat(web): add Members tab to server settings sidebar"
```

---

### Task 7: Server Settings — ServerMembers.svelte Component

**Files:**
- Create: `apps/web/src/lib/components/server-settings/ServerMembers.svelte`

**Step 1: Create the component**

```svelte
<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let demotingUserId = $state<string | null>(null);

	const canDemote = (memberRole: string) =>
		app.isGlobalAdmin || app.currentServerRole === 'Owner';

	const canPromote = (memberRole: string) =>
		memberRole === 'Member' && app.canManageRoles;

	async function promote(userId: string): Promise<void> {
		await app.updateMemberRole(userId, 'Admin');
	}

	async function demote(userId: string): Promise<void> {
		if (demotingUserId !== userId) {
			demotingUserId = userId;
			return;
		}
		await app.updateMemberRole(userId, 'Member');
		demotingUserId = null;
	}

	function cancelDemote(): void {
		demotingUserId = null;
	}
</script>

<section class="server-members-settings">
	<h2 class="section-title">Members</h2>
	<p class="section-desc">Manage member roles for this server.</p>

	<ul class="member-list" role="list">
		{#each app.members as member (member.userId)}
			<li class="member-row">
				{#if member.avatarUrl}
					<img class="member-avatar" src={member.avatarUrl} alt="" />
				{:else}
					<div class="member-avatar-placeholder" aria-hidden="true">
						{member.displayName.slice(0, 1).toUpperCase()}
					</div>
				{/if}

				<div class="member-info">
					<span class="member-name">{member.displayName}</span>
					<span class="member-role role-{member.role.toLowerCase()}">{member.role}</span>
				</div>

				<div class="member-actions">
					{#if member.role === 'Owner' || member.userId === app.me?.user.id}
						<!-- No actions for owner or self -->
					{:else if member.role === 'Admin' && canDemote(member.role)}
						{#if demotingUserId === member.userId}
							<button class="role-btn role-btn-danger" onclick={() => demote(member.userId)}>
								Are you sure?
							</button>
							<button class="role-btn role-btn-cancel" onclick={cancelDemote}>
								Cancel
							</button>
						{:else}
							<button class="role-btn role-btn-demote" onclick={() => demote(member.userId)}>
								Remove Admin
							</button>
						{/if}
					{:else if canPromote(member.role)}
						<button class="role-btn role-btn-promote" onclick={() => promote(member.userId)}>
							Make Admin
						</button>
					{/if}
				</div>
			</li>
		{/each}
	</ul>
</section>

<style>
	.server-members-settings {
		max-width: 660px;
	}

	.section-title {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 4px;
	}

	.section-desc {
		font-size: 13px;
		color: var(--text-muted);
		margin: 0 0 20px;
	}

	.member-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
	}

	.member-row {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 10px 8px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.member-row:hover {
		background: var(--bg-message-hover);
	}

	.member-avatar {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.member-avatar-placeholder {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 15px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.member-info {
		display: flex;
		flex-direction: column;
		min-width: 0;
		flex: 1;
	}

	.member-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.member-role {
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.role-owner {
		color: var(--accent);
	}

	.role-admin {
		color: #f0b232;
	}

	.role-member {
		color: var(--text-muted);
	}

	.member-actions {
		display: flex;
		gap: 6px;
		margin-left: auto;
		flex-shrink: 0;
	}

	.role-btn {
		padding: 6px 12px;
		min-height: 32px;
		font-size: 12px;
		font-weight: 600;
		border-radius: 3px;
		cursor: pointer;
		border: 1px solid transparent;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.role-btn-promote {
		background: var(--accent);
		color: #fff;
	}

	.role-btn-promote:hover {
		filter: brightness(1.1);
	}

	.role-btn-demote {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-demote:hover {
		background: var(--danger);
		color: #fff;
		border-color: var(--danger);
	}

	.role-btn-danger {
		background: var(--danger);
		color: #fff;
		border-color: var(--danger);
	}

	.role-btn-cancel {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-cancel:hover {
		color: var(--text-normal);
		border-color: var(--text-normal);
	}
</style>
```

**Step 2: Verify it compiles**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 3: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerMembers.svelte
git commit -m "feat(web): add ServerMembers settings component with promote/demote"
```

---

### Task 8: Wire ServerMembers into ServerSettingsModal

**Files:**
- Modify: `apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte` (~line 5, ~line 58)

**Step 1: Import ServerMembers**

After the `ServerEmojis` import (line 5), add:

```typescript
import ServerMembers from './ServerMembers.svelte';
```

**Step 2: Add the members tab rendering**

In the template, change the `{#if}` block (~line 58) to:

```svelte
{#if app.serverSettingsCategory === 'emojis'}
    <ServerEmojis />
{:else if app.serverSettingsCategory === 'members'}
    <ServerMembers />
{:else}
    <ServerSettings />
{/if}
```

**Step 3: Verify it compiles**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/server-settings/ServerSettingsModal.svelte
git commit -m "feat(web): wire ServerMembers tab into settings modal"
```

---

### Task 9: Role Badges in Member Sidebar

**Files:**
- Modify: `apps/web/src/lib/components/members/MemberItem.svelte`

**Step 1: Add role badge to the template**

After the `<span class="member-name">` element (line 40), add:

```svelte
{#if member.role === 'Owner' || member.role === 'Admin'}
    <span class="role-badge role-badge-{member.role.toLowerCase()}">{member.role}</span>
{/if}
```

**Step 2: Add badge styles**

In the `<style>` block, add:

```css
.role-badge {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    padding: 1px 5px;
    border-radius: 3px;
    flex-shrink: 0;
    line-height: 1.4;
}

.role-badge-owner {
    color: var(--accent);
    background: rgba(var(--accent-rgb), 0.15);
}

.role-badge-admin {
    color: #f0b232;
    background: rgba(240, 178, 50, 0.15);
}
```

**Step 3: Verify it compiles**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 4: Commit**

```bash
git add apps/web/src/lib/components/members/MemberItem.svelte
git commit -m "feat(web): add Owner/Admin role badges to member sidebar"
```

---

### Task 10: Build Verification

**Step 1: Build the API**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeded

**Step 2: Run frontend checks**

Run: `cd apps/web && npm run check`
Expected: No errors

**Step 3: Commit any remaining changes and verify clean state**

Run: `git status`
Expected: nothing to commit, working tree clean
