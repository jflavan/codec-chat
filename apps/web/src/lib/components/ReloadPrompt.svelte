<script lang="ts">
	import { onMount } from 'svelte';

	let offlineReady = $state(false);
	let needRefresh = $state(false);
	let updateSW: ((reloadPage?: boolean) => Promise<void>) | undefined = $state(undefined);

	onMount(async () => {
		const { useRegisterSW } = await import('virtual:pwa-register/svelte');
		const { offlineReady: or, needRefresh: nr, updateServiceWorker } = useRegisterSW({
			onRegisteredSW(swUrl, registration) {
				if (registration) {
					// Check for updates every hour
					setInterval(() => registration.update(), 60 * 60 * 1000);
				}
			}
		});

		or.subscribe((v) => (offlineReady = v));
		nr.subscribe((v) => (needRefresh = v));
		updateSW = updateServiceWorker;
	});

	function close() {
		offlineReady = false;
		needRefresh = false;
	}

	function update() {
		updateSW?.(true);
	}
</script>

{#if offlineReady || needRefresh}
	<div class="pwa-toast" role="alert" aria-live="assertive">
		{#if offlineReady}
			<p>App ready to work offline.</p>
		{:else}
			<p>New content available. Reload to update.</p>
		{/if}
		<div class="pwa-toast-actions">
			{#if needRefresh}
				<button onclick={update}>Reload</button>
			{/if}
			<button onclick={close}>Close</button>
		</div>
	</div>
{/if}

<style>
	.pwa-toast {
		position: fixed;
		right: 1rem;
		bottom: 1rem;
		z-index: 9999;
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem 1rem;
		border: 1px solid var(--accent, #00ff66);
		border-radius: 6px;
		background: var(--bg-secondary, #07110a);
		color: var(--text-normal, #86ff6b);
		font-family: 'Space Grotesk', monospace, sans-serif;
		font-size: 0.875rem;
		box-shadow: 0 0 12px rgba(0, 255, 102, 0.15);
	}

	.pwa-toast p {
		margin: 0;
	}

	.pwa-toast-actions {
		display: flex;
		gap: 0.5rem;
	}

	.pwa-toast-actions button {
		padding: 0.35rem 0.75rem;
		border: 1px solid var(--accent, #00ff66);
		border-radius: 4px;
		background: transparent;
		color: var(--accent, #00ff66);
		font-family: inherit;
		font-size: 0.8rem;
		cursor: pointer;
		transition: background-color 0.15s;
	}

	.pwa-toast-actions button:hover {
		background: var(--accent, #00ff66);
		color: var(--bg-tertiary, #050b07);
	}

	.pwa-toast-actions button:focus-visible {
		outline: 2px solid var(--accent, #00ff66);
		outline-offset: 2px;
	}
</style>
