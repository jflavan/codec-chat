<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
</script>

<div class="user-panel">
	{#if app.me}
		<div class="user-panel-info">
			{#if app.me.user.avatarUrl}
				<img class="user-panel-avatar" src={app.me.user.avatarUrl} alt="Your avatar" />
			{:else}
				<div class="user-panel-avatar placeholder" aria-hidden="true">
					{app.me.user.displayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<div class="user-panel-names">
				<span class="user-panel-display">{app.me.user.displayName}</span>
				{#if app.currentServerRole}
					<span class="user-panel-role">{app.currentServerRole}</span>
				{/if}
			</div>
		</div>
		<button class="sign-out-btn" onclick={() => app.signOut()} aria-label="Sign out" title="Sign out">
			<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M5 5h7V3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h7v-2H5V5zm16 7-4-4v3H9v2h8v3l4-4z"/>
			</svg>
		</button>
	{:else}
		<div id="google-button" class="google-button"></div>
		<span class="user-panel-status">{app.status}</span>
	{/if}
</div>

<style>
	.user-panel {
		flex-shrink: 0;
		padding: 8px;
		background: var(--bg-tertiary);
		border-top: 1px solid var(--border);
		display: flex;
		align-items: center;
		gap: 8px;
		min-height: 52px;
	}

	.user-panel-info {
		display: flex;
		align-items: center;
		gap: 8px;
		overflow: hidden;
		flex: 1;
		min-width: 0;
	}

	.sign-out-btn {
		background: none;
		border: none;
		padding: 4px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.sign-out-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	.user-panel-avatar {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.user-panel-avatar.placeholder {
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
	}

	.user-panel-names {
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.user-panel-display {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.user-panel-role {
		font-size: 12px;
		color: var(--text-muted);
	}

	.user-panel-status {
		font-size: 12px;
		color: var(--text-muted);
	}
</style>
