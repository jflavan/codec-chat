<script lang="ts">
	import { onMount } from 'svelte';
	import { adminApi } from '$lib/api/client';
	import type { AdminAction, SystemAnnouncement } from '$lib/types/models';
	import DataTable from '$lib/components/shared/DataTable.svelte';
	import Pagination from '$lib/components/shared/Pagination.svelte';
	import ConfirmDialog from '$lib/components/shared/ConfirmDialog.svelte';

	// Admin Action Log
	let actions = $state<AdminAction[]>([]);
	let actionsTotalPages = $state(1);
	let actionsPage = $state(1);
	let actionsTypeFilter = $state('');
	let actionsLoading = $state(false);
	let actionsError = $state('');

	const actionColumns = [
		{ key: 'actorName', label: 'Actor' },
		{ key: 'actionType', label: 'Action Type' },
		{ key: 'targetType', label: 'Target Type' },
		{ key: 'targetId', label: 'Target' },
		{ key: 'reason', label: 'Reason' },
		{ key: 'createdAt', label: 'Timestamp' }
	];

	function formatActionRows(items: AdminAction[]) {
		return items.map((a) => ({
			...a,
			reason: a.reason ?? '—',
			createdAt: new Date(a.createdAt).toLocaleString()
		}));
	}

	async function loadActions(page = actionsPage) {
		actionsLoading = true;
		actionsError = '';
		actionsPage = page;
		try {
			const params = new URLSearchParams({ page: String(page), pageSize: '25' });
			if (actionsTypeFilter) params.set('actionType', actionsTypeFilter);
			const res = await adminApi.getAdminActions(params.toString());
			actions = res.items;
			actionsTotalPages = res.totalPages || 1;
		} catch (e: any) {
			actionsError = e.message || 'Failed to load actions.';
		}
		actionsLoading = false;
	}

	// Announcements
	let announcements = $state<SystemAnnouncement[]>([]);
	let announcementsLoading = $state(false);
	let announcementsError = $state('');
	let announcementSuccess = $state('');

	let newTitle = $state('');
	let newBody = $state('');
	let newExpiry = $state('');
	let creating = $state(false);

	let deleteDialogOpen = $state(false);
	let deleteTargetId = $state('');
	let deleteTargetTitle = $state('');

	async function loadAnnouncements() {
		announcementsLoading = true;
		announcementsError = '';
		try {
			announcements = await adminApi.getAnnouncements();
		} catch (e: any) {
			announcementsError = e.message || 'Failed to load announcements.';
		}
		announcementsLoading = false;
	}

	async function createAnnouncement() {
		if (!newTitle.trim() || !newBody.trim()) {
			announcementsError = 'Title and body are required.';
			return;
		}
		creating = true;
		announcementsError = '';
		announcementSuccess = '';
		try {
			const data: { title: string; body: string; expiresAt?: string } = {
				title: newTitle.trim(),
				body: newBody.trim()
			};
			if (newExpiry) data.expiresAt = new Date(newExpiry).toISOString();
			await adminApi.createAnnouncement(data);
			newTitle = '';
			newBody = '';
			newExpiry = '';
			announcementSuccess = 'Announcement created.';
			await loadAnnouncements();
		} catch (e: any) {
			announcementsError = e.message || 'Failed to create announcement.';
		}
		creating = false;
	}

	function openDeleteDialog(id: string, title: string) {
		deleteTargetId = id;
		deleteTargetTitle = title;
		deleteDialogOpen = true;
	}

	async function confirmDelete() {
		deleteDialogOpen = false;
		announcementsError = '';
		announcementSuccess = '';
		try {
			await adminApi.deleteAnnouncement(deleteTargetId);
			announcementSuccess = 'Announcement deleted.';
			await loadAnnouncements();
		} catch (e: any) {
			announcementsError = e.message || 'Failed to delete announcement.';
		}
	}

	// Connections
	let connections = $state<{ activeUsers: number } | null>(null);
	let connectionsLoading = $state(false);

	async function loadConnections() {
		connectionsLoading = true;
		try {
			connections = await adminApi.getConnections();
		} catch {
			connections = null;
		}
		connectionsLoading = false;
	}

	onMount(() => {
		loadActions();
		loadAnnouncements();
		loadConnections();
	});
</script>

<h1>System</h1>

<!-- Connections -->
<section class="card">
	<h2>Active Connections</h2>
	{#if connectionsLoading}
		<p class="muted">Loading...</p>
	{:else if connections}
		<div class="connections-stat">
			<span class="stat-value">{connections.activeUsers}</span>
			<span class="stat-label">Active Users</span>
		</div>
	{:else}
		<p class="muted">Unavailable</p>
	{/if}
</section>

<!-- Announcements -->
<section class="card">
	<h2>Announcements</h2>
	{#if announcementSuccess}<p class="success">{announcementSuccess}</p>{/if}
	{#if announcementsError}<p class="error">{announcementsError}</p>{/if}

	<div class="create-form">
		<h3>New Announcement</h3>
		<input type="text" placeholder="Title" bind:value={newTitle} class="form-input" />
		<textarea placeholder="Body..." bind:value={newBody} class="form-textarea"></textarea>
		<div class="form-row">
			<label for="expiry-input" class="expiry-label">Expires At (optional):</label>
			<input id="expiry-input" type="datetime-local" bind:value={newExpiry} class="form-input-short" />
			<button class="btn-create" onclick={createAnnouncement} disabled={creating}>
				{creating ? 'Creating…' : 'Create'}
			</button>
		</div>
	</div>

	{#if announcementsLoading}
		<p class="muted">Loading...</p>
	{:else if announcements.length === 0}
		<p class="muted">No announcements.</p>
	{:else}
		<ul class="announcement-list">
			{#each announcements as ann}
				<li class="announcement-item" class:inactive={!ann.isActive}>
					<div class="ann-header">
						<span class="ann-title">{ann.title}</span>
						{#if !ann.isActive}<span class="badge inactive">Inactive</span>{/if}
						<button class="btn-delete" onclick={() => openDeleteDialog(ann.id, ann.title)}>Delete</button>
					</div>
					<p class="ann-body">{ann.body}</p>
					<div class="ann-meta">
						<span class="muted">By {ann.createdBy} · {new Date(ann.createdAt).toLocaleDateString()}</span>
						{#if ann.expiresAt}<span class="muted"> · Expires {new Date(ann.expiresAt).toLocaleDateString()}</span>{/if}
					</div>
				</li>
			{/each}
		</ul>
	{/if}
</section>

<!-- Admin Action Log -->
<section class="card">
	<h2>Admin Action Log</h2>
	<div class="toolbar">
		<input
			type="text"
			placeholder="Filter by action type..."
			bind:value={actionsTypeFilter}
			onchange={() => loadActions(1)}
			class="filter-input"
		/>
	</div>
	{#if actionsError}
		<p class="error">{actionsError}</p>
	{/if}
	{#if actionsLoading}
		<p class="muted">Loading...</p>
	{:else}
		<DataTable columns={actionColumns} rows={formatActionRows(actions)} />
		{#if actionsTotalPages > 1}
			<Pagination page={actionsPage} totalPages={actionsTotalPages} onPageChange={(p) => loadActions(p)} />
		{/if}
	{/if}
</section>

<ConfirmDialog
	open={deleteDialogOpen}
	title="Delete Announcement"
	message={`Delete announcement "${deleteTargetTitle}"? This cannot be undone.`}
	confirmLabel="Delete"
	destructive={true}
	onConfirm={confirmDelete}
	onCancel={() => (deleteDialogOpen = false)}
/>

<style>
	h1 { font-size: 24px; margin-bottom: 24px; }
	h2 { font-size: 16px; font-weight: 600; margin-bottom: 16px; }
	h3 { font-size: 13px; font-weight: 600; color: var(--text-secondary); margin-bottom: 10px; }
	.card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; margin-bottom: 20px; }
	.connections-stat { display: flex; align-items: baseline; gap: 10px; }
	.stat-value { font-size: 36px; font-weight: 700; color: var(--success); }
	.stat-label { font-size: 14px; color: var(--text-muted); }
	.create-form { background: var(--bg-tertiary); border-radius: var(--radius); padding: 16px; margin-bottom: 20px; display: flex; flex-direction: column; gap: 10px; }
	.form-input { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 8px 10px; font-size: 14px; width: 100%; }
	.form-input:focus, .form-input-short:focus { outline: none; border-color: var(--accent); }
	.form-textarea { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 8px 10px; font-size: 14px; width: 100%; min-height: 80px; resize: vertical; }
	.form-textarea:focus { outline: none; border-color: var(--accent); }
	.form-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
	.expiry-label { font-size: 13px; color: var(--text-secondary); }
	.form-input-short { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 10px; font-size: 13px; }
	.btn-create { background: var(--accent); border: none; border-radius: var(--radius); color: #fff; padding: 7px 16px; font-size: 13px; font-weight: 500; }
	.btn-create:disabled { opacity: 0.5; cursor: not-allowed; }
	.btn-create:not(:disabled):hover { background: var(--accent-hover); }
	.announcement-list { list-style: none; display: flex; flex-direction: column; gap: 12px; }
	.announcement-item { background: var(--bg-tertiary); border-radius: var(--radius); padding: 14px; }
	.announcement-item.inactive { opacity: 0.6; }
	.ann-header { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
	.ann-title { font-weight: 600; font-size: 14px; flex: 1; }
	.ann-body { font-size: 13px; color: var(--text-secondary); margin-bottom: 6px; line-height: 1.5; }
	.ann-meta { font-size: 12px; }
	.badge { font-size: 11px; padding: 2px 6px; border-radius: 4px; }
	.badge.inactive { background: var(--bg-secondary); color: var(--text-muted); border: 1px solid var(--border); }
	.btn-delete { background: none; border: 1px solid var(--danger); border-radius: 4px; color: var(--danger); padding: 2px 8px; font-size: 12px; }
	.btn-delete:hover { background: color-mix(in srgb, var(--danger) 15%, transparent); }
	.toolbar { margin-bottom: 14px; }
	.filter-input { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 10px; font-size: 13px; width: 260px; }
	.filter-input:focus { outline: none; border-color: var(--accent); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 10px; }
	.success { color: var(--success); margin-bottom: 10px; }
</style>
