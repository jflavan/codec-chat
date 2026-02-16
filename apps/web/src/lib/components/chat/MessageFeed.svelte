<script lang="ts">
	import { tick, untrack } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import MessageItem from './MessageItem.svelte';

	const app = getAppState();
	const BOTTOM_THRESHOLD = 50;
	const TOP_THRESHOLD = 200;

	let container: HTMLDivElement;
	let isLockedToBottom = $state(true);
	let unreadCount = $state(0);
	let highlightedMessageId = $state<string | null>(null);

	// Internal bookkeeping (not reactive — no $state needed)
	let isAutoScrolling = false;
	let previousChannelId: string | null = null;
	let previousMessageCount = 0;

	function isAtBottom(): boolean {
		if (!container) return true;
		const distance = container.scrollHeight - container.scrollTop - container.clientHeight;
		return distance <= BOTTOM_THRESHOLD;
	}

	function scrollToBottom(instant: boolean): void {
		if (!container) return;
		isAutoScrolling = true;
		container.scrollTo({
			top: container.scrollHeight,
			behavior: instant ? 'instant' : 'smooth'
		});
		// Allow the scroll animation to complete before re-enabling user scroll detection.
		// Instant scrolls settle within a single frame; smooth needs ~300ms.
		setTimeout(() => { isAutoScrolling = false; }, instant ? 50 : 300);
	}

	function handleScroll(): void {
		if (isAutoScrolling) return;

		const atBottom = isAtBottom();
		if (atBottom && !isLockedToBottom) {
			isLockedToBottom = true;
			unreadCount = 0;
		} else if (!atBottom && isLockedToBottom) {
			isLockedToBottom = false;
		}

		// Trigger loading older messages when scrolled near the top.
		if (container && container.scrollTop < TOP_THRESHOLD && app.hasMoreMessages && !app.isLoadingOlderMessages) {
			loadOlderAndPreserveScroll();
		}
	}

	async function loadOlderAndPreserveScroll(): Promise<void> {
		if (!container) return;

		// Capture scroll position relative to the bottom of the scroll area
		// so we can restore it after new content is prepended above.
		const previousScrollHeight = container.scrollHeight;

		await app.loadOlderMessages();

		// Sync previousMessageCount so the auto-scroll effect doesn't
		// misinterpret prepended older messages as new incoming messages.
		previousMessageCount = app.messages.length;

		await tick();

		// Restore scroll position so the viewport stays on the same messages.
		// Suppress the scroll handler during restoration to avoid re-triggering
		// another fetch if scrollTop is still near the top.
		if (container) {
			isAutoScrolling = true;
			const newScrollHeight = container.scrollHeight;
			container.scrollTop += newScrollHeight - previousScrollHeight;
			setTimeout(() => { isAutoScrolling = false; }, 50);
		}
	}

	function jumpToBottom(): void {
		isLockedToBottom = true;
		unreadCount = 0;
		scrollToBottom(false);
	}

	function scrollToMessage(messageId: string): void {
		if (!container) return;
		const el = container.querySelector(`[data-message-id="${CSS.escape(messageId)}"]`);
		if (el) {
			el.scrollIntoView({ behavior: 'smooth', block: 'center' });
			highlightedMessageId = messageId;
			setTimeout(() => {
				if (highlightedMessageId === messageId) highlightedMessageId = null;
			}, 1500);
		}
	}

	// Reset scroll state on channel change
	$effect(() => {
		const channelId = app.selectedChannelId;
		if (channelId !== previousChannelId) {
			previousChannelId = channelId;
			isLockedToBottom = true;
			unreadCount = 0;
			previousMessageCount = 0;
		}
	});

	// Auto-scroll when locked, or track unread when unlocked
	$effect(() => {
		const count = app.messages.length;
		const loading = app.isLoadingMessages;

		if (loading || count === 0) {
			previousMessageCount = count;
			return;
		}

		const newMessages = count - previousMessageCount;
		previousMessageCount = count;
		if (newMessages <= 0) return;

		// Read scroll state without creating a dependency on it
		untrack(() => {
			if (isLockedToBottom) {
				// Use instant scroll for bulk loads (initial load or rapid influx)
				tick().then(() => scrollToBottom(newMessages > 3));
			} else {
				unreadCount += newMessages;
			}
		});
	});
</script>

<div class="feed-wrapper">
	<div class="message-feed" bind:this={container} onscroll={handleScroll}>
		{#if app.isLoadingMessages}
			<p class="muted feed-status">Loading messages…</p>
		{:else if app.messages.length === 0}
			<p class="muted feed-status">No messages yet. Start the conversation!</p>
		{:else}
			{#if app.isLoadingOlderMessages}
				<p class="muted feed-status loading-older">Loading older messages…</p>
			{/if}
			{#each app.messages as message, i}
				{@const prev = i > 0 ? app.messages[i - 1] : null}
				{@const isGrouped = prev?.authorUserId === message.authorUserId && prev?.authorName === message.authorName}
				<div data-message-id={message.id} class:reply-highlight={highlightedMessageId === message.id}>
					<MessageItem {message} grouped={isGrouped} onScrollToMessage={scrollToMessage} />
				</div>
			{/each}
		{/if}
	</div>

	{#if !isLockedToBottom}
		<button class="jump-to-bottom" onclick={jumpToBottom} aria-label="Jump to latest messages">
			{#if unreadCount > 0}
				<span class="jump-badge">{unreadCount}</span>
			{/if}
			<span class="jump-text">{unreadCount > 0 ? 'New' : 'Jump to latest'}</span>
			<svg class="jump-arrow" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
				<path d="M8 11.5a.5.5 0 0 1-.354-.146l-4.5-4.5a.5.5 0 1 1 .708-.708L8 10.293l4.146-4.147a.5.5 0 1 1 .708.708l-4.5 4.5A.5.5 0 0 1 8 11.5z"/>
			</svg>
		</button>
	{/if}
</div>

<style>
	.feed-wrapper {
		flex: 1;
		position: relative;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	.message-feed {
		flex: 1;
		overflow-y: auto;
		padding: 16px 0 8px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.feed-status {
		padding: 16px;
		text-align: center;
	}

	.loading-older {
		padding: 8px;
		font-size: 13px;
	}

	.muted {
		color: var(--text-muted);
	}

	.jump-to-bottom {
		position: absolute;
		bottom: 8px;
		left: 50%;
		transform: translateX(-50%);
		display: flex;
		align-items: center;
		gap: 6px;
		padding: 6px 14px;
		border: 1px solid var(--border);
		border-radius: 20px;
		background: var(--bg-secondary);
		color: var(--accent);
		font-size: 13px;
		font-weight: 500;
		font-family: inherit;
		cursor: pointer;
		white-space: nowrap;
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
		transition: background-color 150ms ease, color 150ms ease;
		z-index: 5;
	}

	.jump-to-bottom:hover {
		background: var(--bg-message-hover);
		color: var(--accent-hover);
	}

	.jump-badge {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		min-width: 20px;
		height: 20px;
		padding: 0 6px;
		border-radius: 10px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-size: 11px;
		font-weight: 700;
	}

	.jump-text {
		line-height: 1;
	}

	.jump-arrow {
		flex-shrink: 0;
		opacity: 0.8;
	}

	.reply-highlight {
		animation: reply-highlight-fade 1.5s ease-out;
	}

	@keyframes reply-highlight-fade {
		0% { background-color: rgba(88, 101, 242, 0.15); }
		100% { background-color: transparent; }
	}

	@media (prefers-reduced-motion: reduce) {
		.reply-highlight {
			animation: none;
			background-color: rgba(88, 101, 242, 0.1);
		}
	}
</style>
