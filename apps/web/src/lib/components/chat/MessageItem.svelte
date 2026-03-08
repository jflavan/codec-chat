<script lang="ts">
	import type { Message, Mention } from '$lib/types/index.js';
	import { formatTime } from '$lib/utils/format.js';
	import ReactionBar from './ReactionBar.svelte';
	import LinkifiedText from './LinkifiedText.svelte';
	import LinkPreviewCard from './LinkPreviewCard.svelte';
	import YouTubeEmbed from './YouTubeEmbed.svelte';
	import ReplyReference from './ReplyReference.svelte';
	import MessageActionBar from './MessageActionBar.svelte';
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

	function handleToggleReaction(emoji: string) {
		app.toggleReaction(message.id, emoji);
	}

	function handleReply() {
		const bodyPreview = message.body.length > 100 ? message.body.slice(0, 100) : message.body;
		app.startReply(message.id, message.authorName, bodyPreview, 'channel');
	}

	const isOwnMessage = $derived(currentUserId != null && message.authorUserId === currentUserId);
	const canDeleteMessage = $derived(isOwnMessage || app.isGlobalAdmin);

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

<article
	class="message"
	class:grouped
	class:mentioned={isMentioned}
>
	<!-- Floating action bar — appears on hover at top-right of message -->
	<MessageActionBar
		{isOwnMessage}
		canDelete={canDeleteMessage}
		onReply={handleReply}
		onReact={handleToggleReaction}
		onEdit={startEdit}
		onDelete={handleDelete}
		isReactionPending={(emoji) => app.isReactionPending(message.id, emoji)}
	/>

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
				<p class="message-body"><LinkifiedText text={message.body} mentions={effectiveMentions} customEmojis={app.customEmojis} /></p>
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
						<YouTubeEmbed videoId={yt.videoId} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
					isPending={(emoji) => app.isReactionPending(message.id, emoji)}
					members={app.members}
					customEmojis={app.customEmojis}
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
					<LinkifiedText text={message.body} mentions={effectiveMentions} customEmojis={app.customEmojis} />
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
						<YouTubeEmbed videoId={yt.videoId} />
					{/each}
				</div>
			{/if}
			{#if (message.reactions ?? []).length > 0}
				<ReactionBar
					reactions={message.reactions}
					{currentUserId}
					onToggle={handleToggleReaction}
					isPending={(emoji) => app.isReactionPending(message.id, emoji)}
					members={app.members}
					customEmojis={app.customEmojis}
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
	}
</style>
