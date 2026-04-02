<script lang="ts">
	import { adminApi } from '$lib/api/client';
	import DataTable from '$lib/components/shared/DataTable.svelte';
	import Pagination from '$lib/components/shared/Pagination.svelte';

	let query = $state('');
	let results = $state<any[]>([]);
	let totalPages = $state(1);
	let currentPage = $state(1);
	let loading = $state(false);
	let error = $state('');
	let hasSearched = $state(false);

	const columns = [
		{ key: 'body', label: 'Content' },
		{ key: 'authorName', label: 'Author' },
		{ key: 'serverName', label: 'Server' },
		{ key: 'channelName', label: 'Channel' },
		{ key: 'createdAt', label: 'Timestamp' }
	];

	function formatRows(items: any[]) {
		return items.map((m) => ({
			...m,
			body: m.body?.length > 120 ? m.body.slice(0, 120) + '…' : (m.body || '—'),
			createdAt: new Date(m.createdAt).toLocaleString()
		}));
	}

	async function doSearch(page = 1) {
		if (query.trim().length < 2) { error = 'Enter at least 2 characters.'; return; }
		loading = true;
		error = '';
		hasSearched = true;
		currentPage = page;
		try {
			const params = new URLSearchParams({ q: query.trim(), page: String(page), pageSize: '25' });
			const res = await adminApi.searchMessages(params.toString());
			results = res.items;
			totalPages = res.totalPages || 1;
		} catch (e: any) {
			error = e.message || 'Search failed.';
		}
		loading = false;
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter') doSearch(1);
	}

	function onPageChange(p: number) {
		doSearch(p);
	}
</script>

<a href="/moderation" class="back-link">← Back to Reports</a>

<h1>Message Search</h1>

<div class="search-bar">
	<input
		type="search"
		placeholder="Search message content (min 2 chars)..."
		bind:value={query}
		onkeydown={handleKeydown}
		class="search-input"
	/>
	<button class="btn-search" onclick={() => doSearch(1)} disabled={query.trim().length < 2}>
		Search
	</button>
</div>

{#if error}
	<p class="error">{error}</p>
{/if}

{#if loading}
	<p class="muted">Searching...</p>
{:else if hasSearched}
	<DataTable {columns} rows={formatRows(results)} />
	{#if totalPages > 1}
		<Pagination page={currentPage} {totalPages} {onPageChange} />
	{/if}
{/if}

<style>
	.back-link { font-size: 13px; color: var(--accent); display: inline-block; margin-bottom: 20px; }
	h1 { font-size: 24px; margin-bottom: 20px; }
	.search-bar { display: flex; gap: 10px; margin-bottom: 20px; }
	.search-input { flex: 1; max-width: 500px; background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 8px 12px; font-size: 14px; }
	.search-input:focus { outline: none; border-color: var(--accent); }
	.btn-search { background: var(--accent); border: none; border-radius: var(--radius); color: #fff; padding: 8px 18px; font-size: 14px; font-weight: 500; }
	.btn-search:disabled { opacity: 0.5; cursor: not-allowed; }
	.btn-search:not(:disabled):hover { background: var(--accent-hover); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
</style>
