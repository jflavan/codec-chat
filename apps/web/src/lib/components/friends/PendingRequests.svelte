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

<div class="pending-requests">
	{#if app.isLoadingFriends}
		<p class="status-text">Loading requests…</p>
	{:else}
		{#if app.incomingRequests.length > 0}
			<h3 class="section-heading">Incoming — {app.incomingRequests.length}</h3>
			<ul class="list" role="list">
				{#each app.incomingRequests as req (req.id)}
					<li class="request-item">
						<div class="request-info">
							{#if req.requester.avatarUrl}
								<img class="avatar" src={req.requester.avatarUrl} alt="" />
							{:else}
								<div class="avatar-placeholder" aria-hidden="true">
									{req.requester.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<div class="request-details">
								<span class="request-name">{req.requester.displayName}</span>
								<span class="request-date">{formatDate(req.createdAt)}</span>
							</div>
						</div>
						<div class="request-actions">
							<button
								class="btn-accept"
								onclick={() => app.acceptFriendRequest(req.id)}
								aria-label="Accept request from {req.requester.displayName}"
								title="Accept"
							>
								✓
							</button>
							<button
								class="btn-decline"
								onclick={() => app.declineFriendRequest(req.id)}
								aria-label="Decline request from {req.requester.displayName}"
								title="Decline"
							>
								✕
							</button>
						</div>
					</li>
				{/each}
			</ul>
		{/if}

		{#if app.outgoingRequests.length > 0}
			<h3 class="section-heading">Outgoing — {app.outgoingRequests.length}</h3>
			<ul class="list" role="list">
				{#each app.outgoingRequests as req (req.id)}
					<li class="request-item">
						<div class="request-info">
							{#if req.recipient.avatarUrl}
								<img class="avatar" src={req.recipient.avatarUrl} alt="" />
							{:else}
								<div class="avatar-placeholder" aria-hidden="true">
									{req.recipient.displayName.slice(0, 1).toUpperCase()}
								</div>
							{/if}
							<div class="request-details">
								<span class="request-name">{req.recipient.displayName}</span>
								<span class="request-date">{formatDate(req.createdAt)}</span>
							</div>
						</div>
						<button
							class="btn-decline"
							onclick={() => app.cancelFriendRequest(req.id)}
							aria-label="Cancel request to {req.recipient.displayName}"
							title="Cancel"
						>
							✕
						</button>
					</li>
				{/each}
			</ul>
		{/if}

		{#if app.incomingRequests.length === 0 && app.outgoingRequests.length === 0}
			<p class="status-text">No pending friend requests.</p>
		{/if}
	{/if}
</div>

<style>
	.pending-requests {
		padding: 8px;
	}

	.status-text {
		color: var(--text-muted);
		font-size: 13px;
		text-align: center;
		padding: 24px 16px;
		margin: 0;
	}

	.section-heading {
		padding: 12px 12px 4px;
		margin: 0;
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
	}

	.request-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 8px 12px;
		min-height: 44px;
		border-radius: 4px;
		transition: background-color 150ms ease;
	}

	.request-item:hover {
		background: var(--bg-message-hover);
	}

	.request-info {
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

	.request-details {
		display: flex;
		flex-direction: column;
		min-width: 0;
	}

	.request-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.request-date {
		font-size: 12px;
		color: var(--text-muted);
	}

	.request-actions {
		display: flex;
		gap: 4px;
		flex-shrink: 0;
	}

	.btn-accept {
		border: none;
		background: transparent;
		color: var(--accent);
		font-size: 18px;
		cursor: pointer;
		padding: 8px 10px;
		min-width: 44px;
		min-height: 44px;
		border-radius: 3px;
		transition: background-color 150ms ease, color 150ms ease;
		font-family: inherit;
		display: grid;
		place-items: center;
	}

	.btn-accept:hover {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.btn-decline {
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-size: 16px;
		cursor: pointer;
		padding: 8px 10px;
		min-width: 44px;
		min-height: 44px;
		border-radius: 3px;
		transition: background-color 150ms ease, color 150ms ease;
		font-family: inherit;
		display: grid;
		place-items: center;
	}

	.btn-decline:hover {
		background: var(--danger);
		color: #fff;
	}
</style>
