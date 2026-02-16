<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import ServerSettings from './ServerSettings.svelte';

	const app = getAppState();

	let dialogEl: HTMLDialogElement;
	let previousFocus: HTMLElement | null = null;

	$effect(() => {
		if (app.serverSettingsOpen) {
			previousFocus = document.activeElement as HTMLElement | null;
			dialogEl?.showModal();
		} else {
			dialogEl?.close();
			previousFocus?.focus();
		}
	});

	function handleBackdropClick(e: MouseEvent) {
		if (e.target === dialogEl) {
			app.closeServerSettings();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault();
			app.closeServerSettings();
		}
	}
</script>

<dialog
	bind:this={dialogEl}
	class="settings-dialog"
	aria-label="Server Settings"
	onclick={handleBackdropClick}
	onkeydown={handleKeydown}
>
	<div class="settings-panel" role="document">
		<div class="settings-content">
			<button
				class="close-btn"
				onclick={() => app.closeServerSettings()}
				aria-label="Close settings"
				title="Close"
			>
				<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/>
				</svg>
			</button>
			<ServerSettings />
		</div>
	</div>
</dialog>

<style>
	.settings-dialog {
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
		z-index: 50;
	}

	.settings-dialog::backdrop {
		background: rgba(0, 0, 0, 0.85);
	}

	.settings-panel {
		display: flex;
		width: 100%;
		height: 100%;
		max-width: 740px;
		margin: 0 auto;
		position: relative;
	}

	.settings-content {
		flex: 1;
		min-width: 0;
		background: var(--bg-primary);
		overflow-y: auto;
		padding: 48px 32px 32px;
		position: relative;
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
		transition: color 150ms ease, background-color 150ms ease;
	}

	.close-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	/* Responsive: adjust padding on small screens */
	@media (max-width: 899px) {
		.settings-content {
			padding: 24px 16px 32px;
		}
	}
</style>
