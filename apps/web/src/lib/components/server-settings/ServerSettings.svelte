<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	let serverNameEdit = $state('');
	let isEditingServerName = $state(false);
	let serverDescriptionEdit = $state('');
	let confirmDeleteServer = $state(false);
	let iconFileInput = $state<HTMLInputElement>();

	function startEditingServerName() {
		serverNameEdit = app.selectedServerName;
		isEditingServerName = true;
	}

	function cancelEditingServerName() {
		isEditingServerName = false;
		serverNameEdit = '';
	}

	async function saveServerName() {
		if (!serverNameEdit.trim()) return;
		await app.updateServerName(serverNameEdit);
		isEditingServerName = false;
		serverNameEdit = '';
	}

	async function handleDeleteServer() {
		if (!app.selectedServerId) return;
		await app.deleteServer(app.selectedServerId);
		confirmDeleteServer = false;
	}

	function triggerIconUpload() {
		iconFileInput?.click();
	}

	async function handleIconFileChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		await app.uploadServerIcon(file);
		input.value = '';
	}

	async function handleRemoveIcon() {
		await app.removeServerIcon();
	}

	$effect(() => {
		const server = app.servers.find((s) => s.serverId === app.selectedServerId);
		serverDescriptionEdit = server?.description ?? '';
	});

	async function saveDescription() {
		await app.updateServerDescription(serverDescriptionEdit);
	}
</script>


<div class="server-settings">
	<h1 class="settings-title">Server Settings</h1>

	<!-- Server Overview Section -->
	<section class="settings-section">
		<h2 class="section-title">Server Overview</h2>

		<!-- Server Icon -->
		<div class="form-group">
			<span class="label" id="server-icon-label">Server Icon</span>
			<div class="icon-upload-area" role="group" aria-labelledby="server-icon-label">
				<div class="icon-preview">
					{#if app.selectedServerIconUrl}
						<img
							src={app.selectedServerIconUrl}
							alt="{app.selectedServerName} icon"
							class="icon-image"
						/>
					{:else}
						<span class="icon-placeholder">{app.selectedServerName.slice(0, 1).toUpperCase()}</span>
					{/if}
				</div>
				{#if app.canManageChannels}
					<div class="icon-actions">
						<button
							type="button"
							class="btn-primary"
							disabled={app.isUploadingServerIcon}
							onclick={triggerIconUpload}
						>
							{app.isUploadingServerIcon ? 'Uploading…' : app.selectedServerIconUrl ? 'Change Icon' : 'Upload Icon'}
						</button>
						{#if app.selectedServerIconUrl}
							<button
								type="button"
								class="btn-secondary"
								disabled={app.isUploadingServerIcon}
								onclick={handleRemoveIcon}
							>
								Remove
							</button>
						{/if}
						<input
							bind:this={iconFileInput}
							type="file"
							accept="image/jpeg,image/png,image/webp,image/gif"
							class="hidden-file-input"
							onchange={handleIconFileChange}
						/>
					</div>
				{/if}
			</div>
		</div>
		
		<div class="form-group">
			<label for="server-name" class="label">Server Name</label>
			{#if isEditingServerName}
				<div class="inline-edit">
					<input
						id="server-name"
						type="text"
						class="input"
						bind:value={serverNameEdit}
						maxlength="100"
						disabled={app.isUpdatingServerName}
						onkeydown={(e) => {
							if (e.key === 'Enter') saveServerName();
							if (e.key === 'Escape') cancelEditingServerName();
						}}
					/>
					<div class="form-meta">
						<span class="char-counter" class:warn={serverNameEdit.length >= 100}>
							{serverNameEdit.length}/100
						</span>
					</div>
					<div class="inline-actions">
						<button
							type="button"
							class="btn-primary"
							disabled={app.isUpdatingServerName || !serverNameEdit.trim()}
							onclick={() => saveServerName()}
						>
							{app.isUpdatingServerName ? 'Saving...' : 'Save'}
						</button>
						<button
							type="button"
							class="btn-secondary"
							disabled={app.isUpdatingServerName}
							onclick={cancelEditingServerName}
						>
							Cancel
						</button>
					</div>
				</div>
			{:else}
				<div class="display-field">
					<span class="field-value">{app.selectedServerName}</span>
					{#if app.canManageChannels}
						<button type="button" class="btn-edit" onclick={startEditingServerName}>
							Edit
						</button>
					{/if}
				</div>
			{/if}
		</div>

		{#if app.canManageChannels}
			<div class="form-group">
				<label for="server-description" class="label">Server Description</label>
				<textarea
					id="server-description"
					class="input textarea"
					maxlength="256"
					rows="3"
					placeholder="Describe your server…"
					bind:value={serverDescriptionEdit}
					onblur={saveDescription}
				></textarea>
				<div class="form-meta">
					<span class="char-counter" class:warn={serverDescriptionEdit.length >= 256}>
						{serverDescriptionEdit.length}/256
					</span>
				</div>
			</div>
		{/if}

		{#if app.canDeleteServer}
			<div class="danger-zone">
				{#if confirmDeleteServer}
					<p class="danger-warning">Are you sure? This will permanently delete the server and all its channels, messages, members, and invites.</p>
					<div class="inline-actions">
						<button type="button" class="btn-danger" disabled={app.isDeletingServer} onclick={handleDeleteServer}>
							{app.isDeletingServer ? 'Deleting…' : 'Delete'}
						</button>
						<button type="button" class="btn-secondary" disabled={app.isDeletingServer} onclick={() => (confirmDeleteServer = false)}>Cancel</button>
					</div>
				{:else}
					<button type="button" class="btn-danger" onclick={() => (confirmDeleteServer = true)}>Delete Server</button>
				{/if}
			</div>
		{/if}
	</section>

</div>

<style>
	.server-settings {
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

.icon-upload-area {
		display: flex;
		align-items: center;
		gap: 16px;
	}

	.icon-preview {
		width: 80px;
		height: 80px;
		border-radius: 16px;
		background: var(--bg-secondary);
		display: grid;
		place-items: center;
		overflow: hidden;
		flex-shrink: 0;
	}

	.icon-image {
		width: 100%;
		height: 100%;
		object-fit: cover;
	}

	.icon-placeholder {
		font-size: 32px;
		font-weight: 600;
		color: var(--text-header);
	}

	.icon-actions {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.hidden-file-input {
		display: none;
	}

	.form-group {
		margin-bottom: 16px;
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

	.display-field {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.field-value {
		font-size: 16px;
		color: var(--text-normal);
		flex: 1;
	}

	.input {
		width: 100%;
		padding: 10px 12px;
		background: var(--bg-secondary);
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

	.input:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.textarea {
		resize: vertical;
		min-height: 72px;
		line-height: 1.5;
	}

	.form-meta {
		display: flex;
		justify-content: flex-end;
		margin-top: 4px;
	}

	.char-counter {
		font-size: 11px;
		color: var(--text-muted);
	}

	.char-counter.warn {
		color: var(--danger);
	}

	.inline-edit {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

.inline-actions {
		display: flex;
		gap: 8px;
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

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
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

	.danger-zone {
		margin-top: 20px;
		padding-top: 16px;
		border-top: 1px solid var(--border);
	}

	.danger-warning {
		color: var(--danger);
		font-size: 14px;
		margin-bottom: 10px;
		line-height: 1.4;
	}

	.btn-danger {
		padding: 8px 16px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.btn-danger:hover:not(:disabled) {
		opacity: 0.9;
	}

	.btn-danger:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	@media (max-width: 899px) {
		.inline-actions {
			flex-direction: column;
		}

		.btn-primary,
		.btn-secondary {
			width: 100%;
		}
	}
</style>
