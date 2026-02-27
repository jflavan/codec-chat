<script lang="ts">
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import UserPanel from './UserPanel.svelte';
	import InvitePanel from './InvitePanel.svelte';
	import VoiceConnectedBar from './VoiceConnectedBar.svelte';

	const app = getAppState();

	const textChannels = $derived(app.channels.filter((c) => c.type !== 'voice'));
	const voiceChannels = $derived(app.channels.filter((c) => c.type === 'voice'));
</script>

<aside class="channel-sidebar" aria-label="Channels">
	<div class="channel-header">
		<h2 class="server-name">{app.selectedServerName}</h2>
		<div class="header-actions">
			{#if app.canManageChannels && app.selectedServerId}
				<button
					class="header-btn"
					aria-label="Server settings"
					title="Server settings"
					onclick={() => app.openServerSettings()}
				>
					<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z"/>
						<path d="M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z"/>
					</svg>
				</button>
			{/if}
			{#if app.canManageInvites && app.selectedServerId}
				<button
					class="header-btn"
					aria-label="Manage invites"
					title="Manage invites"
					onclick={() => { app.showInvitePanel = !app.showInvitePanel; }}
				>
					<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M8 1a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM6 4a2 2 0 1 1 4 0 2 2 0 0 1-4 0zm-2.5 8a3.5 3.5 0 0 1 3.163-3.487A.5.5 0 0 0 7 8H5.5A3.5 3.5 0 0 0 2 11.5v.5h4.05a.5.5 0 0 0 .45-.72A3.48 3.48 0 0 1 3.5 12zM12 8.5a.5.5 0 0 1 .5.5v1.5H14a.5.5 0 0 1 0 1h-1.5V13a.5.5 0 0 1-1 0v-1.5H10a.5.5 0 0 1 0-1h1.5V9a.5.5 0 0 1 .5-.5z"/>
					</svg>
				</button>
			{/if}
		</div>
	</div>

	<div class="channel-list-scroll">
		<!-- Text Channels -->
		<div class="channel-category">
			<span class="category-label">Text Channels</span>
			{#if app.canManageChannels && app.selectedServerId && !app.showCreateChannel}
				<button class="category-action" aria-label="Create channel" onclick={() => { app.showCreateChannel = true; app.newChannelType = 'text'; }}>
					<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M8 2a1 1 0 0 1 1 1v4h4a1 1 0 1 1 0 2H9v4a1 1 0 1 1-2 0V9H3a1 1 0 0 1 0-2h4V3a1 1 0 0 1 1-1z"/>
					</svg>
				</button>
			{/if}
		</div>

		<ul class="channel-list" role="list">
			{#if app.isLoadingChannels}
				<li class="muted channel-item">Loading…</li>
			{:else if textChannels.length === 0 && voiceChannels.length === 0}
				<li class="muted channel-item">No channels yet.</li>
			{:else}
				{#each textChannels as channel}
					{@const mentions = app.channelMentionCount(channel.id)}
					<li>
						<button
							class="channel-item"
							class:active={channel.id === app.selectedChannelId}
							onclick={() => app.selectChannel(channel.id)}
						>
							<svg class="channel-hash" width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
								<path d="M5.2 21L6 17H2l.4-2h4l1.2-6H3.6l.4-2h4L8.8 3h2l-.8 4h4L14.8 3h2l-.8 4H20l-.4 2h-4l-1.2 6h4l-.4 2h-4L13.2 21h-2l.8-4h-4L7.2 21h-2zm4.4-12l-1.2 6h4l1.2-6h-4z"/>
							</svg>
							<span>{channel.name}</span>
							{#if mentions > 0}
								<span class="mention-badge" aria-label="{mentions} mentions">{mentions}</span>
							{/if}
						</button>
					</li>
				{/each}
			{/if}
		</ul>

		<!-- Voice Channels -->
		{#if voiceChannels.length > 0 || (app.canManageChannels && app.selectedServerId)}
			<div class="channel-category">
				<span class="category-label">Voice Channels</span>
				{#if app.canManageChannels && app.selectedServerId && !app.showCreateChannel}
					<button class="category-action" aria-label="Create voice channel" onclick={() => { app.showCreateChannel = true; app.newChannelType = 'voice'; }}>
						<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
							<path d="M8 2a1 1 0 0 1 1 1v4h4a1 1 0 1 1 0 2H9v4a1 1 0 1 1-2 0V9H3a1 1 0 0 1 0-2h4V3a1 1 0 0 1 1-1z"/>
						</svg>
					</button>
				{/if}
			</div>

			<ul class="channel-list" role="list">
				{#each voiceChannels as channel}
					{@const members = app.voiceChannelMembers.get(channel.id) ?? []}
					{@const isActive = channel.id === app.activeVoiceChannelId}
					<li>
						<button
							class="channel-item voice-channel-item"
							class:active={isActive}
							onclick={() => app.joinVoiceChannel(channel.id)}
							disabled={app.isJoiningVoice}
							aria-label="Join {channel.name}"
						>
							<!-- Speaker icon -->
							<svg class="channel-hash" width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
								<path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/>
							</svg>
							<span>{channel.name}</span>
							{#if members.length > 0}
								<span class="voice-count" aria-label="{members.length} connected">{members.length}</span>
							{/if}
						</button>
						{#if members.length > 0}
							<ul class="voice-members" aria-label="Connected members">
								{#each members as member}
									<li class="voice-member">
										{#if member.avatarUrl}
											<img class="voice-avatar" src={member.avatarUrl} alt="" width="14" height="14" />
										{:else}
											<span class="voice-avatar-placeholder"></span>
										{/if}
										<span class="voice-member-name" class:muted-member={member.isMuted}>{member.displayName}</span>
										{#if member.isMuted}
											<svg class="voice-status-icon" width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-label="Muted">
												<path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/>
											</svg>
										{/if}
									</li>
								{/each}
							</ul>
						{/if}
					</li>
				{/each}
			</ul>
		{/if}

		{#if app.canManageChannels && app.selectedServerId && app.showCreateChannel}
			<form class="inline-form channel-create-form" onsubmit={(e) => { e.preventDefault(); app.createChannel(); }}>
				<div class="type-toggle">
					<label class="type-option" class:selected={app.newChannelType === 'text'}>
						<input type="radio" name="channelType" value="text" bind:group={app.newChannelType} />
						# Text
					</label>
					<label class="type-option" class:selected={app.newChannelType === 'voice'}>
						<input type="radio" name="channelType" value="voice" bind:group={app.newChannelType} />
						🔊 Voice
					</label>
				</div>
				<input
					type="text"
					placeholder={app.newChannelType === 'voice' ? 'new-voice' : 'new-channel'}
					maxlength="100"
					bind:value={app.newChannelName}
					disabled={app.isCreatingChannel}
				/>
				<div class="inline-form-actions">
					<button type="submit" class="btn-primary" disabled={app.isCreatingChannel || !app.newChannelName.trim()}>
						{app.isCreatingChannel ? '…' : 'Create'}
					</button>
					<button type="button" class="btn-secondary" onclick={() => { app.showCreateChannel = false; app.newChannelName = ''; }}>Cancel</button>
				</div>
			</form>
		{/if}
	</div>

	{#if app.canManageInvites && app.showInvitePanel}
		<InvitePanel />
	{/if}

	{#if app.activeVoiceChannelId}
		<VoiceConnectedBar />
	{/if}

	<UserPanel />
</aside>

<style>
	.channel-sidebar {
		background: var(--bg-secondary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.channel-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.server-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		flex: 1;
	}

	.header-actions {
		display: flex;
		align-items: center;
		gap: 8px;
		margin-left: auto;
	}

	.header-btn {
		background: none;
		border: none;
		padding: 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		border-radius: 3px;
		width: 44px;
		height: 44px;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.header-btn:hover {
		color: var(--text-header);
	}

	.channel-list-scroll {
		flex: 1;
		overflow-y: auto;
		padding: 8px 8px 16px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
		-webkit-overflow-scrolling: touch;
		overscroll-behavior-y: contain;
	}

	.channel-category {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 16px 8px 4px;
	}

	.category-label {
		font-size: 12px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		color: var(--text-muted);
	}

	.category-action {
		background: none;
		border: none;
		padding: 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		border-radius: 3px;
		width: 18px;
		height: 18px;
	}

	.category-action:hover {
		color: var(--text-header);
	}

	.channel-list {
		list-style: none;
		padding: 0;
		margin: 2px 0 0;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.channel-item {
		display: flex;
		align-items: center;
		gap: 6px;
		width: 100%;
		padding: 8px 8px;
		min-height: 44px;
		border-radius: 4px;
		border: none;
		background: transparent;
		color: var(--text-muted);
		font-size: 15px;
		font-weight: 500;
		cursor: pointer;
		text-align: left;
		font-family: inherit;
		transition: background-color 150ms ease, color 150ms ease;
	}

	.channel-item:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.channel-item.active {
		background: var(--bg-message-hover);
		color: var(--text-header);
		font-weight: 600;
	}

	.channel-hash {
		flex-shrink: 0;
		color: var(--text-muted);
		opacity: 0.7;
	}

	.mention-badge {
		margin-left: auto;
		min-width: 18px;
		height: 18px;
		padding: 0 5px;
		border-radius: 9px;
		background: var(--danger);
		color: #fff;
		font-size: 11px;
		font-weight: 700;
		line-height: 18px;
		text-align: center;
		flex-shrink: 0;
	}

	.channel-create-form {
		padding: 8px;
	}

	/* ── inline form styles ── */
	.inline-form {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.inline-form input {
		padding: 10px;
		border-radius: 4px;
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 16px;
		font-family: inherit;
		outline: none;
	}
	.inline-form input::placeholder {
		color: var(--text-dim);
	}

	.inline-form input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.inline-form-actions {
		display: flex;
		gap: 6px;
	}

	.btn-primary {
		border: none;
		border-radius: 3px;
		padding: 10px 14px;
		min-height: 44px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 600;
		font-size: 14px;
		cursor: pointer;
		font-family: inherit;
		transition: background-color 150ms ease;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--accent-hover);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		border: none;
		border-radius: 3px;
		padding: 10px 14px;
		min-height: 44px;
		background: transparent;
		color: var(--text-normal);
		font-weight: 500;
		font-size: 14px;
		cursor: pointer;
		font-family: inherit;
		transition: color 150ms ease;
	}

	.btn-secondary:hover:not(:disabled) {
		color: var(--text-header);
	}

	.btn-secondary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.muted {
		color: var(--text-muted);
	}

	.voice-channel-item {
		flex-wrap: wrap;
	}

	.voice-count {
		margin-left: auto;
		font-size: 11px;
		color: var(--text-muted);
		background: var(--bg-tertiary);
		padding: 1px 5px;
		border-radius: 8px;
		flex-shrink: 0;
	}

	.voice-members {
		list-style: none;
		padding: 2px 0 4px 28px;
		margin: 0;
		width: 100%;
		display: flex;
		flex-direction: column;
		gap: 3px;
	}

	.voice-member {
		display: flex;
		align-items: center;
		gap: 5px;
	}

	.voice-avatar {
		width: 14px;
		height: 14px;
		border-radius: 50%;
		flex-shrink: 0;
		object-fit: cover;
	}

	.voice-avatar-placeholder {
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: var(--bg-tertiary);
		flex-shrink: 0;
	}

	.voice-member-name {
		font-size: 12px;
		color: var(--text-muted);
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.muted-member {
		opacity: 0.6;
	}

	.voice-status-icon {
		flex-shrink: 0;
		color: var(--text-dim, var(--text-muted));
		opacity: 0.7;
	}

	.type-toggle {
		display: flex;
		gap: 6px;
		margin-bottom: 4px;
	}

	.type-option {
		display: flex;
		align-items: center;
		gap: 4px;
		padding: 6px 10px;
		border-radius: 4px;
		font-size: 13px;
		color: var(--text-muted);
		cursor: pointer;
		user-select: none;
		border: 1px solid transparent;
		transition: border-color 150ms ease, color 150ms ease;
	}

	.type-option input {
		/* Visually hide the radio input while keeping it accessible and keyboard-focusable */
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border: 0;
	}

	.type-option.selected {
		border-color: var(--accent);
		color: var(--text-header);
	}
</style>
