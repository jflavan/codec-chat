<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { ApiError } from '$lib/api/client.js';
	import { initGoogleIdentity } from '$lib/auth/google.js';

	let {
		onclose
	}: {
		onclose: () => void;
	} = $props();

	const auth = getAuthStore();
	const ui = getUIStore();

	let password = $state('');
	let confirmationText = $state('');
	let error = $state('');
	let isDeleting = $state(false);
	let ownedServers = $state<{ id: string; name: string }[]>([]);
	let googleCredential = $state<string | null>(null);

	const hasPassword = $derived(auth.authType === 'local');
	const hasGoogle = $derived(!!auth.me?.user.googleSubject);
	const isOAuthOnly = $derived(!hasPassword && !hasGoogle);

	const needsPassword = $derived(hasPassword);
	const needsGoogle = $derived(hasGoogle && !hasPassword);

	const canSubmit = $derived(
		confirmationText === 'DELETE' &&
		!isDeleting &&
		(needsPassword ? password.length > 0 : true) &&
		(needsGoogle ? googleCredential !== null : true)
	);

	function handleGoogleCredential(credential: string) {
		googleCredential = credential;
	}

	$effect(() => {
		if (needsGoogle) {
			initGoogleIdentity(
				auth.googleClientId,
				(token) => handleGoogleCredential(token),
				{ renderButtonIds: ['delete-google-button'], autoSelect: false }
			);
		}
	});

	async function handleDelete() {
		error = '';
		isDeleting = true;
		ownedServers = [];

		try {
			await auth.deleteAccount(
				needsPassword ? password : undefined,
				needsGoogle ? (googleCredential ?? undefined) : undefined
			);
			onclose();
			ui.closeSettings();
		} catch (e) {
			if (e instanceof ApiError) {
				error = e.message ?? 'Failed to delete account.';
				if (Array.isArray(e.data?.ownedServers)) {
					ownedServers = e.data.ownedServers as { id: string; name: string }[];
				}
			} else {
				error = 'An unexpected error occurred.';
			}
		} finally {
			isDeleting = false;
		}
	}
</script>

<div class="modal-backdrop" role="presentation" onclick={onclose}>
	<div class="modal" role="dialog" aria-modal="true" aria-labelledby="delete-title" onclick={(e) => e.stopPropagation()}>
		<h2 id="delete-title" class="modal-title">Delete Account</h2>

		<div class="warning">
			<p><strong>This action is permanent and cannot be undone.</strong></p>
			<ul>
				<li>Your messages will remain but show as "Deleted User"</li>
				<li>All server memberships will be removed</li>
				<li>All friendships will be removed</li>
				<li>Your account data will be permanently erased</li>
			</ul>
		</div>

		{#if ownedServers.length > 0}
			<div class="owned-servers-warning">
				<p>You must transfer ownership of these servers before deleting your account:</p>
				<ul>
					{#each ownedServers as server}
						<li>{server.name}</li>
					{/each}
				</ul>
			</div>
		{:else}
			{#if needsPassword}
				<label class="field">
					<span class="field-label">Password</span>
					<input
						type="password"
						bind:value={password}
						placeholder="Enter your password"
						autocomplete="current-password"
					/>
				</label>
			{:else if needsGoogle}
				<div class="field">
					<span class="field-label">Re-authenticate with Google</span>
					{#if googleCredential}
						<p class="google-verified">Google identity verified</p>
					{:else}
						<div id="delete-google-button"></div>
					{/if}
				</div>
			{/if}

			<label class="field">
				<span class="field-label">Type <strong>DELETE</strong> to confirm</span>
				<input
					type="text"
					bind:value={confirmationText}
					placeholder="DELETE"
					autocomplete="off"
				/>
			</label>

			{#if error}
				<p class="error">{error}</p>
			{/if}

			<div class="actions">
				<button class="cancel-btn" onclick={onclose} disabled={isDeleting}>Cancel</button>
				<button
					class="delete-btn"
					onclick={handleDelete}
					disabled={!canSubmit}
				>
					{isDeleting ? 'Deleting...' : 'Delete My Account'}
				</button>
			</div>
		{/if}
	</div>
</div>

<style>
	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.7);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
	}

	.modal {
		background: var(--bg-primary);
		border-radius: 8px;
		padding: 24px;
		max-width: 440px;
		width: 90%;
		max-height: 80vh;
		overflow-y: auto;
	}

	.modal-title {
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		margin: 0 0 16px;
	}

	.warning {
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.1);
		border: 1px solid var(--danger);
		border-radius: 4px;
		padding: 12px 16px;
		margin-bottom: 16px;
		font-size: 14px;
		color: var(--text-normal);
	}

	.warning ul {
		margin: 8px 0 0;
		padding-left: 20px;
	}

	.warning li {
		margin: 4px 0;
	}

	.owned-servers-warning {
		background: rgba(var(--danger-rgb, 237, 66, 69), 0.1);
		border: 1px solid var(--danger);
		border-radius: 4px;
		padding: 12px 16px;
		font-size: 14px;
		color: var(--text-normal);
	}

	.owned-servers-warning ul {
		margin: 8px 0 0;
		padding-left: 20px;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 4px;
		margin-bottom: 12px;
	}

	.field-label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.field input {
		padding: 10px 12px;
		background: var(--bg-tertiary);
		border: 1px solid var(--border);
		border-radius: 4px;
		color: var(--text-normal);
		font-size: 14px;
		outline: none;
	}

	.field input:focus {
		border-color: var(--accent);
	}

	.google-verified {
		color: var(--text-positive, #43b581);
		font-size: 14px;
		margin: 4px 0 0;
	}

	.error {
		color: var(--danger);
		font-size: 13px;
		margin: 0 0 12px;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 8px;
		margin-top: 16px;
	}

	.cancel-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: transparent;
		color: var(--text-normal);
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
	}

	.cancel-btn:hover {
		text-decoration: underline;
	}

	.delete-btn {
		padding: 8px 16px;
		min-height: 38px;
		background: var(--danger);
		color: #fff;
		border: none;
		border-radius: 3px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.delete-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.delete-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
</style>
