<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';

	let {
		serverId,
		onGoToServer
	}: {
		serverId: string | null;
		onGoToServer: () => void;
	} = $props();

	const servers = getServerStore();

	const importStatus = $derived(servers.discordImport);
	const isInProgress = $derived(
		importStatus?.status === 'Pending' || importStatus?.status === 'InProgress'
	);
	const isCompleted = $derived(importStatus?.status === 'Completed');
	const isFailed = $derived(importStatus?.status === 'Failed');

	$effect(() => {
		if (serverId) {
			servers.loadDiscordImport(serverId);
		}
	});
</script>

<div class="step">
	{#if isInProgress}
		<h2>Importing...</h2>
		<p class="subtitle">Your Discord server is being imported. You can close this window — the import will continue in the background.</p>

		{#if importStatus?.stage}
			<p class="stage-label">{importStatus.stage}</p>
		{/if}

		<div class="progress-bar">
			{#if importStatus?.percentComplete != null && importStatus.percentComplete > 0}
				<div class="progress-fill" style="width: {importStatus.percentComplete}%"></div>
			{:else}
				<div class="progress-fill pulse"></div>
			{/if}
		</div>

	{:else if isCompleted}
		<div class="success-icon">
			<svg width="48" height="48" viewBox="0 0 48 48" fill="none">
				<circle cx="24" cy="24" r="24" fill="var(--text-positive, #3ba55d)" opacity="0.15" />
				<path d="M20 25.5l3.5 3.5 5-6" stroke="var(--text-positive, #3ba55d)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" fill="none" />
			</svg>
		</div>
		<h2>Import Complete</h2>

		<div class="stats">
			<div class="stat">
				<span class="stat-value">{importStatus?.importedChannels}</span>
				<span class="stat-label">Channels</span>
			</div>
			<div class="stat">
				<span class="stat-value">{importStatus?.importedMessages}</span>
				<span class="stat-label">Messages</span>
			</div>
			<div class="stat">
				<span class="stat-value">{importStatus?.importedMembers}</span>
				<span class="stat-label">Members</span>
			</div>
		</div>

		<button class="btn-primary go-btn" onclick={onGoToServer}>Go to Server</button>

	{:else if isFailed}
		<h2>Import Failed</h2>
		<p class="error-msg">{importStatus?.errorMessage ?? 'An unknown error occurred.'}</p>

		<div class="stats">
			<div class="stat">
				<span class="stat-value">{importStatus?.importedChannels ?? 0}</span>
				<span class="stat-label">Channels</span>
			</div>
			<div class="stat">
				<span class="stat-value">{importStatus?.importedMessages ?? 0}</span>
				<span class="stat-label">Messages</span>
			</div>
			<div class="stat">
				<span class="stat-value">{importStatus?.importedMembers ?? 0}</span>
				<span class="stat-label">Members</span>
			</div>
		</div>

	{:else}
		<h2>Starting import...</h2>
		<div class="progress-bar">
			<div class="progress-fill pulse"></div>
		</div>
	{/if}
</div>

<style>
	.step {
		text-align: center;
	}

	.step h2 {
		margin: 0 0 8px;
		font-size: 20px;
		color: var(--text-header);
	}

	.subtitle {
		color: var(--text-muted);
		font-size: 14px;
		margin: 0 0 24px;
		line-height: 1.4;
	}

	.success-icon {
		margin-bottom: 16px;
	}

	.stats {
		display: flex;
		justify-content: center;
		gap: 32px;
		margin: 20px 0;
	}

	.stat {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 4px;
	}

	.stat-value {
		font-size: 24px;
		font-weight: 700;
		color: var(--text-header);
	}

	.stat-label {
		font-size: 12px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.stage-label {
		color: var(--text-muted);
		font-size: 13px;
		margin: 0 0 12px;
	}

	.progress-bar {
		height: 6px;
		background: var(--bg-tertiary);
		border-radius: 3px;
		overflow: hidden;
		margin: 16px 0;
	}

	.progress-fill {
		height: 100%;
		background: var(--accent);
		border-radius: 3px;
		width: 40%;
	}

	.progress-fill.pulse {
		animation: pulse 1.5s ease-in-out infinite;
	}

	@keyframes pulse {
		0%, 100% { width: 20%; margin-left: 0; }
		50% { width: 50%; margin-left: 25%; }
	}

	.error-msg {
		padding: 10px 14px;
		background: rgba(237, 66, 69, 0.1);
		color: var(--status-danger);
		border-radius: 4px;
		font-size: 14px;
		text-align: left;
		margin-bottom: 16px;
	}

	.go-btn {
		margin-top: 8px;
	}

	.btn-primary {
		padding: 10px 24px;
		font-size: 14px;
		font-weight: 600;
		color: #fff;
		background: var(--accent);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.btn-primary:hover {
		opacity: 0.9;
	}
</style>
