<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import type { Member } from '$lib/types/index.js';
	import ReplyComposerBar from './ReplyComposerBar.svelte';
	import ComposerOverlay from './ComposerOverlay.svelte';

	const app = getAppState();
	let inputEl: HTMLInputElement;
	let fileInputEl: HTMLInputElement;
	let overlayEl: HTMLDivElement;

	/* ───── Mention autocomplete state ───── */
	let showMentionPicker = $state(false);
	let mentionQuery = $state('');
	let mentionStartIndex = $state(0);
	let selectedMentionIndex = $state(0);

	/** Virtual entry representing the @here mention that notifies all channel members. */
	const hereMember: Member = { userId: 'here', displayName: 'here', avatarUrl: '', role: 'Member', joinedAt: '' };

	const filteredMembers: Member[] = $derived.by(() => {
		const q = mentionQuery.toLowerCase();
		const members =
			mentionQuery.length === 0
				? app.members
				: app.members.filter((m) => m.displayName.toLowerCase().includes(q));
		const showHere = 'here'.includes(q);
		return showHere ? [hereMember, ...members] : members;
	});

	function detectMentionTrigger(): void {
		if (!inputEl) return;
		const value = inputEl.value;
		const cursor = inputEl.selectionStart ?? value.length;

		// Walk backwards from cursor to find an unescaped '@'.
		const textBeforeCursor = value.slice(0, cursor);
		const atIndex = textBeforeCursor.lastIndexOf('@');
		if (atIndex === -1 || (atIndex > 0 && textBeforeCursor[atIndex - 1] !== ' ' && atIndex !== 0)) {
			showMentionPicker = false;
			return;
		}

		// Check there's no space between @ and cursor (single-word query).
		const query = textBeforeCursor.slice(atIndex + 1);
		if (query.includes(' ') || query.includes('\n')) {
			showMentionPicker = false;
			return;
		}

		mentionQuery = query;
		mentionStartIndex = atIndex;
		selectedMentionIndex = 0;
		showMentionPicker = true;
	}

	function insertMention(member: Member): void {
		const value = app.messageBody;
		const before = value.slice(0, mentionStartIndex);
		const afterCursor = value.slice(mentionStartIndex + 1 + mentionQuery.length);
		app.messageBody = `${before}@${member.displayName} ${afterCursor}`;
		// @here is resolved as a keyword, not via pendingMentions
		if (member.userId !== 'here') {
			app.pendingMentions.set(member.displayName, member.userId);
		}
		showMentionPicker = false;
		mentionQuery = '';
		inputEl?.focus();
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (showMentionPicker && filteredMembers.length > 0) {
			if (e.key === 'ArrowDown') {
				e.preventDefault();
				selectedMentionIndex = (selectedMentionIndex + 1) % filteredMembers.length;
			} else if (e.key === 'ArrowUp') {
				e.preventDefault();
				selectedMentionIndex =
					(selectedMentionIndex - 1 + filteredMembers.length) % filteredMembers.length;
			} else if (e.key === 'Enter' || e.key === 'Tab') {
				e.preventDefault();
				insertMention(filteredMembers[selectedMentionIndex]);
			} else if (e.key === 'Escape') {
				e.preventDefault();
				showMentionPicker = false;
			}
			return;
		}

		if (e.key === 'Escape' && app.replyingTo?.context === 'channel') {
			e.preventDefault();
			app.cancelReply();
		}
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (showMentionPicker) return;
		await app.sendMessage();
		inputEl?.focus();
	}

	function handleInput(): void {
		app.handleComposerInput();
		detectMentionTrigger();
		syncOverlayScroll();
	}

	function syncOverlayScroll(): void {
		if (overlayEl && inputEl) {
			overlayEl.scrollLeft = inputEl.scrollLeft;
		}
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

	{#if showMentionPicker && filteredMembers.length > 0}
		<ul class="mention-picker" role="listbox" aria-label="Mention a member">
			{#each filteredMembers.slice(0, 8) as member, i (member.userId)}
				<li role="option" aria-selected={i === selectedMentionIndex}>
					<button
						class="mention-option"
						class:selected={i === selectedMentionIndex}
						onmousedown={(e) => { e.preventDefault(); insertMention(member); }}
						type="button"
					>
						{#if member.userId === 'here'}
							<div class="mention-avatar-placeholder here-icon" aria-hidden="true">@</div>
							<span class="mention-name">here <span class="mention-hint">— notify everyone in this channel</span></span>
						{:else if member.avatarUrl}
							<img class="mention-avatar" src={member.avatarUrl} alt="" />
							<span class="mention-name">{member.displayName}</span>
						{:else}
							<div class="mention-avatar-placeholder" aria-hidden="true">
								{member.displayName.slice(0, 1).toUpperCase()}
							</div>
							<span class="mention-name">{member.displayName}</span>
						{/if}
					</button>
				</li>
			{/each}
		</ul>
	{/if}

	{#if app.replyingTo?.context === 'channel'}
		<ReplyComposerBar authorName={app.replyingTo.authorName} bodyPreview={app.replyingTo.bodyPreview} onCancel={() => app.cancelReply()} />
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
		<div class="composer-input-wrapper">
			<div class="composer-input-overlay" bind:this={overlayEl} aria-hidden="true"><ComposerOverlay text={app.messageBody} /></div>
			<input
				bind:this={inputEl}
				class="composer-input"
				type="text"
				placeholder={app.selectedChannelName ? `Message #${app.selectedChannelName}` : 'Select a channel…'}
				bind:value={app.messageBody}
				disabled={!app.selectedChannelId || app.isSending}
				oninput={handleInput}
				onkeydown={handleKeydown}
				onpaste={handlePaste}
			/>
		</div>
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

	.composer-input-wrapper {
		flex: 1;
		position: relative;
		background: var(--input-bg);
		overflow: hidden;
	}

	.composer-input-overlay {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		padding: 12px 16px;
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		color: var(--text-normal);
		pointer-events: none;
		white-space: nowrap;
		overflow: hidden;
	}

	.composer-input {
		position: relative;
		width: 100%;
		padding: 12px 16px;
		border: none;
		background: transparent;
		color: transparent;
		caret-color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		outline: none;
		min-height: 20px;
	}

	.composer-input::placeholder {
		color: var(--text-dim);
	}

	.composer-input::selection {
		background: rgba(88, 101, 242, 0.3);
	}

	.composer-input-wrapper:focus-within {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.composer-input-wrapper:has(.composer-input:disabled) {
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

	/* ───── Mention picker ───── */

	.mention-picker {
		list-style: none;
		margin: 0 0 4px;
		padding: 4px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
		max-height: 240px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.mention-option {
		display: flex;
		align-items: center;
		gap: 8px;
		width: 100%;
		padding: 6px 8px;
		border: none;
		border-radius: 4px;
		background: transparent;
		color: var(--text-normal);
		font-size: 14px;
		font-family: inherit;
		cursor: pointer;
		transition: background-color 100ms ease;
	}

	.mention-option:hover,
	.mention-option.selected {
		background: var(--bg-message-hover);
	}

	.mention-avatar {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.mention-avatar-placeholder {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 11px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.mention-name {
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.mention-hint {
		color: var(--text-muted);
		font-size: 12px;
	}

	.here-icon {
		background: var(--danger, #ed4245);
		font-weight: 700;
		font-size: 14px;
	}
</style>
