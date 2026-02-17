<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import FriendsList from './FriendsList.svelte';
	import PendingRequests from './PendingRequests.svelte';
	import AddFriend from './AddFriend.svelte';

	const app = getAppState();

	const tabs = [
		{ key: 'all' as const, label: 'All Friends' },
		{ key: 'pending' as const, label: 'Pending' },
		{ key: 'add' as const, label: 'Add Friend' }
	];

	const pendingCount = $derived(app.incomingRequests.length);
</script>

<div class="friends-panel">
	<div class="friends-header">
		<button class="mobile-nav-btn" onclick={() => { app.mobileNavOpen = true; }} aria-label="Open navigation">
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M3 5h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 1 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2z"/>
			</svg>
		</button>
		<h2 class="friends-title">Friends</h2>
	</div>

	<nav class="tab-bar" aria-label="Friends tabs">
		{#each tabs as tab (tab.key)}
			<button
				class="tab-button"
				class:active={app.friendsTab === tab.key}
				onclick={() => { app.friendsTab = tab.key; }}
				aria-selected={app.friendsTab === tab.key}
				role="tab"
			>
				{tab.label}
				{#if tab.key === 'pending' && pendingCount > 0}
					<span class="badge">{pendingCount}</span>
				{/if}
			</button>
		{/each}
	</nav>

	<div class="tab-content">
		{#if app.friendsTab === 'all'}
			<FriendsList />
		{:else if app.friendsTab === 'pending'}
			<PendingRequests />
		{:else}
			<AddFriend />
		{/if}
	</div>
</div>

<style>
	.friends-panel {
		display: flex;
		flex-direction: column;
		height: 100%;
		background: var(--bg-primary);
		overflow: hidden;
	}

	.friends-header {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 12px 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.friends-title {
		margin: 0;
		font-size: 16px;
		font-weight: 700;
		color: var(--text-header);
	}

	/* ───── Mobile navigation button ───── */

	.mobile-nav-btn {
		display: none;
		background: none;
		border: none;
		padding: 6px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.mobile-nav-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	@media (max-width: 899px) {
		.mobile-nav-btn {
			display: grid;
			min-width: 44px;
			min-height: 44px;
		}
	}

	.tab-bar {
		display: flex;
		gap: 4px;
		padding: 8px 16px;
		background: var(--bg-secondary);
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.tab-button {
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-family: inherit;
		font-size: 13px;
		font-weight: 600;
		padding: 6px 12px;
		border-radius: 4px;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
		display: flex;
		align-items: center;
		gap: 6px;
	}

	@media (max-width: 768px) {
		.tab-button {
			min-height: 44px;
			padding: 8px 14px;
			font-size: 14px;
		}
	}

	.tab-button:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.tab-button.active {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.badge {
		background: var(--danger);
		color: #fff;
		font-size: 11px;
		font-weight: 700;
		padding: 1px 5px;
		border-radius: 8px;
		min-width: 16px;
		text-align: center;
		line-height: 1.3;
	}

	.tab-content {
		flex: 1;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}
</style>
