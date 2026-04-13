<script lang="ts">
	import type { Member } from '$lib/types/index.js';
import { ReportType } from '$lib/types/index.js';
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import MemberItem from './MemberItem.svelte';

	const auth = getAuthStore();
	const ui = getUIStore();
	const servers = getServerStore();

	function byPresence(a: Member, b: Member): number {
		const aOnline = (ui.userPresence.get(a.userId) ?? 'offline') !== 'offline' ? 0 : 1;
		const bOnline = (ui.userPresence.get(b.userId) ?? 'offline') !== 'offline' ? 0 : 1;
		return aOnline - bOnline;
	}

	/** Group members by their highest hoisted role, ordered by role position. */
	const roleGroups = $derived(() => {
		const groups: { id: string; name: string; color?: string | null; members: Member[] }[] = [];
		const hoisted = new Map<string, { id: string; name: string; color?: string | null; position: number; members: Member[] }>();
		const unhoisted: Member[] = [];

		for (const m of servers.members) {
			// Use displayRole as the hoisted role if available; fall back to unhoisted
			const displayRole = m.displayRole ?? null;
			if (displayRole) {
				const key = displayRole.id;
				if (!hoisted.has(key)) {
					hoisted.set(key, { id: key, name: displayRole.name, color: displayRole.color, position: displayRole.position, members: [] });
				}
				hoisted.get(key)!.members.push(m);
			} else {
				unhoisted.push(m);
			}
		}

		// Sort groups by position (lower = higher rank)
		const sorted = [...hoisted.values()].sort((a, b) => a.position - b.position);
		for (const g of sorted) {
			groups.push({ id: g.id, name: g.name, color: g.color, members: g.members.sort(byPresence) });
		}

		if (unhoisted.length > 0) {
			groups.push({ id: '_other', name: 'Other', color: null, members: unhoisted.sort(byPresence) });
		}

		return groups;
	});
</script>

<aside class="members-sidebar" aria-label="Members">
	{#if !auth.isSignedIn}
		<p class="muted sidebar-status">Sign in to see members.</p>
	{:else if !servers.selectedServerId}
		<p class="muted sidebar-status">Select a server.</p>
	{:else if servers.isLoadingMembers}
		<p class="muted sidebar-status">Loading members…</p>
	{:else if servers.members.length === 0}
		<p class="muted sidebar-status">No members yet.</p>
	{:else}
		{#each roleGroups() as group (group.id)}
			{#if group.members.length > 0}
				<h3 class="member-group-heading">{group.name} — {group.members.length}</h3>
				<ul class="member-list" role="list">
					{#each group.members as member (member.userId)}
						<MemberItem {member} presence={ui.userPresence.get(member.userId) ?? 'offline'} onReport={() => ui.openReportModal(ReportType.User, member.userId, member.displayName)} />
					{/each}
				</ul>
			{/if}
		{/each}
	{/if}
</aside>

<style>
	.members-sidebar {
		background: var(--bg-secondary);
		padding: 16px 8px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
		-webkit-overflow-scrolling: touch;
		overscroll-behavior-y: contain;
	}

	.sidebar-status {
		padding: 8px;
		font-size: 13px;
		text-align: center;
	}

	.member-group-heading {
		padding: 16px 8px 4px;
		margin: 0;
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.member-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
	}

	.muted {
		color: var(--text-muted);
	}
</style>
