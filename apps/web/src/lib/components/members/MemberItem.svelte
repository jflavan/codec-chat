<script lang="ts">
	import type { Member } from '$lib/types/index.js';
	import type { PresenceStatus } from '$lib/types/models.js';
	import PresenceDot from '$lib/components/shared/PresenceDot.svelte';

	let { member, presence = 'offline' }: { member: Member; presence?: PresenceStatus } = $props();

	const hoistedRole = $derived(member.roles.find(r => !r.isSystemRole) ?? member.displayRole ?? null);
	const showBadge = $derived(hoistedRole !== null && hoistedRole.name !== 'Member');
</script>

<li class="member-item" class:offline={presence === 'offline'}>
	<div class="avatar-wrapper">
		{#if member.avatarUrl}
			<img class="member-avatar-img" src={member.avatarUrl} alt="" />
		{:else}
			<div class="member-avatar-placeholder" aria-hidden="true">
				{member.displayName.slice(0, 1).toUpperCase()}
			</div>
		{/if}
		<PresenceDot status={presence} />
	</div>
	<div class="member-info">
		<div class="member-name-row">
			<span class="member-name">{member.displayName}</span>
			{#if showBadge}
				<span
					class="role-badge"
					style:color={hoistedRole?.color ?? 'var(--text-muted)'}
					style:background={hoistedRole?.color ? `${hoistedRole.color}26` : 'var(--bg-tertiary)'}
				>
					{hoistedRole?.name}
				</span>
			{/if}
		</div>
		{#if member.statusText || member.statusEmoji}
			<span class="member-status" title={[member.statusEmoji, member.statusText].filter(Boolean).join(' ')}>
				{#if member.statusEmoji}<span class="status-emoji">{member.statusEmoji}</span>{/if}
				{#if member.statusText}<span class="status-text">{member.statusText}</span>{/if}
			</span>
		{/if}
	</div>
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

	.avatar-wrapper {
		position: relative;
		flex-shrink: 0;
	}

	.avatar-wrapper :global(.presence-dot) {
		position: absolute;
		bottom: -2px;
		right: -2px;
	}

	.offline {
		opacity: 0.5;
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

	.member-info {
		display: flex;
		flex-direction: column;
		overflow: hidden;
		min-width: 0;
		flex: 1;
	}

	.member-name-row {
		display: flex;
		align-items: center;
		gap: 6px;
		overflow: hidden;
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

	.member-status {
		display: flex;
		align-items: center;
		gap: 4px;
		font-size: 12px;
		line-height: 1.3;
		margin-top: 1px;
	}

	.status-emoji {
		flex-shrink: 0;
	}

	.status-text {
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
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
</style>
