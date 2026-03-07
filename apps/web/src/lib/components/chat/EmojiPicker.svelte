<script lang="ts">
	import { emojiCategories } from '$lib/data/emojis';
	import { getFrequentEmojis } from '$lib/utils/emoji-frequency';
	import type { CustomEmoji } from '$lib/types/models';

	let {
		onSelect,
		mode,
		onClose,
		customEmojis = [],
		flipped = false
	}: {
		onSelect: (emoji: string) => void;
		mode: 'reaction' | 'insert';
		onClose: () => void;
		customEmojis?: CustomEmoji[];
		flipped?: boolean;
	} = $props();

	let search = $state('');
	let scrollContainer = $state<HTMLDivElement>();
	let searchInput = $state<HTMLInputElement>();
	let containerEl = $state<HTMLDivElement>();

	/** Compute max-height based on available viewport space below the picker.
	 *  Called once at render — not reactive to scroll/resize (by design). */
	function getMaxHeight(): string {
		if (!flipped || !containerEl) return '420px';
		const rect = containerEl.getBoundingClientRect();
		const available = window.innerHeight - rect.top - 16;
		return `${Math.min(420, Math.max(200, available))}px`;
	}

	const frequentEmojis = getFrequentEmojis(16);

	type DisplayCategory = {
		id: string;
		name: string;
		icon: string;
		items: Array<{ emoji: string; name: string; isCustom: boolean; imageUrl?: string }>;
	};

	const allCategories: DisplayCategory[] = $derived.by(() => {
		const query = search.toLowerCase().trim();
		const cats: DisplayCategory[] = [];

		// Frequent category
		if (frequentEmojis.length > 0) {
			const frequentItems = frequentEmojis.map((e) => ({ emoji: e, name: e, isCustom: false }));
			const filtered = query
				? frequentItems.filter((item) => item.emoji.includes(query) || item.name.toLowerCase().includes(query))
				: frequentItems;
			if (filtered.length > 0) {
				cats.push({ id: 'frequent', name: 'Frequently Used', icon: '\u{1F553}', items: filtered });
			}
		}

		// Custom emojis category
		if (customEmojis.length > 0) {
			const customItems = customEmojis.map((e) => ({
				emoji: `:${e.name}:`,
				name: e.name,
				isCustom: true,
				imageUrl: e.imageUrl
			}));
			const filtered = query
				? customItems.filter((item) => item.name.toLowerCase().includes(query))
				: customItems;
			if (filtered.length > 0) {
				cats.push({ id: 'custom', name: 'Custom', icon: '\u{2B50}', items: filtered });
			}
		}

		// Standard emoji categories
		for (const cat of emojiCategories) {
			const items = cat.emojis.map((e) => ({ emoji: e.emoji, name: e.name, isCustom: false }));
			const filtered = query
				? items.filter(
						(item) =>
							item.name.toLowerCase().includes(query) ||
							cat.emojis
								.find((ce) => ce.emoji === item.emoji)
								?.keywords.some((kw) => kw.toLowerCase().includes(query))
					)
				: items;
			if (filtered.length > 0) {
				cats.push({ id: cat.id, name: cat.name, icon: cat.icon, items: filtered });
			}
		}

		return cats;
	});

	function handleSelect(emoji: string) {
		onSelect(emoji);
		if (mode === 'reaction') onClose();
	}

	function scrollToCategory(id: string) {
		const el = scrollContainer?.querySelector(`[data-category="${id}"]`);
		el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
	}

	$effect(() => {
		searchInput?.focus();
	});
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="picker-backdrop" onclick={onClose}></div>

<div
	bind:this={containerEl}
	class="emoji-picker-container"
	class:flipped
	style:max-height={flipped ? getMaxHeight() : undefined}
	role="dialog"
	aria-label="Emoji picker"
>
	<div class="picker-search">
		<input
			bind:this={searchInput}
			bind:value={search}
			type="text"
			placeholder="Search emojis..."
			aria-label="Search emojis"
		/>
	</div>

	<div class="category-tabs">
		{#each allCategories as cat (cat.id)}
			<button
				class="category-tab"
				title={cat.name}
				onclick={() => scrollToCategory(cat.id)}
			>
				{cat.icon}
			</button>
		{/each}
	</div>

	<div class="emoji-scroll" bind:this={scrollContainer}>
		{#each allCategories as cat (cat.id)}
			<div data-category={cat.id}>
				<div class="category-header">{cat.name}</div>
				<div class="emoji-grid">
					{#each cat.items as item (item.emoji)}
						<button
							class="emoji-btn"
							title={item.name}
							onclick={() => handleSelect(item.emoji)}
						>
							{#if item.isCustom && item.imageUrl}
								<img src={item.imageUrl} alt={item.name} width="24" height="24" />
							{:else}
								{item.emoji}
							{/if}
						</button>
					{/each}
				</div>
			</div>
		{/each}
	</div>
</div>

<style>
	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 99;
	}

	.emoji-picker-container {
		position: absolute;
		z-index: 100;
		bottom: calc(100% + 4px);
		right: 0;
		width: 352px;
		max-height: 420px;
		display: flex;
		flex-direction: column;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
		overflow: hidden;
	}

	.emoji-picker-container.flipped {
		bottom: unset;
		top: calc(100% + 4px);
	}

	.picker-search {
		padding: 8px;
		border-bottom: 1px solid var(--border);
	}

	.picker-search input {
		width: 100%;
		padding: 8px 12px;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		outline: none;
		box-sizing: border-box;
	}

	.picker-search input::placeholder {
		color: var(--text-dim);
	}

	.picker-search input:focus {
		border-color: var(--accent);
	}

	.category-tabs {
		display: flex;
		padding: 4px 8px;
		gap: 2px;
		border-bottom: 1px solid var(--border);
		overflow-x: auto;
	}

	.category-tab {
		padding: 4px 6px;
		background: none;
		border: none;
		border-radius: 4px;
		cursor: pointer;
		font-size: 18px;
		line-height: 1;
		opacity: 0.6;
		transition: opacity 0.12s, background 0.12s;
	}

	.category-tab:hover {
		opacity: 1;
		background: var(--bg-tertiary);
	}

	.emoji-scroll {
		flex: 1;
		overflow-y: auto;
		overflow-x: hidden;
		padding: 4px 8px 8px;
	}

	.category-header {
		position: sticky;
		top: 0;
		padding: 6px 4px;
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.05em;
		background: var(--bg-secondary);
		z-index: 1;
	}

	.emoji-grid {
		display: grid;
		grid-template-columns: repeat(8, 1fr);
		gap: 2px;
	}

	.emoji-btn {
		display: flex;
		align-items: center;
		justify-content: center;
		width: 100%;
		aspect-ratio: 1;
		background: none;
		border: none;
		border-radius: 4px;
		cursor: pointer;
		font-size: 22px;
		line-height: 1;
		transition: background 0.12s;
	}

	.emoji-btn:hover {
		background: var(--bg-tertiary);
	}

	.emoji-btn img {
		width: 24px;
		height: 24px;
		object-fit: contain;
	}

	@media (max-width: 768px) {
		.emoji-picker-container {
			position: fixed;
			bottom: 0;
			left: 0;
			right: 0;
			top: unset;
			width: 100%;
			max-height: 60vh;
			border-radius: 12px 12px 0 0;
			padding-bottom: env(safe-area-inset-bottom);
			animation: slide-up 200ms ease;
			z-index: 100;
		}

		.picker-backdrop {
			background: rgba(0, 0, 0, 0.5);
		}
	}

	@keyframes slide-up {
		from {
			transform: translateY(100%);
		}
		to {
			transform: translateY(0);
		}
	}
</style>
