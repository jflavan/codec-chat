<script lang="ts">
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getChannelStore } from '$lib/state/channel-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import { getVoiceStore } from '$lib/state/voice-store.svelte.js';
	import MessageFeed from './MessageFeed.svelte';
	import TypingIndicator from './TypingIndicator.svelte';
	import Composer from './Composer.svelte';
	import SearchPanel from '$lib/components/search/SearchPanel.svelte';
	import PinnedMessagesPanel from './PinnedMessagesPanel.svelte';
	import VideoGrid from '$lib/components/voice/VideoGrid.svelte';

	const ui = getUIStore();
	const servers = getServerStore();
	const channelStore = getChannelStore();
	const msgStore = getMessageStore();
	const voice = getVoiceStore();

	let isDragOver = $state(false);
	let dragCounter = 0;

	// ── server description inline edit ────────────────────────────────────────
	let editingServerDescription = $state(false);
	let serverDescriptionDraft = $state('');

	const selectedServer = $derived(servers.servers.find((s) => s.serverId === servers.selectedServerId) ?? null);

	function startEditServerDescription() {
		serverDescriptionDraft = selectedServer?.description ?? '';
		editingServerDescription = true;
	}

	async function saveServerDescription() {
		editingServerDescription = false;
		await servers.updateServerDescription(serverDescriptionDraft.trim());
	}

	// ── channel description inline edit ───────────────────────────────────────
	let editingChannelDescription = $state(false);
	let channelDescriptionDraft = $state('');

	const selectedChannel = $derived(channelStore.channels.find((c) => c.id === channelStore.selectedChannelId) ?? null);

	function startEditChannelDescription() {
		channelDescriptionDraft = selectedChannel?.description ?? '';
		editingChannelDescription = true;
	}

	async function saveChannelDescription() {
		if (!channelStore.selectedChannelId) return;
		editingChannelDescription = false;
		await channelStore.updateChannelDescription(channelStore.selectedChannelId, channelDescriptionDraft.trim());
	}

	function handleDragEnter(e: DragEvent): void {
		if (!e.dataTransfer?.types.includes('Files')) return;
		e.preventDefault();
		dragCounter++;
		isDragOver = true;
	}

	function handleDragOver(e: DragEvent): void {
		if (!e.dataTransfer?.types.includes('Files')) return;
		e.preventDefault();
		e.dataTransfer.dropEffect = 'copy';
	}

	function handleDragLeave(e: DragEvent): void {
		e.preventDefault();
		dragCounter--;
		if (dragCounter <= 0) {
			dragCounter = 0;
			isDragOver = false;
		}
	}

	function handleDrop(e: DragEvent): void {
		e.preventDefault();
		dragCounter = 0;
		isDragOver = false;
		if (!channelStore.selectedChannelId) return;
		const file = e.dataTransfer?.files[0];
		if (file?.type.startsWith('image/')) {
			msgStore.attachImage(file);
		}
	}
</script>

<div class="chat-wrapper" class:search-open={msgStore.isSearchOpen}>
<main
	class="chat-main"
	aria-label="Chat"
	ondragenter={handleDragEnter}
	ondragover={handleDragOver}
	ondragleave={handleDragLeave}
	ondrop={handleDrop}
>
	<header class="chat-header">
		<button class="mobile-nav-btn" onclick={() => { ui.mobileNavOpen = true; }} aria-label="Open navigation">
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M3 5h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 1 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2z"/>
			</svg>
		</button>
		<div class="chat-header-left">
			<svg class="channel-hash" width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M5.2 21L6 17H2l.4-2h4l1.2-6H3.6l.4-2h4L8.8 3h2l-.8 4h4L14.8 3h2l-.8 4H20l-.4 2h-4l-1.2 6h4l-.4 2h-4L13.2 21h-2l.8-4h-4L7.2 21h-2zm4.4-12l-1.2 6h4l1.2-6h-4z"/>
			</svg>
			<h1 class="chat-channel-name">
				{channelStore.selectedChannelName ?? 'Select a channel'}
			</h1>
			{#if selectedChannel?.description || (servers.canManageChannels && channelStore.selectedChannelId)}
				{#if !editingChannelDescription}
					<span class="header-divider" aria-hidden="true">|</span>
					<span
						class="channel-description"
						title={selectedChannel?.description ?? ''}
					>
						{selectedChannel?.description ? selectedChannel.description.slice(0, 80) + (selectedChannel.description.length > 80 ? '…' : '') : ''}
						{#if servers.canManageChannels}
							<button class="edit-pencil" aria-label="Edit channel description" onclick={startEditChannelDescription}>
								<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
									<path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"/>
								</svg>
							</button>
						{/if}
					</span>
				{:else}
					<span class="header-divider" aria-hidden="true">|</span>
					<input
						class="description-input"
						type="text"
						bind:value={channelDescriptionDraft}
						placeholder="Channel topic…"
						maxlength="256"
						onblur={saveChannelDescription}
						onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); saveChannelDescription(); } if (e.key === 'Escape') { editingChannelDescription = false; } }}
					/>
				{/if}
			{/if}
		</div>
		<button
			class="pin-btn"
			onclick={() => msgStore.togglePinnedPanel()}
			title="Pinned Messages"
			aria-label="Pinned Messages"
			class:active={msgStore.showPinnedPanel}
		>
			<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/>
			</svg>
			{#if msgStore.pinnedMessageCount > 0}
				<span class="pin-badge">{msgStore.pinnedMessageCount}</span>
			{/if}
		</button>
		<button
			class="search-btn"
			onclick={() => msgStore.toggleSearch()}
			title="Search messages"
			aria-label="Search messages"
			class:active={msgStore.isSearchOpen}
		>
			<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
				<circle cx="11" cy="11" r="8"/>
				<path d="m21 21-4.3-4.3"/>
			</svg>
		</button>
		<button class="mobile-members-btn" onclick={() => { ui.mobileMembersOpen = true; }} aria-label="Show members">
			<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
				<path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"/>
			</svg>
		</button>
	</header>

	{#if isDragOver && channelStore.selectedChannelId}
		<div class="drop-overlay" aria-hidden="true">
			<div class="drop-overlay-content">
				<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
					<path d="M19 7v2.99s-1.99.01-2 0V7h-3s.01-1.99 0-2h3V2h2v3h3v2h-3zm-3 4V8h-3V5H5c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2v-8h-3zM5 19l3-4 2 3 3-4 4 5H5z"/>
				</svg>
				<span class="drop-overlay-text">Drop image to upload</span>
			</div>
		</div>
	{/if}

	<div class="chat-body">
		{#if ui.error}
			<div class="error-banner" role="alert">{ui.error}</div>
		{/if}

		{#if voice.activeVoiceChannelId || voice.activeCall}
			<VideoGrid />
		{/if}

		<div class="feed-container">
			<MessageFeed />
			<TypingIndicator />
		</div>
		<Composer />
	</div>
</main>
{#if msgStore.isSearchOpen}
	<SearchPanel />
{/if}
{#if msgStore.showPinnedPanel}
	<PinnedMessagesPanel />
{/if}
</div>

<style>
	.chat-wrapper {
		display: flex;
		height: 100%;
		overflow: hidden;
	}

	.chat-main {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
		position: relative;
		height: 100%;
		flex: 1;
		min-width: 0;
	}

	.chat-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
		gap: 8px;
	}

	.chat-header-left {
		display: flex;
		align-items: center;
		gap: 6px;
		flex: 1;
		min-width: 0;
	}

	.chat-channel-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		flex-shrink: 0;
	}

	.header-divider {
		color: var(--border);
		font-size: 18px;
		flex-shrink: 0;
		margin: 0 4px;
	}

	.channel-description {
		display: flex;
		align-items: center;
		gap: 4px;
		font-size: 13px;
		color: var(--text-muted);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		max-width: 240px;
		flex-shrink: 1;
		min-width: 0;
	}

	.channel-description:hover .edit-pencil {
		opacity: 1;
	}

	.edit-pencil {
		background: none;
		border: none;
		padding: 2px;
		color: var(--text-muted);
		cursor: pointer;
		display: inline-grid;
		place-items: center;
		border-radius: 3px;
		opacity: 0;
		flex-shrink: 0;
		transition: opacity 150ms ease, color 150ms ease;
	}

	.edit-pencil:hover {
		color: var(--text-header);
	}

	.description-input {
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 13px;
		font-family: inherit;
		border-radius: 4px;
		padding: 2px 6px;
		outline: none;
		max-width: 240px;
		flex-shrink: 1;
	}

	.description-input:focus {
		box-shadow: 0 0 0 2px var(--accent);
	}

	.channel-hash {
		flex-shrink: 0;
		width: 24px;
		height: 24px;
		color: var(--text-muted);
		opacity: 0.7;
	}

	/* ───── Mobile navigation buttons ───── */

	.mobile-nav-btn,
	.mobile-members-btn {
		display: none;
		background: none;
		border: none;
		padding: 6px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.mobile-nav-btn:hover,
	.mobile-members-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	@media (max-width: 899px) {
		.mobile-nav-btn,
		.mobile-members-btn {
			display: grid;
			min-width: 44px;
			min-height: 44px;
		}
	}

	/* ───── Search button ───── */

	.pin-btn {
		position: relative;
		background: none;
		border: none;
		padding: 6px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}
	.pin-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}
	.pin-btn.active {
		color: var(--accent);
	}
	.pin-badge {
		position: absolute;
		top: -2px;
		right: -2px;
		background: var(--brand-500, #5865f2);
		color: white;
		font-size: 0.625rem;
		font-weight: 700;
		border-radius: 50%;
		min-width: 16px;
		height: 16px;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 0 4px;
	}

	.search-btn {
		background: none;
		border: none;
		padding: 6px;
		border-radius: 4px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease, background-color 150ms ease;
	}

	.search-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	.search-btn.active {
		color: var(--accent);
	}

	/* ───── Drop overlay ───── */

	.drop-overlay {
		position: absolute;
		inset: 0;
		z-index: 50;
		display: grid;
		place-items: center;
		background: rgba(0, 0, 0, 0.6);
		pointer-events: none;
	}

	.drop-overlay-content {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		padding: 32px 48px;
		border-radius: 12px;
		border: 2px dashed var(--accent);
		background: var(--bg-secondary);
		color: var(--accent);
	}

	.drop-overlay-text {
		font-size: 18px;
		font-weight: 600;
		color: var(--text-header);
	}

	.chat-body {
		flex: 1;
		position: relative;
		display: flex;
		flex-direction: column;
		overflow: hidden;
		min-height: 0;
	}

	.feed-container {
		flex: 1;
		position: relative;
		overflow: hidden;
		min-height: 0;
		display: flex;
		flex-direction: column;
	}

	/* Mobile: Ensure chat fills the grid cell (parent already sets dvh height) */
	@media (max-width: 899px) {
		.chat-main {
			height: 100%;
		}
	}

	.error-banner {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		z-index: 10;
		padding: 10px 16px;
		background: var(--danger);
		color: var(--bg-tertiary);
		font-size: 14px;
		font-weight: 500;
		text-align: center;
		pointer-events: none;
		animation: banner-lifecycle 5s ease forwards;
	}

	@keyframes banner-lifecycle {
		0%   { opacity: 1; transform: translateY(0); }
		75%  { opacity: 1; transform: translateY(0); }
		100% { opacity: 0; transform: translateY(-8px); }
	}
</style>
