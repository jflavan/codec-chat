<script lang="ts">
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import ServerSettingsSidebar from './ServerSettingsSidebar.svelte';
	import ServerSettings from './ServerSettings.svelte';
	import ServerEmojis from './ServerEmojis.svelte';
	import ServerMembers from './ServerMembers.svelte';
	import ServerRoles from './ServerRoles.svelte';
	import ServerChannels from './ServerChannels.svelte';
	import ServerInvites from './ServerInvites.svelte';
	import ServerWebhooks from './ServerWebhooks.svelte';
	import ServerAuditLog from './ServerAuditLog.svelte';
	import ServerBans from './ServerBans.svelte';

	const ui = getUIStore();

	let dialogEl: HTMLDialogElement;
	let previousFocus: HTMLElement | null = null;

	$effect(() => {
		if (ui.serverSettingsOpen) {
			previousFocus = document.activeElement as HTMLElement | null;
			dialogEl?.showModal();
		} else {
			dialogEl?.close();
			previousFocus?.focus();
		}
	});

	function handleBackdropClick(e: MouseEvent) {
		if (e.target === dialogEl) {
			ui.closeServerSettings();
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			e.preventDefault();
			ui.closeServerSettings();
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
		<div class="settings-sidebar-col">
			<ServerSettingsSidebar />
		</div>
		<div class="settings-content-col">
			<button
				class="close-btn"
				onclick={() => ui.closeServerSettings()}
				aria-label="Close settings"
				title="Close"
			>
				<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/>
				</svg>
			</button>
			{#if ui.serverSettingsCategory === 'channels'}
				<ServerChannels />
			{:else if ui.serverSettingsCategory === 'invites'}
				<ServerInvites />
			{:else if ui.serverSettingsCategory === 'webhooks'}
				<ServerWebhooks />
			{:else if ui.serverSettingsCategory === 'emojis'}
				<ServerEmojis />
			{:else if ui.serverSettingsCategory === 'roles'}
				<ServerRoles />
			{:else if ui.serverSettingsCategory === 'members'}
				<ServerMembers />
			{:else if ui.serverSettingsCategory === 'bans'}
				<ServerBans />
			{:else if ui.serverSettingsCategory === 'audit-log'}
				<ServerAuditLog />
			{:else}
				<ServerSettings />
			{/if}
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
		max-width: 940px;
		margin: 0 auto;
		position: relative;
	}

	.settings-sidebar-col {
		width: 200px;
		flex-shrink: 0;
		background: var(--bg-tertiary);
		overflow-y: auto;
		padding: 48px 8px 16px;
	}

	.settings-content-col {
		flex: 1;
		min-width: 0;
		max-width: 740px;
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
		min-width: 44px;
		min-height: 44px;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.close-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	@media (max-width: 899px) {
		.settings-panel {
			flex-direction: column;
		}

		.settings-sidebar-col {
			width: 100%;
			padding: 12px 8px 0;
			overflow-y: visible;
		}

		.settings-content-col {
			max-width: 100%;
			padding: 24px 16px calc(32px + env(safe-area-inset-bottom, 0));
		}

		.close-btn {
			top: 8px;
			right: 8px;
		}
	}
</style>
