<script lang="ts">
	import { fade } from 'svelte/transition';
	import { onMount } from 'svelte';
	import { PUBLIC_RECAPTCHA_SITE_KEY } from '$env/static/public';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
	const siteKey = PUBLIC_RECAPTCHA_SITE_KEY;

	let mode = $state<'signin' | 'signup'>('signin');
	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let nickname = $state('');
	let error = $state('');
	let isSubmitting = $state(false);

	onMount(() => {
		if (!siteKey) return;
		const script = document.createElement('script');
		script.src = `https://www.google.com/recaptcha/enterprise.js?render=${siteKey}`;
		script.async = true;
		document.head.appendChild(script);
		return () => script.remove();
	});

	async function getRecaptchaToken(action: string): Promise<string | undefined> {
		if (!siteKey) return undefined;
		try {
			return await (window as any).grecaptcha.enterprise.execute(siteKey, { action });
		} catch {
			return undefined;
		}
	}

	function validateEmail(v: string): boolean {
		return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
	}

	function validate(): string | null {
		if (!email.trim()) return 'Email is required.';
		if (!validateEmail(email.trim())) return 'Enter a valid email address.';
		if (!password) return 'Password is required.';
		if (password.length < 8) return 'Password must be at least 8 characters.';
		if (mode === 'signup') {
			if (password !== confirmPassword) return 'Passwords do not match.';
			if (nickname.trim().length < 2) return 'Nickname must be at least 2 characters.';
			if (nickname.trim().length > 32) return 'Nickname must be 32 characters or fewer.';
		}
		return null;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		error = '';
		const validationError = validate();
		if (validationError) {
			error = validationError;
			return;
		}

		isSubmitting = true;
		try {
			const action = mode === 'signup' ? 'register' : 'login';
			const recaptchaToken = await getRecaptchaToken(action);
			const response = mode === 'signup'
				? await app.register(email.trim(), password, nickname.trim(), recaptchaToken)
				: await app.login(email.trim(), password, recaptchaToken);
			await app.handleLocalAuth(response);
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Something went wrong.';
		} finally {
			isSubmitting = false;
		}
	}

	function toggleMode() {
		mode = mode === 'signin' ? 'signup' : 'signin';
		error = '';
	}
</script>

<div class="login-screen" transition:fade={{ duration: 300 }}>
	<div class="login-content">
		<div class="logo">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>
		<p class="tagline">Real-time chat, open source.</p>

		<div class="sign-in-section">
			<p class="sign-in-label">Sign in to start chatting</p>
			<div id="login-google-button"></div>
		</div>

		<div class="divider">
			<span class="divider-line"></span>
			<span class="divider-text">or</span>
			<span class="divider-line"></span>
		</div>

		<form class="auth-form" onsubmit={handleSubmit}>
			<div class="mode-toggle">
				<button
					type="button"
					class="mode-btn"
					class:active={mode === 'signin'}
					onclick={() => { mode = 'signin'; error = ''; }}
				>Sign in</button>
				<button
					type="button"
					class="mode-btn"
					class:active={mode === 'signup'}
					onclick={() => { mode = 'signup'; error = ''; }}
				>Create account</button>
			</div>

			{#if error}
				<div class="error-message">{error}</div>
			{/if}

			<label class="field">
				<span class="field-label">Email</span>
				<input
					type="email"
					bind:value={email}
					placeholder="you@example.com"
					autocomplete="email"
					required
				/>
			</label>

			<label class="field">
				<span class="field-label">Password</span>
				<input
					type="password"
					bind:value={password}
					placeholder="Min 8 characters"
					autocomplete={mode === 'signin' ? 'current-password' : 'new-password'}
					required
				/>
			</label>

			{#if mode === 'signup'}
				<label class="field">
					<span class="field-label">Confirm password</span>
					<input
						type="password"
						bind:value={confirmPassword}
						placeholder="Re-enter password"
						autocomplete="new-password"
						required
					/>
				</label>

				<label class="field">
					<span class="field-label">
						Nickname
						<span class="char-counter" class:over={nickname.trim().length > 32}>
							{nickname.trim().length}/32
						</span>
					</span>
					<input
						type="text"
						bind:value={nickname}
						placeholder="2–32 characters"
						minlength={2}
						maxlength={32}
						autocomplete="username"
						required
					/>
				</label>
			{/if}

			<button type="submit" class="submit-btn" disabled={isSubmitting}>
				{#if isSubmitting}
					Working...
				{:else if mode === 'signin'}
					Sign in
				{:else}
					Create account
				{/if}
			</button>

			<p class="toggle-text">
				{#if mode === 'signin'}
					Don't have an account?
					<button type="button" class="toggle-link" onclick={toggleMode}>Create one</button>
				{:else}
					Already have an account?
					<button type="button" class="toggle-link" onclick={toggleMode}>Sign in</button>
				{/if}
			</p>
		</form>
		{#if siteKey}
			<p class="recaptcha-branding">
				This site is protected by reCAPTCHA and the Google
				<a href="https://policies.google.com/privacy" target="_blank" rel="noopener">Privacy Policy</a> and
				<a href="https://policies.google.com/terms" target="_blank" rel="noopener">Terms of Service</a> apply.
			</p>
		{/if}
	</div>
	<div class="scanlines"></div>
</div>

<style>
	.login-screen {
		position: fixed;
		inset: 0;
		z-index: 100;
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

	.login-content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 20px;
		z-index: 1;
		padding: 32px;
		width: 100%;
		max-width: 400px;
	}

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

	.tagline {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 16px;
		color: var(--text-muted);
		letter-spacing: 2px;
	}

	.sign-in-section {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 16px;
		margin-top: 8px;
	}

	.sign-in-label {
		margin: 0;
		font-size: 14px;
		font-weight: 600;
		color: var(--text-header);
		letter-spacing: 1px;
		text-transform: uppercase;
	}

	/* ───── Divider ───── */

	.divider {
		display: flex;
		align-items: center;
		gap: 12px;
		width: 100%;
	}

	.divider-line {
		flex: 1;
		height: 1px;
		background: var(--border);
	}

	.divider-text {
		font-family: 'Space Grotesk', monospace;
		font-size: 12px;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 2px;
	}

	/* ───── Auth form ───── */

	.auth-form {
		display: flex;
		flex-direction: column;
		gap: 14px;
		width: 100%;
	}

	.mode-toggle {
		display: flex;
		border: 1px solid var(--border);
		border-radius: 4px;
		overflow: hidden;
	}

	.mode-btn {
		flex: 1;
		padding: 8px 0;
		border: none;
		background: var(--bg-secondary);
		color: var(--text-muted);
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
		font-weight: 600;
		letter-spacing: 1px;
		text-transform: uppercase;
		cursor: pointer;
		transition: background 0.15s, color 0.15s;
	}

	.mode-btn.active {
		background: var(--accent);
		color: var(--bg-tertiary);
	}

	.mode-btn:not(.active):hover {
		background: var(--bg-primary);
		color: var(--text-header);
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

	.toggle-text {
		margin: 0;
		text-align: center;
		font-family: 'Space Grotesk', monospace;
		font-size: 13px;
		color: var(--text-muted);
	}

	.toggle-link {
		all: unset;
		color: var(--accent);
		cursor: pointer;
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.toggle-link:hover {
		color: var(--accent-hover);
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

		.tagline {
			font-size: 14px;
		}
	}

	@media (prefers-reduced-motion: reduce) {
		.logo {
			animation: none;
		}
	}

	/* ───── reCAPTCHA ───── */

	:global(.grecaptcha-badge) {
		visibility: hidden !important;
	}

	.recaptcha-branding {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 11px;
		color: var(--text-dim);
		text-align: center;
		line-height: 1.5;
	}

	.recaptcha-branding a {
		color: var(--text-muted);
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.recaptcha-branding a:hover {
		color: var(--accent);
	}
</style>
