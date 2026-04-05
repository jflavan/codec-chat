<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import DeleteAccountModal from './DeleteAccountModal.svelte';

	const auth = getAuthStore();
	const ui = getUIStore();

	let showDeleteModal = $state(false);
</script>

<div class="account-settings" role="tabpanel" aria-labelledby="tab-account">
	<h2 class="section-title">My Account</h2>

	{#if auth.me}
		<div class="info-grid">
			<div class="info-row">
				<span class="info-label">Email</span>
				<span class="info-value">{auth.me.user.email ?? '—'}</span>
			</div>
			<div class="info-row">
				<span class="info-label">Google Display Name</span>
				<span class="info-value">{auth.me.user.displayName}</span>
			</div>
		</div>

		<div class="sign-out-section">
			<button class="sign-out-btn" onclick={() => { ui.closeSettings(); auth.signOut(); }}>
				Sign Out
			</button>
		</div>

		<div class="danger-zone">
			<h3 class="danger-title">Delete Account</h3>
			<p class="danger-description">
				Permanently delete your account. Your messages will remain but show as "Deleted User."
			</p>
			<button class="delete-account-btn" onclick={() => showDeleteModal = true}>
				Delete Account
			</button>
		</div>
	{/if}
</div>

{#if showDeleteModal}
	<DeleteAccountModal onclose={() => showDeleteModal = false} />
{/if}

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
		padding: 8px 16px;
		min-height: 44px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.sign-out-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.sign-out-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.danger-zone {
		margin-top: 16px;
		padding: 16px;
		border: 1px solid var(--danger);
		border-radius: 8px;
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.05);
	}

	.danger-title {
		font-size: 16px;
		font-weight: 700;
		color: var(--danger);
		margin: 0 0 4px;
	}

	.danger-description {
		font-size: 14px;
		color: var(--text-muted);
		margin: 0 0 12px;
	}

	.delete-account-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: transparent;
		color: var(--danger);
		border: 1px solid var(--danger);
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.delete-account-btn:hover {
		background: var(--danger);
		color: #fff;
	}
</style>
