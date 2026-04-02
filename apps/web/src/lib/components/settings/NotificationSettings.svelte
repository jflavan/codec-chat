<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';

	const auth = getAuthStore();

	let isToggling = $state(false);

	async function togglePush() {
		isToggling = true;
		try {
			if (auth.pushNotificationsEnabled) {
				await auth.disablePushNotifications();
			} else {
				await auth.enablePushNotifications();
			}
		} finally {
			isToggling = false;
		}
	}
</script>

<section class="settings-section">
	<h2 class="settings-heading">Notifications</h2>

	<div class="setting-row">
		<div class="setting-info">
			<h3 class="setting-label">Push Notifications</h3>
			<p class="setting-description">
				Receive notifications for DMs, mentions, and friend requests even when Codec is in the background.
			</p>
			{#if !auth.pushNotificationsSupported}
				<p class="setting-warning">
					Push notifications are not supported in this browser.
				</p>
			{/if}
		</div>
		<button
			class="toggle-btn"
			class:active={auth.pushNotificationsEnabled}
			disabled={!auth.pushNotificationsSupported || isToggling}
			onclick={togglePush}
			aria-label={auth.pushNotificationsEnabled ? 'Disable push notifications' : 'Enable push notifications'}
		>
			<span class="toggle-thumb"></span>
		</button>
	</div>
</section>

<style>
	.settings-section {
		max-width: 600px;
	}

	.settings-heading {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 16px;
	}

	.setting-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 16px;
		padding: 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
	}

	.setting-info {
		flex: 1;
		min-width: 0;
	}

	.setting-label {
		font-size: 16px;
		font-weight: 500;
		color: var(--text-header);
		margin: 0 0 4px;
	}

	.setting-description {
		font-size: 14px;
		color: var(--text-muted);
		margin: 0;
		line-height: 1.4;
	}

	.setting-warning {
		font-size: 13px;
		color: var(--text-danger, #ff6b6b);
		margin: 8px 0 0;
	}

	.toggle-btn {
		position: relative;
		width: 44px;
		height: 24px;
		border-radius: 12px;
		border: none;
		background: var(--bg-tertiary);
		cursor: pointer;
		flex-shrink: 0;
		transition: background-color 200ms ease;
		padding: 0;
	}

	.toggle-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.toggle-btn.active {
		background: var(--accent);
	}

	.toggle-thumb {
		position: absolute;
		top: 2px;
		left: 2px;
		width: 20px;
		height: 20px;
		border-radius: 50%;
		background: white;
		transition: transform 200ms ease;
	}

	.toggle-btn.active .toggle-thumb {
		transform: translateX(20px);
	}
</style>
