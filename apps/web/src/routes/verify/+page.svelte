<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import { ApiClient } from '$lib/api/client.js';

	const apiBaseUrl = env.PUBLIC_API_BASE_URL ?? '';

	let status = $state<'loading' | 'success' | 'error'>('loading');
	let errorMessage = $state('');

	onMount(async () => {
		const token = $page.url.searchParams.get('token');
		if (!token) {
			status = 'error';
			errorMessage = 'No verification token provided.';
			return;
		}

		try {
			const api = new ApiClient(apiBaseUrl);
			await api.verifyEmail(token);
			status = 'success';
		} catch (err: unknown) {
			status = 'error';
			errorMessage = err instanceof Error ? err.message : 'Verification failed.';
		}
	});
</script>

<svelte:head>
	<title>Verify Email - Codec</title>
</svelte:head>

<div class="verify-page">
	<div class="verify-card">
		{#if status === 'loading'}
			<div class="icon">&#8987;</div>
			<h2>Verifying your email...</h2>
		{:else if status === 'success'}
			<div class="icon">&#10003;</div>
			<h2>Email verified!</h2>
			<p>Your email has been verified. You can now use Codec.</p>
			<button class="btn-primary" onclick={() => goto('/')}>
				Go to Codec
			</button>
		{:else}
			<div class="icon">&#10007;</div>
			<h2>Verification failed</h2>
			<p>{errorMessage}</p>
			<button class="btn-primary" onclick={() => goto('/')}>
				Go to Codec
			</button>
		{/if}
	</div>
</div>

<style>
	.verify-page {
		display: flex;
		align-items: center;
		justify-content: center;
		min-height: 100vh;
		background: var(--bg-primary, #313338);
	}

	.verify-card {
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
</style>
