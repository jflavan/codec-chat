<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();

	function formatDate(dateStr: string): string {
		return new Date(dateStr).toLocaleDateString(undefined, {
			year: 'numeric',
			month: 'short',
			day: 'numeric'
		});
	}
</script>

<div class="friends-list">
	{#if app.isLoadingFriends}
		<p class="status-text">Loading friends…</p>
	{:else if app.friends.length === 0}
		<p class="status-text">No friends yet. Send a friend request to get started!</p>
	{:else}
		<ul class="list" role="list">
			{#each app.friends as friend (friend.friendshipId)}
				<li class="friend-item">
					<button
						class="friend-info"
						onclick={() => app.openDmWithUser(friend.user.id)}
						aria-label="Message {friend.user.displayName}"
					>
						{#if friend.user.avatarUrl}
							<img class="avatar" src={friend.user.avatarUrl} alt="" />
						{:else}
							<div class="avatar-placeholder" aria-hidden="true">
								{friend.user.displayName.slice(0, 1).toUpperCase()}
							</div>
						{/if}
						<div class="friend-details">
							<span class="friend-name">{friend.user.displayName}</span>
							<span class="friend-since">Friends since {formatDate(friend.since)}</span>
						</div>
					</button>
					<button
						class="btn-danger"
						onclick={() => app.removeFriend(friend.friendshipId)}
						aria-label="Remove {friend.user.displayName}"
						title="Remove friend"
					>
						✕
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</div>

<style>
	.friends-list {
		padding: 8px;
	}

	.status-text {
		color: var(--text-muted);
		font-size: 13px;
		text-align: center;
		padding: 24px 16px;
		margin: 0;
	}

	.list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
	}

	.friend-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		min-height: 44px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.friend-item:hover {
		background: var(--bg-message-hover);
	}

	.friend-info {
		display: flex;
		align-items: center;
		gap: 10px;
		min-width: 0;
		background: none;
		border: none;
		padding: 0;
		cursor: pointer;
		font-family: inherit;
		text-align: left;
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

	.friend-details {
		display: flex;
		flex-direction: column;
		min-width: 0;
	}

	.friend-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.friend-since {
		font-size: 12px;
		color: var(--text-muted);
	}

	.btn-danger {
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-size: 16px;
		cursor: pointer;
		padding: 8px 10px;
		min-width: 44px;
		min-height: 44px;
		border-radius: 4px;
		transition: background-color 150ms ease, color 150ms ease;
		flex-shrink: 0;
		font-family: inherit;
		display: grid;
		place-items: center;
	}

	.btn-danger:hover {
		background: var(--danger);
		color: #fff;
	}
</style>
