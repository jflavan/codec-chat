<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { hasPermission, Permission } from '$lib/types/index.js';

	const auth = getAuthStore();
	const servers = getServerStore();

	let kickingUserId = $state<string | null>(null);
	let changingRoleUserId = $state<string | null>(null);
	let banningUserId = $state<string | null>(null);
	let banReason = $state('');
	let banDeleteMessages = $state(false);

	const isOwner = $derived(servers.isServerOwner);

	const canKick = (member: { userId: string; highestPosition: number }) =>
		servers.canKickMembers &&
		member.userId !== auth.me?.user.id &&
		!isMemberOwner(member.userId) &&
		(isOwner || member.highestPosition > (servers.members.find(m => m.userId === auth.me?.user.id)?.highestPosition ?? 999));

	const canBan = (member: { userId: string; highestPosition: number }) =>
		servers.canBanMembers &&
		member.userId !== auth.me?.user.id &&
		!isMemberOwner(member.userId) &&
		(isOwner || member.highestPosition > (servers.members.find(m => m.userId === auth.me?.user.id)?.highestPosition ?? 999));

	const canChangeRole = (member: { userId: string }) =>
		servers.canManageRoles &&
		member.userId !== auth.me?.user.id &&
		!isMemberOwner(member.userId);

	function isMemberOwner(userId: string): boolean {
		return servers.members.find(m => m.userId === userId)?.roles.some(r => r.isSystemRole && r.name === 'Owner') ?? false;
	}

	/** Roles that the current user can assign (only roles below their own position). */
	const assignableRoles = $derived(() => {
		const myPosition = servers.members.find(m => m.userId === auth.me?.user.id)?.highestPosition ?? 999;
		return servers.serverRoles
			.filter(r => !r.isSystemRole || r.name !== 'Owner')
			.filter(r => isOwner || r.position > myPosition);
	});

	$effect(() => {
		if (servers.selectedServerId && servers.canManageRoles) {
			servers.loadRoles();
		}
	});

	async function addRole(userId: string, roleId: string): Promise<void> {
		await servers.addMemberRole(userId, roleId);
		changingRoleUserId = null;
	}

	async function kick(userId: string): Promise<void> {
		if (kickingUserId !== userId) {
			kickingUserId = userId;
			return;
		}
		await servers.kickMember(userId);
		kickingUserId = null;
	}

	function cancelKick(): void {
		kickingUserId = null;
	}

	async function ban(userId: string): Promise<void> {
		kickingUserId = null;
		changingRoleUserId = null;
		if (banningUserId !== userId) {
			banningUserId = userId;
			banReason = '';
			banDeleteMessages = false;
			return;
		}
		await servers.banMember(userId, { reason: banReason || undefined, deleteMessages: banDeleteMessages });
		banningUserId = null;
		banReason = '';
		banDeleteMessages = false;
	}

	function cancelBan(): void {
		banningUserId = null;
		banReason = '';
		banDeleteMessages = false;
	}
</script>

{#snippet banButton(member: { userId: string; displayName: string; highestPosition: number })}
	{#if canBan(member)}
		{#if banningUserId === member.userId}
			<div class="ban-confirm">
				<input
					class="ban-reason-input"
					type="text"
					placeholder="Reason (optional)"
					aria-label="Ban reason (optional)"
					bind:value={banReason}
					maxlength="512"
				/>
				<label class="ban-delete-label">
					<input type="checkbox" bind:checked={banDeleteMessages} />
					Delete messages
				</label>
				<div class="ban-confirm-actions">
					<button class="role-btn role-btn-danger" onclick={() => ban(member.userId)}>
						Confirm Ban
					</button>
					<button class="role-btn role-btn-cancel" onclick={cancelBan}>
						Cancel
					</button>
				</div>
			</div>
		{:else}
			<button class="role-btn role-btn-ban" onclick={() => ban(member.userId)} aria-label="Ban {member.displayName}">
				Ban
			</button>
		{/if}
	{/if}
{/snippet}

<section class="server-members-settings">
	<h2 class="section-title">Members</h2>
	<p class="section-desc">Manage member roles for this server.</p>

	<ul class="member-list" role="list">
		{#each servers.members as member (member.userId)}
			<li class="member-row">
				{#if member.avatarUrl}
					<img class="member-avatar" src={member.avatarUrl} alt="{member.displayName}'s avatar" />
				{:else}
					<div class="member-avatar-placeholder" aria-hidden="true">
						{member.displayName.slice(0, 1).toUpperCase()}
					</div>
				{/if}

				<div class="member-info">
					<span class="member-name">{member.displayName}</span>
					<span
						class="member-role"
						style:color={member.displayRole?.color ?? 'var(--text-muted)'}
					>
						{member.displayRole?.name ?? 'Member'}
					</span>
				</div>

				<div class="member-actions">
					{#if isMemberOwner(member.userId) || member.userId === auth.me?.user.id}
						<!-- No actions for owner or self -->
					{:else}
						{#if canChangeRole(member)}
							{#if changingRoleUserId === member.userId}
								<select
									class="role-select"
									aria-label="Select role to assign to {member.displayName}"
									onchange={(e) => addRole(member.userId, (e.target as HTMLSelectElement).value)}
								>
									<option value="" disabled selected>Add role…</option>
									{#each assignableRoles() as role (role.id)}
										<option value={role.id}>{role.name}</option>
									{/each}
								</select>
								<button class="role-btn role-btn-cancel" onclick={() => (changingRoleUserId = null)}>
									Cancel
								</button>
							{:else}
								<button class="role-btn role-btn-promote" onclick={() => (changingRoleUserId = member.userId)}>
									Change Role
								</button>
							{/if}
						{/if}

						{#if canKick(member)}
							{#if kickingUserId === member.userId}
								<button class="role-btn role-btn-danger" onclick={() => kick(member.userId)}>
									Are you sure?
								</button>
								<button class="role-btn role-btn-cancel" onclick={cancelKick}>
									Cancel
								</button>
							{:else}
								<button class="role-btn role-btn-kick" onclick={() => kick(member.userId)} aria-label="Kick {member.displayName}">
									Kick
								</button>
							{/if}
						{/if}

						{@render banButton(member)}
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

	.member-actions {
		display: flex;
		gap: 6px;
		margin-left: auto;
		flex-shrink: 0;
	}

	.role-select {
		padding: 4px 8px;
		font-size: 12px;
		border-radius: 3px;
		border: 1px solid var(--border);
		background: var(--bg-tertiary);
		color: var(--text-normal);
		cursor: pointer;
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
		color: var(--bg-tertiary);
	}

	.role-btn-promote:hover {
		background: var(--accent-hover);
	}

	.role-btn-kick {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-kick:hover {
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

	.role-btn-ban {
		background: transparent;
		color: var(--danger);
		border-color: var(--danger);
	}

	.role-btn-ban:hover {
		background: var(--danger);
		color: #fff;
	}

	.ban-confirm {
		display: flex;
		flex-direction: column;
		gap: 6px;
		padding: 8px;
		background: var(--bg-tertiary);
		border-radius: 4px;
		min-width: 200px;
	}

	.ban-reason-input {
		padding: 6px 8px;
		border: 1px solid var(--text-muted);
		border-radius: 3px;
		background: var(--bg-primary);
		color: var(--text-normal);
		font-size: 12px;
	}

	.ban-reason-input::placeholder {
		color: var(--text-muted);
	}

	.ban-delete-label {
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 12px;
		color: var(--text-muted);
		cursor: pointer;
	}

	.ban-confirm-actions {
		display: flex;
		gap: 6px;
	}
</style>
