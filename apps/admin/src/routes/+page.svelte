<script lang="ts">
	import { onMount } from 'svelte';
	import { adminApi } from '$lib/api/client';
	import { getAdminState } from '$lib/state/admin-state.svelte';
	import StatCard from '$lib/components/dashboard/StatCard.svelte';

	const appState = getAdminState();
	let loading = $state(true);

	onMount(async () => {
		try {
			appState.stats = await adminApi.getStats();
		} catch (e) {
			console.error('Failed to load stats:', e);
		}
		loading = false;
	});

	const stats = $derived(appState.stats);
	const live = $derived(appState.stats?.live);
</script>

<h1>Dashboard</h1>

{#if loading}
	<p class="muted">Loading stats...</p>
{:else if stats}
	<section class="stats-grid">
		<StatCard label="Total Users" value={stats.users.total.toLocaleString()} color="var(--accent)" />
		<StatCard label="New Users (24h)" value={stats.users.new24h.toLocaleString()} color="var(--success)" />
		<StatCard label="Total Servers" value={stats.servers.total.toLocaleString()} color="var(--accent)" />
		<StatCard label="Messages (24h)" value={stats.messages.last24h.toLocaleString()} color="var(--warning)" />
		<StatCard label="Open Reports" value={stats.openReports.toLocaleString()} color={stats.openReports > 0 ? 'var(--danger)' : 'var(--text-muted)'} />
		<StatCard label="Active Connections" value={live?.activeConnections.toLocaleString() ?? '—'} color="var(--success)" />
		<StatCard label="Messages/min" value={live?.messagesPerMinute.toLocaleString() ?? '—'} color="var(--warning)" />
	</section>

	<section class="quick-links">
		<h2>Quick Links</h2>
		<div class="links">
			{#if stats.openReports > 0}
				<a href="/moderation" class="link-card danger">
					<span class="count">{stats.openReports}</span>
					<span>Unresolved Reports</span>
				</a>
			{/if}
			<a href="/users" class="link-card">View All Users</a>
			<a href="/servers" class="link-card">View All Servers</a>
		</div>
	</section>
{/if}

<style>
	h1 { font-size: 24px; margin-bottom: 24px; }
	h2 { font-size: 18px; margin-bottom: 12px; }
	.muted { color: var(--text-muted); }
	.stats-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 16px; margin-bottom: 32px; }
	.quick-links { margin-top: 16px; }
	.links { display: flex; gap: 12px; flex-wrap: wrap; }
	.link-card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 14px 20px; color: var(--text-primary); display: flex; align-items: center; gap: 8px; transition: border-color 0.15s; }
	.link-card:hover { border-color: var(--accent); }
	.link-card.danger { border-color: var(--danger); }
	.count { font-weight: 700; font-size: 18px; color: var(--danger); }
</style>
