<script lang="ts">
	import type { Message, Mention } from '$lib/types/index.js';
	import { formatTime } from '$lib/utils/format.js';
	import ReactionBar from './ReactionBar.svelte';
	import LinkifiedText from './LinkifiedText.svelte';
	import LinkPreviewCard from './LinkPreviewCard.svelte';
	import YouTubeEmbed from './YouTubeEmbed.svelte';
	import ReplyReference from './ReplyReference.svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { extractYouTubeUrls } from '$lib/utils/youtube.js';

	let {
		message,
		grouped = false,
		onScrollToMessage
	}: {
		message: Message;
		grouped?: boolean;
		onScrollToMessage?: (messageId: string) => void;
	} = $props();

	const app = getAppState();
	const currentUserId = $derived(app.me?.user.id ?? null);

	/* Build a comprehensive mentions list: API-provided mentions take priority,
	   then fall back to the current server member list for any unresolved IDs. */
	const effectiveMentions: Mention[] = $derived.by(() => {
		const apiMentions = message.mentions ?? [];
		const seen = new Set(apiMentions.map((m) => m.userId));
		const memberFallbacks = app.members
			.filter((m) => !seen.has(m.userId))
			.map((m) => ({ userId: m.userId, displayName: m.displayName }));
		return [...apiMentions, ...memberFallbacks];
	});

	const isMentioned = $derived(
		Boolean(
			(currentUserId && message.body?.toLowerCase().includes(`<@${currentUserId.toLowerCase()}>`)) ||
			message.body?.toLowerCase().includes('<@here>')
		)
	);

	/** YouTube URLs in the message body that have no matching backend link preview. */
	const uncoveredYouTubeUrls = $derived.by(() => {
		if (!message.body) return [];
		const all = extractYouTubeUrls(message.body);
		if (!all.length) return [];
		const coveredIds = new Set(
			(message.linkPreviews ?? [])
				.map((lp) => {
					const match = /[\w-]{11}/.exec(lp.url);
					return match?.[0] ?? null;
				})
				.filter(Boolean)
		);
		return all.filter((yt) => !coveredIds.has(yt.videoId));
	});

	let showPicker = $state(false);
	const quickEmojis = ['👍', '❤️', '😂', '🎉', '🔥', '👀', '🚀', '💯'];

	function handleToggleReaction(emoji: string) {
		app.toggleReaction(message.id, emoji);
		showPicker = false;
	}

	function closePicker() {
		showPicker = false;
	}

	function handleReply() {
		const bodyPreview = message.body.length > 100 ? message.body.slice(0, 100) : message.body;
		app.startReply(message.id, message.authorName, bodyPreview, 'channel');
	}

	const isOwnMessage = $derived(currentUserId != null && message.authorUserId === currentUserId);

	let isEditing = $state(false);
	let editBody = $state('');

	function handleDelete() {
		app.deleteMessage(message.id);
	}

	function startEdit() {
		editBody = message.body;
		isEditing = true;
	}

	function cancelEdit() {
		isEditing = false;
		editBody = '';
	}

	async function saveEdit() {
		const trimmed = editBody.trim();
		if (!trimmed || trimmed === message.body) {
			cancelEdit();
			return;
		}
		await app.editMessage(message.id, trimmed);
		isEditing = false;
		editBody = '';
	}

	function handleEditKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			saveEdit();
		} else if (e.key === 'Escape') {
			e.preventDefault();
			cancelEdit();
		}
	}
</script>

<article class="message" class:grouped class:mentioned={isMentioned}>
	<!-- Floating action bar — appears on hover at top-right of message -->
	<div class="message-actions" class:picker-open={showPicker}>
		<button
			class="action-btn"
			onclick={handleReply}
			title="Reply"
			aria-label="Reply to message"
		>
			<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
				<path d="M6.598 2.152a.5.5 0 0 1 .052.707L3.354 6.5H11.5a4.5 4.5 0 0 1 0 9h-1a.5.5 0 0 1 0-1h1a3.5 3.5 0 1 0 0-7H3.354l3.296 3.641a.5.5 0 1 1-.74.672l-4.2-4.638a.5.5 0 0 1 0-.672l4.2-4.638a.5.5 0 0 1 .688-.053z"/>
			</svg>
		</button>
		<button
			class="action-btn"
			onclick={() => (showPicker = !showPicker)}
			title="Add reaction"
			aria-label="Add reaction"
		>
			<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
				<path
					d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"
				/>
			</svg>
		</button>
		{#if isOwnMessage}
			<button
				class="action-btn"
				onclick={startEdit}
				title="Edit message"
				aria-label="Edit message"
			>
				<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
					<path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168l10-10ZM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5Zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293l6.5-6.5Z"/>
				</svg>
			</button>
			<button
				class="action-btn action-btn-danger"
				onclick={handleDelete}
				title="Delete message"
				aria-label="Delete message"
			>
				<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
					<path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6Z"/>
					<path d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1ZM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118ZM2.5 3h11V2h-11v1Z"/>
				</svg>
			</button>
		{/if}

		{#if showPicker}
			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<div class="picker-backdrop" onclick={closePicker} onkeydown={closePicker}></div>
			<div class="emoji-picker" role="menu">
				{#each quickEmojis as emoji}
					<button
						class="emoji-option"
						onclick={() => handleToggleReaction(emoji)}
						role="menuitem"
						aria-label="React with {emoji}"
					>
						{emoji}
					</button>
				{/each}
			</div>
		{/if}
	</div>

	{#if !grouped}
		<div class="message-avatar-col">
			{#if message.authorAvatarUrl}
				<img class="message-avatar-img" src={message.authorAvatarUrl} alt="" />
			{:else}
				<div class="message-avatar" aria-hidden="true">
					{message.authorName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
		</div>
		<div class="message-content">
			{#if message.replyContext}
				<ReplyReference
					replyContext={message.replyContext}
					onClickGoToOriginal={onScrollToMessage}
				/>
			{/if}
			<div class="message-header">
				<strong class="message-author">{message.authorName}</strong>
				<time class="message-time">{formatTime(message.createdAt)}</time>
				{#if message.editedAt}
					<span class="edited-label">(edited)</span>
				{/if}
			</div>
			{#if isEditing}
				<div class="edit-container">
					<textarea
						class="edit-input"
						bind:value={editBody}
						onkeydown={handleEditKeydown}
					></textarea>
					<div class="edit-actions">
						<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveEdit}>save</button></span>
					</div>
				</div>
			{:else if message.body}
				<p class="message-body"><LinkifiedText text={message.body} mentions={effectiveMentions} /></p>
			{/if}
			{#if message.imageUrl}
				<button type="button" class="message-image-link" onclick={() => app.openImagePreview(message.imageUrl!)}>
					<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
				</button>
			{/if}
			{#if message.linkPreviews?.length}
				<div class="link-previews">
					{#each message.linkPreviews as preview}
						<LinkPreviewCard {preview} />
					{/each}
				</div>
			{/if}
			{#if uncoveredYouTubeUrls.length}
				<div class="link-previews">
					{#each uncoveredYouTubeUrls as yt}
						<YouTubeEmbed videoId={yt.videoId} url={yt.url} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
				/>
			{/if}
		</div>
	{:else}
		<div class="message-avatar-col">
			<time class="message-time-inline">{formatTime(message.createdAt)}</time>
		</div>
		<div class="message-content">
			{#if message.replyContext}
				<ReplyReference
					replyContext={message.replyContext}
					onClickGoToOriginal={onScrollToMessage}
				/>
			{/if}
			{#if isEditing}
				<div class="edit-container">
					<textarea
						class="edit-input"
						bind:value={editBody}
						onkeydown={handleEditKeydown}
					></textarea>
					<div class="edit-actions">
						<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveEdit}>save</button></span>
					</div>
				</div>
			{:else if message.body}
				<p class="message-body">
					<LinkifiedText text={message.body} mentions={effectiveMentions} />
					{#if message.editedAt}
						<span class="edited-label">(edited)</span>
					{/if}
				</p>
			{/if}
			{#if message.imageUrl}
				<button type="button" class="message-image-link" onclick={() => app.openImagePreview(message.imageUrl!)}>
					<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
				</button>
			{/if}
			{#if message.linkPreviews?.length}
				<div class="link-previews">
					{#each message.linkPreviews as preview}
						<LinkPreviewCard {preview} />
					{/each}
				</div>
			{/if}
			{#if uncoveredYouTubeUrls.length}
				<div class="link-previews">
					{#each uncoveredYouTubeUrls as yt}
						<YouTubeEmbed videoId={yt.videoId} url={yt.url} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
				/>
			{/if}
		</div>
	{/if}
</article>

<style>
	.message {
		position: relative;
		display: grid;
		grid-template-columns: 56px 1fr;
		padding: 2px 16px;
		transition: background-color 150ms ease;
	}

	.message:hover {
		background: var(--bg-message-hover);
	}

	.message.mentioned {
		background: rgba(88, 101, 242, 0.08);
		border-left: 3px solid var(--accent);
		padding-left: 13px;
	}

	.message.mentioned:hover {
		background: rgba(88, 101, 242, 0.12);
	}

	.message:not(.grouped) {
		margin-top: 16px;
	}

	.message.grouped {
		margin-top: 0;
	}

	/* ───── Floating action bar ───── */

	.message-actions {
		position: absolute;
		top: -14px;
		right: 32px;
		z-index: 5;
		display: flex;
		align-items: center;
		opacity: 0;
		pointer-events: none;
		transition: opacity 120ms ease;
	}

	.message:hover .message-actions,
	.message-actions.picker-open {
		opacity: 1;
		pointer-events: auto;
	}

	.action-btn {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 34px;
		height: 32px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		color: var(--text-dim);
		cursor: pointer;
		box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3);
		transition:
			color 120ms ease,
			background-color 120ms ease,
			border-color 120ms ease;
	}

	.action-btn:hover {
		color: var(--text-normal);
		background: var(--bg-message-hover);
		border-color: var(--text-dim);
	}

	.action-btn-danger:hover {
		color: #ed4245;
		background: rgba(237, 66, 69, 0.1);
		border-color: #ed4245;
	}

	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 9;
	}

	.emoji-picker {
		position: absolute;
		bottom: calc(100% + 4px);
		right: 0;
		display: flex;
		gap: 2px;
		padding: 6px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
		z-index: 10;
	}

	.emoji-option {
		width: 32px;
		height: 32px;
		display: grid;
		place-items: center;
		border: none;
		border-radius: 6px;
		background: transparent;
		font-size: 18px;
		cursor: pointer;
		transition: background-color 100ms ease;
	}

	.emoji-option:hover {
		background: var(--bg-message-hover);
	}

	/* ───── Message layout ───── */

	.message-avatar-col {
		display: flex;
		justify-content: center;
		padding-top: 2px;
	}

	.message-avatar {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 16px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.message-avatar-img {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.message-content {
		min-width: 0;
	}

	.message-header {
		display: flex;
		align-items: baseline;
		gap: 8px;
	}

	.message-author {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.message-time {
		font-size: 12px;
		color: var(--text-muted);
	}

	.message-time-inline {
		font-size: 11px;
		color: transparent;
		text-align: center;
		width: 100%;
		display: block;
	}

	.message:hover .message-time-inline {
		color: var(--text-muted);
	}

	.message-body {
		margin: 2px 0 0;
		color: var(--text-normal);
		line-height: 1.375;
		word-break: break-word;
	}

	.message-image-link {
		display: block;
		margin-top: 4px;
		max-width: 400px;
		border-radius: 8px;
		overflow: hidden;
		border: none;
		background: none;
		padding: 0;
		cursor: pointer;
	}

	.message-image {
		display: block;
		max-width: 100%;
		max-height: 300px;
		border-radius: 8px;
		object-fit: contain;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.message-image-link:hover .message-image {
		opacity: 0.85;
	}

	.link-previews {
		display: flex;
		flex-direction: column;
		gap: 4px;
		margin-top: 4px;
	}

	/* ───── Edited label ───── */

	.edited-label {
		font-size: 11px;
		color: var(--text-muted);
		margin-left: 4px;
	}

	/* ───── Inline edit mode ───── */

	.edit-container {
		margin-top: 2px;
	}

	.edit-input {
		width: 100%;
		box-sizing: border-box;
		min-height: 44px;
		padding: 8px 12px;
		border-radius: 8px;
		border: 1px solid var(--accent);
		background: var(--bg-primary);
		color: var(--text-normal);
		font-family: inherit;
		font-size: 15px;
		line-height: 1.375;
		resize: vertical;
	}

	.edit-input:focus {
		outline: none;
		border-color: var(--accent);
		box-shadow: 0 0 0 2px rgba(88, 101, 242, 0.3);
	}

	.edit-actions {
		margin-top: 4px;
	}

	.edit-hint {
		font-size: 12px;
		color: var(--text-muted);
	}

	.edit-link-btn {
		background: none;
		border: none;
		padding: 0;
		color: var(--accent);
		font-size: 12px;
		cursor: pointer;
		font-family: inherit;
	}

	.edit-link-btn:hover {
		text-decoration: underline;
	}
</style>
