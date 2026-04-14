<script lang="ts">
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import type { AuditLogEntry } from '$lib/types/index.js';

	const servers = getServerStore();

	$effect(() => {
		if (servers.selectedServerId) {
			servers.loadAuditLog();
		}
	});

	function formatRelativeTime(isoDate: string): string {
		const date = new Date(isoDate);
		const now = new Date();
		const diffMs = now.getTime() - date.getTime();
		const diffSecs = Math.floor(diffMs / 1000);
		if (diffSecs < 60) return 'just now';
		const diffMins = Math.floor(diffSecs / 60);
		if (diffMins < 60) return `${diffMins}m ago`;
		const diffHours = Math.floor(diffMins / 60);
		if (diffHours < 24) return `${diffHours}h ago`;
		const diffDays = Math.floor(diffHours / 24);
		if (diffDays < 30) return `${diffDays}d ago`;
		return date.toLocaleDateString();
	}

	function formatAction(entry: AuditLogEntry): string {
		const action = entry.action;
		const details = entry.details ? ` — ${entry.details}` : '';
		const actionMap: Record<string, string> = {
			ServerRenamed: 'renamed the server',
			ServerDescriptionChanged: 'updated the server description',
			ServerIconChanged: 'changed the server icon',
			ServerDeleted: 'deleted the server',
			ChannelCreated: 'created a channel',
			ChannelRenamed: 'renamed a channel',
			ChannelDeleted: 'deleted a channel',
			ChannelDescriptionChanged: 'updated a channel description',
			ChannelPurged: 'purged all messages in a channel',
			ChannelMoved: 'reordered channels',
			CategoryCreated: 'created a category',
			CategoryRenamed: 'renamed a category',
			CategoryDeleted: 'deleted a category',
			MemberKicked: 'kicked a member',
			MemberRoleChanged: "changed a member's role",
			InviteCreated: 'created an invite',
			InviteRevoked: 'revoked an invite',
			EmojiUploaded: 'uploaded a custom emoji',
			EmojiRenamed: 'renamed a custom emoji',
			EmojiDeleted: 'deleted an emoji',
			MessageDeletedByAdmin: 'deleted a message',
			MemberBanned: 'banned a member',
			MemberUnbanned: 'unbanned a member',
		};
		const description = actionMap[action] ?? action.replace(/([A-Z])/g, ' $1').trim().toLowerCase();
		return description + details;
	}

	let scrollEl = $state<HTMLElement>();

	function handleScroll() {
		if (!scrollEl) return;
		const { scrollTop, scrollHeight, clientHeight } = scrollEl;
		if (scrollHeight - scrollTop - clientHeight < 120 && servers.hasMoreAuditLog && !servers.isLoadingAuditLog) {
			servers.loadOlderAuditLog();
		}
	}
</script>

<div class="server-audit-log">
	<h1 class="settings-title">Audit Log</h1>

	{#if servers.isLoadingAuditLog && servers.auditLogEntries.length === 0}
		<p class="muted centered">Loading…</p>
	{:else if servers.auditLogEntries.length === 0}
		<p class="muted centered">No audit log entries.</p>
	{:else}
		<div class="log-list" bind:this={scrollEl} onscroll={handleScroll}>
			{#each servers.auditLogEntries as entry (entry.id)}
				<div class="log-entry">
					<div class="actor-avatar">
						{#if entry.actorAvatarUrl}
							<img src={entry.actorAvatarUrl} alt="" aria-hidden="true" class="avatar-img" />
						{:else}
							<span class="avatar-placeholder">{(entry.actorDisplayName || '?').slice(0, 1).toUpperCase()}</span>
						{/if}
					</div>
					<div class="entry-body">
						<span class="actor-name">{entry.actorDisplayName || 'Deleted User'}</span>
						<span class="action-description">{formatAction(entry)}</span>
					</div>
					<span class="entry-time" title={new Date(entry.createdAt).toLocaleString()}>
						{formatRelativeTime(entry.createdAt)}
					</span>
				</div>
			{/each}
			{#if servers.isLoadingAuditLog}
				<div class="loading-more">Loading more…</div>
			{:else if !servers.hasMoreAuditLog && servers.auditLogEntries.length > 0}
				<div class="end-marker muted">End of audit log</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.server-audit-log {
		max-width: 680px;
		display: flex;
		flex-direction: column;
		height: 100%;
	}

	.settings-title {
		font-size: 20px;
		font-weight: 600;
		color: var(--text-header);
		margin: 0 0 20px;
		flex-shrink: 0;
	}

	.log-list {
		flex: 1;
		overflow-y: auto;
		display: flex;
		flex-direction: column;
		gap: 1px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.log-entry {
		display: flex;
		align-items: flex-start;
		gap: 12px;
		padding: 10px 12px;
		border-radius: 4px;
		background: var(--bg-secondary);
		transition: background-color 150ms ease;
	}

	.log-entry:hover {
		background: var(--bg-message-hover);
	}

	.actor-avatar {
		width: 32px;
		height: 32px;
		border-radius: 50%;
		background: var(--bg-tertiary);
		display: grid;
		place-items: center;
		flex-shrink: 0;
		overflow: hidden;
	}

	.avatar-img {
		width: 100%;
		height: 100%;
		object-fit: cover;
	}

	.avatar-placeholder {
		font-size: 14px;
		font-weight: 600;
		color: var(--text-muted);
	}

	.entry-body {
		flex: 1;
		min-width: 0;
		font-size: 14px;
		line-height: 1.4;
		color: var(--text-normal);
	}

	.actor-name {
		font-weight: 600;
		color: var(--text-header);
		margin-right: 4px;
	}

	.action-description {
		color: var(--text-normal);
	}

	.entry-time {
		font-size: 11px;
		color: var(--text-muted);
		white-space: nowrap;
		flex-shrink: 0;
		margin-top: 2px;
	}

	.loading-more,
	.end-marker {
		text-align: center;
		font-size: 13px;
		padding: 12px;
	}

	.muted {
		color: var(--text-muted);
	}

	.centered {
		text-align: center;
	}
</style>
