<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import SearchResultItem from './SearchResultItem.svelte';
	import SearchFilterBar from './SearchFilterBar.svelte';

	let { isDm = false }: { isDm?: boolean } = $props();

	const app = getAppState();

	let inputValue = $state('');
	let debounceTimer: ReturnType<typeof setTimeout> | null = null;

	$effect(() => {
		return () => { if (debounceTimer) clearTimeout(debounceTimer); };
	});

	function handleInput(e: Event): void {
		inputValue = (e.target as HTMLInputElement).value;

		if (debounceTimer) clearTimeout(debounceTimer);

		if (inputValue.trim().length < 2) {
			app.searchQuery = inputValue;
			return;
		}

		debounceTimer = setTimeout(() => {
			app.searchMessages(inputValue, app.searchFilters);
		}, 300);
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			e.preventDefault();
			e.stopPropagation();
			app.toggleSearch();
		}
	}

	function handleJump(messageId: string, channelId: string, isDm: boolean): void {
		app.jumpToMessage(messageId, channelId, isDm);
	}

	const totalPages = $derived(
		app.searchResults
			? Math.max(1, Math.ceil(app.searchResults.totalCount / app.searchResults.pageSize))
			: 1
	);

	const currentPage = $derived(app.searchResults?.page ?? 1);

	const hasSearched = $derived(app.searchQuery.trim().length >= 2 && !app.isSearching);
	const noResults = $derived(hasSearched && app.searchResults !== null && app.searchResults.results.length === 0);
</script>

<aside class="search-panel" aria-label="Search messages">
	<header class="search-header">
		<h2 class="search-title">Search</h2>
		<button
			class="search-close"
			onclick={() => app.toggleSearch()}
			type="button"
			aria-label="Close search"
		>
			<svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
				<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
			</svg>
		</button>
	</header>

	<div class="search-input-wrapper">
		<svg class="search-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
			<circle cx="11" cy="11" r="8"/>
			<path d="m21 21-4.3-4.3"/>
		</svg>
		<input
			class="search-input"
			type="text"
			placeholder="Search messages..."
			value={inputValue}
			oninput={handleInput}
			onkeydown={handleKeydown}
		/>
	</div>

	<SearchFilterBar {isDm} />

	<div class="search-results">
		{#if app.isSearching}
			<p class="search-status">Searching...</p>
		{:else if noResults}
			<p class="search-status">No results found.</p>
		{:else if app.searchResults && app.searchResults.results.length > 0}
			<div class="results-list">
				{#each app.searchResults.results as result (result.id)}
					<SearchResultItem {result} query={app.searchQuery} onJump={handleJump} />
				{/each}
			</div>

			{#if totalPages > 1}
				<div class="search-pagination">
					<button
						class="page-btn"
						disabled={currentPage <= 1}
						onclick={() => app.searchPage(currentPage - 1)}
						type="button"
						aria-label="Previous page"
					>
						<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
							<path d="M10.354 3.646a.5.5 0 0 1 0 .708L6.707 8l3.647 3.646a.5.5 0 0 1-.708.708l-4-4a.5.5 0 0 1 0-.708l4-4a.5.5 0 0 1 .708 0z"/>
						</svg>
					</button>
					<span class="page-info">Page {currentPage} of {totalPages}</span>
					<button
						class="page-btn"
						disabled={currentPage >= totalPages}
						onclick={() => app.searchPage(currentPage + 1)}
						type="button"
						aria-label="Next page"
					>
						<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
							<path d="M5.646 3.646a.5.5 0 0 1 .708 0l4 4a.5.5 0 0 1 0 .708l-4 4a.5.5 0 0 1-.708-.708L9.293 8 5.646 4.354a.5.5 0 0 1 0-.708z"/>
						</svg>
					</button>
				</div>
			{/if}
		{:else if app.searchQuery.trim().length < 2 && app.searchQuery.length > 0}
			<p class="search-status">Type at least 2 characters to search.</p>
		{/if}
	</div>
</aside>

<style>
	.search-panel {
		width: 340px;
		flex-shrink: 0;
		display: flex;
		flex-direction: column;
		background: var(--bg-secondary);
		border-left: 1px solid var(--border);
		height: 100%;
		overflow: hidden;
	}

	.search-header {
		height: 48px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0 12px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.search-title {
		margin: 0;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
	}

	.search-close {
		background: none;
		border: none;
		padding: 6px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.search-close:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	.search-input-wrapper {
		display: flex;
		align-items: center;
		gap: 8px;
		margin: 12px 12px 0;
		padding: 8px 10px;
		background: var(--input-bg);
		border: 1px solid var(--border);
		border-radius: 6px;
		transition: border-color 150ms ease;
	}

	.search-input-wrapper:focus-within {
		border-color: var(--accent);
	}

	.search-icon {
		flex-shrink: 0;
		color: var(--text-muted);
	}

	.search-input {
		flex: 1;
		background: none;
		border: none;
		color: var(--text-normal);
		font-size: 13px;
		font-family: inherit;
		outline: none;
		min-width: 0;
	}

	.search-input::placeholder {
		color: var(--text-dim);
	}

	.search-results {
		flex: 1;
		overflow-y: auto;
		padding: 4px 8px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.search-status {
		text-align: center;
		color: var(--text-muted);
		font-size: 13px;
		padding: 24px 12px;
		margin: 0;
	}

	.results-list {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.search-pagination {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 12px;
		padding: 12px 0 8px;
		flex-shrink: 0;
	}

	.page-btn {
		background: none;
		border: 1px solid var(--border);
		border-radius: 4px;
		padding: 4px 8px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		font-family: inherit;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.page-btn:hover:not(:disabled) {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	.page-btn:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.page-info {
		font-size: 12px;
		color: var(--text-muted);
	}

	@media (max-width: 899px) {
		.search-panel {
			position: fixed;
			top: 0;
			right: 0;
			bottom: 0;
			width: 100%;
			max-width: 340px;
			z-index: 55;
			animation: slide-in-right 200ms ease;
		}

		@keyframes slide-in-right {
			from { transform: translateX(100%); }
			to { transform: translateX(0); }
		}
	}
</style>
