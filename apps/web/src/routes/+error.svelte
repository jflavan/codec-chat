<script lang="ts">
	import { page } from '$app/state';
</script>

<div class="error-page" role="alert">
	<h1>{page.status}</h1>
	<p>{page.error?.message ?? 'Something went wrong.'}</p>
	<a href="/">Go home</a>
</div>

<style>
	.error-page {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		min-height: 100vh;
		background: var(--bg-primary, #1a1a2e);
		color: var(--text-primary, #e0e0e0);
		font-family: system-ui, -apple-system, sans-serif;
		text-align: center;
		padding: 2rem;
	}

	h1 {
		font-size: 4rem;
		margin: 0 0 0.5rem;
		font-weight: 700;
		color: var(--text-heading, #ffffff);
	}

	p {
		font-size: 1.125rem;
		margin: 0 0 2rem;
		opacity: 0.8;
		max-width: 400px;
	}

	a {
		color: var(--accent, #5865f2);
		text-decoration: none;
		font-weight: 500;
		padding: 0.5rem 1.5rem;
		border: 1px solid var(--accent, #5865f2);
		border-radius: 4px;
		transition: background 0.15s;
	}

	a:hover {
		background: rgba(88, 101, 242, 0.1);
	}
</style>
