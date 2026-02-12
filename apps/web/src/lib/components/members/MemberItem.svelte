<script lang="ts">
	import type { Member } from '$lib/types/index.js';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	let { member }: { member: Member } = $props();

	const app = getAppState();

	const canKick = $derived(
		app.canKickMembers &&
			member.userId !== app.me?.user.id &&
			member.role !== 'Owner' &&
			!(app.currentServerRole === 'Admin' && member.role === 'Admin')
	);

	let confirming = $state(false);

	async function handleKick(): Promise<void> {
		if (!confirming) {
			confirming = true;
			return;
		}
		await app.kickMember(member.userId);
		confirming = false;
	}

	function cancelKick(): void {
		confirming = false;
	}
</script>

<li class="member-item">
	{#if member.avatarUrl}
		<img class="member-avatar-img" src={member.avatarUrl} alt="" />
	{:else}
		<div class="member-avatar-placeholder" aria-hidden="true">
			{member.displayName.slice(0, 1).toUpperCase()}
		</div>
	{/if}
	<span class="member-name">{member.displayName}</span>
	{#if canKick}
		{#if confirming}
			<button class="kick-btn kick-confirm" onclick={handleKick} aria-label="Confirm kick {member.displayName}">
				Confirm
			</button>
			<button class="kick-btn kick-cancel" onclick={cancelKick} aria-label="Cancel kick">
				✕
			</button>
		{:else}
			<button class="kick-btn" onclick={handleKick} aria-label="Kick {member.displayName}">
				Kick
			</button>
		{/if}
	{/if}
</li>

<style>
	.member-item {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 6px 8px;
		border-radius: 4px;
		cursor: default;
		transition: background-color 150ms ease;
	}

	.member-item:hover {
		background: var(--bg-message-hover);
	}

	.member-avatar-img {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.member-avatar-placeholder {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 14px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.member-name {
		font-size: 14px;
		font-weight: 500;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.member-item:hover .member-name {
		color: var(--text-normal);
	}

	.kick-btn {
		margin-left: auto;
		padding: 2px 8px;
		font-size: 11px;
		font-weight: 600;
		border: 1px solid var(--text-muted);
		border-radius: 3px;
		background: transparent;
		color: var(--text-muted);
		cursor: pointer;
		opacity: 0;
		transition: opacity 150ms ease, background-color 150ms ease, color 150ms ease;
		flex-shrink: 0;
	}

	.member-item:hover .kick-btn {
		opacity: 1;
	}

	.kick-btn:hover {
		background: var(--danger, #ed4245);
		color: #fff;
		border-color: var(--danger, #ed4245);
	}

	.kick-confirm {
		opacity: 1;
		background: var(--danger, #ed4245);
		color: #fff;
		border-color: var(--danger, #ed4245);
	}

	.kick-cancel {
		opacity: 1;
		padding: 2px 5px;
	}
</style>
