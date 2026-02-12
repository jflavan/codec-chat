<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import DmList from '$lib/components/dm/DmList.svelte';

	const app = getAppState();

	const tabs = [
		{ key: 'friends' as const, label: 'Friends' },
		{ key: 'pending' as const, label: 'Pending' },
		{ key: 'add' as const, label: 'Add Friend' }
	] as const;

	const pendingCount = $derived(app.incomingRequests.length);

	function handleFriendsClick(): void {
		app.activeDmChannelId = null;
		app.dmMessages = [];
		app.dmTypingUsers = [];
		app.dmMessageBody = '';
	}
</script>

<aside class="home-sidebar">
	<div class="sidebar-header">
		<button
			class="friends-nav"
			class:active={!app.activeDmChannelId}
			onclick={handleFriendsClick}
		>
			<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"/>
			</svg>
			<span>Friends</span>
			{#if pendingCount > 0}
				<span class="badge">{pendingCount}</span>
			{/if}
		</button>
	</div>

	<div class="dm-section">
		<DmList />
	</div>
</aside>

<style>
	.home-sidebar {
		display: flex;
		flex-direction: column;
		height: 100%;
		background: var(--bg-secondary);
		border-right: 1px solid var(--border);
		overflow: hidden;
	}

	.sidebar-header {
		padding: 12px 8px 8px;
		flex-shrink: 0;
	}

	.friends-nav {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
		padding: 8px 12px;
		border: none;
		border-radius: 4px;
		background: transparent;
		color: var(--text-muted);
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.friends-nav:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.friends-nav.active {
		background: var(--bg-message-hover);
		color: var(--text-header);
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
		margin-left: auto;
	}

	.dm-section {
		flex: 1;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}
</style>
