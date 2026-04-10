<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { searchGifs, getTrendingGifs, type GiphyGif } from '$lib/services/giphy.js';

	let {
		onSelect,
		onClose
	}: {
		onSelect: (gifUrl: string) => void;
		onClose: () => void;
	} = $props();

	let search = $state('');
	let gifs = $state<GiphyGif[]>([]);
	let isLoading = $state(false);
	let searchInput = $state<HTMLInputElement>();
	let debounceTimer: ReturnType<typeof setTimeout> | undefined;

	async function loadTrending() {
		isLoading = true;
		try {
			gifs = await getTrendingGifs(30);
		} finally {
			isLoading = false;
		}
	}

	async function performSearch(query: string) {
		isLoading = true;
		try {
			gifs = query.trim() ? await searchGifs(query, 30) : await getTrendingGifs(30);
		} finally {
			isLoading = false;
		}
	}

	function handleSearchInput() {
		clearTimeout(debounceTimer);
		debounceTimer = setTimeout(() => performSearch(search), 350);
	}

	function handleSelect(gif: GiphyGif) {
		onSelect(gif.originalUrl);
		onClose();
	}

	const isTouchDevice = typeof window !== 'undefined' && window.matchMedia('(pointer: coarse)').matches;

	onMount(() => {
		loadTrending();
		if (!isTouchDevice) searchInput?.focus();
	});

	onDestroy(() => clearTimeout(debounceTimer));
</script>

<div class="gif-picker">
	<div class="gif-search">
		<input
			bind:this={searchInput}
			bind:value={search}
			type="text"
			placeholder="Search GIFs..."
			aria-label="Search GIFs"
			oninput={handleSearchInput}
		/>
	</div>

	<div class="gif-scroll">
		{#if isLoading && gifs.length === 0}
			<div class="gif-loading" aria-live="polite">Loading GIFs…</div>
		{:else if gifs.length === 0}
			<div class="gif-empty">No GIFs found</div>
		{:else}
			<div class="gif-grid">
				{#each gifs as gif (gif.id)}
					<button
						type="button"
						class="gif-item"
						title={gif.title}
						onclick={() => handleSelect(gif)}
					>
						<img
							src={gif.previewUrl}
							alt={gif.title}
							loading="lazy"
							width={gif.previewWidth}
							height={gif.previewHeight}
						/>
					</button>
				{/each}
			</div>
		{/if}

		<a class="giphy-attribution" href="https://giphy.com" target="_blank" rel="noopener noreferrer">
			<span class="giphy-powered-by">Powered by</span>
			<span class="giphy-logo">GIPHY</span>
		</a>
	</div>
</div>

<style>
	.gif-picker {
		display: flex;
		flex-direction: column;
		height: 100%;
		min-height: 0;
	}

	.gif-search {
		flex-shrink: 0;
		padding: 8px;
		border-bottom: 1px solid var(--border);
	}

	.gif-search input {
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

	.gif-search input::placeholder {
		color: var(--text-dim);
	}

	.gif-search input:focus {
		border-color: var(--accent);
	}

	.gif-scroll {
		flex: 1;
		overflow-y: auto;
		overflow-x: hidden;
		padding: 4px;
	}

	.gif-grid {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: 4px;
	}

	.gif-item {
		position: relative;
		display: block;
		width: 100%;
		padding: 0;
		border: none;
		border-radius: 4px;
		overflow: hidden;
		cursor: pointer;
		background: var(--bg-tertiary);
		transition: opacity 0.12s;
	}

	.gif-item:hover {
		opacity: 0.8;
	}

	.gif-item img {
		display: block;
		width: 100%;
		height: auto;
		object-fit: cover;
	}

	.gif-loading,
	.gif-empty {
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 32px 16px;
		color: var(--text-muted);
		font-size: 14px;
	}

	.giphy-attribution {
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 8px;
		gap: 5px;
		text-decoration: none;
	}

	.giphy-attribution:hover .giphy-logo {
		opacity: 0.8;
	}

	.giphy-powered-by {
		font-size: 11px;
		color: var(--text-muted);
		letter-spacing: 0.02em;
	}

	.giphy-logo {
		font-size: 13px;
		font-weight: 800;
		letter-spacing: 0.04em;
		text-transform: uppercase;
		background: linear-gradient(90deg, #00ccff, #9933ff, #ff6666);
		background-clip: text;
		-webkit-background-clip: text;
		-webkit-text-fill-color: transparent;
		transition: opacity 0.12s;
	}

	@media (max-width: 768px) {
		.gif-search input {
			font-size: 16px;
			padding: 10px 12px;
			min-height: 44px;
			box-sizing: border-box;
		}

		.gif-item {
			min-height: 44px;
		}
	}
</style>
