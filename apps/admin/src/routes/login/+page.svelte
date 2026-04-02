<script lang="ts">
	import { goto } from '$app/navigation';
	import { adminApi } from '$lib/api/client';
	import { setToken, verifyAdmin, clearToken } from '$lib/auth/auth';

	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);

	async function handleLogin() {
		error = '';
		submitting = true;
		try {
			const result = await adminApi.login(email, password);
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
	form { display: flex; flex-direction: column; gap: 12px; }
	input { background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius); padding: 10px 14px; color: var(--text-primary); font-size: 14px; }
	input:focus { outline: none; border-color: var(--accent); }
	button { background: var(--accent); color: white; border: none; border-radius: var(--radius); padding: 10px 14px; font-size: 14px; font-weight: 600; }
	button:hover:not(:disabled) { background: var(--accent-hover); }
	button:disabled { opacity: 0.6; }
</style>
