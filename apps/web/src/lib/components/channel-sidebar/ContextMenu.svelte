<script lang="ts">
	import { tick } from 'svelte';

	let { x, y, items, onClose }: {
		x: number;
		y: number;
		items: { label: string; onClick: () => void }[];
		onClose: () => void;
	} = $props();

	let menuEl = $state<HTMLDivElement | null>(null);

	$effect(() => {
		// Focus first menu item when the menu opens
		tick().then(() => {
			const first = menuEl?.querySelector<HTMLButtonElement>('[role="menuitem"]');
			first?.focus();
		});
	});

	function handleMenuKeydown(e: KeyboardEvent) {
		const items = menuEl?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]');
		if (!items || items.length === 0) return;
		const focused = document.activeElement as HTMLButtonElement;
		const idx = Array.from(items).indexOf(focused);

		if (e.key === 'ArrowDown') {
			e.preventDefault();
			items[(idx + 1) % items.length]?.focus();
		} else if (e.key === 'ArrowUp') {
			e.preventDefault();
			items[(idx - 1 + items.length) % items.length]?.focus();
		} else if (e.key === 'Home') {
			e.preventDefault();
			items[0]?.focus();
		} else if (e.key === 'End') {
			e.preventDefault();
			items[items.length - 1]?.focus();
		} else if (e.key === 'Escape') {
			onClose();
		} else if (e.key === 'Tab') {
			// Trap focus inside the menu
			e.preventDefault();
		}
	}
</script>

<svelte:window onclick={onClose} />

<div
	class="context-menu"
	style="left: {x}px; top: {y}px;"
	role="menu"
	aria-label="Channel options"
	tabindex="-1"
	bind:this={menuEl}
	onkeydown={handleMenuKeydown}
>
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
