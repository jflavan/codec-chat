<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	function truncate(text: string, max: number): string {
		return text.length > max ? text.slice(0, max) + '…' : text;
	}
</script>

<div class="dm-list">
	<h3 class="dm-list-header">Direct Messages</h3>

	{#if app.isLoadingDmConversations}
		<p class="status-text">Loading conversations…</p>
	{:else if app.dmConversations.length === 0}
		<p class="status-text">No conversations yet. Click a friend to start chatting!</p>
	{:else}
		<ul class="list" role="list">
			{#each app.dmConversations as conversation (conversation.id)}
				<li class="dm-item" class:active={conversation.id === app.activeDmChannelId}>
					<button
						class="dm-button"
						onclick={() => app.selectDmConversation(conversation.id)}
						aria-label="Open conversation with {conversation.participant.displayName}"
					>
						{#if conversation.participant.avatarUrl}
							<img class="avatar" src={conversation.participant.avatarUrl} alt="" />
						{:else}
							<div class="avatar-placeholder" aria-hidden="true">
								{conversation.participant.displayName.slice(0, 1).toUpperCase()}
							</div>
						{/if}
						<div class="dm-details">
							<span class="dm-name">{conversation.participant.displayName}</span>
							{#if conversation.lastMessage}
								<span class="dm-preview">
									{truncate(conversation.lastMessage.body, 40)}
								</span>
							{/if}
						</div>					{#if app.unreadDmCounts.get(conversation.id)}
						<span class="unread-badge" aria-label="{app.unreadDmCounts.get(conversation.id)} unread messages">
							{app.unreadDmCounts.get(conversation.id)}
						</span>
					{/if}					</button>
					<button
						class="close-btn"
						onclick={(e: MouseEvent) => { e.stopPropagation(); app.closeDmConversation(conversation.id); }}
						aria-label="Close conversation with {conversation.participant.displayName}"
						title="Close conversation"
					>
						✕
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</div>

<style>
	.dm-list {
		padding: 8px 0;
	}

	.dm-list-header {
		margin: 0;
		padding: 6px 16px 8px;
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.02em;
		color: var(--text-muted);
	}

	.status-text {
		color: var(--text-muted);
		font-size: 13px;
		text-align: center;
		padding: 16px;
		margin: 0;
	}

	.list {
		list-style: none;
		padding: 0;
		margin: 0;
	}

	.dm-item {
		display: flex;
		align-items: center;
		padding: 0 8px;
		border-radius: 4px;
		margin: 0 8px;
		transition: background-color 150ms ease;
	}

	.dm-item:hover {
		background: var(--bg-message-hover);
	}

	.dm-item.active {
		background: var(--bg-message-hover);
	}

	.dm-button {
		flex: 1;
		display: flex;
		align-items: center;
		gap: 10px;
		background: none;
		border: none;
		padding: 8px 4px;
		min-height: 44px;
		cursor: pointer;
		min-width: 0;
		text-align: left;
		font-family: inherit;
	}

	.avatar {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.avatar-placeholder {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.dm-details {
		display: flex;
		flex-direction: column;
		min-width: 0;
	}

	.dm-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.dm-item.active .dm-name {
		color: var(--text-header);
	}

	.dm-preview {
		font-size: 12px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.close-btn {
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-size: 14px;
		cursor: pointer;
		padding: 8px 8px;
		min-width: 36px;
		min-height: 36px;
		border-radius: 3px;
		flex-shrink: 0;
		opacity: 0;
		transition: opacity 150ms ease, background-color 150ms ease, color 150ms ease;
		font-family: inherit;
	}

	.dm-item:hover .close-btn {
		opacity: 1;
	}

	@media (max-width: 768px) {
		.close-btn {
			opacity: 1;
		}
	}

	.close-btn:hover {
		background: var(--danger);
		color: #fff;
	}

	.unread-badge {
		min-width: 18px;
		height: 18px;
		padding: 0 5px;
		border-radius: 9px;
		background: var(--danger);
		color: #fff;
		font-size: 11px;
		font-weight: 700;
		line-height: 18px;
		text-align: center;
		flex-shrink: 0;
		margin-right: 2px;
	}
</style>
