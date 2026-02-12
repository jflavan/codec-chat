<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import MemberItem from './MemberItem.svelte';

	const app = getAppState();

	const owners = $derived(app.members.filter((m) => m.role === 'Owner'));
	const admins = $derived(app.members.filter((m) => m.role === 'Admin'));
	const regulars = $derived(app.members.filter((m) => m.role === 'Member'));
</script>

<aside class="members-sidebar" aria-label="Members">
	{#if !app.isSignedIn}
		<p class="muted sidebar-status">Sign in to see members.</p>
	{:else if !app.selectedServerId}
		<p class="muted sidebar-status">Select a server.</p>
	{:else if app.isLoadingMembers}
		<p class="muted sidebar-status">Loading members…</p>
	{:else if app.members.length === 0}
		<p class="muted sidebar-status">No members yet.</p>
	{:else}
		{#if owners.length > 0}
			<h3 class="member-group-heading">Owner — {owners.length}</h3>
			<ul class="member-list" role="list">
				{#each owners as member (member.userId)}
					<MemberItem {member} />
				{/each}
			</ul>
		{/if}

		{#if admins.length > 0}
			<h3 class="member-group-heading">Admin — {admins.length}</h3>
			<ul class="member-list" role="list">
				{#each admins as member (member.userId)}
					<MemberItem {member} />
				{/each}
			</ul>
		{/if}

		{#if regulars.length > 0}
			<h3 class="member-group-heading">Other — {regulars.length}</h3>
			<ul class="member-list" role="list">
				{#each regulars as member (member.userId)}
					<MemberItem {member} />
				{/each}
			</ul>
		{/if}
	{/if}
</aside>

<style>
	.members-sidebar {
		background: var(--bg-secondary);
		padding: 16px 8px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
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
