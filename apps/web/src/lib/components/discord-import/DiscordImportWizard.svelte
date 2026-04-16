<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { ApiError, type ApiClient } from '$lib/api/client.js';
	import WizardStepDestination from './WizardStepDestination.svelte';
	import WizardStepBotSetup from './WizardStepBotSetup.svelte';
	import WizardStepConnect from './WizardStepConnect.svelte';
	import WizardStepProgress from './WizardStepProgress.svelte';

	let { api }: { api: ApiClient } = $props();

	const auth = getAuthStore();
	const servers = getServerStore();
	const ui = getUIStore();

	let dialogEl: HTMLDialogElement;
	let previousFocus: HTMLElement | null = null;

	let currentStep = $state(1);
	let destinationMode = $state<'create' | 'existing'>(ui.discordWizardMode);
	let newServerName = $state('');
	let selectedServerId = $state<string | null>(ui.discordWizardServerId ?? null);
	let applicationId = $state('');
	let botToken = $state('');
	let guildId = $state('');
	let connectStepValid = $state(false);
	let importServerId = $state<string | null>(null);
	let importError = $state<string | null>(null);
	let isStarting = $state(false);

	$effect(() => {
		if (ui.discordWizardOpen) {
			previousFocus = document.activeElement as HTMLElement | null;
			currentStep = 1;
			destinationMode = ui.discordWizardMode;
			selectedServerId = ui.discordWizardServerId ?? null;
			newServerName = '';
			applicationId = '';
			botToken = '';
			guildId = '';
			connectStepValid = false;
			importServerId = null;
			importError = null;
			isStarting = false;
			dialogEl?.showModal();
		} else {
			dialogEl?.close();
			previousFocus?.focus();
		}
	});

	function close() {
		ui.closeDiscordWizard();
	}

	function handleBackdropClick(e: MouseEvent) {
		if (e.target === dialogEl) close();
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault();
			close();
		}
	}

	const canGoNext = $derived.by(() => {
		if (currentStep === 1) {
			return destinationMode === 'create' ? newServerName.trim().length > 0 : selectedServerId !== null;
		}
		if (currentStep === 2) return true;
		if (currentStep === 3) return connectStepValid;
		return false;
	});

	function handleNext() {
		if (currentStep < 4) {
			currentStep++;
		}
	}

	async function startImport() {
		if (!auth.idToken) return;
		importError = null;
		isStarting = true;

		let serverId = selectedServerId;

		try {
			// Create server if needed
			if (destinationMode === 'create') {
				const result = await api.createServer(auth.idToken, newServerName.trim());
				serverId = result.id;
				await servers.loadServers();
			}

			if (!serverId) return;
			importServerId = serverId;

			await api.startDiscordImport(auth.idToken, serverId, botToken.trim(), guildId.trim());
			currentStep = 4;
		} catch (e) {
			if (e instanceof ApiError) {
				importError = e.message;
			} else if (e instanceof Error) {
				importError = e.message;
			} else {
				importError = 'An unexpected error occurred.';
			}
		} finally {
			isStarting = false;
		}
	}

	// Owned servers for the dropdown
	const ownedServers = $derived(
		servers.servers.filter(s => s.isOwner)
	);
</script>

<dialog
	bind:this={dialogEl}
	class="wizard-dialog"
	aria-labelledby="discord-wizard-title"
	onclick={handleBackdropClick}
	onkeydown={handleKeydown}
>
	<div class="wizard-panel">
		<h2 id="discord-wizard-title" class="visually-hidden">Import from Discord</h2>
		<button class="close-btn" onclick={close} aria-label="Close dialog">
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
				<path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
			</svg>
		</button>

		<!-- Step indicators -->
		{#if currentStep < 4}
			<div class="step-indicators">
				{#each [1, 2, 3] as step}
					<div class="step-dot" class:active={currentStep === step} class:completed={currentStep > step}></div>
				{/each}
			</div>
		{/if}

		<div class="wizard-content">
			{#if currentStep === 1}
				<WizardStepDestination
					bind:mode={destinationMode}
					bind:newServerName={newServerName}
					bind:selectedServerId={selectedServerId}
					{ownedServers}
				/>
			{:else if currentStep === 2}
				<WizardStepBotSetup bind:applicationId={applicationId} />
			{:else if currentStep === 3}
				<WizardStepConnect
					bind:botToken={botToken}
					bind:guildId={guildId}
					bind:isValid={connectStepValid}
				/>
				{#if importError}
					<div class="import-error" role="alert" aria-live="assertive">{importError}</div>
				{/if}
			{:else if currentStep === 4}
				<WizardStepProgress
					serverId={importServerId}
					onGoToServer={() => {
						close();
						if (importServerId) {
							servers.selectedServerId = importServerId;
						}
					}}
				/>
			{/if}
		</div>

		<!-- Navigation -->
		{#if currentStep < 4}
			<div class="wizard-footer">
				{#if currentStep > 1}
					<button class="btn-secondary" onclick={() => currentStep--}>Back</button>
				{:else}
					<div></div>
				{/if}

				{#if currentStep === 3}
					<button class="btn-primary" disabled={!canGoNext || isStarting} onclick={startImport}>
						{isStarting ? 'Starting...' : 'Start Import'}
					</button>
				{:else}
					<button class="btn-primary" disabled={!canGoNext} onclick={handleNext}>
						Next
					</button>
				{/if}
			</div>
		{/if}
	</div>
</dialog>

<style>
	.wizard-dialog {
		position: fixed;
		inset: 0;
		width: 100vw;
		height: 100vh;
		max-width: 100vw;
		max-height: 100vh;
		margin: 0;
		padding: 0;
		border: none;
		background: transparent;
		z-index: 60;
	}

	.wizard-dialog::backdrop {
		background: rgba(0, 0, 0, 0.85);
	}

	.wizard-panel {
		width: 100%;
		max-width: 520px;
		margin: 80px auto;
		background: var(--bg-primary);
		border-radius: 12px;
		padding: 32px;
		position: relative;
		max-height: calc(100vh - 160px);
		overflow-y: auto;
	}

	.close-btn {
		position: absolute;
		top: 12px;
		right: 12px;
		background: none;
		border: none;
		padding: 6px;
		border-radius: 50%;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		min-width: 36px;
		min-height: 36px;
	}

	.close-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	.step-indicators {
		display: flex;
		justify-content: center;
		gap: 8px;
		margin-bottom: 24px;
	}

	.step-dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		background: var(--bg-tertiary);
		transition: background 200ms ease;
	}

	.step-dot.active {
		background: var(--accent);
	}

	.step-dot.completed {
		background: var(--success);
	}

	.wizard-content {
		min-height: 200px;
	}

	.import-error {
		padding: 10px 14px;
		background: rgba(var(--danger-rgb), 0.1);
		color: var(--danger);
		border-radius: 4px;
		font-size: 14px;
		margin-top: 12px;
	}

	.wizard-footer {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-top: 24px;
		padding-top: 16px;
		border-top: 1px solid var(--bg-tertiary);
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

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-primary:hover:not(:disabled) {
		opacity: 0.9;
	}

	.btn-secondary {
		padding: 10px 24px;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-normal);
		background: var(--bg-tertiary);
		border: none;
		border-radius: 4px;
		cursor: pointer;
	}

	.btn-secondary:hover {
		background: var(--bg-message-hover);
	}
</style>
