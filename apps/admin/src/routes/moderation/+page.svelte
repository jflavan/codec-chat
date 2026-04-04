<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { adminApi } from '$lib/api/client';
	import type { Report } from '$lib/types/models';
	import DataTable from '$lib/components/shared/DataTable.svelte';
	import Pagination from '$lib/components/shared/Pagination.svelte';

	const REPORT_STATUS_LABELS: Record<number, string> = {
		0: 'Open',
		1: 'Reviewing',
		2: 'Resolved',
		3: 'Dismissed'
	};
	const REPORT_TYPE_LABELS: Record<number, string> = {
		0: 'User',
		1: 'Message',
		2: 'Server'
	};

	let reports = $state<Report[]>([]);
	let totalPages = $state(1);
	let currentPage = $state(1);
	let statusFilter = $state('');
	let typeFilter = $state('');
	let loading = $state(false);
	let error = $state('');

	const columns = [
		{ key: 'typeLabel', label: 'Type' },
		{ key: 'targetId', label: 'Target ID' },
		{ key: 'reporterName', label: 'Reporter' },
		{ key: 'reasonShort', label: 'Reason' },
		{ key: 'statusLabel', label: 'Status' },
		{ key: 'createdAt', label: 'Created' },
		{ key: 'relatedCount', label: 'Related' }
	];

	function formatRows(items: Report[]) {
		return items.map((r) => ({
			...r,
			typeLabel: REPORT_TYPE_LABELS[r.reportType] ?? String(r.reportType),
			statusLabel: REPORT_STATUS_LABELS[r.status] ?? String(r.status),
			reasonShort: r.reason.length > 80 ? r.reason.slice(0, 80) + '…' : r.reason,
			createdAt: new Date(r.createdAt).toLocaleDateString()
		}));
	}

	async function loadReports() {
		loading = true;
		error = '';
		try {
			const params = new URLSearchParams({ page: String(currentPage), pageSize: '25' });
			if (statusFilter !== '') params.set('status', statusFilter);
			if (typeFilter !== '') params.set('reportType', typeFilter);
			const res = await adminApi.getReports(params.toString());
			reports = res.items;
			totalPages = res.totalPages || 1;
		} catch (e: any) {
			error = e.message || 'Failed to load reports.';
		}
		loading = false;
	}

	function onFilterChange() {
		currentPage = 1;
		loadReports();
	}

	function onPageChange(p: number) {
		currentPage = p;
		loadReports();
	}

	onMount(() => loadReports());
</script>

<div class="header-row">
	<h1>Report Queue</h1>
	<a href="/moderation/search" class="btn-secondary">Message Search</a>
</div>

<div class="toolbar">
	<select bind:value={statusFilter} onchange={onFilterChange} class="filter-select">
		<option value="">All Statuses</option>
		<option value="0">Open</option>
		<option value="1">Reviewing</option>
		<option value="2">Resolved</option>
		<option value="3">Dismissed</option>
	</select>
	<select bind:value={typeFilter} onchange={onFilterChange} class="filter-select">
		<option value="">All Types</option>
		<option value="0">User</option>
		<option value="1">Message</option>
		<option value="2">Server</option>
	</select>
</div>

{#if error}
	<p class="error">{error}</p>
{/if}

{#if loading}
	<p class="muted">Loading...</p>
{:else}
	<DataTable
		{columns}
		rows={formatRows(reports)}
		onRowClick={(row) => goto('/moderation/' + row.id)}
	/>
	{#if totalPages > 1}
		<Pagination page={currentPage} {totalPages} {onPageChange} />
	{/if}
{/if}

<style>
	.header-row { display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px; }
	h1 { font-size: 24px; }
	.btn-secondary { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 14px; font-size: 13px; }
	.btn-secondary:hover { border-color: var(--accent); color: var(--accent); }
	.toolbar { display: flex; gap: 10px; margin-bottom: 16px; }
	.filter-select { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 10px; font-size: 13px; }
	.filter-select:focus { outline: none; border-color: var(--accent); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
</style>
