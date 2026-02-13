<script lang="ts">
	import type { Message } from '$lib/types/index.js';
	import { formatTime } from '$lib/utils/format.js';
	import ReactionBar from './ReactionBar.svelte';
	import LinkifiedText from './LinkifiedText.svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let { message, grouped = false }: { message: Message; grouped?: boolean } = $props();

	const app = getAppState();
	const currentUserId = $derived(app.me?.user.id ?? null);

	let showPicker = $state(false);
	const quickEmojis = ['👍', '❤️', '😂', '🎉', '🔥', '👀', '🚀', '💯'];

	function handleToggleReaction(emoji: string) {
		app.toggleReaction(message.id, emoji);
		showPicker = false;
	}

	function closePicker() {
		showPicker = false;
	}
</script>

<article class="message" class:grouped>
	<!-- Floating action bar — appears on hover at top-right of message -->
	<div class="message-actions" class:picker-open={showPicker}>
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
			<div class="message-header">
				<strong class="message-author">{message.authorName}</strong>
				<time class="message-time">{formatTime(message.createdAt)}</time>
			</div>
			<p class="message-body"><LinkifiedText text={message.body} /></p>
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
			<p class="message-body"><LinkifiedText text={message.body} /></p>
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

	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 9;
	}

	.emoji-picker {
		position: absolute;
		top: calc(100% + 4px);
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
</style>
