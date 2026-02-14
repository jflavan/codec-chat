<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let showJoinByCode = $state(false);
	let inviteCode = $state('');

	let createWrapperEl = $state<HTMLElement>();
	let joinWrapperEl = $state<HTMLElement>();

	function handleWindowClick(e: MouseEvent) {
		const target = e.target as Node;
		if (app.showCreateServer && createWrapperEl && !createWrapperEl.contains(target)) {
			app.showCreateServer = false;
			app.newServerName = '';
		}
		if (showJoinByCode && joinWrapperEl && !joinWrapperEl.contains(target)) {
			showJoinByCode = false;
			inviteCode = '';
		}
	}

	async function handleJoinByCode() {
		const code = inviteCode.trim();
		if (!code) return;
		await app.joinViaInvite(code);
		inviteCode = '';
		showJoinByCode = false;
	}
</script>

<svelte:window onclick={handleWindowClick} />

<nav class="server-sidebar" aria-label="Servers">
	<div class="server-list">
		<!-- Home icon -->
		<button
			class="server-icon home-icon"
			class:active={app.showFriendsPanel}
			aria-label="Home"
			title="Home"
			onclick={() => app.goHome()}
		>
			<svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M2.3 7.7l9-5.4a1.2 1.2 0 0 1 1.4 0l9 5.4a.6.6 0 0 1-.3 1.1H19v8.6a.6.6 0 0 1-.6.6h-3.8v-5a1 1 0 0 0-1-1h-3.2a1 1 0 0 0-1 1v5H5.6a.6.6 0 0 1-.6-.6V8.8H2.6a.6.6 0 0 1-.3-1.1z"/>
			</svg>
			{#if app.homeBadgeCount > 0}
				<span class="notification-badge" aria-label="{app.homeBadgeCount} notifications">
					{app.homeBadgeCount}
				</span>
			{/if}
		</button>

		<div class="server-separator" role="separator"></div>

		{#if !app.isSignedIn}
			<p class="muted server-hint">Sign in</p>
		{:else if app.isLoadingServers}
			<p class="muted server-hint">…</p>
		{:else}
			{#each app.servers as server}
				{@const mentionCount = app.serverMentionCount(server.serverId)}
				<div class="server-pill-wrapper">
					<div class="server-pill" class:active={server.serverId === app.selectedServerId}></div>
					<button
						class="server-icon"
						class:active={server.serverId === app.selectedServerId}
						onclick={() => app.selectServer(server.serverId)}
						aria-label="Server: {server.name}"
						title={server.name}
					>
						{server.name.slice(0, 1).toUpperCase()}
						{#if mentionCount > 0}
							<span class="notification-badge" aria-label="{mentionCount} mentions">
								{mentionCount}
							</span>
						{/if}
					</button>
				</div>
			{/each}
		{/if}

	</div>

	{#if app.isSignedIn}
		<div class="server-actions">
			<div class="server-action-wrapper" bind:this={createWrapperEl}>
				<button
					class="server-icon add-server"
					aria-label="Create a server"
					title="Create a server"
					onclick={() => { app.showCreateServer = !app.showCreateServer; }}
				>
					<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
						<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
					</svg>
				</button>
				{#if app.showCreateServer}
					<div class="server-create-popover">
						<form class="inline-form" onsubmit={(e) => { e.preventDefault(); app.createServer(); }}>
							<input
								type="text"
								placeholder="Server name"
								maxlength="100"
								bind:value={app.newServerName}
								disabled={app.isCreatingServer}
							/>
							<div class="inline-form-actions">
								<button type="submit" class="btn-primary" disabled={app.isCreatingServer || !app.newServerName.trim()}>
									{app.isCreatingServer ? '…' : 'Create'}
								</button>
								<button type="button" class="btn-secondary" onclick={() => { app.showCreateServer = false; app.newServerName = ''; }}>Cancel</button>
							</div>
						</form>
					</div>
				{/if}
			</div>

			<div class="server-action-wrapper" bind:this={joinWrapperEl}>
				<button
					class="server-icon join-by-code"
					aria-label="Join with invite code"
					title="Join with invite code"
					onclick={() => { showJoinByCode = !showJoinByCode; }}
				>
					<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
						<path d="M5.5 3A2.5 2.5 0 0 0 3 5.5v1a.5.5 0 0 0 1 0v-1A1.5 1.5 0 0 1 5.5 4h1a.5.5 0 0 0 0-1h-1zm8 0a.5.5 0 0 0 0 1h1A1.5 1.5 0 0 1 16 5.5v1a.5.5 0 0 0 1 0v-1A2.5 2.5 0 0 0 14.5 3h-1zM3.5 13a.5.5 0 0 1 .5.5v1A1.5 1.5 0 0 0 5.5 16h1a.5.5 0 0 1 0 1h-1A2.5 2.5 0 0 1 3 14.5v-1a.5.5 0 0 1 .5-.5zm13 0a.5.5 0 0 1 .5.5v1a2.5 2.5 0 0 1-2.5 2.5h-1a.5.5 0 0 1 0-1h1a1.5 1.5 0 0 0 1.5-1.5v-1a.5.5 0 0 1 .5-.5zM8 10a2 2 0 1 1 4 0 2 2 0 0 1-4 0z"/>
					</svg>
				</button>
				{#if showJoinByCode}
					<div class="server-create-popover">
						<form class="inline-form" onsubmit={(e) => { e.preventDefault(); handleJoinByCode(); }}>
							<input
								type="text"
								placeholder="Invite code"
								maxlength="8"
								bind:value={inviteCode}
								disabled={app.isJoining}
							/>
							<div class="inline-form-actions">
								<button type="submit" class="btn-primary" disabled={app.isJoining || !inviteCode.trim()}>
									{app.isJoining ? '…' : 'Join'}
								</button>
								<button type="button" class="btn-secondary" onclick={() => { showJoinByCode = false; inviteCode = ''; }}>Cancel</button>
							</div>
						</form>
					</div>
				{/if}
			</div>
		</div>
	{/if}
</nav>

<style>
	.server-sidebar {
		background: var(--bg-tertiary);
		display: flex;
		flex-direction: column;
		align-items: center;
		padding: 12px 0 12px;
		box-sizing: border-box;
		z-index: 1;
	}

	.server-list {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		width: 100%;
		flex: 1;
		min-height: 0;
		overflow-y: auto;
		scrollbar-width: none;
	}

	.server-list::-webkit-scrollbar {
		display: none;
	}

	.server-actions {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		width: 100%;
		padding-top: 8px;
	}

	.server-pill-wrapper {
		position: relative;
		display: flex;
		align-items: center;
		justify-content: center;
		width: 100%;
	}

	.server-pill {
		position: absolute;
		left: 0;
		width: 4px;
		border-radius: 0 4px 4px 0;
		background: var(--text-header);
		height: 8px;
		opacity: 0;
		transition: height 150ms ease, opacity 150ms ease;
	}

	.server-pill-wrapper:hover .server-pill:not(.active) {
		opacity: 1;
		height: 20px;
	}

	.server-pill.active {
		opacity: 1;
		height: 36px;
	}

	.server-icon {
		position: relative;
		width: 48px;
		height: 48px;
		border-radius: 50%;
		border: none;
		background: var(--bg-primary);
		color: var(--text-header);
		font-size: 18px;
		font-weight: 600;
		display: grid;
		place-items: center;
		cursor: pointer;
		transition: border-radius 200ms ease, background-color 200ms ease, color 200ms ease;
		font-family: inherit;
	}

	.server-icon:hover,
	.server-icon.active {
		border-radius: 16px;
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.home-icon {
		position: relative;
		background: var(--bg-primary);
		color: var(--text-header);
	}

	.home-icon:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.notification-badge {
		position: absolute;
		bottom: -2px;
		right: -4px;
		min-width: 18px;
		height: 18px;
		padding: 0 5px;
		border-radius: 9px;
		background: var(--danger);
		color: #fff;
		font-size: 11px;
		font-weight: 700;
		line-height: 18px;
		text-align: center;
		pointer-events: none;
		box-shadow: 0 0 0 3px var(--bg-tertiary);
	}

	.add-server {
		background: var(--bg-primary);
		color: var(--success);
	}

	.add-server:hover {
		background: var(--success);
		color: var(--bg-tertiary);
		border-radius: 16px;
	}

	.join-by-code {
		background: var(--bg-primary);
		color: var(--accent);
	}

	.join-by-code:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
		border-radius: 16px;
	}

	.server-separator {
		width: 32px;
		height: 2px;
		background: var(--border);
		border-radius: 1px;
		margin: 4px 0;
	}

	.server-hint {
		font-size: 10px;
		text-align: center;
		margin: 0;
	}

	.server-action-wrapper {
		position: relative;
		display: flex;
		align-items: center;
		justify-content: center;
		width: 100%;
	}

	.server-create-popover {
		position: absolute;
		left: 76px;
		bottom: 0;
		z-index: 40;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		padding: 12px;
		width: 220px;
		box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
	}

	/* Mobile: Reposition popover to be visible on narrow screens */
	@media (max-width: 899px) {
		.server-create-popover {
			position: fixed;
			left: 50%;
			top: 50%;
			transform: translate(-50%, -50%);
			width: calc(100vw - 32px);
			max-width: 320px;
			z-index: 70;
		}
	}

	/* ── shared form styles (scoped) ── */
	.inline-form {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.inline-form input {
		padding: 8px 10px;
		border-radius: 4px;
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		outline: none;
	}

	.inline-form input::placeholder {
		color: var(--text-dim);
	}

	.inline-form input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.inline-form-actions {
		display: flex;
		gap: 6px;
	}

	.btn-primary {
		border: none;
		border-radius: 3px;
		padding: 6px 14px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 600;
		font-size: 13px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		border: none;
		border-radius: 3px;
		padding: 6px 14px;
		background: transparent;
		color: var(--text-muted);
		font-weight: 500;
		font-size: 13px;
		cursor: pointer;
		font-family: inherit;
		transition: color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		color: var(--text-header);
	}

	.muted {
		color: var(--text-muted);
	}
</style>
