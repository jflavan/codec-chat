<script lang="ts">
	import { goto } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import { onMount } from 'svelte';
	import { adminApi } from '$lib/api/client';
	import { setToken, verifyAdmin, clearToken } from '$lib/auth/auth';

	const siteKey = env.PUBLIC_RECAPTCHA_SITE_KEY ?? '';
	const googleClientId = env.PUBLIC_GOOGLE_CLIENT_ID ?? '';
	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);

	onMount(() => {
		if (siteKey) {
			const script = document.createElement('script');
			script.src = `https://www.google.com/recaptcha/enterprise.js?render=${siteKey}`;
			script.async = true;
			document.head.appendChild(script);
		}

		if (googleClientId) {
			const script = document.createElement('script');
			script.src = 'https://accounts.google.com/gsi/client';
			script.async = true;
			script.defer = true;
			script.onload = () => {
				const google = (window as any).google;
				if (!google?.accounts?.id) return;
				google.accounts.id.initialize({
					client_id: googleClientId,
					callback: (response: { credential: string }) => handleGoogleCredential(response.credential),
					auto_select: false,
					log_level: 'none'
				});
				google.accounts.id.renderButton(
					document.getElementById('google-signin-btn'),
					{ theme: 'outline', size: 'large', width: 320, text: 'signin_with' }
				);
			};
			document.head.appendChild(script);
		}
	});

	async function handleGoogleCredential(credential: string) {
		error = '';
		submitting = true;
		try {
			const result = await adminApi.googleAuth(credential);
			setToken(result.accessToken);
			localStorage.setItem('admin_refresh_token', result.refreshToken);

			const isAdmin = await verifyAdmin();
			if (!isAdmin) {
				clearToken();
				error = 'Access denied. You are not a global admin.';
				return;
			}
			await goto('/');
		} catch (e: any) {
			error = e.message || 'Google sign-in failed';
		} finally {
			submitting = false;
		}
	}

	async function getRecaptchaToken(action: string): Promise<string | undefined> {
		if (!siteKey) return undefined;
		try {
			return await (window as any).grecaptcha.enterprise.execute(siteKey, { action });
		} catch {
			return undefined;
		}
	}

	async function handleLogin() {
		error = '';
		submitting = true;
		try {
			const recaptchaToken = await getRecaptchaToken('login');
			const result = await adminApi.login(email, password, recaptchaToken);
			setToken(result.accessToken);
			localStorage.setItem('admin_refresh_token', result.refreshToken);

			const isAdmin = await verifyAdmin();
			if (!isAdmin) {
				clearToken();
				error = 'Access denied. You are not a global admin.';
				return;
			}
			await goto('/');
		} catch (e: any) {
			error = e.message || 'Login failed';
		} finally {
			submitting = false;
		}
	}
</script>

<div class="login-container">
	<div class="login-card">
		<h1>Codec Admin</h1>
		<p class="subtitle">Sign in to access the admin panel</p>

		{#if error}
			<div class="error">{error}</div>
		{/if}

		{#if googleClientId}
			<div id="google-signin-btn" class="google-btn"></div>
			<div class="divider"><span>or</span></div>
		{/if}

		<form onsubmit={(e) => { e.preventDefault(); handleLogin(); }}>
			<input type="email" placeholder="Email" bind:value={email} required />
			<input type="password" placeholder="Password" bind:value={password} required />
			<button type="submit" disabled={submitting}>{submitting ? 'Signing in...' : 'Sign In'}</button>
		</form>
	</div>
</div>

<style>
	.login-container { display: flex; align-items: center; justify-content: center; min-height: 100vh; background: var(--bg-primary); }
	.login-card { background: var(--bg-secondary); padding: 40px; border-radius: 12px; width: 400px; border: 1px solid var(--border); }
	h1 { font-size: 24px; margin-bottom: 8px; }
	.subtitle { color: var(--text-muted); margin-bottom: 24px; }
	.error { background: rgba(239, 68, 68, 0.1); border: 1px solid var(--danger); color: var(--danger); padding: 10px 14px; border-radius: var(--radius); margin-bottom: 16px; font-size: 14px; }
	.google-btn { display: flex; justify-content: center; }
	.divider { display: flex; align-items: center; gap: 12px; margin: 16px 0; color: var(--text-muted); font-size: 13px; }
	.divider::before, .divider::after { content: ''; flex: 1; height: 1px; background: var(--border); }
	form { display: flex; flex-direction: column; gap: 12px; }
	input { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); padding: 10px 14px; color: var(--text-primary); font-size: 14px; }
	input:focus { outline: none; border-color: var(--accent); }
	button { background: var(--accent); color: white; border: none; border-radius: var(--radius); padding: 10px 14px; font-size: 14px; font-weight: 600; }
	button:hover:not(:disabled) { background: var(--accent-hover); }
	button:disabled { opacity: 0.6; }
</style>
