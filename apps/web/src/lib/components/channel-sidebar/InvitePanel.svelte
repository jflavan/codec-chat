<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let copiedId = $state<string | null>(null);

	function copyCode(code: string, inviteId: string) {
		navigator.clipboard.writeText(code);
		copiedId = inviteId;
		setTimeout(() => {
			if (copiedId === inviteId) copiedId = null;
		}, 2000);
	}

	$effect(() => {
		if (app.showInvitePanel && app.selectedServerId) {
			app.loadInvites();
		}
	});
</script>

<div class="invite-panel">
	<div class="invite-header">
		<h3>Server Invites</h3>
		<button class="close-btn" aria-label="Close invite panel" onclick={() => { app.showInvitePanel = false; }}>
			<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
				<path d="M3.72 3.72a.75.75 0 0 1 1.06 0L8 6.94l3.22-3.22a.75.75 0 1 1 1.06 1.06L9.06 8l3.22 3.22a.75.75 0 1 1-1.06 1.06L8 9.06l-3.22 3.22a.75.75 0 0 1-1.06-1.06L6.94 8 3.72 4.78a.75.75 0 0 1 0-1.06z"/>
			</svg>
		</button>
	</div>

	<div class="invite-actions">
		<button
			class="btn-primary create-invite-btn"
			onclick={() => app.createInvite()}
			disabled={app.isCreatingInvite}
		>
			{app.isCreatingInvite ? 'Creating…' : 'Generate Invite Code'}
		</button>
	</div>

	<div class="invite-list">
		{#if app.isLoadingInvites}
			<p class="muted">Loading invites…</p>
		{:else if app.serverInvites.length === 0}
			<p class="muted">No active invites.</p>
		{:else}
			{#each app.serverInvites as invite (invite.id)}
				<div class="invite-item">
					<div class="invite-code-row">
						<code class="invite-code">{invite.code}</code>
						<button
							class="copy-btn"
							aria-label="Copy invite code"
							onclick={() => copyCode(invite.code, invite.id)}
						>
							{copiedId === invite.id ? '✓' : 'Copy'}
						</button>
						<button
							class="revoke-btn"
							aria-label="Revoke invite"
							onclick={() => app.revokeInvite(invite.id)}
						>
							Revoke
						</button>
					</div>
					<div class="invite-meta">
						<span>Uses: {invite.useCount}{invite.maxUses ? `/${invite.maxUses}` : ''}</span>
						{#if invite.expiresAt}
							<span>Expires: {new Date(invite.expiresAt).toLocaleDateString()}</span>
						{:else}
							<span>Never expires</span>
						{/if}
					</div>
				</div>
			{/each}
		{/if}
	</div>
</div>

<style>
	.invite-panel {
		padding: 8px;
		border-top: 1px solid var(--border);
	}

	.invite-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 4px 8px;
	}

	.invite-header h3 {
		margin: 0;
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.close-btn {
		background: none;
		border: none;
		padding: 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		border-radius: 3px;
		width: 20px;
		height: 20px;
	}

	.close-btn:hover {
		color: var(--text-header);
	}

	.invite-actions {
		padding: 8px;
	}

	.create-invite-btn {
		width: 100%;
	}

	.invite-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
		padding: 0 8px 8px;
		max-height: 200px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.invite-item {
		background: var(--bg-tertiary);
		border-radius: 4px;
		padding: 8px;
	}

	.invite-code-row {
		display: flex;
		align-items: center;
		gap: 6px;
	}

	.invite-code {
		flex: 1;
		font-family: 'JetBrains Mono', monospace;
		font-size: 14px;
		color: var(--accent);
		letter-spacing: 0.05em;
	}

	.copy-btn {
		border: none;
		border-radius: 3px;
		padding: 3px 8px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 600;
		font-size: 11px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
	}

	.copy-btn:hover {
		background: var(--accent-hover);
	}

	.revoke-btn {
		border: none;
		border-radius: 3px;
		padding: 3px 8px;
		background: transparent;
		color: var(--danger);
		font-weight: 600;
		font-size: 11px;
		cursor: pointer;
		font-family: inherit;
		transition: color 150ms ease;
	}

	.revoke-btn:hover {
		color: var(--text-header);
		background: var(--danger);
	}

	.invite-meta {
		display: flex;
		gap: 12px;
		margin-top: 4px;
		font-size: 11px;
		color: var(--text-muted);
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

	.muted {
		color: var(--text-muted);
		font-size: 13px;
		margin: 0;
		padding: 4px 0;
	}
</style>
