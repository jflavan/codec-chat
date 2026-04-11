<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import { formatTime } from '$lib/utils/format.js';

	const servers = getServerStore();
	const msgStore = getMessageStore();
</script>

<aside class="pinned-panel">
	<header class="pinned-panel-header">
		<h2>Pinned Messages</h2>
		<button class="close-btn" onclick={() => { msgStore.showPinnedPanel = false; }} aria-label="Close">
			<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M18.3 5.71a1 1 0 0 0-1.41 0L12 10.59 7.11 5.7A1 1 0 0 0 5.7 7.11L10.59 12 5.7 16.89a1 1 0 1 0 1.41 1.41L12 13.41l4.89 4.89a1 1 0 0 0 1.41-1.41L13.41 12l4.89-4.89a1 1 0 0 0 0-1.4z"/>
			</svg>
		</button>
	</header>

	<div class="pinned-list">
		{#if msgStore.pinnedMessages.length === 0}
			<div class="pinned-empty">
				<svg width="40" height="40" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/>
				</svg>
				<p>No pinned messages yet.</p>
			</div>
		{:else}
			{#each msgStore.pinnedMessages as pin (pin.messageId)}
				<div class="pinned-item">
					<div class="pinned-item-header">
						<span class="pinned-author">{pin.message.authorName}</span>
						<span class="pinned-date">{formatTime(pin.message.createdAt)}</span>
					</div>
					<p class="pinned-body">{pin.message.body}</p>
					<div class="pinned-item-footer">
						<button
							class="jump-btn"
							onclick={() => msgStore.jumpToMessage(pin.messageId, pin.channelId, false)}
						>
							Jump to message
						</button>
						{#if servers.canPinMessages}
							<button
								class="unpin-btn"
								onclick={() => msgStore.unpinMessage(pin.messageId)}
							>
								Unpin
							</button>
						{/if}
					</div>
				</div>
			{/each}
		{/if}
	</div>
</aside>

<style>
	.pinned-panel {
		width: 340px;
		min-width: 340px;
		height: 100%;
		background: var(--bg-secondary);
		border-left: 1px solid var(--border-subtle);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}
	.pinned-panel-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 12px 16px;
		border-bottom: 1px solid var(--border-subtle);
	}
	.pinned-panel-header h2 {
		font-size: 0.9375rem;
		font-weight: 600;
		margin: 0;
		color: var(--text-normal);
	}
	.close-btn {
		background: none;
		border: none;
		color: var(--text-muted);
		cursor: pointer;
		padding: 4px;
		border-radius: 4px;
	}
	.close-btn:hover {
		color: var(--text-normal);
		background: var(--bg-modifier-hover);
	}
	.pinned-list {
		flex: 1;
		overflow-y: auto;
		padding: 8px;
	}
	.pinned-empty {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		padding: 40px 16px;
		color: var(--text-muted);
		text-align: center;
		gap: 8px;
	}
	.pinned-item {
		padding: 12px;
		border-radius: 8px;
		background: var(--bg-tertiary);
		margin-bottom: 8px;
	}
	.pinned-item:hover {
		background: var(--bg-modifier-hover);
	}
	.pinned-item-header {
		display: flex;
		align-items: center;
		gap: 8px;
		margin-bottom: 4px;
	}
	.pinned-author {
		font-size: 0.875rem;
		font-weight: 600;
		color: var(--text-normal);
	}
	.pinned-date {
		font-size: 0.75rem;
		color: var(--text-muted);
	}
	.pinned-body {
		font-size: 0.875rem;
		color: var(--text-normal);
		margin: 0 0 8px;
		line-height: 1.4;
		overflow: hidden;
		text-overflow: ellipsis;
		display: -webkit-box;
		-webkit-line-clamp: 3;
		line-clamp: 3;
		-webkit-box-orient: vertical;
	}
	.pinned-item-footer {
		display: flex;
		gap: 8px;
	}
	.jump-btn,
	.unpin-btn {
		font-size: 0.75rem;
		padding: 4px 8px;
		border-radius: 4px;
		border: none;
		cursor: pointer;
		background: none;
	}
	.jump-btn {
		color: var(--brand-500, #5865f2);
	}
	.jump-btn:hover {
		text-decoration: underline;
	}
	.unpin-btn {
		color: var(--text-danger);
	}
	.unpin-btn:hover {
		background: var(--bg-modifier-hover);
	}
</style>
