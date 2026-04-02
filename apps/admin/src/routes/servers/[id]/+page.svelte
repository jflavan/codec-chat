<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { adminApi } from '$lib/api/client';
	import ConfirmDialog from '$lib/components/shared/ConfirmDialog.svelte';

	let server = $state<any>(null);
	let loading = $state(true);
	let error = $state('');
	let actionError = $state('');
	let actionSuccess = $state('');

	let dialogOpen = $state(false);
	let dialogTitle = $state('');
	let dialogMessage = $state('');
	let dialogLabel = $state('Confirm');
	let dialogDestructive = $state(false);
	let dialogRequireInput = $state<string | undefined>(undefined);
	let dialogAction = $state<() => Promise<void>>(() => Promise.resolve());

	let quarantineReason = $state('');
	let transferUserId = $state('');

	async function load() {
		loading = true;
		error = '';
		try {
			const data = await adminApi.getServer(page.params.id!);
			const ownerMember = data.members?.find((m: any) => m.userId === data.ownerId);
			server = { ...data.server, ownerId: data.ownerId, ownerName: ownerMember?.displayName, memberCount: data.memberCount, members: data.members, channels: data.channels, roles: data.roles };
		} catch (e: any) {
			error = e.message || 'Failed to load server.';
		}
		loading = false;
	}

	function openDialog(opts: {
		title: string;
		message: string;
		label?: string;
		destructive?: boolean;
		requireInput?: string;
		action: () => Promise<void>;
	}) {
		dialogTitle = opts.title;
		dialogMessage = opts.message;
		dialogLabel = opts.label ?? 'Confirm';
		dialogDestructive = opts.destructive ?? false;
		dialogRequireInput = opts.requireInput;
		dialogAction = opts.action;
		dialogOpen = true;
	}

	async function runAction() {
		dialogOpen = false;
		actionError = '';
		actionSuccess = '';
		try {
			await dialogAction();
			actionSuccess = 'Action completed.';
			await load();
		} catch (e: any) {
			actionError = e.message || 'Action failed.';
		}
	}

	function handleQuarantine() {
		if (!quarantineReason.trim()) { actionError = 'Please enter a reason.'; return; }
		openDialog({
			title: 'Quarantine Server',
			message: `Quarantine "${server.name}"? Reason: "${quarantineReason}"`,
			label: 'Quarantine',
			destructive: true,
			action: () => adminApi.quarantineServer(server.id, quarantineReason)
		});
	}

	function handleUnquarantine() {
		openDialog({
			title: 'Remove Quarantine',
			message: `Lift quarantine on "${server.name}"?`,
			label: 'Unquarantine',
			action: () => adminApi.unquarantineServer(server.id)
		});
	}

	function handleTransfer() {
		if (!transferUserId.trim()) { actionError = 'Please enter a user ID.'; return; }
		openDialog({
			title: 'Transfer Ownership',
			message: `Transfer ownership of "${server.name}" to user ${transferUserId}?`,
			label: 'Transfer',
			destructive: true,
			action: () => adminApi.transferOwnership(server.id, transferUserId.trim())
		});
	}

	function handleDelete() {
		openDialog({
			title: 'Delete Server',
			message: `This will permanently delete the server and all its data. Type the server name to confirm.`,
			label: 'Delete',
			destructive: true,
			requireInput: server.name,
			action: () => adminApi.deleteServer(server.id, 'Admin deletion')
		});
	}

	onMount(() => load());
</script>

<a href="/servers" class="back-link">← Back to Servers</a>

{#if loading}
	<p class="muted">Loading...</p>
{:else if error}
	<p class="error">{error}</p>
{:else if server}
	<div class="page-header">
		<h1>{server.name}</h1>
		{#if server.isQuarantined}<span class="badge quarantined">Quarantined</span>{/if}
	</div>

	{#if actionSuccess}<p class="success">{actionSuccess}</p>{/if}
	{#if actionError}<p class="error">{actionError}</p>{/if}

	<div class="sections">
		<!-- Info -->
		<section class="card">
			<h2>Server Info</h2>
			<div class="info-grid">
				<div class="info-item"><span class="label">ID</span><span class="value mono">{server.id}</span></div>
				<div class="info-item"><span class="label">Owner</span><span class="value">
					{#if server.ownerName}
						<a href="/users/{server.ownerId}">{server.ownerName}</a>
					{:else}
						—
					{/if}
				</span></div>
				<div class="info-item"><span class="label">Members</span><span class="value">{server.memberCount}</span></div>
				<div class="info-item"><span class="label">Created</span><span class="value">{new Date(server.createdAt).toLocaleString()}</span></div>
				{#if server.description}
					<div class="info-item full"><span class="label">Description</span><span class="value">{server.description}</span></div>
				{/if}
			</div>
		</section>

		<!-- Members -->
		{#if server.members?.length}
			<section class="card">
				<h2>Members ({server.members.length})</h2>
				<ul class="item-list">
					{#each server.members as m}
						<li><a href="/users/{m.userId}">{m.displayName}</a><span class="muted">{new Date(m.joinedAt).toLocaleDateString()}</span></li>
					{/each}
				</ul>
			</section>
		{/if}

		<!-- Channels -->
		{#if server.channels?.length}
			<section class="card">
				<h2>Channels ({server.channels.length})</h2>
				<ul class="item-list">
					{#each server.channels as ch}
						<li><span>{ch.name}</span><span class="muted">{ch.type === 1 ? 'Voice' : 'Text'}</span></li>
					{/each}
				</ul>
			</section>
		{/if}

		<!-- Roles -->
		{#if server.roles?.length}
			<section class="card">
				<h2>Roles ({server.roles.length})</h2>
				<ul class="item-list">
					{#each server.roles as r}
						<li><span>{r.name}</span></li>
					{/each}
				</ul>
			</section>
		{/if}

		<!-- Actions -->
		<section class="card">
			<h2>Actions</h2>
			<div class="actions-layout">
				{#if !server.isQuarantined}
					<div class="action-row">
						<input type="text" placeholder="Quarantine reason..." bind:value={quarantineReason} class="reason-input" />
						<button class="btn danger" onclick={handleQuarantine}>Quarantine</button>
					</div>
				{:else}
					<button class="btn" onclick={handleUnquarantine}>Remove Quarantine</button>
				{/if}
				<div class="action-row">
					<input type="text" placeholder="New owner user ID..." bind:value={transferUserId} class="reason-input" />
					<button class="btn danger" onclick={handleTransfer}>Transfer Ownership</button>
				</div>
				<button class="btn danger" onclick={handleDelete}>Delete Server</button>
			</div>
		</section>
	</div>
{/if}

<ConfirmDialog
	open={dialogOpen}
	title={dialogTitle}
	message={dialogMessage}
	confirmLabel={dialogLabel}
	destructive={dialogDestructive}
	requireInput={dialogRequireInput}
	onConfirm={runAction}
	onCancel={() => (dialogOpen = false)}
/>

<style>
	.back-link { font-size: 13px; color: var(--accent); display: inline-block; margin-bottom: 20px; }
	.page-header { display: flex; align-items: center; gap: 10px; margin-bottom: 24px; }
	h1 { font-size: 24px; }
	h2 { font-size: 15px; font-weight: 600; margin-bottom: 14px; }
	.badge { font-size: 11px; padding: 2px 8px; border-radius: 4px; font-weight: 600; }
	.badge.quarantined { background: var(--warning); color: #000; }
	.sections { display: flex; flex-direction: column; gap: 16px; }
	.card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; }
	.info-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
	.info-item { display: flex; flex-direction: column; gap: 2px; }
	.info-item.full { grid-column: 1 / -1; }
	.label { font-size: 11px; text-transform: uppercase; color: var(--text-muted); letter-spacing: 0.5px; }
	.value { font-size: 14px; }
	.mono { font-family: monospace; font-size: 12px; }
	.item-list { list-style: none; display: flex; flex-direction: column; gap: 8px; }
	.item-list li { display: flex; align-items: center; gap: 12px; font-size: 14px; }
	.actions-layout { display: flex; flex-direction: column; gap: 10px; }
	.action-row { display: flex; gap: 8px; align-items: center; }
	.reason-input { flex: 1; background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 10px; font-size: 13px; }
	.reason-input:focus { outline: none; border-color: var(--accent); }
	.btn { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 14px; font-size: 13px; transition: border-color 0.15s; white-space: nowrap; }
	.btn:hover { border-color: var(--accent); }
	.btn.danger { border-color: var(--danger); color: var(--danger); }
	.btn.danger:hover { background: color-mix(in srgb, var(--danger) 15%, var(--bg-tertiary)); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
	.success { color: var(--success); margin-bottom: 12px; }
</style>
