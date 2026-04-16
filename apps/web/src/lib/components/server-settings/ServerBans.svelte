<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';

	const servers = getServerStore();

	let unbanningUserId = $state<string | null>(null);

	$effect(() => {
		if (servers.selectedServerId) {
			servers.loadBans();
		}
	});

	async function unban(userId: string): Promise<void> {
		if (unbanningUserId !== userId) {
			unbanningUserId = userId;
			return;
		}
		await servers.unbanMember(userId);
		unbanningUserId = null;
	}

	function cancelUnban(): void {
		unbanningUserId = null;
	}

	function formatDate(isoDate: string): string {
		return new Date(isoDate).toLocaleDateString(undefined, {
			year: 'numeric',
			month: 'short',
			day: 'numeric'
		});
	}
</script>

<section class="server-bans-settings">
	<h2 class="section-title">Bans</h2>
	<p class="section-desc">View and manage banned users for this server.</p>

	{#if servers.isLoadingBans}
		<p class="loading-text">Loading bans…</p>
	{:else if servers.bans.length === 0}
		<p class="empty-text">No banned users.</p>
	{:else}
		<ul class="ban-list" role="list">
			{#each servers.bans as ban (ban.userId)}
				<li class="ban-row">
					{#if ban.avatarUrl}
						<img class="ban-avatar" src={ban.avatarUrl} alt="{ban.displayName}'s avatar" />
					{:else}
						<div class="ban-avatar-placeholder" aria-hidden="true">
							{ban.displayName.slice(0, 1).toUpperCase()}
						</div>
					{/if}

					<div class="ban-info">
						<span class="ban-name">{ban.displayName}</span>
						<span class="ban-meta">
							Banned {formatDate(ban.bannedAt)}
							{#if ban.reason}
								— {ban.reason}
							{/if}
						</span>
					</div>

					<div class="ban-actions">
						{#if unbanningUserId === ban.userId}
							<span class="visually-hidden" role="alert">Confirm unban of {ban.displayName}</span>
							<button class="role-btn role-btn-danger" onclick={() => unban(ban.userId)}>
								Are you sure?
							</button>
							<button class="role-btn role-btn-cancel" onclick={cancelUnban}>
								Cancel
							</button>
						{:else}
							<button class="role-btn role-btn-unban" onclick={() => unban(ban.userId)} aria-label="Unban {ban.displayName}">
								Unban
							</button>
						{/if}
					</div>
				</li>
			{/each}
		</ul>
	{/if}
</section>

<style>
	.server-bans-settings {
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

	.loading-text,
	.empty-text {
		font-size: 14px;
		color: var(--text-muted);
	}

	.ban-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
	}

	.ban-row {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 10px 8px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.ban-row:hover {
		background: var(--bg-message-hover);
	}

	.ban-avatar {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.ban-avatar-placeholder {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		background: var(--danger);
		color: #fff;
		font-weight: 700;
		font-size: 15px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.ban-info {
		display: flex;
		flex-direction: column;
		min-width: 0;
		flex: 1;
	}

	.ban-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.ban-meta {
		font-size: 12px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.ban-actions {
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

	.role-btn-unban {
		background: transparent;
		color: var(--text-muted);
		border-color: var(--text-muted);
	}

	.role-btn-unban:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
		border-color: var(--accent);
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
