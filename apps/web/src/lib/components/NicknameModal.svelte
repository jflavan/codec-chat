<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';

	const auth = getAuthStore();

	const MAX_LEN = 32;
	const MIN_LEN = 2;

	let nickname = $state(auth.me?.user.displayName ?? '');
	let isSubmitting = $state(false);
	let error = $state('');
	let nicknameInput = $state<HTMLInputElement>(undefined!);

	$effect(() => {
		if (nicknameInput) nicknameInput.focus();
	});

	const trimmed = $derived(nickname.trim());
	const isValid = $derived(trimmed.length >= MIN_LEN && trimmed.length <= MAX_LEN);

	async function handleSubmit(e: Event): Promise<void> {
		e.preventDefault();
		if (!isValid || isSubmitting) return;
		error = '';
		isSubmitting = true;
		try {
			await auth.confirmNickname(trimmed);
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Something went wrong.';
		} finally {
			isSubmitting = false;
		}
	}

	function handleKeydown(e: KeyboardEvent): void {
		// Escape does NOT dismiss — nickname is required
		if (e.key === 'Escape') {
			e.preventDefault();
			e.stopPropagation();
		}
	}
</script>

<div
	class="overlay"
	role="dialog"
	aria-modal="true"
	aria-label="Choose your nickname"
	tabindex="-1"
	onkeydown={handleKeydown}
>
	<div class="modal">
		<div class="logo">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>

		<h1 class="heading">Choose your nickname</h1>
		<p class="subtext">This is how others will see you in chat</p>

		<form class="form" onsubmit={handleSubmit}>
			{#if error}
				<div class="error-message">{error}</div>
			{/if}

			<label class="field">
				<span class="field-label">
					Nickname
					<span class="char-counter" class:over={trimmed.length > MAX_LEN}>
						{trimmed.length}/{MAX_LEN}
					</span>
				</span>
				<input
					type="text"
					bind:value={nickname}
					bind:this={nicknameInput}
					placeholder="2–32 characters"
					minlength={MIN_LEN}
					maxlength={MAX_LEN}
					autocomplete="username"
					required
				/>
			</label>

			<button type="submit" class="submit-btn" disabled={!isValid || isSubmitting}>
				{isSubmitting ? 'Setting up...' : 'Continue'}
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

	.char-counter {
		font-weight: 400;
		text-transform: none;
		letter-spacing: 0;
		color: var(--text-dim);
	}

	.char-counter.over {
		color: var(--danger);
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
