<script lang="ts">
	import type { Member } from '$lib/types/index.js';

	let { member }: { member: Member } = $props();
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
	{#if member.role === 'Owner' || member.role === 'Admin'}
		<span class="role-badge role-badge-{member.role.toLowerCase()}">{member.role}</span>
	{/if}
</li>

<style>
	.member-item {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 8px 8px;
		min-height: 44px;
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

	.role-badge {
		font-size: 10px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		padding: 1px 5px;
		border-radius: 3px;
		flex-shrink: 0;
		line-height: 1.4;
	}

	.role-badge-owner {
		color: var(--accent);
		background: rgba(var(--accent-rgb), 0.15);
	}

	.role-badge-admin {
		color: #f0b232;
		background: rgba(240, 178, 50, 0.15);
	}

</style>
