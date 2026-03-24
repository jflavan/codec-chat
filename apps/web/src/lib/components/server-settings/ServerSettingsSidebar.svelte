<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const categories = $derived.by(() => {
		const cats: { id: 'general' | 'channels' | 'invites' | 'webhooks' | 'emojis' | 'roles' | 'members' | 'bans' | 'audit-log'; label: string }[] = [
			{ id: 'general', label: 'General' }
		];
		if (app.canManageChannels) {
			cats.push({ id: 'channels', label: 'Channels' });
		}
		if (app.canManageInvites) {
			cats.push({ id: 'invites', label: 'Invites' });
			cats.push({ id: 'webhooks', label: 'Webhooks' });
		}
		if (app.canManageEmojis) {
			cats.push({ id: 'emojis', label: 'Emojis' });
		}
		if (app.canManageRoles) {
			cats.push({ id: 'roles', label: 'Roles' });
			cats.push({ id: 'members', label: 'Members' });
			cats.push({ id: 'bans', label: 'Bans' });
		}
		if (app.canViewAuditLog) {
			cats.push({ id: 'audit-log', label: 'Audit Log' });
		}
		return cats;
	});
</script>

<nav class="settings-sidebar" aria-label="Server settings categories">
	<ul class="category-list" role="tablist" aria-orientation="vertical">
		{#each categories as cat (cat.id)}
			<li role="presentation">
				<button
					role="tab"
					class="category-item"
					class:active={app.serverSettingsCategory === cat.id}
					aria-selected={app.serverSettingsCategory === cat.id}
					onclick={() => { app.serverSettingsCategory = cat.id; }}
				>
					<span class="category-label">{cat.label}</span>
				</button>
			</li>
		{/each}
	</ul>
</nav>

<style>
	.settings-sidebar {
		display: flex;
		flex-direction: column;
	}

	.category-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.category-item {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
		padding: 8px 16px;
		border: none;
		border-left: 3px solid transparent;
		background: none;
		color: var(--text-muted);
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		border-radius: 0 4px 4px 0;
		transition: background-color 150ms ease, color 150ms ease;
		text-align: left;
	}

	.category-item:hover {
		background: var(--bg-message-hover);
	}

	.category-item.active {
		background: var(--bg-message-hover);
		color: var(--text-header);
		border-left-color: var(--accent);
	}

	.category-label {
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	@media (max-width: 899px) {
		.category-list {
			flex-direction: row;
			gap: 0;
			overflow-x: auto;
			-webkit-overflow-scrolling: touch;
			scrollbar-width: none;
		}

		.category-list::-webkit-scrollbar {
			display: none;
		}

		.category-item {
			border-left: none;
			border-bottom: 3px solid transparent;
			border-radius: 4px 4px 0 0;
			padding: 12px 16px;
			min-height: 44px;
			justify-content: center;
			flex-shrink: 0;
		}

		.category-item.active {
			border-bottom-color: var(--accent);
			border-left-color: transparent;
		}
	}
</style>
