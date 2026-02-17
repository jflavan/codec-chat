<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
</script>

<div class="account-settings" role="tabpanel" aria-labelledby="tab-account">
	<h2 class="section-title">My Account</h2>

	{#if app.me}
		<div class="info-grid">
			<div class="info-row">
				<span class="info-label">Email</span>
				<span class="info-value">{app.me.user.email ?? '—'}</span>
			</div>
			<div class="info-row">
				<span class="info-label">Google Display Name</span>
				<span class="info-value">{app.me.user.displayName}</span>
			</div>
		</div>

		<div class="sign-out-section">
			<button class="sign-out-btn" onclick={() => { app.closeSettings(); app.signOut(); }}>
				Sign Out
			</button>
		</div>
	{/if}
</div>

<style>
	.account-settings {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.section-title {
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		margin: 0;
	}

	.info-grid {
		display: flex;
		flex-direction: column;
		gap: 16px;
		padding: 16px;
		background: var(--bg-secondary);
		border-radius: 8px;
		border: 1px solid var(--border);
	}

	.info-row {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.info-label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.info-value {
		font-size: 15px;
		color: var(--text-normal);
	}

	.sign-out-section {
		padding-top: 8px;
	}

	.sign-out-btn {
		padding: 10px 20px;
		min-height: 44px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 4px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.sign-out-btn:hover {
		opacity: 0.9;
	}
</style>
