<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { adminApi } from '$lib/api/client';

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

	let report = $state<any>(null);
	let loading = $state(true);
	let error = $state('');
	let actionError = $state('');
	let actionSuccess = $state('');
	let resolution = $state('');
	let dismissReason = $state('');

	async function load() {
		loading = true;
		error = '';
		try {
			report = await adminApi.getReport($page.params.id!);
		} catch (e: any) {
			error = e.message || 'Failed to load report.';
		}
		loading = false;
	}

	async function doAction(data: any) {
		actionError = '';
		actionSuccess = '';
		try {
			await adminApi.updateReport(report.id, data);
			actionSuccess = 'Report updated.';
			await load();
		} catch (e: any) {
			actionError = e.message || 'Action failed.';
		}
	}

	function handleAssignSelf() {
		doAction({ assignToSelf: true });
	}

	function handleMarkReviewing() {
		doAction({ status: 1 });
	}

	function handleResolve() {
		if (!resolution.trim()) { actionError = 'Please enter a resolution note.'; return; }
		doAction({ status: 2, resolution: resolution.trim() });
	}

	function handleDismiss() {
		if (!dismissReason.trim()) { actionError = 'Please enter a reason.'; return; }
		doAction({ status: 3, dismissReason: dismissReason.trim() });
	}

	onMount(() => load());
</script>

<a href="/moderation" class="back-link">← Back to Reports</a>

{#if loading}
	<p class="muted">Loading...</p>
{:else if error}
	<p class="error">{error}</p>
{:else if report}
	<h1>Report</h1>

	{#if actionSuccess}<p class="success">{actionSuccess}</p>{/if}
	{#if actionError}<p class="error">{actionError}</p>{/if}

	<div class="sections">
		<!-- Report info -->
		<section class="card">
			<h2>Report Details</h2>
			<div class="info-grid">
				<div class="info-item"><span class="label">ID</span><span class="value mono">{report.id}</span></div>
				<div class="info-item"><span class="label">Type</span><span class="value">{REPORT_TYPE_LABELS[report.reportType] ?? report.reportType}</span></div>
				<div class="info-item"><span class="label">Status</span><span class="value">{REPORT_STATUS_LABELS[report.status] ?? report.status}</span></div>
				<div class="info-item"><span class="label">Reporter</span><span class="value">{report.reporterName}</span></div>
				<div class="info-item"><span class="label">Target ID</span><span class="value mono">{report.targetId}</span></div>
				<div class="info-item"><span class="label">Created</span><span class="value">{new Date(report.createdAt).toLocaleString()}</span></div>
				{#if report.assignedToUserId}
					<div class="info-item"><span class="label">Assigned To</span><span class="value mono">{report.assignedToUserId}</span></div>
				{/if}
				{#if report.relatedCount > 0}
					<div class="info-item"><span class="label">Related Reports</span><span class="value">{report.relatedCount}</span></div>
				{/if}
			</div>
			<div class="reason-box">
				<span class="label">Reason</span>
				<p class="reason-text">{report.reason}</p>
			</div>
		</section>

		<!-- Target snapshot -->
		{#if report.targetSnapshot || report.targetName}
			<section class="card">
				<h2>Target Info</h2>
				<p class="target-info">{report.targetSnapshot ?? report.targetName}</p>
			</section>
		{/if}

		<!-- Actions -->
		{#if report.status === 0 || report.status === 1}
			<section class="card">
				<h2>Actions</h2>
				<div class="action-group">
					{#if !report.assignedToUserId}
						<button class="btn" onclick={handleAssignSelf}>Assign to Me</button>
					{/if}
					{#if report.status === 0}
						<button class="btn" onclick={handleMarkReviewing}>Mark Reviewing</button>
					{/if}
					<div class="textarea-row">
						<textarea placeholder="Resolution note..." bind:value={resolution} class="textarea"></textarea>
						<button class="btn success" onclick={handleResolve}>Resolve</button>
					</div>
					<div class="textarea-row">
						<textarea placeholder="Dismiss reason..." bind:value={dismissReason} class="textarea"></textarea>
						<button class="btn danger" onclick={handleDismiss}>Dismiss</button>
					</div>
				</div>
			</section>
		{/if}

		<!-- Resolution -->
		{#if report.resolution}
			<section class="card">
				<h2>Resolution</h2>
				<p class="resolution-text">{report.resolution}</p>
			</section>
		{/if}
	</div>
{/if}

<style>
	.back-link { font-size: 13px; color: var(--accent); display: inline-block; margin-bottom: 20px; }
	h1 { font-size: 24px; margin-bottom: 24px; }
	h2 { font-size: 15px; font-weight: 600; margin-bottom: 14px; }
	.sections { display: flex; flex-direction: column; gap: 16px; }
	.card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; }
	.info-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 12px; margin-bottom: 16px; }
	.info-item { display: flex; flex-direction: column; gap: 2px; }
	.label { font-size: 11px; text-transform: uppercase; color: var(--text-muted); letter-spacing: 0.5px; }
	.value { font-size: 14px; }
	.mono { font-family: monospace; font-size: 12px; }
	.reason-box { margin-top: 4px; }
	.reason-text, .resolution-text { font-size: 14px; color: var(--text-secondary); margin-top: 6px; line-height: 1.5; }
	.target-info { font-size: 14px; color: var(--text-secondary); }
	.action-group { display: flex; flex-direction: column; gap: 10px; }
	.textarea-row { display: flex; gap: 8px; align-items: flex-start; }
	.textarea { flex: 1; background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 8px 10px; font-size: 13px; min-height: 64px; resize: vertical; }
	.textarea:focus { outline: none; border-color: var(--accent); }
	.btn { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 14px; font-size: 13px; transition: border-color 0.15s; white-space: nowrap; }
	.btn:hover { border-color: var(--accent); }
	.btn.danger { border-color: var(--danger); color: var(--danger); }
	.btn.success { border-color: var(--success); color: var(--success); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
	.success { color: var(--success); margin-bottom: 12px; }
</style>
