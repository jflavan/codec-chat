<script lang="ts">
	import type { ReplyContext } from '$lib/types/index.js';

	let {
		replyContext,
		onClickGoToOriginal
	}: {
		replyContext: ReplyContext;
		onClickGoToOriginal?: (messageId: string) => void;
	} = $props();

	function handleClick() {
		if (!replyContext.isDeleted && onClickGoToOriginal) {
			onClickGoToOriginal(replyContext.messageId);
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter' || e.key === ' ') {
			e.preventDefault();
			handleClick();
		}
	}
</script>

{#if replyContext.isDeleted}
	<div
		class="reply-reference deleted"
		aria-label="Original message was deleted"
	>
		<span class="reply-icon" aria-hidden="true">↩</span>
		<em class="reply-deleted-text">Original message was deleted</em>
	</div>
{:else}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="reply-reference"
		role="button"
		tabindex="0"
		aria-label="Reply to message from {replyContext.authorName}: {replyContext.bodyPreview}"
		onclick={handleClick}
		onkeydown={handleKeydown}
	>
		<span class="reply-icon" aria-hidden="true">↩</span>
		{#if replyContext.authorAvatarUrl}
			<img class="reply-avatar" src={replyContext.authorAvatarUrl} alt="" />
		{:else}
			<div class="reply-avatar-placeholder" aria-hidden="true">
				{replyContext.authorName.slice(0, 1).toUpperCase()}
			</div>
		{/if}
		<span class="reply-author">{replyContext.authorName}</span>
		<span class="reply-body-preview">{replyContext.bodyPreview}</span>
	</div>
{/if}

<style>
	.reply-reference {
		display: flex;
		align-items: center;
		gap: 6px;
		padding: 4px 8px;
		margin-bottom: 4px;
		border-radius: 4px;
		background: rgba(var(--bg-secondary-rgb, 7, 17, 10), 0.5);
		cursor: pointer;
		font-size: 13px;
		line-height: 1.2;
		transition: background-color 150ms ease;
	}

	.reply-reference:hover:not(.deleted) {
		background: rgba(var(--bg-secondary-rgb, 7, 17, 10), 0.8);
	}

	.reply-reference:focus-visible {
		outline: 2px solid var(--accent);
		outline-offset: 1px;
	}

	.reply-reference.deleted {
		cursor: default;
	}

	.reply-icon {
		color: var(--text-muted);
		font-size: 14px;
		flex-shrink: 0;
		opacity: 0.6;
	}

	.reply-avatar {
		width: 16px;
		height: 16px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.reply-avatar-placeholder {
		width: 16px;
		height: 16px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 9px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.reply-author {
		color: var(--accent);
		font-weight: 600;
		flex-shrink: 0;
	}

	.reply-body-preview {
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		max-width: 400px;
	}

	.reply-deleted-text {
		color: var(--text-muted);
		font-style: italic;
	}
</style>
