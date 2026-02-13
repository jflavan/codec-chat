<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
	let inputEl: HTMLInputElement;
	let fileInputEl: HTMLInputElement;

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		await app.sendMessage();
		inputEl?.focus();
	}

	function handleFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (file) {
			app.attachImage(file);
		}
		input.value = '';
	}

	function handlePaste(e: ClipboardEvent) {
		const items = e.clipboardData?.items;
		if (!items) return;
		for (const item of items) {
			if (item.type.startsWith('image/')) {
				e.preventDefault();
				const file = item.getAsFile();
				if (file) {
					app.attachImage(file);
				}
				return;
			}
		}
	}
</script>

<form class="composer" onsubmit={handleSubmit}>
	{#if app.pendingImagePreview}
		<div class="image-preview">
			<img src={app.pendingImagePreview} alt="Attachment preview" class="preview-thumb" />
			<button
				type="button"
				class="remove-preview"
				onclick={() => app.clearPendingImage()}
				aria-label="Remove image"
			>
				<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
				</svg>
			</button>
		</div>
	{/if}
	<div class="composer-row">
		<input type="file" accept="image/jpeg,image/png,image/webp,image/gif" class="sr-only" bind:this={fileInputEl} onchange={handleFileSelect} />
		<button
			class="composer-attach"
			type="button"
			onclick={() => fileInputEl?.click()}
			disabled={!app.selectedChannelId || app.isSending}
			aria-label="Attach image"
		>
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
			</svg>
		</button>
		<input
			bind:this={inputEl}
			class="composer-input"
			type="text"
			placeholder={app.selectedChannelName ? `Message #${app.selectedChannelName}` : 'Select a channel…'}
			bind:value={app.messageBody}
			disabled={!app.selectedChannelId || app.isSending}
			oninput={() => app.handleComposerInput()}
			onpaste={handlePaste}
		/>
		<button
			class="composer-send"
			type="submit"
			disabled={!app.selectedChannelId || (!app.messageBody.trim() && !app.pendingImage) || app.isSending}
			aria-label="Send message"
		>
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
			</svg>
		</button>
	</div>
</form>

<style>
	.composer {
		flex-shrink: 0;
		padding: 0 16px 24px;
		display: flex;
		flex-direction: column;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-row {
		display: flex;
		align-items: center;
		gap: 0;
	}

	.composer-attach {
		background: var(--input-bg);
		border: none;
		padding: 12px 10px;
		border-radius: 8px 0 0 8px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-attach:hover:not(:disabled) {
		color: var(--accent);
	}

	.composer-attach:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.composer-input {
		flex: 1;
		padding: 12px 16px;
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

	/* ───── Image preview ───── */

	.image-preview {
		position: relative;
		display: inline-flex;
		margin-bottom: 8px;
		border-radius: 8px;
		overflow: hidden;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		max-width: 200px;
	}

	.preview-thumb {
		max-width: 200px;
		max-height: 150px;
		object-fit: contain;
		display: block;
	}

	.remove-preview {
		position: absolute;
		top: 4px;
		right: 4px;
		width: 22px;
		height: 22px;
		border-radius: 50%;
		border: none;
		background: rgba(0, 0, 0, 0.6);
		color: #fff;
		cursor: pointer;
		display: grid;
		place-items: center;
		padding: 0;
		transition: background-color 150ms ease;
	}

	.remove-preview:hover {
		background: rgba(0, 0, 0, 0.85);
	}

	.sr-only {
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border-width: 0;
	}
</style>
