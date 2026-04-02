<script lang="ts">
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';

	const auth = getAuthStore();

	let resendCooldown = $state(0);
	let resendError = $state('');
	let resendSuccess = $state(false);
	let checking = $state(false);
	let intervalId: ReturnType<typeof setInterval> | null = null;

	function startCooldown() {
		resendCooldown = 120;
		intervalId = setInterval(() => {
			resendCooldown--;
			if (resendCooldown <= 0 && intervalId) {
				clearInterval(intervalId);
				intervalId = null;
			}
		}, 1000);
	}

	async function handleResend() {
		resendError = '';
		resendSuccess = false;
		try {
			await auth.resendVerification();
			resendSuccess = true;
			startCooldown();
		} catch (err: unknown) {
			if (err instanceof Error && err.message.includes('429')) {
				resendError = 'Please wait before requesting another email.';
				startCooldown();
			} else {
				resendError = err instanceof Error ? err.message : 'Failed to resend.';
			}
		}
	}

	async function handleCheck() {
		checking = true;
		const verified = await auth.checkEmailVerified();
		if (!verified) {
			checking = false;
		}
	}

	function formatTime(seconds: number): string {
		const m = Math.floor(seconds / 60);
		const s = seconds % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	}
</script>

<div class="verification-overlay">
	<div class="verification-card">
		<div class="icon">&#9993;</div>
		<h2>Check your email</h2>
		<p>We sent a verification link to your email address. Click the link to verify your account and start using Codec.</p>

		<div class="actions">
			<button class="btn-primary" onclick={handleCheck} disabled={checking}>
				{checking ? 'Checking...' : "I've verified my email"}
			</button>

			<button
				class="btn-secondary"
				onclick={handleResend}
				disabled={resendCooldown > 0}
			>
				{resendCooldown > 0
					? `Resend in ${formatTime(resendCooldown)}`
					: 'Resend verification email'}
			</button>
		</div>

		{#if resendSuccess}
			<p class="success">Verification email sent!</p>
		{/if}
		{#if resendError}
			<p class="error">{resendError}</p>
		{/if}

		<button class="btn-link" onclick={() => auth.signOut()}>Sign out</button>
	</div>
</div>

<style>
	.verification-overlay {
		position: fixed;
		inset: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--bg-primary, #313338);
		z-index: 100;
	}

	.verification-card {
		text-align: center;
		max-width: 440px;
		padding: 40px;
	}

	.icon {
		font-size: 48px;
		margin-bottom: 16px;
	}

	h2 {
		color: var(--text-primary, #f2f3f5);
		margin-bottom: 8px;
	}

	p {
		color: var(--text-secondary, #b5bac1);
		line-height: 1.5;
		margin-bottom: 24px;
	}

	.actions {
		display: flex;
		flex-direction: column;
		gap: 12px;
		margin-bottom: 16px;
	}

	.btn-primary {
		padding: 12px 24px;
		background: var(--brand-primary, #5865f2);
		color: white;
		border: none;
		border-radius: 4px;
		font-size: 16px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-primary:hover {
		background: var(--brand-hover, #4752c4);
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		padding: 12px 24px;
		background: var(--bg-secondary, #2b2d31);
		color: var(--text-primary, #f2f3f5);
		border: 1px solid var(--border-subtle, #3f4147);
		border-radius: 4px;
		font-size: 14px;
		cursor: pointer;
	}

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-link {
		background: none;
		border: none;
		color: var(--text-muted, #949ba4);
		font-size: 14px;
		cursor: pointer;
		text-decoration: underline;
	}

	.btn-link:hover {
		color: var(--text-secondary, #b5bac1);
	}

	.success {
		color: var(--status-positive, #23a55a);
		font-size: 14px;
	}

	.error {
		color: var(--status-danger, #f23f43);
		font-size: 14px;
	}
</style>
