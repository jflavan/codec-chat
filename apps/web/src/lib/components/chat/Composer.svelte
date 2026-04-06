<script lang="ts">
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getChannelStore } from '$lib/state/channel-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import type { Member } from '$lib/types/index.js';
	import ReplyComposerBar from './ReplyComposerBar.svelte';
	import ComposerOverlay from './ComposerOverlay.svelte';
	import EmojiPicker from './EmojiPicker.svelte';
	import GifPicker from './GifPicker.svelte';
	import { recordEmojiUse } from '$lib/utils/emoji-frequency.js';

	const ui = getUIStore();
	const servers = getServerStore();
	const channelStore = getChannelStore();
	const msgStore = getMessageStore();
	let inputEl: HTMLTextAreaElement;
	let fileInputEl: HTMLInputElement;
	let overlayEl: HTMLDivElement;

	const LINE_HEIGHT = 20;
	const MAX_LINES = 5;
	let showPicker = $state(false);
	let pickerTab = $state<'emoji' | 'gif'>('emoji');

	/* ───── Mention autocomplete state ───── */
	let showMentionPicker = $state(false);
	let mentionQuery = $state('');
	let mentionStartIndex = $state(0);
	let selectedMentionIndex = $state(0);

	/** Virtual entry representing the @here mention that notifies all channel members. */
	const hereMember: Member = { userId: 'here', displayName: 'here', avatarUrl: '', roles: [], displayRole: null, highestPosition: 999, permissions: 0, joinedAt: '' };

	const filteredMembers: Member[] = $derived.by(() => {
		const q = mentionQuery.toLowerCase();
		const members =
			mentionQuery.length === 0
				? servers.members
				: servers.members.filter((m) => m.displayName.toLowerCase().includes(q));
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
		const value = msgStore.messageBody;
		const before = value.slice(0, mentionStartIndex);
		const afterCursor = value.slice(mentionStartIndex + 1 + mentionQuery.length);
		msgStore.messageBody = `${before}@${member.displayName} ${afterCursor}`;
		// @here is resolved as a keyword, not via pendingMentions
		if (member.userId !== 'here') {
			msgStore.pendingMentions.set(member.displayName, member.userId);
		}
		showMentionPicker = false;
		mentionQuery = '';
		inputEl?.focus();
	}

	function handleEmojiInsert(emoji: string) {
		recordEmojiUse(emoji);
		if (!inputEl) return;
		const start = inputEl.selectionStart ?? msgStore.messageBody.length;
		const end = inputEl.selectionEnd ?? start;
		const before = msgStore.messageBody.slice(0, start);
		const after = msgStore.messageBody.slice(end);
		msgStore.messageBody = before + emoji + after;
		// Set cursor position after inserted emoji
		const newPos = start + emoji.length;
		requestAnimationFrame(() => {
			inputEl.focus();
			inputEl.setSelectionRange(newPos, newPos);
		});
	}

	function handleGifSelect(gifUrl: string) {
		showPicker = false;
		pickerTab = 'emoji';
		msgStore.sendGifMessage(gifUrl);
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

		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			if (msgStore.messageBody.trim() || msgStore.pendingImage || msgStore.pendingFile) {
				msgStore.sendMessage();
				requestAnimationFrame(() => autoResize());
			}
			return;
		}

		if (e.key === 'Enter' && e.shiftKey) {
			requestAnimationFrame(() => autoResize());
		}

		if (e.key === 'Escape' && msgStore.replyingTo?.context === 'channel') {
			e.preventDefault();
			msgStore.cancelReply();
		}
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (showMentionPicker) return;
		await msgStore.sendMessage();
		requestAnimationFrame(() => autoResize());
		inputEl?.focus();
	}

	function handleInput(): void {
		msgStore.handleComposerInput();
		detectMentionTrigger();
		autoResize();
		syncOverlayScroll();
	}

	const BASE_HEIGHT = 38; // must match .composer-input CSS height

	function autoResize(): void {
		if (!inputEl) return;
		inputEl.style.height = `${BASE_HEIGHT}px`;
		const maxHeight = LINE_HEIGHT * MAX_LINES + (BASE_HEIGHT - LINE_HEIGHT);
		const scrollHeight = inputEl.scrollHeight;
		inputEl.style.height = `${Math.min(scrollHeight, maxHeight)}px`;
		inputEl.style.overflowY = scrollHeight > maxHeight ? 'auto' : 'hidden';
	}

	function syncOverlayScroll(): void {
		if (overlayEl && inputEl) {
			requestAnimationFrame(() => {
				overlayEl.scrollLeft = inputEl.scrollLeft;
				overlayEl.scrollTop = inputEl.scrollTop;
			});
		}
	}

	function handleFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (file) {
			if (file.type.startsWith('image/')) {
				msgStore.attachImage(file);
			} else {
				msgStore.attachFile(file);
			}
		}
		input.value = '';
	}

	$effect(() => {
		if (inputEl) autoResize();
	});

	function handlePaste(e: ClipboardEvent) {
		const items = e.clipboardData?.items;
		if (!items) return;
		for (const item of items) {
			if (item.type.startsWith('image/')) {
				e.preventDefault();
				const file = item.getAsFile();
				if (file) {
					msgStore.attachImage(file);
				}
				return;
			}
		}
	}
</script>

<form class="composer" onsubmit={handleSubmit}>
	{#if msgStore.pendingImagePreview}
		<div class="image-preview">
			<img src={msgStore.pendingImagePreview} alt="Attachment preview" class="preview-thumb" />
			<button
				type="button"
				class="remove-preview"
				onclick={() => msgStore.clearPendingImage()}
				aria-label="Remove image"
			>
				<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
				</svg>
			</button>
		</div>
	{/if}
	{#if msgStore.pendingFile}
		<div class="file-preview">
			<svg class="file-preview-icon" width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-1 2l5 5h-5V4zM6 20V4h7v5h5v11H6z"/></svg>
			<span class="file-preview-name">{msgStore.pendingFile.name}</span>
			<button
				type="button"
				class="remove-preview"
				onclick={() => msgStore.clearPendingFile()}
				aria-label="Remove file"
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

	{#if msgStore.replyingTo?.context === 'channel'}
		<ReplyComposerBar authorName={msgStore.replyingTo.authorName} bodyPreview={msgStore.replyingTo.bodyPreview} onCancel={() => msgStore.cancelReply()} />
	{/if}

	{#if !ui.isHubConnected}
		<div class="composer-row">
			<div class="composer-input-wrapper composer-disconnected">
				<span class="connecting-message" aria-live="polite">Codec connecting<span class="animated-ellipsis"></span></span>
			</div>
		</div>
	{:else}
		<div class="composer-row">
			<input type="file" class="sr-only" bind:this={fileInputEl} onchange={handleFileSelect} />
			<button
				class="composer-attach"
				type="button"
				onclick={() => fileInputEl?.click()}
				disabled={!channelStore.selectedChannelId || msgStore.isSending}
				aria-label="Attach file"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
				</svg>
			</button>
			<div class="composer-input-wrapper">
				<div class="composer-input-overlay" bind:this={overlayEl} aria-hidden="true"><ComposerOverlay text={msgStore.messageBody} customEmojis={servers.customEmojis} /></div>
				<textarea
					bind:this={inputEl}
					class="composer-input"
					rows="1"
					inputmode="text"
					autocomplete="off"
					placeholder={channelStore.selectedChannelName ? `Message #${channelStore.selectedChannelName}` : 'Select a channel…'}
					bind:value={msgStore.messageBody}
					disabled={!channelStore.selectedChannelId || msgStore.isSending}
					oninput={handleInput}
					onkeydown={handleKeydown}
					onscroll={syncOverlayScroll}
					onpaste={handlePaste}
				></textarea>
			</div>
			<button
				class="composer-emoji"
				type="button"
				onclick={() => { showPicker = !showPicker; if (showPicker) pickerTab = 'emoji'; }}
				disabled={!channelStore.selectedChannelId || msgStore.isSending}
				aria-label="Add emoji or GIF"
				title="Add emoji or GIF"
			>
				<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"/>
				</svg>
			</button>
			<button
				class="composer-send"
				type="submit"
				disabled={!channelStore.selectedChannelId || (!msgStore.messageBody.trim() && !msgStore.pendingImage && !msgStore.pendingFile) || msgStore.isSending}
				aria-label="Send message"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
				</svg>
			</button>
		</div>
		{#if showPicker}
			<div class="composer-picker-wrapper">
				<!-- svelte-ignore a11y_no_static_element_interactions -->
				<div class="picker-backdrop" onclick={() => { showPicker = false; }}></div>
				<div class="picker-container" role="dialog" aria-label="Emoji and GIF picker">
					<div class="picker-tab-bar">
						<button
							class="picker-tab"
							class:active={pickerTab === 'emoji'}
							type="button"
							onclick={() => (pickerTab = 'emoji')}
						>Emoji</button>
						<button
							class="picker-tab"
							class:active={pickerTab === 'gif'}
							type="button"
							onclick={() => (pickerTab = 'gif')}
						>GIFs</button>
					</div>
					<div class="picker-body">
						{#if pickerTab === 'emoji'}
							<EmojiPicker
								mode="insert"
								embedded={true}
								onSelect={handleEmojiInsert}
								onClose={() => { showPicker = false; }}
								customEmojis={servers.customEmojis}
							/>
						{:else}
							<GifPicker
								onSelect={handleGifSelect}
								onClose={() => { showPicker = false; }}
							/>
						{/if}
					</div>
				</div>
			</div>
		{/if}
	{/if}
</form>

<style>
	.composer {
		flex-shrink: 0;
		padding: 8px 16px 24px;
		display: flex;
		flex-direction: column;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-row {
		display: flex;
		align-items: flex-end;
		gap: 0;
	}

	.composer-attach {
		background: var(--input-bg);
		border: none;
		box-sizing: border-box;
		padding: 9px 10px;
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
		padding: 9px 16px;
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		color: var(--text-normal);
		pointer-events: none;
		white-space: pre-wrap;
		word-wrap: break-word;
		overflow: hidden;
	}

	.composer-input {
		display: block;
		position: relative;
		width: 100%;
		box-sizing: border-box;
		margin: 0;
		padding: 9px 16px;
		border: none;
		background: transparent;
		color: transparent;
		caret-color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		outline: none;
		height: 38px;
		resize: none;
		overflow-y: hidden;
	}

	.composer-input::placeholder {
		color: var(--text-dim);
	}

	.composer-input::selection {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.3);
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
		box-sizing: border-box;
		padding: 9px 12px;
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

	.composer-emoji {
		background: var(--input-bg);
		border: none;
		box-sizing: border-box;
		padding: 9px 6px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-emoji:hover:not(:disabled) {
		color: var(--accent);
	}

	.composer-emoji:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.composer-picker-wrapper {
		position: relative;
	}

	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 99;
	}

	.picker-container {
		position: absolute;
		z-index: 100;
		bottom: calc(100% + 4px);
		right: 0;
		width: 352px;
		max-height: 420px;
		display: flex;
		flex-direction: column;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
		overflow: hidden;
	}

	.picker-tab-bar {
		display: flex;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.picker-tab {
		flex: 1;
		padding: 8px 12px;
		background: none;
		border: none;
		border-bottom: 2px solid transparent;
		color: var(--text-muted);
		font-size: 0.85rem;
		font-weight: 600;
		cursor: pointer;
		transition: color 0.15s, border-color 0.15s;
	}

	.picker-tab:hover {
		color: var(--text-primary);
	}

	.picker-tab.active {
		color: var(--accent);
		border-bottom-color: var(--accent);
	}

	.picker-body {
		flex: 1;
		min-height: 0;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	/* ───── Disconnected state ───── */

	.composer-disconnected {
		border-radius: 8px;
		opacity: 0.5;
		display: flex;
		align-items: center;
	}

	.connecting-message {
		padding: 9px 16px;
		font-size: 15px;
		color: var(--text-dim);
		line-height: 20px;
		user-select: none;
	}

	.animated-ellipsis::after {
		content: '';
		animation: ellipsis-cycle 1.5s steps(4, end) infinite;
	}

	@keyframes ellipsis-cycle {
		0%   { content: ''; }
		25%  { content: '.'; }
		50%  { content: '..'; }
		75%  { content: '...'; }
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

	/* ───── File preview ───── */

	.file-preview {
		position: relative;
		display: inline-flex;
		align-items: center;
		gap: 8px;
		margin-bottom: 8px;
		padding: 8px 32px 8px 12px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		max-width: 300px;
	}

	.file-preview-icon {
		flex-shrink: 0;
		color: var(--text-muted);
	}

	.file-preview-name {
		font-size: 13px;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
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
		background: var(--danger);
		font-weight: 700;
		font-size: 14px;
	}

	/* ───── Mobile adjustments ───── */

	@media (max-width: 768px) {
		.composer {
			padding: 0 16px calc(16px + env(safe-area-inset-bottom, 0));
		}

		.composer-attach,
		.composer-send {
			min-width: 44px;
			min-height: 44px;
		}

		.composer-emoji {
			min-width: 44px;
			min-height: 44px;
		}

		.composer-input {
			font-size: 16px;
			padding: 9px 16px;
			resize: none;
		}

		.composer-input-overlay {
			font-size: 16px;
		}

		.mention-option {
			padding: 10px 8px;
			min-height: 44px;
		}
	}
</style>
