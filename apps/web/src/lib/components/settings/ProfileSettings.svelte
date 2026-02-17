<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	const ACCEPTED_TYPES = 'image/jpeg,image/png,image/webp,image/gif';
	const MAX_NICKNAME_LENGTH = 32;

	let nicknameInput = $state(app.me?.user.nickname ?? '');
	let isSaving = $state(false);
	let fileInput = $state<HTMLInputElement | undefined>(undefined);

	// Keep local input in sync when profile changes from outside.
	$effect(() => {
		nicknameInput = app.me?.user.nickname ?? '';
	});

	const previewName = $derived(
		nicknameInput.trim()
			? nicknameInput.trim()
			: (app.me?.user.displayName ?? '')
	);

	const hasChanged = $derived(
		nicknameInput.trim() !== (app.me?.user.nickname ?? '')
	);

	const hasCustomAvatar = $derived(
		app.me?.user.avatarUrl && !app.me.user.avatarUrl.includes('googleusercontent.com')
	);

	async function saveNickname() {
		const trimmed = nicknameInput.trim();
		if (!trimmed || trimmed.length > MAX_NICKNAME_LENGTH) return;
		isSaving = true;
		try {
			await app.setNickname(trimmed);
		} finally {
			isSaving = false;
		}
	}

	async function resetNickname() {
		isSaving = true;
		try {
			await app.removeNickname();
			nicknameInput = '';
		} finally {
			isSaving = false;
		}
	}

	function openFilePicker() {
		fileInput?.click();
	}

	async function handleFileChange() {
		const file = fileInput?.files?.[0];
		if (!file) return;
		await app.uploadAvatar(file);
		if (fileInput) fileInput.value = '';
	}
</script>

<div class="profile-settings" role="tabpanel" aria-labelledby="tab-profile">
	<h2 class="section-title">My Profile</h2>

	<!-- Profile Preview Card -->
	{#if app.me}
		<div class="preview-card">
			<input
				bind:this={fileInput}
				type="file"
				accept={ACCEPTED_TYPES}
				class="sr-only"
				onchange={handleFileChange}
				aria-label="Upload avatar image"
			/>
			<button
				class="preview-avatar-btn"
				onclick={openFilePicker}
				disabled={app.isUploadingAvatar}
				title="Click to change avatar"
				aria-label="Change avatar"
			>
				{#if app.isUploadingAvatar}
					<div class="preview-avatar placeholder" aria-hidden="true">…</div>
				{:else if app.me.user.avatarUrl}
					<img class="preview-avatar" src={app.me.user.avatarUrl} alt="Your avatar" />
				{:else}
					<div class="preview-avatar placeholder" aria-hidden="true">
						{previewName.slice(0, 1).toUpperCase()}
					</div>
				{/if}
				<div class="avatar-overlay" aria-hidden="true">Change</div>
			</button>
			<div class="preview-info">
				<span class="preview-name">{previewName}</span>
				<span class="preview-email">{app.me.user.email ?? ''}</span>
				<span class="preview-helper">This is how others see you</span>
			</div>
		</div>

		{#if hasCustomAvatar}
			<button
				class="remove-avatar-btn"
				onclick={() => app.deleteAvatar()}
				disabled={app.isUploadingAvatar}
			>
				Remove Avatar
			</button>
		{/if}

		<!-- Nickname Field -->
		<div class="field-group">
			<label class="field-label" for="nickname-input">
				Nickname
				<span class="field-helper">This is how you'll appear across Codec</span>
			</label>
			<div class="nickname-row">
				<div class="input-wrapper">
					<input
						id="nickname-input"
						type="text"
						class="nickname-input"
						placeholder="Enter a nickname..."
						maxlength={MAX_NICKNAME_LENGTH}
						bind:value={nicknameInput}
					/>
					<span class="char-counter" class:warn={nicknameInput.length >= MAX_NICKNAME_LENGTH}>
						{nicknameInput.length}/{MAX_NICKNAME_LENGTH}
					</span>
				</div>
				<button
					class="save-btn"
					disabled={!hasChanged || isSaving || !nicknameInput.trim() || nicknameInput.trim().length > MAX_NICKNAME_LENGTH}
					onclick={saveNickname}
				>
					{isSaving ? 'Saving…' : 'Save'}
				</button>
			</div>
			{#if app.me.user.nickname}
				<button class="reset-link" onclick={resetNickname} disabled={isSaving}>
					Reset to Google display name
				</button>
			{/if}
		</div>
	{/if}
</div>

<style>
	.profile-settings {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.section-title {
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		margin: 0;
	}

	/* ───── Preview Card ───── */

	.preview-card {
		display: flex;
		align-items: center;
		gap: 16px;
		padding: 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
		border: 1px solid var(--border);
	}

	.preview-avatar-btn {
		position: relative;
		background: none;
		border: none;
		padding: 0;
		cursor: pointer;
		border-radius: 50%;
		flex-shrink: 0;
		line-height: 0;
	}

	.preview-avatar-btn:disabled {
		cursor: wait;
		opacity: 0.6;
	}

	.preview-avatar {
		width: 64px;
		height: 64px;
		border-radius: 50%;
		object-fit: cover;
	}

	.preview-avatar.placeholder {
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 24px;
		display: grid;
		place-items: center;
	}

	.avatar-overlay {
		position: absolute;
		inset: 0;
		border-radius: 50%;
		background: rgba(0, 0, 0, 0.6);
		display: grid;
		place-items: center;
		color: var(--text-header);
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		opacity: 0;
		transition: opacity 150ms ease;
		pointer-events: none;
	}

	.preview-avatar-btn:hover .avatar-overlay,
	.preview-avatar-btn:focus-visible .avatar-overlay {
		opacity: 1;
	}

	.preview-info {
		display: flex;
		flex-direction: column;
		gap: 2px;
		overflow: hidden;
	}

	.preview-name {
		font-size: 18px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.preview-email {
		font-size: 13px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.preview-helper {
		font-size: 12px;
		color: var(--text-dim);
		margin-top: 2px;
	}

	.remove-avatar-btn {
		align-self: flex-start;
		background: none;
		border: 1px solid var(--danger);
		color: var(--danger);
		padding: 10px 12px;
		min-height: 44px;
		border-radius: 3px;
		font-size: 13px;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.remove-avatar-btn:hover:not(:disabled) {
		background: var(--danger);
		color: #fff;
	}

	.remove-avatar-btn:disabled {
		opacity: 0.5;
		cursor: wait;
	}

	/* ───── Nickname Field ───── */

	.field-group {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.field-label {
		display: flex;
		flex-direction: column;
		gap: 2px;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
	}

	.field-helper {
		font-size: 12px;
		font-weight: 400;
		color: var(--text-dim);
	}

	.nickname-row {
		display: flex;
		align-items: stretch;
		gap: 8px;
	}

	.input-wrapper {
		flex: 1;
		position: relative;
		display: flex;
		align-items: center;
	}

	.nickname-input {
		width: 100%;
		padding: 10px 56px 10px 12px;
		background: var(--input-bg);
		border: 1px solid var(--border);
		border-radius: 8px;
		color: var(--text-normal);
		font-size: 16px;
		font-family: inherit;
		outline: none;
		transition: border-color 150ms ease, box-shadow 150ms ease;
	}

	.nickname-input::placeholder {
		color: var(--text-dim);
	}

	.nickname-input:focus {
		border-color: var(--accent);
		box-shadow: 0 0 0 2px rgba(0, 255, 102, 0.15);
	}

	.char-counter {
		position: absolute;
		right: 12px;
		font-size: 11px;
		color: var(--text-dim);
		pointer-events: none;
	}

	.char-counter.warn {
		color: var(--warn);
	}

	.save-btn {
		padding: 10px 16px;
		min-height: 44px;
		background: var(--accent);
		color: var(--bg-tertiary);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		white-space: nowrap;
		transition: background-color 150ms ease;
	}

	.save-btn:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.save-btn:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.reset-link {
		align-self: flex-start;
		background: none;
		border: none;
		padding: 0;
		color: var(--danger);
		font-size: 13px;
		cursor: pointer;
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.reset-link:hover:not(:disabled) {
		opacity: 0.8;
	}

	.reset-link:disabled {
		opacity: 0.5;
		cursor: wait;
	}

	/* Visually hidden */
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
</style>
