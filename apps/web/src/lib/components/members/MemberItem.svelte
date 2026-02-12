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
</style>
