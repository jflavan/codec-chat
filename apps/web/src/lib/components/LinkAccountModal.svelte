<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';

	const auth = getAuthStore();

	let password = $state('');
	let isSubmitting = $state(false);
	let error = $state('');
	let passwordInput = $state<HTMLInputElement>(undefined!);
	let dialogEl = $state<HTMLDivElement>(undefined!);
	let errorRef = $state<HTMLDivElement | null>(null);

	$effect(() => {
		if (passwordInput) passwordInput.focus();
	});

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			// Escape dismisses the link account modal (cancel action)
			handleCancel();
		}
		if (e.key === 'Tab') {
			const focusable = dialogEl?.querySelectorAll<HTMLElement>(
				'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
			);
			if (!focusable || focusable.length === 0) return;
			const first = focusable[0];
			const last = focusable[focusable.length - 1];
			if (e.shiftKey && document.activeElement === first) {
				e.preventDefault();
				last.focus();
			} else if (!e.shiftKey && document.activeElement === last) {
				e.preventDefault();
				first.focus();
			}
		}
	}

	async function handleSubmit(e: Event): Promise<void> {
		e.preventDefault();
		if (!password || isSubmitting) return;
		error = '';
		isSubmitting = true;
		try {
			const data = await auth.linkGoogle(auth.linkingEmail, password, auth.pendingGoogleCredential);
			await auth.handleLinkGoogleSuccess(data);
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Something went wrong.';
			setTimeout(() => errorRef?.focus(), 0);
		} finally {
			isSubmitting = false;
		}
	}

	function handleCancel(): void {
		auth.needsLinking = false;
		auth.linkingEmail = '';
		auth.pendingGoogleCredential = '';
		auth.signOut();
	}
</script>

<div
	class="overlay"
	role="dialog"
	aria-modal="true"
	aria-labelledby="link-account-title"
	tabindex="-1"
	bind:this={dialogEl}
	onkeydown={handleKeydown}
>
	<div class="modal">
		<div class="logo" aria-hidden="true">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>

		<h2 id="link-account-title" class="heading">Link your Google account</h2>
		<p class="subtext">
			An account with <strong class="email">{auth.linkingEmail}</strong> already exists.
			Enter your password to link your Google account.
		</p>

		<form class="form" onsubmit={handleSubmit}>
			{#if error}
				<div
					id="link-account-error"
					class="error-message"
					role="alert"
					aria-live="assertive"
					tabindex="-1"
					bind:this={errorRef}
				>{error}</div>
			{/if}

			<div class="field">
				<label class="field-label" for="link-account-password">Password</label>
				<input
					id="link-account-password"
					type="password"
					bind:value={password}
					bind:this={passwordInput}
					placeholder="Your password"
					autocomplete="current-password"
					required
					aria-required="true"
					aria-describedby={error ? 'link-account-error' : undefined}
				/>
			</div>

			<button type="submit" class="submit-btn" disabled={!password || isSubmitting}>
				{isSubmitting ? 'Linking...' : 'Link Account'}
			</button>

			<button type="button" class="cancel-btn" onclick={handleCancel} disabled={isSubmitting}>
				Cancel
			</button>
		</form>
	</div>

	<div class="scanlines"></div>
</div>

<style>
	.overlay {
		position: fixed;
		inset: 0;
		z-index: 200;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-tertiary);
		overflow: auto;
	}

	.scanlines {
		position: fixed;
		inset: 0;
		pointer-events: none;
		background: repeating-linear-gradient(
			to bottom,
			transparent,
			transparent 2px,
			rgba(0, 0, 0, 0.15) 2px,
			rgba(0, 0, 0, 0.15) 4px
		);
	}

	.modal {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 20px;
		z-index: 1;
		padding: 32px;
		width: 100%;
		max-width: 400px;
	}

	/* ───── Logo ───── */

	.logo {
		font-family: 'Space Grotesk', monospace;
		font-size: 56px;
		font-weight: 700;
		letter-spacing: 8px;
		color: var(--accent);
		text-shadow: 0 0 20px rgba(0, 255, 102, 0.5), 0 0 40px rgba(0, 255, 102, 0.2);
		animation: glow 2s ease-in-out infinite alternate;
	}

	.bracket {
		color: var(--text-muted);
	}

	.name {
		color: var(--accent);
	}

	/* ───── Heading / subtext ───── */

	.heading {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 20px;
		font-weight: 700;
		color: var(--text-header);
		letter-spacing: 1px;
		text-align: center;
	}

	.subtext {
		margin: -8px 0 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		color: var(--text-muted);
		text-align: center;
	}

	.email {
		color: var(--accent);
		font-weight: 600;
		font-style: normal;
	}

	/* ───── Form ───── */

	.form {
		display: flex;
		flex-direction: column;
		gap: 14px;
		width: 100%;
	}

	.error-message {
		padding: 8px 12px;
		border-radius: 4px;
		background: rgba(255, 59, 59, 0.12);
		border: 1px solid rgba(255, 59, 59, 0.3);
		color: #ff6b6b;
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.field-label {
		display: flex;
		justify-content: space-between;
		font-family: 'Space Grotesk', monospace;
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 1px;
	}

	.field input {
		padding: 10px 12px;
		border: 1px solid var(--border);
		border-radius: 4px;
		background: var(--bg-secondary);
		color: var(--text-header);
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		outline: none;
		transition: border-color 0.15s;
	}

	.field input::placeholder {
		color: var(--text-dim);
	}

	.field input:focus {
		border-color: var(--accent);
	}

	.submit-btn {
		margin-top: 4px;
		padding: 12px;
		border: none;
		border-radius: 4px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 2px;
		cursor: pointer;
		transition: opacity 0.15s;
	}

	.submit-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.submit-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.cancel-btn {
		padding: 10px;
		border: 1px solid var(--border);
		border-radius: 4px;
		background: transparent;
		color: var(--text-muted);
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 1px;
		cursor: pointer;
		transition: border-color 0.15s, color 0.15s;
	}

	.cancel-btn:hover:not(:disabled) {
		border-color: var(--text-muted);
		color: var(--text-header);
	}

	.cancel-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	/* ───── Animations ───── */

	@keyframes glow {
		from {
			text-shadow: 0 0 20px rgba(0, 255, 102, 0.4), 0 0 40px rgba(0, 255, 102, 0.15);
		}
		to {
			text-shadow: 0 0 30px rgba(0, 255, 102, 0.6), 0 0 60px rgba(0, 255, 102, 0.3);
		}
	}

	@media (max-width: 768px) {
		.logo {
			font-size: 36px;
			letter-spacing: 4px;
		}
	}

	@media (prefers-reduced-motion: reduce) {
		.logo {
			animation: none;
		}
	}
</style>
