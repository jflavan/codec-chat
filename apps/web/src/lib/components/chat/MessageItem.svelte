<script lang="ts">
	import type { Message } from '$lib/types/index.js';
	import { formatTime } from '$lib/utils/format.js';

	let { message, grouped = false }: { message: Message; grouped?: boolean } = $props();
</script>

<article class="message" class:grouped>
	{#if !grouped}
		<div class="message-avatar-col">
			<div class="message-avatar" aria-hidden="true">
				{message.authorName.slice(0, 1).toUpperCase()}
			</div>
		</div>
		<div class="message-content">
			<div class="message-header">
				<strong class="message-author">{message.authorName}</strong>
				<time class="message-time">{formatTime(message.createdAt)}</time>
			</div>
			<p class="message-body">{message.body}</p>
		</div>
	{:else}
		<div class="message-avatar-col">
			<time class="message-time-inline">{formatTime(message.createdAt)}</time>
		</div>
		<div class="message-content">
			<p class="message-body">{message.body}</p>
		</div>
	{/if}
</article>

<style>
	.message {
		display: grid;
		grid-template-columns: 56px 1fr;
		padding: 2px 16px;
		transition: background-color 150ms ease;
	}

	.message:hover {
		background: var(--bg-message-hover);
	}

	.message:not(.grouped) {
		margin-top: 16px;
	}

	.message.grouped {
		margin-top: 0;
	}

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
</style>
