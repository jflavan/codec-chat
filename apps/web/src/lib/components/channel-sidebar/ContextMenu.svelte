<script lang="ts">
	let { x, y, items, onClose }: {
		x: number;
		y: number;
		items: { label: string; onClick: () => void }[];
		onClose: () => void;
	} = $props();
</script>

<svelte:window onclick={onClose} onkeydown={(e) => e.key === 'Escape' && onClose()} />

<div class="context-menu" style="left: {x}px; top: {y}px;" role="menu">
	{#each items as item}
		<button
			class="context-menu-item"
			role="menuitem"
			onclick={(e) => { e.stopPropagation(); item.onClick(); onClose(); }}
		>{item.label}</button>
	{/each}
</div>

<style>
	.context-menu {
		position: fixed;
		z-index: 1000;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 6px;
		box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
		padding: 4px;
		min-width: 160px;
	}

	.context-menu-item {
		display: block;
		width: 100%;
		padding: 8px 12px;
		border: none;
		background: transparent;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		text-align: left;
		cursor: pointer;
		border-radius: 4px;
		transition: background-color 100ms ease, color 100ms ease;
	}

	.context-menu-item:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
	}
</style>
