<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const categories = [
		{ id: 'profile' as const, label: 'My Profile', icon: '👤' },
		{ id: 'account' as const, label: 'My Account', icon: '🔒' }
	];
</script>

<nav class="settings-sidebar" aria-label="Settings categories">
	<ul class="category-list" role="tablist" aria-orientation="vertical">
		{#each categories as cat (cat.id)}
			<li role="presentation">
				<button
					role="tab"
					class="category-item"
					class:active={app.settingsCategory === cat.id}
					aria-selected={app.settingsCategory === cat.id}
					onclick={() => { app.settingsCategory = cat.id; }}
				>
					<span class="category-icon" aria-hidden="true">{cat.icon}</span>
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

	.category-icon {
		font-size: 16px;
		flex-shrink: 0;
	}

	.category-label {
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	/* Responsive: horizontal tabs on small screens */
	@media (max-width: 899px) {
		.category-list {
			flex-direction: row;
			gap: 0;
		}

		.category-item {
			border-left: none;
			border-bottom: 3px solid transparent;
			border-radius: 4px 4px 0 0;
			padding: 12px 16px;
			min-height: 44px;
			justify-content: center;
		}

		.category-item.active {
			border-bottom-color: var(--accent);
			border-left-color: transparent;
		}
	}
</style>
