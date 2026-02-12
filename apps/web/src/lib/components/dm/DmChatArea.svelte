<script lang="ts">
	import { tick, untrack } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { formatTime } from '$lib/utils/format.js';

	const app = getAppState();
	const BOTTOM_THRESHOLD = 50;

	let container: HTMLDivElement;
	let dmInputEl: HTMLInputElement;
	let isLockedToBottom = $state(true);
	let unreadCount = $state(0);

	let isAutoScrolling = false;
	let previousChannelId: string | null = null;
	let previousMessageCount = 0;

	function isAtBottom(): boolean {
		if (!container) return true;
		return container.scrollHeight - container.scrollTop - container.clientHeight <= BOTTOM_THRESHOLD;
	}

	function scrollToBottom(instant: boolean): void {
		if (!container) return;
		isAutoScrolling = true;
		container.scrollTo({ top: container.scrollHeight, behavior: instant ? 'instant' : 'smooth' });
		setTimeout(() => { isAutoScrolling = false; }, instant ? 50 : 300);
	}

	function handleScroll(): void {
		if (isAutoScrolling) return;
		const atBottom = isAtBottom();
		if (atBottom && !isLockedToBottom) {
			isLockedToBottom = true;
			unreadCount = 0;
		} else if (!atBottom && isLockedToBottom) {
			isLockedToBottom = false;
		}
	}

	function jumpToBottom(): void {
		isLockedToBottom = true;
		unreadCount = 0;
		scrollToBottom(false);
	}

	// Reset scroll on channel change
	$effect(() => {
		const channelId = app.activeDmChannelId;
		if (channelId !== previousChannelId) {
			previousChannelId = channelId;
			isLockedToBottom = true;
			unreadCount = 0;
			previousMessageCount = 0;
		}
	});

	// Auto-scroll when locked, or track unread when unlocked
	$effect(() => {
		const count = app.dmMessages.length;
		const loading = app.isLoadingDmMessages;

		if (loading || count === 0) {
			previousMessageCount = count;
			return;
		}

		const newMessages = count - previousMessageCount;
		previousMessageCount = count;
		if (newMessages <= 0) return;

		untrack(() => {
			if (isLockedToBottom) {
				tick().then(() => scrollToBottom(newMessages > 3));
			} else {
				unreadCount += newMessages;
			}
		});
	});

	async function handleDmSubmit(e: SubmitEvent) {
		e.preventDefault();
		await app.sendDmMessage();
		dmInputEl?.focus();
	}
</script>

<main class="dm-chat" aria-label="Direct message conversation">
	<header class="dm-header">
		<div class="dm-header-left">
			{#if app.activeDmParticipant?.avatarUrl}
				<img class="header-avatar" src={app.activeDmParticipant.avatarUrl} alt="" />
			{:else if app.activeDmParticipant}
				<div class="header-avatar-placeholder" aria-hidden="true">
					{app.activeDmParticipant.displayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<h1 class="dm-participant-name">
				{app.activeDmParticipant?.displayName ?? 'Select a conversation'}
			</h1>
		</div>
	</header>

	{#if app.error}
		<div class="error-banner" role="alert">{app.error}</div>
	{/if}

	<!-- Message feed -->
	<div class="feed-wrapper">
		<div class="message-feed" bind:this={container} onscroll={handleScroll}>
			{#if app.isLoadingDmMessages}
				<p class="muted feed-status">Loading messages…</p>
			{:else if app.dmMessages.length === 0}
				<p class="muted feed-status">
					No messages yet. Say hi to {app.activeDmParticipant?.displayName ?? 'your friend'}!
				</p>
			{:else}
				{#each app.dmMessages as message, i}
					{@const prev = i > 0 ? app.dmMessages[i - 1] : null}
					{@const isGrouped = prev?.authorUserId === message.authorUserId && prev?.authorName === message.authorName}
					<article class="message" class:grouped={isGrouped}>
						{#if !isGrouped}
							<div class="message-avatar-col">
								{#if message.authorAvatarUrl}
									<img class="message-avatar-img" src={message.authorAvatarUrl} alt="" />
								{:else}
									<div class="message-avatar" aria-hidden="true">
										{message.authorName.slice(0, 1).toUpperCase()}
									</div>
								{/if}
							</div>
							<div class="message-content">
								<div class="message-header">
									<strong class="message-author">{message.authorName}</strong>
									<time class="message-time">{formatTime(message.createdAt)}</time>
								</div>
								<p class="message-body">{message.body}</p>
							</div>
						{:else}
							<div class="message-avatar-col">
								<time class="message-time-inline">{formatTime(message.createdAt)}</time>
							</div>
							<div class="message-content">
								<p class="message-body">{message.body}</p>
							</div>
						{/if}
					</article>
				{/each}
			{/if}
		</div>

		{#if !isLockedToBottom}
			<button class="jump-to-bottom" onclick={jumpToBottom} aria-label="Jump to latest messages">
				{#if unreadCount > 0}
					<span class="jump-badge">{unreadCount}</span>
				{/if}
				<span class="jump-text">{unreadCount > 0 ? 'New' : 'Jump to latest'}</span>
				<svg class="jump-arrow" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M8 11.5a.5.5 0 0 1-.354-.146l-4.5-4.5a.5.5 0 1 1 .708-.708L8 10.293l4.146-4.147a.5.5 0 1 1 .708.708l-4.5 4.5A.5.5 0 0 1 8 11.5z"/>
				</svg>
			</button>
		{/if}
	</div>

	<!-- Typing indicator -->
	{#if app.dmTypingUsers.length > 0}
		<div class="typing-indicator" aria-live="polite">
			<span class="typing-dots" aria-hidden="true">
				<span class="dot"></span><span class="dot"></span><span class="dot"></span>
			</span>
			<span class="typing-text">
				{#if app.dmTypingUsers.length === 1}
					<strong>{app.dmTypingUsers[0]}</strong> is typing…
				{:else}
					Several people are typing…
				{/if}
			</span>
		</div>
	{/if}

	<!-- Composer -->
	<form class="composer" onsubmit={handleDmSubmit}>
		<input
			bind:this={dmInputEl}
			class="composer-input"
			type="text"
			placeholder={app.activeDmParticipant ? `Message @${app.activeDmParticipant.displayName}` : 'Select a conversation…'}
			bind:value={app.dmMessageBody}
			disabled={!app.activeDmChannelId || app.isSendingDm}
			oninput={() => app.handleDmComposerInput()}
		/>
		<button
			class="composer-send"
			type="submit"
			disabled={!app.activeDmChannelId || !app.dmMessageBody.trim() || app.isSendingDm}
			aria-label="Send message"
		>
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
			</svg>
		</button>
	</form>
</main>

<style>
	.dm-chat {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	/* ───── Header ───── */
	.dm-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.dm-header-left {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.header-avatar {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.header-avatar-placeholder {
		width: 24px;
		height: 24px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 12px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.dm-participant-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.error-banner {
		padding: 8px 16px;
		background: var(--danger);
		color: var(--bg-tertiary);
		font-size: 14px;
		flex-shrink: 0;
	}

	/* ───── Message feed ───── */
	.feed-wrapper {
		flex: 1;
		position: relative;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	.message-feed {
		flex: 1;
		overflow-y: auto;
		padding: 16px 0 8px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.feed-status { padding: 16px; text-align: center; }
	.muted { color: var(--text-muted); }

	.message {
		position: relative;
		display: grid;
		grid-template-columns: 56px 1fr;
		padding: 2px 16px;
		transition: background-color 150ms ease;
	}

	.message:hover { background: var(--bg-message-hover); }
	.message:not(.grouped) { margin-top: 16px; }
	.message.grouped { margin-top: 0; }

	.message-avatar-col {
		display: flex;
		justify-content: center;
		padding-top: 2px;
	}

	.message-avatar {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-weight: 700;
		font-size: 16px;
		display: grid;
		place-items: center;
		flex-shrink: 0;
	}

	.message-avatar-img {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		object-fit: cover;
		flex-shrink: 0;
	}

	.message-content { min-width: 0; }

	.message-header {
		display: flex;
		align-items: baseline;
		gap: 8px;
	}

	.message-author {
		font-size: 16px;
		font-weight: 600;
		color: var(--text-header);
	}

	.message-time {
		font-size: 12px;
		color: var(--text-muted);
	}

	.message-time-inline {
		font-size: 11px;
		color: transparent;
		text-align: center;
		width: 100%;
		display: block;
	}

	.message:hover .message-time-inline { color: var(--text-muted); }

	.message-body {
		margin: 2px 0 0;
		color: var(--text-normal);
		line-height: 1.375;
		word-break: break-word;
	}

	/* ───── Jump to bottom ───── */
	.jump-to-bottom {
		position: absolute;
		bottom: 8px;
		left: 50%;
		transform: translateX(-50%);
		display: flex;
		align-items: center;
		gap: 6px;
		padding: 6px 14px;
		border: 1px solid var(--border);
		border-radius: 20px;
		background: var(--bg-secondary);
		color: var(--accent);
		font-size: 13px;
		font-weight: 500;
		font-family: inherit;
		cursor: pointer;
		white-space: nowrap;
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
		transition: background-color 150ms ease, color 150ms ease;
		z-index: 5;
	}

	.jump-to-bottom:hover {
		background: var(--bg-message-hover);
		color: var(--accent-hover);
	}

	.jump-badge {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		min-width: 20px;
		height: 20px;
		padding: 0 6px;
		border-radius: 10px;
		background: var(--accent);
		color: var(--bg-tertiary);
		font-size: 11px;
		font-weight: 700;
	}

	.jump-text { line-height: 1; }
	.jump-arrow { flex-shrink: 0; opacity: 0.8; }

	/* ───── Typing indicator ───── */
	.typing-indicator {
		flex-shrink: 0;
		padding: 2px 16px 4px;
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 13px;
		color: var(--text-muted);
		background: var(--bg-primary);
		min-height: 20px;
	}

	.typing-dots {
		display: inline-flex;
		gap: 3px;
		align-items: center;
	}

	.typing-dots .dot {
		width: 6px;
		height: 6px;
		border-radius: 50%;
		background: var(--text-muted);
		animation: typing-bounce 1.4s infinite ease-in-out both;
	}

	.typing-dots .dot:nth-child(1) { animation-delay: 0s; }
	.typing-dots .dot:nth-child(2) { animation-delay: 0.2s; }
	.typing-dots .dot:nth-child(3) { animation-delay: 0.4s; }

	@keyframes typing-bounce {
		0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
		40% { transform: scale(1); opacity: 1; }
	}

	.typing-text strong {
		color: var(--text-header);
		font-weight: 600;
	}

	/* ───── Composer ───── */
	.composer {
		flex-shrink: 0;
		padding: 0 16px 24px;
		display: flex;
		align-items: center;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-input {
		flex: 1;
		padding: 12px 16px;
		border-radius: 8px 0 0 8px;
		border: none;
		background: var(--input-bg);
		color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		outline: none;
		min-height: 20px;
	}

	.composer-input::placeholder { color: var(--text-dim); }
	.composer-input:focus { box-shadow: 0 0 0 2px var(--accent); }
	.composer-input:disabled { opacity: 0.5; }

	.composer-send {
		background: var(--input-bg);
		border: none;
		padding: 12px;
		border-radius: 0 8px 8px 0;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-send:hover:not(:disabled) { color: var(--accent); }

	.composer-send:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}
</style>
