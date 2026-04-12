<script lang="ts">
	import type { Message, Mention } from '$lib/types/index.js';
import { ReportType } from '$lib/types/index.js';
	import { formatTime } from '$lib/utils/format.js';
	import ReactionBar from './ReactionBar.svelte';
	import LinkifiedText from './LinkifiedText.svelte';
	import LinkPreviewCard from './LinkPreviewCard.svelte';
	import FileCard from './FileCard.svelte';
	import YouTubeEmbed from './YouTubeEmbed.svelte';
	import ReplyReference from './ReplyReference.svelte';
	import MessageActionBar from './MessageActionBar.svelte';
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
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

	const auth = getAuthStore();
	const ui = getUIStore();
	const servers = getServerStore();
	const msgStore = getMessageStore();
	const currentUserId = $derived(auth.me?.user.id ?? null);

	/* Build a comprehensive mentions list: API-provided mentions take priority,
	   then fall back to the current server member list for any unresolved IDs. */
	const effectiveMentions: Mention[] = $derived.by(() => {
		const apiMentions = message.mentions ?? [];
		const seen = new Set(apiMentions.map((m) => m.userId));
		const memberFallbacks = servers.members
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

	function handleToggleReaction(emoji: string) {
		msgStore.toggleReaction(message.id, emoji);
	}

	function handleReply() {
		const bodyPreview = message.body.length > 100 ? message.body.slice(0, 100) : message.body;
		msgStore.startReply(message.id, message.authorName, bodyPreview);
	}

	const isOwnMessage = $derived(currentUserId != null && message.authorUserId === currentUserId);
	const canDeleteMessage = $derived(isOwnMessage || auth.isGlobalAdmin);

	let isEditing = $state(false);
	let editBody = $state('');

	function handleDelete() {
		msgStore.deleteMessage(message.id);
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
		await msgStore.editMessage(message.id, trimmed);
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

	const isPinned = $derived(msgStore.pinnedMessageIds.has(message.id));
	const isSystemMessage = $derived(message.messageType === 2);
</script>

{#if isSystemMessage}
<div class="system-message">
	<svg class="system-pin-icon" width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
		<path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/>
	</svg>
	<span class="system-message-text">{message.body}</span>
</div>
{:else}
<article
	class="message"
	class:grouped
	class:mentioned={isMentioned}
>
	<!-- Floating action bar — appears on hover at top-right of message -->
	<MessageActionBar
		{isOwnMessage}
		canDelete={canDeleteMessage}
		canPin={servers.canPinMessages}
		{isPinned}
		onPin={() => msgStore.pinMessage(message.id)}
		onUnpin={() => msgStore.unpinMessage(message.id)}
		onReply={handleReply}
		onReact={handleToggleReaction}
		onEdit={startEdit}
		onDelete={handleDelete}
		onReport={() => ui.openReportModal(ReportType.Message, message.id, 'Message by ' + message.authorName)}
		isReactionPending={(emoji) => ui.isReactionPending(message.id, emoji)}
	/>

	{#if isPinned}
		<div class="pin-indicator">
			<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/>
			</svg>
			<span>Pinned</span>
		</div>
	{/if}

	{#if !grouped}
		<div class="message-avatar-col">
			{#if message.authorAvatarUrl}
				<img class="message-avatar-img" src={message.authorAvatarUrl} alt="" />
			{:else}
				<div class="message-avatar" class:deleted-avatar={!message.authorUserId} aria-hidden="true">
					{message.authorUserId ? message.authorName.slice(0, 1).toUpperCase() : '?'}
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
				<strong class="message-author" class:deleted-user={!message.authorUserId}>{message.authorUserId ? message.authorName : 'Deleted User'}</strong>
				<time class="message-time" datetime={message.createdAt}>{formatTime(message.createdAt)}</time>
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
						aria-label="Edit message"
					></textarea>
					<div class="edit-actions">
						<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveEdit}>save</button></span>
					</div>
				</div>
			{:else if message.body}
				<p class="message-body"><LinkifiedText text={message.body} mentions={effectiveMentions} customEmojis={servers.customEmojis} /></p>
			{/if}
			{#if message.imageUrl}
				<button type="button" class="message-image-link" onclick={() => ui.openImagePreview(message.imageUrl!)} aria-label="View full-size image">
					<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
				</button>
			{/if}
			{#if message.fileUrl && message.fileName}
				<FileCard fileUrl={message.fileUrl} fileName={message.fileName} fileSize={message.fileSize} fileContentType={message.fileContentType} />
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
						<YouTubeEmbed videoId={yt.videoId} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
					isPending={(emoji) => ui.isReactionPending(message.id, emoji)}
					members={servers.members}
					customEmojis={servers.customEmojis}
				/>
			{/if}
		</div>
	{:else}
		<div class="message-avatar-col">
			<time class="message-time-inline" datetime={message.createdAt}>{formatTime(message.createdAt)}</time>
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
						aria-label="Edit message"
					></textarea>
					<div class="edit-actions">
						<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveEdit}>save</button></span>
					</div>
				</div>
			{:else if message.body}
				<p class="message-body">
					<LinkifiedText text={message.body} mentions={effectiveMentions} customEmojis={servers.customEmojis} />
					{#if message.editedAt}
						<span class="edited-label">(edited)</span>
					{/if}
				</p>
			{/if}
			{#if message.imageUrl}
				<button type="button" class="message-image-link" onclick={() => ui.openImagePreview(message.imageUrl!)} aria-label="View full-size image">
					<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
				</button>
			{/if}
			{#if message.fileUrl && message.fileName}
				<FileCard fileUrl={message.fileUrl} fileName={message.fileName} fileSize={message.fileSize} fileContentType={message.fileContentType} />
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
						<YouTubeEmbed videoId={yt.videoId} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
					isPending={(emoji) => ui.isReactionPending(message.id, emoji)}
					members={servers.members}
					customEmojis={servers.customEmojis}
				/>
			{/if}
		</div>
	{/if}
</article>
{/if}

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
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.08);
		border-left: 3px solid var(--accent);
		padding-left: 13px;
	}

	.message.mentioned:hover {
		background: rgba(var(--accent-rgb, 0, 255, 102), 0.12);
	}

	.message:not(.grouped) {
		margin-top: 16px;
	}

	.message.grouped {
		margin-top: 0;
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

	.message-author.deleted-user {
		color: var(--text-muted);
		font-style: italic;
	}

	.deleted-avatar {
		opacity: 0.5;
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
		box-shadow: 0 0 0 2px rgba(var(--accent-rgb, 0, 255, 102), 0.3);
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

	/* ───── Mobile adjustments ───── */

	@media (max-width: 768px) {
		.message-time-inline {
			font-size: 10px;
		}

		/* Show inline time on grouped messages (no hover on mobile) */
		.message .message-time-inline {
			color: var(--text-muted);
			opacity: 0.5;
		}

		/* Highlight tapped message on mobile (no hover available) */
		.message:focus-within {
			background: var(--bg-message-hover);
		}

		.message:focus {
			outline: none;
		}
	}
	.pin-indicator {
		display: flex;
		align-items: center;
		gap: 4px;
		font-size: 0.75rem;
		color: var(--text-muted);
		padding: 2px 0 2px 72px;
	}
	.pin-indicator svg {
		fill: var(--text-muted);
	}
	.system-message {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 6px;
		padding: 4px 16px;
		color: var(--text-muted);
		font-size: 0.8125rem;
	}
	.system-pin-icon {
		fill: var(--text-muted);
	}
</style>
