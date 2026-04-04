<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { adminApi } from '$lib/api/client';
	import type { AdminServer } from '$lib/types/models';
	import DataTable from '$lib/components/shared/DataTable.svelte';
	import Pagination from '$lib/components/shared/Pagination.svelte';

	let servers = $state<AdminServer[]>([]);
	let totalPages = $state(1);
	let currentPage = $state(1);
	let search = $state('');
	let loading = $state(false);
	let error = $state('');
	let debounceTimer: ReturnType<typeof setTimeout>;

	const columns = [
		{ key: 'name', label: 'Name' },
		{ key: 'memberCount', label: 'Members' },
		{ key: 'createdAt', label: 'Created' },
		{ key: 'status', label: 'Status' }
	];

	function formatRows(items: AdminServer[]) {
		return items.map((s) => ({
			...s,
			createdAt: new Date(s.createdAt).toLocaleDateString(),
			status: s.isQuarantined ? 'Quarantined' : 'Active'
		}));
	}

	async function loadServers() {
		loading = true;
		error = '';
		try {
			const params = new URLSearchParams({
				page: String(currentPage),
				pageSize: '25'
			});
			if (search.trim()) params.set('search', search.trim());
			const res = await adminApi.getServers(params.toString());
			servers = res.items;
			totalPages = res.totalPages || 1;
		} catch (e: any) {
			error = e.message || 'Failed to load servers.';
		}
		loading = false;
	}

	function onSearchInput() {
		clearTimeout(debounceTimer);
		currentPage = 1;
		debounceTimer = setTimeout(() => loadServers(), 300);
	}

	function onPageChange(p: number) {
		currentPage = p;
		loadServers();
	}

	onMount(() => loadServers());
</script>

<h1>Servers</h1>

<div class="toolbar">
	<input
		type="search"
		placeholder="Search by name..."
		bind:value={search}
		oninput={onSearchInput}
		class="search-input"
	/>
</div>

{#if error}
	<p class="error">{error}</p>
{/if}

{#if loading}
	<p class="muted">Loading...</p>
{:else}
	<DataTable
		{columns}
		rows={formatRows(servers)}
		onRowClick={(row) => goto('/servers/' + row.id)}
	/>
	{#if totalPages > 1}
		<Pagination page={currentPage} {totalPages} {onPageChange} />
	{/if}
{/if}

<style>
	h1 { font-size: 24px; margin-bottom: 20px; }
	.toolbar { margin-bottom: 16px; }
	.search-input {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: var(--radius);
		color: var(--text-primary);
		padding: 8px 12px;
		font-size: 14px;
		width: 300px;
	}
	.search-input:focus { outline: none; border-color: var(--accent); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
</style>
