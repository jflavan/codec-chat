<script lang="ts">
	import { onMount } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { validateState } from '$lib/auth/oauth.js';

	const app = getAppState();
	let error = $state('');

	onMount(async () => {
		const params = new URLSearchParams(window.location.search);
		const code = params.get('code');
		const state = params.get('state');
		const errorParam = params.get('error');

		if (errorParam) {
			error = `GitHub login failed: ${errorParam}`;
			return;
		}

		if (!code || !state || !validateState(state)) {
			error = 'Invalid OAuth callback. Please try again.';
			return;
		}

		try {
			await app.handleOAuthCallback('github', code);
			// Navigate to app root
			window.history.replaceState({}, '', '/');
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'GitHub authentication failed.';
		}
	});
</script>

<div class="oauth-callback">
	{#if error}
		<p class="error">{error}</p>
		<a href="/">Back to login</a>
	{:else}
		<p>Signing in with GitHub...</p>
	{/if}
</div>

<style>
	.oauth-callback {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		height: 100vh;
		font-family: 'Space Grotesk', monospace;
		color: var(--text-header);
		background: var(--bg-tertiary);
		gap: 16px;
	}
	.error {
		color: #ff6b6b;
	}
	a {
		color: var(--accent);
	}
</style>
