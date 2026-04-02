<script lang="ts">
	import '$lib/styles/global.css';
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { getToken, verifyAdmin, clearToken } from '$lib/auth/auth';
	import { createAdminState } from '$lib/state/admin-state.svelte';
	import { AdminHubService } from '$lib/services/admin-hub';
	import { adminApi } from '$lib/api/client';
	import Sidebar from '$lib/components/layout/Sidebar.svelte';

	let { children } = $props();

	const appState = createAdminState();
	const hub = new AdminHubService();
	let authenticated = $state(false);
	let loading = $state(true);

	const isLoginPage = $derived(page.url.pathname === '/login');

	onMount(() => {
		async function init() {
			if (isLoginPage) { loading = false; return; }

			const token = getToken();
			if (!token) { await goto('/login'); loading = false; return; }

			const isAdmin = await verifyAdmin();
			if (!isAdmin) { clearToken(); await goto('/login'); loading = false; return; }

			try {
				appState.currentUser = await adminApi.getMe();
				await hub.start((data) => appState.updateLiveStats(data));
				authenticated = true;
			} catch {
				clearToken();
				await goto('/login');
			}
			loading = false;
		}

		init();

		return () => { hub.stop(); };
	});
</script>

{#if loading}
	<div class="loading">Loading...</div>
{:else if isLoginPage}
	{@render children()}
{:else if authenticated}
	<div class="app-layout">
		<Sidebar />
		<main class="content">
			{@render children()}
		</main>
	</div>
{/if}

<style>
	.loading { display: flex; align-items: center; justify-content: center; height: 100vh; color: var(--text-muted); }
	.app-layout { display: flex; }
	.content { margin-left: var(--sidebar-width); flex: 1; padding: 24px; min-height: 100vh; }
</style>
