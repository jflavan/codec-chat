<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { adminApi } from '$lib/api/client';
	import ConfirmDialog from '$lib/components/shared/ConfirmDialog.svelte';

	let user = $state<any>(null);
	let loading = $state(true);
	let error = $state('');
	let actionError = $state('');
	let actionSuccess = $state('');

	// Dialog state
	let dialogOpen = $state(false);
	let dialogTitle = $state('');
	let dialogMessage = $state('');
	let dialogLabel = $state('Confirm');
	let dialogDestructive = $state(false);
	let dialogRequireInput = $state<string | undefined>(undefined);
	let dialogAction = $state<() => Promise<void>>(() => Promise.resolve());

	// Reason input for disable
	let disableReason = $state('');

	async function load() {
		loading = true;
		error = '';
		try {
			const data = await adminApi.getUser(page.params.id!);
			user = { ...data.user, memberships: data.memberships, recentMessages: data.recentMessages, reportHistory: data.reportHistory, adminHistory: data.adminHistory };
		} catch (e: any) {
			error = e.message || 'Failed to load user.';
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
			actionSuccess = 'Action completed successfully.';
			await load();
		} catch (e: any) {
			actionError = e.message || 'Action failed.';
		}
	}

	function handleDisable() {
		if (!disableReason.trim()) { actionError = 'Please enter a reason.'; return; }
		openDialog({
			title: 'Disable User',
			message: `Disable ${user.displayName}? Reason: "${disableReason}"`,
			label: 'Disable',
			destructive: true,
			action: () => adminApi.disableUser(user.id, disableReason)
		});
	}

	function handleEnable() {
		openDialog({
			title: 'Enable User',
			message: `Re-enable account for ${user.displayName}?`,
			label: 'Enable',
			action: () => adminApi.enableUser(user.id)
		});
	}

	function handleForceLogout() {
		openDialog({
			title: 'Force Logout',
			message: `Invalidate all sessions for ${user.displayName}?`,
			label: 'Force Logout',
			destructive: true,
			action: () => adminApi.forceLogout(user.id)
		});
	}

	function handleResetPassword() {
		openDialog({
			title: 'Reset Password',
			message: `Remove the password credential for ${user.email}? The user will need to use another auth provider (Google, GitHub, Discord, or SAML) to sign in.`,
			label: 'Reset',
			action: () => adminApi.resetPassword(user.id)
		});
	}

	function handlePromote() {
		openDialog({
			title: 'Promote to Global Admin',
			message: `Grant global admin privileges to ${user.displayName}?`,
			label: 'Promote',
			destructive: true,
			action: () => adminApi.setGlobalAdmin(user.id, true)
		});
	}

	function handleDemote() {
		openDialog({
			title: 'Demote Global Admin',
			message: `Remove global admin privileges from ${user.displayName}?`,
			label: 'Demote',
			destructive: true,
			action: () => adminApi.setGlobalAdmin(user.id, false)
		});
	}

	onMount(() => load());
</script>

<a href="/users" class="back-link">← Back to Users</a>

{#if loading}
	<p class="muted">Loading...</p>
{:else if error}
	<p class="error">{error}</p>
{:else if user}
	<div class="page-header">
		<h1>{user.displayName}</h1>
		{#if user.isGlobalAdmin}<span class="badge admin">Admin</span>{/if}
		{#if user.isDisabled}<span class="badge disabled">Disabled</span>{/if}
	</div>

	{#if actionSuccess}<p class="success">{actionSuccess}</p>{/if}
	{#if actionError}<p class="error">{actionError}</p>{/if}

	<div class="sections">
		<!-- Profile -->
		<section class="card">
			<h2>Profile</h2>
			<div class="info-grid">
				<div class="info-item"><span class="label">ID</span><span class="value mono">{user.id}</span></div>
				<div class="info-item"><span class="label">Email</span><span class="value">{user.email ?? '—'}</span></div>
				<div class="info-item"><span class="label">Nickname</span><span class="value">{user.nickname ?? '—'}</span></div>
				<div class="info-item"><span class="label">Created</span><span class="value">{new Date(user.createdAt).toLocaleString()}</span></div>
			</div>
		</section>

		<!-- Auth Providers -->
		<section class="card">
			<h2>Auth Providers</h2>
			<div class="providers">
				{#each [['Google', user.hasGoogle], ['GitHub', user.hasGitHub], ['Discord', user.hasDiscord], ['SAML', user.hasSaml], ['Email/Password', user.hasPassword]] as [name, enabled]}
					<span class="provider" class:active={enabled}>{name}</span>
				{/each}
			</div>
		</section>

		<!-- Server Memberships -->
		{#if user.memberships?.length}
			<section class="card">
				<h2>Server Memberships ({user.memberships.length})</h2>
				<ul class="member-list">
					{#each user.memberships as m}
						<li>
							<a href="/servers/{m.serverId}">{m.serverName}</a>
							<span class="muted">joined {new Date(m.joinedAt).toLocaleDateString()}</span>
						</li>
					{/each}
				</ul>
			</section>
		{/if}

		<!-- Recent Messages -->
		{#if user.recentMessages?.length}
			<section class="card">
				<h2>Recent Messages</h2>
				<ul class="message-list">
					{#each user.recentMessages as msg}
						<li>
							<span class="msg-time muted">{new Date(msg.createdAt).toLocaleString()}</span>
							<span class="msg-body">{msg.content}</span>
						</li>
					{/each}
				</ul>
			</section>
		{/if}

		<!-- Actions -->
		<section class="card">
			<h2>Actions</h2>
			<div class="action-group">
				{#if !user.isDisabled}
					<div class="disable-row">
						<input
							type="text"
							placeholder="Reason for disable..."
							bind:value={disableReason}
							class="reason-input"
						/>
						<button class="btn danger" onclick={handleDisable}>Disable User</button>
					</div>
				{:else}
					<button class="btn" onclick={handleEnable}>Enable User</button>
				{/if}
				<button class="btn danger" onclick={handleForceLogout}>Force Logout</button>
				{#if user.hasPassword}
					<button class="btn" onclick={handleResetPassword}>Reset Password</button>
				{/if}
				{#if !user.isGlobalAdmin}
					<button class="btn danger" onclick={handlePromote}>Promote to Global Admin</button>
				{:else}
					<button class="btn danger" onclick={handleDemote}>Demote Global Admin</button>
				{/if}
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
	.badge.admin { background: var(--accent); color: #fff; }
	.badge.disabled { background: var(--danger); color: #fff; }
	.sections { display: flex; flex-direction: column; gap: 16px; }
	.card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; }
	.info-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
	.info-item { display: flex; flex-direction: column; gap: 2px; }
	.label { font-size: 11px; text-transform: uppercase; color: var(--text-muted); letter-spacing: 0.5px; }
	.value { font-size: 14px; }
	.mono { font-family: monospace; font-size: 12px; }
	.providers { display: flex; gap: 8px; flex-wrap: wrap; }
	.provider { font-size: 12px; padding: 3px 10px; border-radius: 4px; background: var(--bg-tertiary); border: 1px solid var(--border); color: var(--text-muted); }
	.provider.active { background: color-mix(in srgb, var(--accent) 20%, var(--bg-tertiary)); border-color: var(--accent); color: var(--accent); }
	.member-list, .message-list { list-style: none; display: flex; flex-direction: column; gap: 8px; }
	.member-list li, .message-list li { font-size: 14px; display: flex; align-items: center; gap: 12px; }
	.msg-body { color: var(--text-secondary); font-size: 13px; }
	.msg-time { font-size: 12px; white-space: nowrap; }
	.action-group { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
	.disable-row { display: flex; gap: 8px; align-items: center; width: 100%; }
	.reason-input { flex: 1; background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 10px; font-size: 13px; }
	.reason-input:focus { outline: none; border-color: var(--accent); }
	.btn { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-primary); padding: 7px 14px; font-size: 13px; transition: border-color 0.15s; }
	.btn:hover { border-color: var(--accent); }
	.btn.danger { border-color: var(--danger); color: var(--danger); }
	.btn.danger:hover { background: color-mix(in srgb, var(--danger) 15%, var(--bg-tertiary)); }
	.muted { color: var(--text-muted); }
	.error { color: var(--danger); margin-bottom: 12px; }
	.success { color: var(--success); margin-bottom: 12px; }
</style>
