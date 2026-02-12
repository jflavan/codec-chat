<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
	let inputEl: HTMLInputElement;

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		await app.sendMessage();
		inputEl?.focus();
	}
</script>

<form class="composer" onsubmit={handleSubmit}>
	<input
		bind:this={inputEl}
		class="composer-input"
		type="text"
		placeholder={app.selectedChannelName ? `Message #${app.selectedChannelName}` : 'Select a channel…'}
		bind:value={app.messageBody}
		disabled={!app.selectedChannelId || app.isSending}
		oninput={() => app.handleComposerInput()}
	/>
	<button
		class="composer-send"
		type="submit"
		disabled={!app.selectedChannelId || !app.messageBody.trim() || app.isSending}
		aria-label="Send message"
	>
		<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
			<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
		</svg>
	</button>
</form>

<style>
	.composer {
		flex-shrink: 0;
		padding: 0 16px 24px;
		display: flex;
		align-items: center;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-input {
		flex: 1;
		padding: 12px 16px;
		border-radius: 8px 0 0 8px;
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		outline: none;
		min-height: 20px;
	}

	.composer-input::placeholder {
		color: var(--text-dim);
	}

	.composer-input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.composer-input:disabled {
		opacity: 0.5;
	}

	.composer-send {
		background: var(--input-bg);
		border: none;
		padding: 12px 12px;
		border-radius: 0 8px 8px 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-send:hover:not(:disabled) {
		color: var(--accent);
	}

	.composer-send:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}
</style>
