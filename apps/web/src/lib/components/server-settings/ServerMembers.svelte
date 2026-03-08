<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let demotingUserId = $state<string | null>(null);

	const canDemote = () =>
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
					{:else if member.role === 'Admin' && canDemote()}
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
		color: var(--bg-tertiary);
	}

	.role-btn-promote:hover {
		background: var(--accent-hover);
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
