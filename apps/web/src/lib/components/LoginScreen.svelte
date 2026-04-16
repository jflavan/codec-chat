<script lang="ts">
	import { fade } from 'svelte/transition';
	import { onMount } from 'svelte';
	import { env } from '$env/dynamic/public';
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getOAuthConfig, getGitHubAuthUrl, getDiscordAuthUrl } from '$lib/auth/oauth.js';

	const auth = getAuthStore();
	const siteKey = env.PUBLIC_RECAPTCHA_SITE_KEY ?? '';
	let githubEnabled = $state(false);
	let githubClientId = $state('');
	let discordEnabled = $state(false);
	let discordClientId = $state('');

	let mode = $state<'signin' | 'signup'>('signin');
	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let nickname = $state('');
	let error = $state('');
	let isSubmitting = $state(false);
	let errorRef = $state<HTMLDivElement | null>(null);

	onMount(async () => {
		if (siteKey) {
			const script = document.createElement('script');
			script.src = `https://www.google.com/recaptcha/enterprise.js?render=${siteKey}`;
			script.async = true;
			document.head.appendChild(script);
		}

		const config = await getOAuthConfig();
		if (config) {
			githubEnabled = config.github.enabled;
			githubClientId = config.github.clientId;
			discordEnabled = config.discord.enabled;
			discordClientId = config.discord.clientId;
		}
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
			// Move focus to error message so screen readers announce it
			setTimeout(() => errorRef?.focus(), 0);
			return;
		}

		isSubmitting = true;
		try {
			const action = mode === 'signup' ? 'register' : 'login';
			const recaptchaToken = await getRecaptchaToken(action);
			const response = mode === 'signup'
				? await auth.register(email.trim(), password, nickname.trim(), recaptchaToken)
				: await auth.login(email.trim(), password, recaptchaToken);
			await auth.handleLocalAuth(response);
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Something went wrong.';
			setTimeout(() => errorRef?.focus(), 0);
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
		<div class="logo" aria-hidden="true">
			<span class="bracket">[</span>
			<span class="name">CODEC</span>
			<span class="bracket">]</span>
		</div>
		<p class="tagline">Real-time chat, open source.</p>

		<div class="sign-in-section">
			<p class="sign-in-label" id="sign-in-heading">Sign in to start chatting</p>
			<div id="login-google-button"></div>
			{#if githubEnabled}
				<button class="oauth-btn github-btn" onclick={() => window.location.href = getGitHubAuthUrl(githubClientId)}>
					<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/></svg>
					Continue with GitHub
				</button>
			{/if}
			{#if discordEnabled}
				<button class="oauth-btn discord-btn" onclick={() => window.location.href = getDiscordAuthUrl(discordClientId)}>
					<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor"><path d="M20.317 4.37a19.791 19.791 0 00-4.885-1.515.074.074 0 00-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 00-5.487 0 12.64 12.64 0 00-.617-1.25.077.077 0 00-.079-.037A19.736 19.736 0 003.677 4.37a.07.07 0 00-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 00.031.057 19.9 19.9 0 005.993 3.03.078.078 0 00.084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 00-.041-.106 13.107 13.107 0 01-1.872-.892.077.077 0 01-.008-.128 10.2 10.2 0 00.372-.292.074.074 0 01.077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 01.078.01c.12.098.246.198.373.292a.077.077 0 01-.006.127 12.299 12.299 0 01-1.873.892.077.077 0 00-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 00.084.028 19.839 19.839 0 006.002-3.03.077.077 0 00.032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 00-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/></svg>
					Continue with Discord
				</button>
			{/if}
		</div>

		<div class="divider">
			<span class="divider-line"></span>
			<span class="divider-text">or</span>
			<span class="divider-line"></span>
		</div>

		<form class="auth-form" onsubmit={handleSubmit} aria-labelledby="form-heading" novalidate>
			<h2 class="form-heading" id="form-heading">
				{mode === 'signin' ? 'Sign in with email' : 'Create a new account'}
			</h2>

			<div class="mode-toggle" role="radiogroup" aria-label="Authentication mode">
				<button
					type="button"
					role="radio"
					class="mode-btn"
					class:active={mode === 'signin'}
					aria-checked={mode === 'signin'}
					onclick={() => { mode = 'signin'; error = ''; }}
				>Sign in</button>
				<button
					type="button"
					role="radio"
					class="mode-btn"
					class:active={mode === 'signup'}
					aria-checked={mode === 'signup'}
					onclick={() => { mode = 'signup'; error = ''; }}
				>Create account</button>
			</div>

			{#if error}
				<div
					id="auth-error"
					class="error-message"
					role="alert"
					aria-live="assertive"
					tabindex="-1"
					bind:this={errorRef}
				>{error}</div>
			{/if}

			<div class="field">
				<label class="field-label" for="auth-email">Email address</label>
				<input
					id="auth-email"
					type="email"
					bind:value={email}
					placeholder="you@example.com"
					autocomplete="email"
					required
					aria-required="true"
					aria-describedby={error ? 'auth-error' : undefined}
				/>
			</div>

			<div class="field">
				<label class="field-label" for="auth-password">Password</label>
				<input
					id="auth-password"
					type="password"
					bind:value={password}
					placeholder="Min 8 characters"
					autocomplete={mode === 'signin' ? 'current-password' : 'new-password'}
					required
					aria-required="true"
					aria-describedby={error ? 'auth-error' : undefined}
				/>
			</div>

			{#if mode === 'signup'}
				<div class="field">
					<label class="field-label" for="auth-confirm-password">Confirm password</label>
					<input
						id="auth-confirm-password"
						type="password"
						bind:value={confirmPassword}
						placeholder="Re-enter password"
						autocomplete="new-password"
						required
						aria-required="true"
					/>
				</div>

				<div class="field">
					<label class="field-label" for="auth-nickname">
						Nickname
						<span class="char-counter" class:over={nickname.trim().length > 32} aria-hidden="true">
							{nickname.trim().length}/32
						</span>
					</label>
					<input
						id="auth-nickname"
						type="text"
						bind:value={nickname}
						placeholder="2–32 characters"
						minlength={2}
						maxlength={32}
						autocomplete="username"
						required
						aria-required="true"
						aria-label={`Nickname (${nickname.trim().length} of 32 characters)`}
					/>
				</div>
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

	/* ───── OAuth buttons ───── */

	.oauth-btn {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 10px;
		width: 100%;
		max-width: 240px;
		padding: 10px 16px;
		border: 1px solid var(--border);
		border-radius: 4px;
		background: var(--bg-secondary);
		color: var(--text-header);
		font-family: 'Space Grotesk', monospace;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.15s, border-color 0.15s;
	}

	.oauth-btn:hover {
		background: var(--bg-primary);
		border-color: var(--text-muted);
	}

	.github-btn svg {
		color: var(--text-header);
	}

	.discord-btn svg {
		color: #5865F2;
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

	/* Visually hidden heading — present for screen readers only */
	.form-heading {
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border: 0;
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
