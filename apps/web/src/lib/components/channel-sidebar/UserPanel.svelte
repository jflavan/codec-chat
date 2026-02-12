<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	/** Accepted image MIME types for avatar uploads. */
	const ACCEPTED_TYPES = 'image/jpeg,image/png,image/webp,image/gif';

	let fileInput: HTMLInputElement;

	function openFilePicker() {
		fileInput?.click();
	}

	async function handleFileChange() {
		const file = fileInput?.files?.[0];
		if (!file) return;
		await app.uploadAvatar(file);
		// Reset so the same file can be re-selected.
		if (fileInput) fileInput.value = '';
	}
</script>

<div class="user-panel">
	{#if app.me}
		<input
			bind:this={fileInput}
			type="file"
			accept={ACCEPTED_TYPES}
			class="sr-only"
			onchange={handleFileChange}
			aria-label="Upload avatar image"
		/>
		<div class="user-panel-info">
			<button
				class="avatar-upload-btn"
				onclick={openFilePicker}
				disabled={app.isUploadingAvatar}
				title="Click to upload a new avatar"
				aria-label="Upload avatar"
			>
				{#if app.isUploadingAvatar}
					<div class="user-panel-avatar placeholder" aria-hidden="true">…</div>
				{:else if app.me.user.avatarUrl}
					<img class="user-panel-avatar" src={app.me.user.avatarUrl} alt="Your avatar" />
				{:else}
					<div class="user-panel-avatar placeholder" aria-hidden="true">
						{app.me.user.displayName.slice(0, 1).toUpperCase()}
					</div>
				{/if}
				<div class="avatar-upload-overlay" aria-hidden="true">
					<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
						<path d="M12 4a1 1 0 0 1 1 1v6h6a1 1 0 1 1 0 2h-6v6a1 1 0 1 1-2 0v-6H5a1 1 0 1 1 0-2h6V5a1 1 0 0 1 1-1z" />
					</svg>
				</div>
			</button>
			<div class="user-panel-names">
				<span class="user-panel-display">{app.me.user.displayName}</span>
				{#if app.currentServerRole}
					<span class="user-panel-role">{app.currentServerRole}</span>
				{/if}
			</div>
		</div>
		<button class="sign-out-btn" onclick={() => app.signOut()} aria-label="Sign out" title="Sign out">
			<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M5 5h7V3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h7v-2H5V5zm16 7-4-4v3H9v2h8v3l4-4z"/>
			</svg>
		</button>
	{:else}
		<div id="google-button" class="google-button"></div>
		<span class="user-panel-status">{app.status}</span>
	{/if}
</div>

<style>
	.user-panel {
		flex-shrink: 0;
		padding: 8px;
		background: var(--bg-tertiary);
		border-top: 1px solid var(--border);
		display: flex;
		align-items: center;
		gap: 8px;
		min-height: 52px;
	}

	.user-panel-info {
		display: flex;
		align-items: center;
		gap: 8px;
		overflow: hidden;
		flex: 1;
		min-width: 0;
	}

	.sign-out-btn {
		background: none;
		border: none;
		padding: 4px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.sign-out-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	/* ───── Avatar upload button ───── */

	.avatar-upload-btn {
		position: relative;
		background: none;
		border: none;
		padding: 0;
		cursor: pointer;
		border-radius: 50%;
		flex-shrink: 0;
		line-height: 0;
	}

	.avatar-upload-btn:disabled {
		cursor: wait;
		opacity: 0.6;
	}

	.avatar-upload-overlay {
		position: absolute;
		inset: 0;
		border-radius: 50%;
		background: rgba(0, 0, 0, 0.55);
		display: grid;
		place-items: center;
		color: var(--text-header);
		opacity: 0;
		transition: opacity 150ms ease;
		pointer-events: none;
	}

	.avatar-upload-btn:hover .avatar-upload-overlay,
	.avatar-upload-btn:focus-visible .avatar-upload-overlay {
		opacity: 1;
	}

	/* Visually hidden file input */
	.sr-only {
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

	.user-panel-avatar {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.user-panel-avatar.placeholder {
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
	}

	.user-panel-names {
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.user-panel-display {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.user-panel-role {
		font-size: 12px;
		color: var(--text-muted);
	}

	.user-panel-status {
		font-size: 12px;
		color: var(--text-muted);
	}
</style>
