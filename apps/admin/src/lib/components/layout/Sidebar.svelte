<script lang="ts">
	import { page } from '$app/stores';
	import { getAdminState } from '$lib/state/admin-state.svelte';

	const state = getAdminState();
	const reportCount = $derived(state.openReportCount);

	const navItems = [
		{ href: '/', label: 'Dashboard', icon: '📊' },
		{ href: '/users', label: 'Users', icon: '👤' },
		{ href: '/servers', label: 'Servers', icon: '🏠' },
		{ href: '/moderation', label: 'Moderation', icon: '🛡' },
		{ href: '/system', label: 'System', icon: '⚙' }
	];

	function isActive(href: string, pathname: string): boolean {
		if (href === '/') return pathname === '/';
		return pathname.startsWith(href);
	}
</script>

<nav class="sidebar">
	<div class="logo">Codec Admin</div>
	<ul>
		{#each navItems as item}
			<li>
				<a href={item.href} class:active={isActive(item.href, $page.url.pathname)}>
					<span class="icon">{item.icon}</span>
					<span class="label">{item.label}</span>
					{#if item.href === '/moderation' && reportCount > 0}
						<span class="badge">{reportCount}</span>
					{/if}
				</a>
			</li>
		{/each}
	</ul>
	<div class="user-info">
		{state.currentUser?.displayName ?? 'Admin'}
	</div>
</nav>

<style>
	.sidebar { width: var(--sidebar-width); height: 100vh; background: var(--bg-secondary); border-right: 1px solid var(--border); display: flex; flex-direction: column; position: fixed; left: 0; top: 0; }
	.logo { padding: 20px 16px; font-weight: 700; font-size: 16px; color: var(--accent); border-bottom: 1px solid var(--border); }
	ul { list-style: none; padding: 8px; flex: 1; }
	li a { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: var(--radius); color: var(--text-secondary); transition: background 0.15s; }
	li a:hover { background: var(--bg-tertiary); color: var(--text-primary); }
	li a.active { background: var(--accent); color: white; }
	.icon { font-size: 16px; }
	.badge { background: var(--danger); color: white; font-size: 11px; padding: 2px 6px; border-radius: 10px; margin-left: auto; }
	.user-info { padding: 12px 16px; border-top: 1px solid var(--border); color: var(--text-muted); font-size: 12px; }
</style>
