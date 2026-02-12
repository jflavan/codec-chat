<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import MessageItem from './MessageItem.svelte';

	const app = getAppState();
</script>

<div class="message-feed">
	{#if app.isLoadingMessages}
		<p class="muted feed-status">Loading messages…</p>
	{:else if app.messages.length === 0}
		<p class="muted feed-status">No messages yet. Start the conversation!</p>
	{:else}
		{#each app.messages as message, i}
			{@const prev = i > 0 ? app.messages[i - 1] : null}
			{@const isGrouped = prev?.authorUserId === message.authorUserId && prev?.authorName === message.authorName}
			<MessageItem {message} grouped={isGrouped} />
		{/each}
	{/if}
</div>

<style>
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

	.muted {
		color: var(--text-muted);
	}
</style>
