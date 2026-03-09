<script lang="ts">
	import { onDestroy } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const MAX_EMOJIS = 50;

	// Upload form state
	let emojiName = $state('');
	let selectedFile = $state<File | null>(null);
	let filePreviewUrl = $state('');
	let fileInputEl = $state<HTMLInputElement>();
	let fileInputKey = $state(0);

	// Rename state
	let renamingId = $state('');
	let renameValue = $state('');

	// Delete confirmation state
	let deletingId = $state('');

	// Inline upload error state
	let uploadError = $state('');
	let errorTimeout: ReturnType<typeof setTimeout> | undefined;
	onDestroy(() => clearTimeout(errorTimeout));

	const nameValid = $derived(/^[a-zA-Z0-9_]{2,32}$/.test(emojiName));
	const canUpload = $derived(
		nameValid && selectedFile !== null && !app.isUploadingEmoji && app.customEmojis.length < MAX_EMOJIS
	);

	const renameValid = $derived(/^[a-zA-Z0-9_]{2,32}$/.test(renameValue));

	function handleFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		if (filePreviewUrl) URL.revokeObjectURL(filePreviewUrl);
		selectedFile = file;
		filePreviewUrl = URL.createObjectURL(file);
	}

	async function handleUpload() {
		if (!canUpload || !selectedFile) return;
		uploadError = '';
		clearTimeout(errorTimeout);
		try {
			await app.uploadCustomEmoji(emojiName, selectedFile);
			// Reset form only on success
			emojiName = '';
			selectedFile = null;
			if (filePreviewUrl) URL.revokeObjectURL(filePreviewUrl);
			filePreviewUrl = '';
			fileInputKey++;
		} catch (e) {
			uploadError = e instanceof Error ? e.message : 'Upload failed. Please try again.';
			errorTimeout = setTimeout(() => { uploadError = ''; }, 5000);
		}
	}

	function startRename(id: string, currentName: string) {
		renamingId = id;
		renameValue = currentName;
	}

	async function saveRename() {
		if (!renamingId || !renameValid) return;
		await app.renameCustomEmoji(renamingId, renameValue.trim());
		renamingId = '';
		renameValue = '';
	}

	function cancelRename() {
		renamingId = '';
		renameValue = '';
	}

	async function confirmDelete() {
		if (!deletingId) return;
		await app.deleteCustomEmoji(deletingId);
		deletingId = '';
	}

	function handleRenameKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter') {
			e.preventDefault();
			saveRename();
		} else if (e.key === 'Escape') {
			e.preventDefault();
			cancelRename();
		}
	}
</script>

<section class="settings-section">
	<h2 class="section-title">Custom Emojis</h2>
	<p class="section-desc">{app.customEmojis.length} / {MAX_EMOJIS} emoji slots used</p>

	<!-- Upload form -->
	<div class="upload-form">
		<div class="upload-row">
			<div class="form-group">
				<label for="emoji-name" class="label">Name</label>
				<input
					id="emoji-name"
					type="text"
					class="input"
					bind:value={emojiName}
					placeholder="emoji_name"
					maxlength="32"
				/>
				<span class="form-hint">2-32 characters, letters, numbers, underscores</span>
			</div>
			<div class="form-group">
				<span class="label" id="emoji-image-label">Image</span>
				<div class="file-select-area" role="group" aria-labelledby="emoji-image-label">
					{#key fileInputKey}
						<input
							bind:this={fileInputEl}
							type="file"
							accept="image/png,image/jpeg,image/webp,image/gif"
							class="hidden-file-input"
							onchange={handleFileSelect}
						/>
					{/key}
					<button type="button" class="btn-secondary" onclick={() => fileInputEl?.click()}>
						{selectedFile ? selectedFile.name : 'Choose File'}
					</button>
					{#if filePreviewUrl}
						<img src={filePreviewUrl} alt="Preview" class="upload-preview" />
					{/if}
				</div>
				<span class="form-hint">PNG, JPEG, WebP, or GIF. Max 512KB.</span>
			</div>
		</div>
		<button
			type="button"
			class="btn-primary"
			disabled={!canUpload}
			onclick={handleUpload}
		>
			{app.isUploadingEmoji ? 'Uploading...' : 'Upload Emoji'}
		</button>
		{#if uploadError}
			<p class="upload-error" role="alert">{uploadError}</p>
		{/if}
	</div>

	<!-- Emoji list -->
	{#if app.customEmojis.length > 0}
		<div class="emoji-list">
			{#each app.customEmojis as emoji (emoji.id)}
				<div class="emoji-row">
					<img src={emoji.imageUrl} alt={emoji.name} width="32" height="32" />
					{#if renamingId === emoji.id}
						<input
							class="input rename-input"
							bind:value={renameValue}
							onkeydown={handleRenameKeydown}
							maxlength="32"
						/>
						<div class="inline-actions">
							<button type="button" class="btn-primary" disabled={!renameValid} onclick={saveRename}>Save</button>
							<button type="button" class="btn-secondary" onclick={cancelRename}>Cancel</button>
						</div>
					{:else}
						<span class="emoji-name">:{emoji.name}:</span>
						<div class="inline-actions">
							<button
								type="button"
								class="btn-edit"
								onclick={() => startRename(emoji.id, emoji.name)}
							>
								Rename
							</button>
							<button
								type="button"
								class="btn-danger-sm"
								onclick={() => (deletingId = emoji.id)}
							>
								Delete
							</button>
						</div>
					{/if}
				</div>

				<!-- Delete confirmation -->
				{#if deletingId === emoji.id}
					<div class="delete-confirm">
						<span class="danger-warning-inline">Delete :{emoji.name}:?</span>
						<div class="inline-actions">
							<button type="button" class="btn-danger-sm" onclick={confirmDelete}>Yes, Delete</button>
							<button type="button" class="btn-secondary-sm" onclick={() => (deletingId = '')}>Cancel</button>
						</div>
					</div>
				{/if}
			{/each}
		</div>
	{:else}
		<p class="empty-state">No custom emojis yet. Upload one to get started!</p>
	{/if}
</section>

<style>
	.settings-section {
		margin-bottom: 32px;
	}

	.section-title {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 16px;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.section-desc {
		color: var(--text-muted);
		font-size: 14px;
		margin: 4px 0 16px;
	}

	.upload-form {
		padding: 16px;
		border: 1px solid var(--border);
		border-radius: 8px;
		background: var(--bg-secondary);
		margin-bottom: 24px;
	}

	.upload-row {
		display: flex;
		gap: 16px;
		margin-bottom: 12px;
	}

	.upload-row .form-group {
		flex: 1;
	}

	.form-group {
		margin-bottom: 12px;
	}

	.label {
		display: block;
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
		margin-bottom: 8px;
	}

	.input {
		width: 100%;
		box-sizing: border-box;
		padding: 10px 12px;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		transition: border-color 150ms ease;
	}

	.input:focus {
		outline: none;
		border-color: var(--accent);
	}

	.form-hint {
		display: block;
		font-size: 12px;
		color: var(--text-dim);
		margin-top: 4px;
	}

	.file-select-area {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.hidden-file-input {
		display: none;
	}

	.upload-preview {
		width: 32px;
		height: 32px;
		object-fit: contain;
		border-radius: 4px;
	}

	.upload-error {
		color: var(--danger);
		font-size: 13px;
		margin: 8px 0 0;
	}

	.emoji-list {
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.emoji-row {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 8px 12px;
		border-radius: 6px;
		background: var(--bg-secondary);
	}

	.emoji-row img {
		width: 32px;
		height: 32px;
		object-fit: contain;
		flex-shrink: 0;
	}

	.emoji-name {
		color: var(--text-normal);
		font-size: 14px;
		flex: 1;
	}

	.rename-input {
		flex: 1;
	}

	.inline-actions {
		display: flex;
		gap: 8px;
		flex-shrink: 0;
	}

	.delete-confirm {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 8px 12px 8px 56px;
	}

	.empty-state {
		color: var(--text-muted);
		font-size: 14px;
		text-align: center;
		padding: 24px;
		background: var(--bg-secondary);
		border-radius: 8px;
	}

	/* Buttons - matching ServerSettings patterns */
	.btn-primary {
		padding: 8px 16px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
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
		padding: 8px 16px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		color: var(--text-header);
	}

	.btn-edit {
		padding: 6px 12px;
		background: none;
		color: var(--accent);
		border: 1px solid var(--accent);
		border-radius: 3px;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.btn-edit:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.btn-danger-sm {
		padding: 4px 10px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 12px;
		font-weight: 500;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.btn-danger-sm:hover:not(:disabled) {
		opacity: 0.9;
	}

	.btn-secondary-sm {
		padding: 4px 10px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 12px;
		font-weight: 500;
		cursor: pointer;
		transition: color 150ms ease;
	}

	.btn-secondary-sm:hover:not(:disabled) {
		color: var(--text-header);
	}

	.danger-warning-inline {
		color: var(--danger);
		font-size: 12px;
		white-space: nowrap;
	}

	@media (max-width: 899px) {
		.upload-row {
			flex-direction: column;
			gap: 0;
		}
	}
</style>
