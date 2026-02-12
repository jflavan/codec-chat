<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
</script>

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
			{#if app.incomingRequests.length > 0}
				<span class="notification-badge" aria-label="{app.incomingRequests.length} pending friend requests">
					{app.incomingRequests.length}
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
					</button>
				</div>
			{/each}
		{/if}

		{#if app.isSignedIn}
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
			{:else}
				<button
					class="server-icon add-server"
					aria-label="Create a server"
					title="Create a server"
					onclick={() => { app.showCreateServer = true; }}
				>
					<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
						<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
					</svg>
				</button>
			{/if}
		{/if}

		{#if app.isSignedIn && app.discoverServers.some((s) => !s.isMember)}
			<div class="server-separator" role="separator"></div>
			{#if app.isLoadingDiscover}
				<p class="muted server-hint">…</p>
			{:else}
				{#each app.discoverServers as server}
					{#if !server.isMember}
						<button
							class="server-icon discover-icon"
							onclick={() => app.joinServer(server.id)}
							disabled={app.isJoining}
							aria-label="Join {server.name}"
							title="Join {server.name}"
						>
							{server.name.slice(0, 1).toUpperCase()}
						</button>
					{/if}
				{/each}
			{/if}
		{/if}
	</div>
</nav>

<style>
	.server-sidebar {
		background: var(--bg-tertiary);
		display: flex;
		flex-direction: column;
		align-items: center;
		padding: 12px 0 12px;
		overflow-y: auto;
		scrollbar-width: none;
	}

	.server-sidebar::-webkit-scrollbar {
		display: none;
	}

	.server-list {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		width: 100%;
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

	.discover-icon {
		background: var(--bg-primary);
		color: var(--success);
		border: 2px dashed var(--border);
		width: 44px;
		height: 44px;
	}

	.discover-icon:hover {
		border-color: var(--success);
		background: var(--success);
		color: var(--bg-tertiary);
		border-radius: 16px;
	}

	.discover-icon:disabled {
		opacity: 0.5;
		cursor: not-allowed;
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

	.server-create-popover {
		position: absolute;
		left: 76px;
		top: 50%;
		transform: translateY(-50%);
		z-index: 40;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		padding: 12px;
		width: 220px;
		box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
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
