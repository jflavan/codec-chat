<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';

	const servers = getServerStore();

	let copiedId = $state<string | null>(null);
	let expiresInHours = $state<string>('never');
	let maxUsesInput = $state<string>('');

	$effect(() => {
		if (servers.selectedServerId) {
			servers.loadInvites();
		}
	});

	function getBaseUrl() {
		if (typeof window !== 'undefined') {
			return window.location.origin;
		}
		return '';
	}

	function copyLink(code: string, inviteId: string) {
		const url = `${getBaseUrl()}/invite/${code}`;
		navigator.clipboard.writeText(url);
		copiedId = inviteId;
		setTimeout(() => {
			if (copiedId === inviteId) copiedId = null;
		}, 2000);
	}

	async function handleGenerateInvite() {
		const hours = expiresInHours === 'never' ? null : Number(expiresInHours);
		const uses = maxUsesInput.trim() ? Number(maxUsesInput.trim()) : null;
		await servers.createInvite({ expiresInHours: hours, maxUses: uses });
	}

	function formatExpiry(expiresAt: string | null): string {
		if (!expiresAt) return 'Never';
		const date = new Date(expiresAt);
		const now = new Date();
		const diff = date.getTime() - now.getTime();
		if (diff <= 0) return 'Expired';
		const hours = Math.floor(diff / (1000 * 60 * 60));
		if (hours < 1) {
			const mins = Math.floor(diff / (1000 * 60));
			return `${mins}m`;
		}
		if (hours < 24) return `${hours}h`;
		const days = Math.floor(hours / 24);
		return `${days}d`;
	}
</script>

<div class="server-invites">
	<h1 class="settings-title">Invites</h1>

	<section class="settings-section">
		<h2 class="section-title">Generate Invite</h2>
		<div class="create-form">
			<div class="form-row">
				<div class="form-group">
					<label for="expires-select" class="label">Expires After</label>
					<select id="expires-select" class="select" bind:value={expiresInHours}>
						<option value="1">1 hour</option>
						<option value="6">6 hours</option>
						<option value="12">12 hours</option>
						<option value="24">24 hours</option>
						<option value="168">7 days</option>
						<option value="never">Never</option>
					</select>
				</div>
				<div class="form-group">
					<label for="max-uses-input" class="label">Max Uses</label>
					<input
						id="max-uses-input"
						type="number"
						class="input"
						placeholder="Unlimited"
						min="1"
						bind:value={maxUsesInput}
					/>
				</div>
			</div>
			<button
				type="button"
				class="btn-primary"
				disabled={servers.isCreatingInvite}
				onclick={handleGenerateInvite}
			>
				{servers.isCreatingInvite ? 'Generating…' : 'Generate Invite'}
			</button>
		</div>
	</section>

	<section class="settings-section">
		<h2 class="section-title">Active Invites</h2>
		{#if servers.isLoadingInvites}
			<p class="muted">Loading invites…</p>
		{:else if servers.serverInvites.length === 0}
			<p class="muted">No active invites.</p>
		{:else}
			<div class="invite-table" role="table" aria-label="Active invites">
				<div class="invite-table-header" role="row">
					<span class="col-code" role="columnheader">Code</span>
					<span class="col-creator" role="columnheader">Created by</span>
					<span class="col-uses" role="columnheader">Uses</span>
					<span class="col-expires" role="columnheader">Expires</span>
					<span class="col-actions" role="columnheader"><span class="visually-hidden">Actions</span></span>
				</div>
				{#each servers.serverInvites as invite (invite.id)}
					<div class="invite-row" role="row">
						<span class="col-code" role="cell">
							<code class="invite-code">{invite.code}</code>
						</span>
						<span class="col-creator muted" role="cell">{invite.createdByUserId?.slice(0, 8) ?? 'unknown'}…</span>
						<span class="col-uses muted" role="cell">
							{invite.useCount}{invite.maxUses != null ? `/${invite.maxUses}` : ''}
						</span>
						<span class="col-expires muted" role="cell">{formatExpiry(invite.expiresAt)}</span>
						<span class="col-actions" role="cell">
							<button
								type="button"
								class="btn-copy"
								aria-label="Copy invite link for code {invite.code}"
								onclick={() => copyLink(invite.code, invite.id)}
							>
								{copiedId === invite.id ? 'Copied!' : 'Copy'}
							</button>
							<button
								type="button"
								class="btn-revoke"
								aria-label="Revoke invite {invite.code}"
								onclick={() => servers.revokeInvite(invite.id)}
							>
								Revoke
							</button>
						</span>
					</div>
				{/each}
			</div>
		{/if}
	</section>
</div>

<style>
	.server-invites {
		max-width: 600px;
	}

	.settings-title {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 24px;
	}

	.settings-section {
		margin-bottom: 32px;
		padding-bottom: 32px;
		border-bottom: 1px solid var(--border);
	}

	.settings-section:last-child {
		border-bottom: none;
	}

	.section-title {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 16px;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.create-form {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.form-row {
		display: flex;
		gap: 16px;
	}

	.form-group {
		display: flex;
		flex-direction: column;
		gap: 6px;
		flex: 1;
	}

	.label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.select,
	.input {
		padding: 8px 10px;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		transition: border-color 150ms ease;
		width: 100%;
		box-sizing: border-box;
	}

	.select:focus,
	.input:focus {
		outline: none;
		border-color: var(--accent);
	}

	.btn-primary {
		padding: 8px 16px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
		align-self: flex-start;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.invite-table {
		display: flex;
		flex-direction: column;
		gap: 0;
		border: 1px solid var(--border);
		border-radius: 4px;
		overflow: hidden;
	}

	.invite-table-header,
	.invite-row {
		display: grid;
		grid-template-columns: 1fr 1fr auto auto auto;
		gap: 8px;
		padding: 8px 12px;
		align-items: center;
	}

	.invite-table-header {
		background: var(--bg-tertiary);
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.invite-row {
		background: var(--bg-secondary);
		border-top: 1px solid var(--border);
		font-size: 13px;
	}

	.col-code {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.col-creator,
	.col-uses,
	.col-expires {
		white-space: nowrap;
	}

	.col-actions {
		display: flex;
		gap: 6px;
		align-items: center;
	}

	.invite-code {
		font-family: 'JetBrains Mono', monospace;
		font-size: 13px;
		color: var(--accent);
		letter-spacing: 0.05em;
	}

	.btn-copy {
		padding: 4px 8px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
		white-space: nowrap;
	}

	.btn-copy:hover {
		background: var(--accent-hover);
	}

	.btn-revoke {
		padding: 4px 8px;
		background: transparent;
		color: var(--danger);
		border: 1px solid var(--danger);
		border-radius: 3px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease, color 150ms ease;
		white-space: nowrap;
	}

	.btn-revoke:hover {
		background: var(--danger);
		color: #fff;
	}

	.muted {
		color: var(--text-muted);
	}

	.visually-hidden {
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border: 0;
	}

	@media (max-width: 600px) {
		.form-row {
			flex-direction: column;
		}

		.invite-table-header,
		.invite-row {
			grid-template-columns: 1fr auto auto;
		}

		.col-creator,
		.col-uses {
			display: none;
		}
	}
</style>
