<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';

	const servers = getServerStore();
	const ui = getUIStore();

	let botToken = $state('');
	let guildId = $state('');

	const serverId = $derived(servers.selectedServerId);
	const importStatus = $derived(servers.discordImport);
	const isInProgress = $derived(
		importStatus?.status === 'Pending' || importStatus?.status === 'InProgress'
	);
	const isRehostingMedia = $derived(importStatus?.status === 'RehostingMedia');
	const isCompleted = $derived(importStatus?.status === 'Completed');
	const isFailed = $derived(importStatus?.status === 'Failed');
	const mappings = $derived(servers.discordUserMappings);

	const isStaleRehost = $derived.by(() => {
		if (importStatus?.status !== 'RehostingMedia') return false;
		const rehostStarted = importStatus.lastSyncedAt ?? importStatus.startedAt;
		if (!rehostStarted) return false;
		return new Date(rehostStarted).getTime() < Date.now() - 30 * 60 * 1000;
	});

	$effect(() => {
		if (serverId) {
			servers.loadDiscordImport(serverId);
		}
	});

	$effect(() => {
		if (isCompleted && serverId) {
			servers.loadDiscordUserMappings(serverId);
		}
	});

	async function handleStart() {
		if (!serverId || !botToken.trim() || !guildId.trim()) return;
		await servers.startDiscordImport(serverId, botToken.trim(), guildId.trim());
		botToken = '';
	}

	async function handleResync() {
		if (!serverId || !botToken.trim()) return;
		const resolvedGuildId = importStatus?.discordGuildId ?? guildId.trim();
		if (!resolvedGuildId) return;
		await servers.resyncDiscordImport(serverId, botToken.trim(), resolvedGuildId);
		botToken = '';
	}

	async function handleCancel() {
		if (!serverId) return;
		await servers.cancelDiscordImport(serverId);
	}

	async function handleClaim(discordUserId: string) {
		if (!serverId) return;
		await servers.claimDiscordIdentity(serverId, discordUserId);
	}
</script>

<div class="discord-import">
	<h2>Import from Discord</h2>
	<p class="description">
		Import channels, messages, roles, emojis, and members from a Discord server using a bot token.
	</p>

	{#if servers.isLoadingImport}
		<p class="loading">Loading import status...</p>
	{:else if isInProgress}
		<div class="status-card in-progress">
			<h3>Import in Progress</h3>
			{#if importStatus?.stage}
				<p class="stage-label">{importStatus.stage}</p>
			{/if}
			<div
				class="progress-bar"
				role="progressbar"
				aria-label="Import progress"
				aria-valuenow={importStatus?.percentComplete ?? 0}
				aria-valuemin={0}
				aria-valuemax={100}
			>
				{#if importStatus?.percentComplete != null && importStatus.percentComplete > 0}
					<div class="progress-fill" style="width: {importStatus.percentComplete}%"></div>
				{:else}
					<div class="progress-fill pulse"></div>
				{/if}
			</div>
			<button class="cancel-btn" onclick={handleCancel}>Cancel Import</button>
		</div>
	{:else if isRehostingMedia}
		<div class="import-status">
			<h3>Import Complete — Optimizing Media</h3>
			{#if isStaleRehost}
				<p class="stale-warning" role="alert">Media optimization may have stalled. You can re-sync to retry, or cancel the import.</p>
			{:else}
				<p>Messages have been imported. Images are being re-hosted in the background.</p>
			{/if}

			{#if importStatus?.stage}
				<p class="stage-label">{importStatus.stage}</p>
			{/if}

			{#if !isStaleRehost}
				<div
					class="progress-bar"
					role="progressbar"
					aria-label="Media rehosting progress"
					aria-valuenow={importStatus?.percentComplete ?? 0}
					aria-valuemin={0}
					aria-valuemax={100}
				>
					{#if importStatus?.percentComplete != null && importStatus.percentComplete > 0}
						<div class="progress-fill" style="width: {importStatus.percentComplete}%"></div>
					{:else}
						<div class="progress-fill pulse"></div>
					{/if}
				</div>
			{/if}

			<div class="import-stats">
				<span>{importStatus?.importedChannels ?? 0} channels</span>
				<span>{importStatus?.importedMessages ?? 0} messages</span>
				<span>{importStatus?.importedMembers ?? 0} members</span>
			</div>
		</div>

		{#if isStaleRehost}
			<div class="import-form">
				<h3>Re-sync</h3>
				<p class="description">The previous import's media optimization was interrupted. Provide a bot token to retry.</p>
				<label class="form-label">
					Bot Token
					<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
				</label>
				<div class="button-row">
					<button
						class="start-btn"
						disabled={servers.isStartingImport || !botToken.trim()}
						onclick={handleResync}
					>
						{servers.isStartingImport ? 'Starting...' : 'Re-sync'}
					</button>
					<button class="cancel-btn" onclick={handleCancel}>Cancel Import</button>
				</div>
			</div>
		{/if}
	{:else if isFailed}
		<div class="status-card failed">
			<h3>Import Failed</h3>
			<p class="error-msg">{importStatus?.errorMessage}</p>
			<p class="partial-stats">
				Imported so far: {importStatus?.importedChannels ?? 0} channels,
				{importStatus?.importedMessages ?? 0} messages,
				{importStatus?.importedMembers ?? 0} members
			</p>
		</div>

		{#if importStatus?.importedMessages}
			<div class="import-form">
				<h3>Re-sync</h3>
				<p class="description">The previous import partially succeeded. Re-sync to retry failed steps and pull in any new messages.</p>
				<label class="form-label">
					Bot Token
					<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
				</label>
				<button
					class="start-btn"
					disabled={servers.isStartingImport || !botToken.trim()}
					onclick={handleResync}
				>
					{servers.isStartingImport ? 'Starting...' : 'Re-sync'}
				</button>
			</div>
		{:else}
			<div class="import-form">
				<h3>Retry Import</h3>
				<label class="form-label">
					Bot Token
					<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
				</label>
				<label class="form-label">
					Discord Guild ID
					<input type="text" bind:value={guildId} placeholder="e.g. 123456789012345678" class="form-input" />
				</label>
				<button
					class="start-btn"
					disabled={servers.isStartingImport || !botToken.trim() || !guildId.trim()}
					onclick={handleStart}
				>
					{servers.isStartingImport ? 'Starting...' : 'Retry Import'}
				</button>
			</div>
		{/if}
	{:else if isCompleted}
		<div class="status-card completed">
			<h3>Import Complete</h3>
			<div class="progress-stats">
				<span>{importStatus?.importedChannels} channels</span>
				<span>{importStatus?.importedMessages} messages</span>
				<span>{importStatus?.importedMembers} members</span>
			</div>
			<p class="completed-at">Completed {importStatus?.completedAt ? new Date(importStatus.completedAt).toLocaleString() : ''}</p>
		</div>

		<div class="import-form">
			<h3>Re-sync</h3>
			<p class="description">Pull in new messages since the last import. Requires a fresh bot token.</p>
			<label class="form-label">
				Bot Token
				<input type="password" bind:value={botToken} placeholder="Paste your Discord bot token" class="form-input" />
			</label>
			<button
				class="start-btn"
				disabled={servers.isStartingImport || !botToken.trim()}
				onclick={handleResync}
			>
				{servers.isStartingImport ? 'Starting...' : 'Re-sync'}
			</button>
		</div>

		{#if mappings.length > 0}
			<div class="claim-section">
				<h3>Claim Discord Identity</h3>
				<p class="description">Members can claim their Discord identity to reassign their imported messages.</p>
				<ul class="mapping-list">
					{#each mappings as mapping (mapping.discordUserId)}
						<li class="mapping-item">
							<div class="mapping-user">
								{#if mapping.discordAvatarUrl}
									<img class="mapping-avatar" src={mapping.discordAvatarUrl} alt="{mapping.discordUsername}'s avatar" />
								{:else}
									<div class="mapping-avatar-placeholder">
										{mapping.discordUsername.slice(0, 1).toUpperCase()}
									</div>
								{/if}
								<span class="mapping-name">{mapping.discordUsername}</span>
							</div>
							{#if mapping.codecUserId}
								<span class="claimed-badge">Claimed</span>
							{:else}
								<button class="claim-btn" onclick={() => handleClaim(mapping.discordUserId)}>
									This is me
								</button>
							{/if}
						</li>
					{/each}
				</ul>
			</div>
		{/if}
	{:else}
		<div class="no-import">
			<p class="description">
				No import has been started for this server. Use the wizard to set up a Discord bot and import channels, messages, roles, emojis, and members.
			</p>
			<button
				class="start-btn"
				onclick={() => { if (serverId) ui.openDiscordWizard('existing', serverId); }}
			>
				Import from Discord
			</button>
		</div>
	{/if}
</div>

<style>
	.discord-import {
		max-width: 600px;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 20px;
		color: var(--text-header);
	}

	h3 {
		margin: 0 0 8px;
		font-size: 16px;
		color: var(--text-header);
	}

	.description {
		color: var(--text-muted);
		font-size: 14px;
		margin: 0 0 20px;
		line-height: 1.4;
	}

	.loading {
		color: var(--text-muted);
		font-size: 14px;
	}

	.status-card {
		padding: 16px;
		border-radius: 8px;
		margin-bottom: 24px;
	}

	.status-card.in-progress { background: var(--bg-secondary); }
	.status-card.completed { background: var(--bg-secondary); }
	.status-card.failed { background: rgba(var(--danger-rgb), 0.1); }

	.progress-stats {
		display: flex;
		gap: 16px;
		font-size: 14px;
		color: var(--text-normal);
		margin: 8px 0 12px;
	}

	.progress-bar {
		height: 8px;
		background: var(--bg-tertiary);
		border-radius: 4px;
		overflow: hidden;
		margin-bottom: 12px;
	}

	.progress-fill {
		height: 100%;
		background: var(--accent);
		border-radius: 4px;
		transition: width 300ms ease;
	}

	.progress-fill.pulse {
		animation: pulse 1.5s ease-in-out infinite;
	}

	@keyframes pulse {
		0%, 100% { width: 20%; margin-left: 0; }
		50% { width: 50%; margin-left: 25%; }
	}

	.stage-label {
		color: var(--text-muted);
		font-size: 13px;
		margin: 4px 0 8px;
	}

	.error-msg {
		color: var(--danger);
		font-size: 14px;
		margin: 8px 0;
	}

	.partial-stats {
		font-size: 13px;
		color: var(--text-muted);
		margin: 4px 0 0;
	}

	.completed-at {
		font-size: 13px;
		color: var(--text-muted);
		margin: 8px 0 0;
	}

	.import-form {
		margin-bottom: 24px;
	}

	.form-label {
		display: block;
		font-size: 13px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		margin-bottom: 12px;
	}

	.form-input {
		display: block;
		width: 100%;
		margin-top: 6px;
		padding: 10px 12px;
		font-size: 14px;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: 1px solid var(--bg-tertiary);
		border-radius: 4px;
		outline: none;
		box-sizing: border-box;
	}

	.form-input:focus {
		border-color: var(--accent);
	}

	.start-btn {
		padding: 10px 24px;
		font-size: 14px;
		font-weight: 600;
		color: #fff;
		background: var(--accent);
		border: none;
		border-radius: 4px;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.start-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.start-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.import-status {
		padding: 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
		margin-bottom: 24px;
		color: var(--text-normal);
	}

	.import-stats {
		display: flex;
		gap: 16px;
		font-size: 14px;
		color: var(--text-normal);
		margin: 8px 0 0;
	}

	.cancel-btn {
		padding: 8px 16px;
		font-size: 13px;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.cancel-btn:hover {
		background: var(--bg-message-hover);
	}

	.stale-warning {
		color: var(--warning, #faa61a);
		font-size: 14px;
		margin: 4px 0 12px;
		line-height: 1.4;
	}

	.button-row {
		display: flex;
		gap: 12px;
		align-items: center;
	}

	.no-import {
		text-align: center;
		padding: 32px 0;
	}

	.no-import .description {
		margin-bottom: 16px;
	}

	.claim-section {
		margin-top: 24px;
	}

	.mapping-list {
		list-style: none;
		padding: 0;
		margin: 0;
	}

	.mapping-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.mapping-item:hover {
		background: var(--bg-message-hover);
	}

	.mapping-user {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.mapping-avatar, .mapping-avatar-placeholder {
		width: 32px;
		height: 32px;
		border-radius: 50%;
	}

	.mapping-avatar-placeholder {
		display: grid;
		place-items: center;
		background: var(--accent);
		color: #fff;
		font-size: 14px;
		font-weight: 600;
	}

	.mapping-name {
		font-size: 14px;
		color: var(--text-normal);
	}

	.claimed-badge {
		font-size: 12px;
		color: var(--success);
		font-weight: 600;
	}

	.claim-btn {
		padding: 6px 12px;
		font-size: 13px;
		color: #fff;
		background: var(--accent);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.claim-btn:hover {
		opacity: 0.9;
	}
</style>
