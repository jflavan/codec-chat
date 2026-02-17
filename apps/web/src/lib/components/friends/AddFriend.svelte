<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
	let searchInput = $state('');
	let debounceTimer: ReturnType<typeof setTimeout> | null = null;

	function handleInput(): void {
		if (debounceTimer) clearTimeout(debounceTimer);
		debounceTimer = setTimeout(() => {
			app.searchUsers(searchInput);
		}, 300);
	}
</script>

<div class="add-friend">
	<div class="search-bar">
		<input
			type="text"
			placeholder="Enter a username or email…"
			bind:value={searchInput}
			oninput={handleInput}
			class="search-input"
			aria-label="Search for users"
		/>
	</div>

	<div class="results">
		{#if app.isSearchingUsers}
			<p class="status-text">Searching…</p>
		{:else if searchInput.trim().length < 2}
			<p class="status-text">Type at least 2 characters to search.</p>
		{:else if app.userSearchResults.length === 0}
			<p class="status-text">No users found.</p>
		{:else}
			<ul class="list" role="list">
				{#each app.userSearchResults as user (user.id)}
					<li class="user-item">
						<div class="user-info">
							{#if user.avatarUrl}
								<img class="avatar" src={user.avatarUrl} alt="" />
							{:else}
								<div class="avatar-placeholder" aria-hidden="true">
									{user.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<div class="user-details">
								<span class="user-name">{user.displayName}</span>
								{#if user.email}
									<span class="user-email">{user.email}</span>
								{/if}
							</div>
						</div>
						{#if user.relationshipStatus === 'Accepted'}
							<span class="status-badge friends">Friends</span>
						{:else if user.relationshipStatus === 'Pending'}
							<span class="status-badge pending">Pending</span>
						{:else}
							<button
								class="btn-send"
								onclick={() => app.sendFriendRequest(user.id)}
								aria-label="Send friend request to {user.displayName}"
							>
								Send Request
							</button>
						{/if}
					</li>
				{/each}
			</ul>
		{/if}
	</div>
</div>

<style>
	.add-friend {
		padding: 8px;
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.search-bar {
		padding: 4px 4px 0;
	}

	.search-input {
		width: 100%;
		padding: 12px 12px;
		border-radius: 4px;
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 16px;
		font-family: inherit;
		outline: none;
		box-sizing: border-box;
	}

	.search-input::placeholder {
		color: var(--text-dim);
	}

	.search-input:focus {
		box-shadow: 0 0 0 2px var(--accent);
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

	.user-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		min-height: 44px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.user-item:hover {
		background: var(--bg-message-hover);
	}

	.user-info {
		display: flex;
		align-items: center;
		gap: 10px;
		min-width: 0;
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

	.user-details {
		display: flex;
		flex-direction: column;
		min-width: 0;
	}

	.user-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.user-email {
		font-size: 12px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.btn-send {
		border: none;
		border-radius: 3px;
		padding: 10px 12px;
		min-height: 44px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 600;
		font-size: 13px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
		flex-shrink: 0;
		white-space: nowrap;
	}

	.btn-send:hover {
		background: var(--accent-hover);
	}

	.status-badge {
		font-size: 12px;
		font-weight: 600;
		padding: 4px 10px;
		border-radius: 3px;
		flex-shrink: 0;
		white-space: nowrap;
	}

	.status-badge.friends {
		background: var(--accent);
		color: var(--bg-tertiary);
		opacity: 0.6;
	}

	.status-badge.pending {
		background: var(--bg-secondary);
		color: var(--text-muted);
		border: 1px solid var(--border);
	}
</style>
