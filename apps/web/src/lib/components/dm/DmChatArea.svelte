<script lang="ts">
	import { onMount, tick, untrack } from 'svelte';
	import { getAppState } from '$lib/state/app-state.svelte.js';
	import { formatTime } from '$lib/utils/format.js';
	import LinkifiedText from '$lib/components/chat/LinkifiedText.svelte';
	import LinkPreviewCard from '$lib/components/chat/LinkPreviewCard.svelte';
	import YouTubeEmbed from '$lib/components/chat/YouTubeEmbed.svelte';
	import ReplyReference from '$lib/components/chat/ReplyReference.svelte';
	import ReplyComposerBar from '$lib/components/chat/ReplyComposerBar.svelte';
	import ComposerOverlay from '$lib/components/chat/ComposerOverlay.svelte';
	import { extractYouTubeUrls } from '$lib/utils/youtube.js';

	const app = getAppState();
	const BOTTOM_THRESHOLD = 50;

	let container: HTMLDivElement;
	let dmInputEl: HTMLInputElement;
	let dmFileInputEl: HTMLInputElement;
	let dmOverlayEl: HTMLDivElement;
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

	/* ───── Reply highlight & scroll ───── */
	let highlightedMessageId = $state<string | null>(null);

	function scrollToMessage(messageId: string): void {
		const el = container?.querySelector(`[data-message-id="${CSS.escape(messageId)}"]`);
		if (!el) return;
		el.scrollIntoView({ behavior: 'smooth', block: 'center' });
		highlightedMessageId = messageId;
		setTimeout(() => { highlightedMessageId = null; }, 1500);
	}

	function handleReply(message: typeof app.dmMessages[0]): void {
		app.startReply(message.id, message.authorName, message.body?.slice(0, 100) ?? '', 'dm');
	}

	/* ───── Inline message editing ───── */
	let editingDmMessageId = $state<string | null>(null);
	let editDmBody = $state('');

	function startDmEdit(message: typeof app.dmMessages[0]): void {
		editDmBody = message.body;
		editingDmMessageId = message.id;
	}

	function cancelDmEdit(): void {
		editingDmMessageId = null;
		editDmBody = '';
	}

	async function saveDmEdit(): Promise<void> {
		const trimmed = editDmBody.trim();
		if (!trimmed || !editingDmMessageId) {
			cancelDmEdit();
			return;
		}
		const msg = app.dmMessages.find((m) => m.id === editingDmMessageId);
		if (trimmed === msg?.body) {
			cancelDmEdit();
			return;
		}
		await app.editDmMessage(editingDmMessageId, trimmed);
		editingDmMessageId = null;
		editDmBody = '';
	}

	function handleDmEditKeydown(e: KeyboardEvent): void {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			saveDmEdit();
		} else if (e.key === 'Escape') {
			e.preventDefault();
			cancelDmEdit();
		}
	}

	// Ensure the DM feed scrolls to the bottom on initial mount.
	onMount(() => {
		if (!app.isLoadingDmMessages && app.dmMessages.length > 0) {
			tick().then(() => {
				requestAnimationFrame(() => scrollToBottom(true));
			});
		}
	});

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

	function handleDmFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (file) {
			app.attachDmImage(file);
		}
		input.value = '';
	}

	function handleDmPaste(e: ClipboardEvent) {
		const items = e.clipboardData?.items;
		if (!items) return;
		for (const item of items) {
			if (item.type.startsWith('image/')) {
				e.preventDefault();
				const file = item.getAsFile();
				if (file) {
					app.attachDmImage(file);
				}
				return;
			}
		}
	}

	function handleDmKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape' && app.replyingTo?.context === 'dm') {
			e.preventDefault();
			app.cancelReply();
		}
	}

	function handleDmInput(): void {
		app.handleDmComposerInput();
		syncDmOverlayScroll();
	}

	function syncDmOverlayScroll(): void {
		if (dmOverlayEl && dmInputEl) {
			dmOverlayEl.scrollLeft = dmInputEl.scrollLeft;
		}
	}

	/* ───── Drag-and-drop image ───── */
	let isDragOver = $state(false);
	let dragCounter = 0;

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
		if (!app.activeDmChannelId) return;
		const file = e.dataTransfer?.files[0];
		if (file?.type.startsWith('image/')) {
			app.attachDmImage(file);
		}
	}
</script>

<main
	class="dm-chat"
	aria-label="Direct message conversation"
	ondragenter={handleDragEnter}
	ondragover={handleDragOver}
	ondragleave={handleDragLeave}
	ondrop={handleDrop}
>
	<header class="dm-header">
		<button class="mobile-nav-btn" onclick={() => { app.mobileNavOpen = true; }} aria-label="Open navigation">
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M3 5h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 1 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2z"/>
			</svg>
		</button>
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

	<div class="dm-body">
		{#if isDragOver && app.activeDmChannelId}
			<div class="drop-overlay" aria-hidden="true">
				<div class="drop-overlay-content">
					<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
						<path d="M19 7v2.99s-1.99.01-2 0V7h-3s.01-1.99 0-2h3V2h2v3h3v2h-3zm-3 4V8h-3V5H5c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2v-8h-3zM5 19l3-4 2 3 3-4 4 5H5z"/>
					</svg>
					<span class="drop-overlay-text">Drop image to upload</span>
				</div>
			</div>
		{/if}
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
				{#each app.dmMessages as message, i (message.id)}
					{@const prev = i > 0 ? app.dmMessages[i - 1] : null}
					{@const isGrouped = prev?.authorUserId === message.authorUserId && prev?.authorName === message.authorName}
					{@const ytUrls = message.body ? extractYouTubeUrls(message.body) : []}
					{@const coveredIds = new Set((message.linkPreviews ?? []).map((lp) => { const m = /[\w-]{11}/.exec(lp.url); return m?.[0] ?? ''; }).filter(Boolean))}
					{@const uncoveredYt = ytUrls.filter((yt) => !coveredIds.has(yt.videoId))}				<div data-message-id={message.id} class:reply-highlight={highlightedMessageId === message.id}>					<article class="message" class:grouped={isGrouped}>
					<!-- Reply action bar -->
					<div class="dm-message-actions">
						<button class="dm-action-btn" aria-label="Reply" onclick={() => handleReply(message)}>
							<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
								<path d="M6.598 2.152a.5.5 0 0 1 .049.703L3.354 6.5H9.5A4.5 4.5 0 0 1 14 11v1a.5.5 0 0 1-1 0v-1A3.5 3.5 0 0 0 9.5 7.5H3.354l3.293 3.645a.5.5 0 0 1-.742.67l-4-4.43a.5.5 0 0 1 0-.67l4-4.43a.5.5 0 0 1 .703-.049z"/>
							</svg>
						</button>
						{#if message.authorUserId === app.me?.user.id}
							<button class="dm-action-btn" aria-label="Edit message" onclick={() => startDmEdit(message)}>
								<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
									<path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168l10-10ZM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5Zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293l6.5-6.5Z"/>
								</svg>
							</button>
							<button class="dm-action-btn dm-action-btn-danger" aria-label="Delete message" onclick={() => app.deleteDmMessage(message.id)}>
								<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
									<path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6Z"/>
									<path d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1ZM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118ZM2.5 3h11V2h-11v1Z"/>
								</svg>
							</button>
						{/if}
					</div>

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
							{#if message.replyContext}
								<ReplyReference replyContext={message.replyContext} onClickGoToOriginal={() => scrollToMessage(message.replyContext!.messageId)} />
							{/if}
								<div class="message-header">
									<strong class="message-author">{message.authorName}</strong>
									<time class="message-time">{formatTime(message.createdAt)}</time>
									{#if message.editedAt}
										<span class="edited-label">(edited)</span>
									{/if}
								</div>
							{#if editingDmMessageId === message.id}
								<div class="edit-container">
									<textarea
										class="edit-input"
										bind:value={editDmBody}
										onkeydown={handleDmEditKeydown}
									></textarea>
									<div class="edit-actions">
										<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelDmEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveDmEdit}>save</button></span>
									</div>
								</div>
							{:else if message.body}
								<p class="message-body"><LinkifiedText text={message.body} /></p>
							{/if}
							{#if message.imageUrl}
								<button type="button" class="message-image-link" onclick={() => app.openImagePreview(message.imageUrl!)}>
									<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
								</button>
							{/if}
							{#if message.linkPreviews?.length}
								<div class="link-previews">
									{#each message.linkPreviews as preview}
										<LinkPreviewCard {preview} />
									{/each}
								</div>
							{/if}
							{#if uncoveredYt.length}
								<div class="link-previews">
									{#each uncoveredYt as yt}
										<YouTubeEmbed videoId={yt.videoId} />
									{/each}
								</div>
							{/if}
						</div>
					{:else}
						<div class="message-avatar-col">
							<time class="message-time-inline">{formatTime(message.createdAt)}</time>
						</div>
						<div class="message-content">
							{#if message.replyContext}
								<ReplyReference replyContext={message.replyContext} onClickGoToOriginal={() => scrollToMessage(message.replyContext!.messageId)} />
							{/if}
							{#if editingDmMessageId === message.id}
								<div class="edit-container">
									<textarea
										class="edit-input"
										bind:value={editDmBody}
										onkeydown={handleDmEditKeydown}
									></textarea>
									<div class="edit-actions">
										<span class="edit-hint">Escape to <button class="edit-link-btn" onclick={cancelDmEdit}>cancel</button> &middot; Enter to <button class="edit-link-btn" onclick={saveDmEdit}>save</button></span>
									</div>
								</div>
							{:else if message.body}
								<p class="message-body">
									<LinkifiedText text={message.body} />
									{#if message.editedAt}
										<span class="edited-label">(edited)</span>
									{/if}
								</p>
							{/if}
							{#if message.imageUrl}
								<button type="button" class="message-image-link" onclick={() => app.openImagePreview(message.imageUrl!)}>
									<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
								</button>
							{/if}
							{#if message.linkPreviews?.length}
								<div class="link-previews">
									{#each message.linkPreviews as preview}
										<LinkPreviewCard {preview} />
									{/each}
								</div>
							{/if}
							{#if uncoveredYt.length}
								<div class="link-previews">
									{#each uncoveredYt as yt}
										<YouTubeEmbed videoId={yt.videoId} />
									{/each}
								</div>
							{/if}
							</div>
						{/if}
					</article>
					</div>
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
		{#if app.replyingTo?.context === 'dm'}
			<ReplyComposerBar authorName={app.replyingTo.authorName} bodyPreview={app.replyingTo.bodyPreview} onCancel={() => app.cancelReply()} />
		{/if}
		{#if app.pendingDmImagePreview}
			<div class="image-preview">
				<img src={app.pendingDmImagePreview} alt="Attachment preview" class="preview-thumb" />
				<button
					type="button"
					class="remove-preview"
					onclick={() => app.clearPendingDmImage()}
					aria-label="Remove image"
				>
					<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
					</svg>
				</button>
			</div>
		{/if}
		{#if !app.isHubConnected}
			<div class="composer-row">
				<div class="composer-input-wrapper composer-disconnected">
					<span class="connecting-message" aria-live="polite">Codec connecting<span class="animated-ellipsis"></span></span>
				</div>
			</div>
		{:else}
			<div class="composer-row">
			<input type="file" accept="image/jpeg,image/png,image/webp,image/gif" class="sr-only" bind:this={dmFileInputEl} onchange={handleDmFileSelect} />
			<button
				class="composer-attach"
				type="button"
				onclick={() => dmFileInputEl?.click()}
				disabled={!app.activeDmChannelId || app.isSendingDm}
				aria-label="Attach image"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
				</svg>
			</button>
			<div class="composer-input-wrapper">
				<div class="composer-input-overlay" bind:this={dmOverlayEl} aria-hidden="true"><ComposerOverlay text={app.dmMessageBody} /></div>
				<input
					bind:this={dmInputEl}
					class="composer-input"
					type="text"
					placeholder={app.activeDmParticipant ? `Message @${app.activeDmParticipant.displayName}` : 'Select a conversation…'}
					bind:value={app.dmMessageBody}
					disabled={!app.activeDmChannelId || app.isSendingDm}
					oninput={handleDmInput}
					onkeydown={handleDmKeydown}
					onpaste={handleDmPaste}
				/>
			</div>
			<button
				class="composer-send"
				type="submit"
				disabled={!app.activeDmChannelId || (!app.dmMessageBody.trim() && !app.pendingDmImage) || app.isSendingDm}
				aria-label="Send message"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
				</svg>
			</button>
			</div>
		{/if}
		</form>
	</div>
</main>

<style>
	.dm-chat {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
		position: relative;
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

	/* ───── Header ───── */
	.dm-header {
		height: 48px;
		display: flex;
		align-items: center;
		padding: 0 16px;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
		gap: 8px;
	}

	.dm-header-left {
		display: flex;
		align-items: center;
		gap: 10px;
		flex: 1;
		min-width: 0;
	}

	/* ───── Mobile navigation button ───── */

	.mobile-nav-btn {
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

	.mobile-nav-btn:hover {
		color: var(--text-header);
		background: var(--bg-message-hover);
	}

	@media (max-width: 899px) {
		.mobile-nav-btn {
			display: grid;
		}
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

	.dm-body {
		flex: 1;
		position: relative;
		display: flex;
		flex-direction: column;
		overflow: hidden;
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
		contain: content;
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
		flex-direction: column;
		gap: 0;
		background: var(--bg-primary);
	}

	.composer-row {
		display: flex;
		align-items: center;
		gap: 0;
	}

	.composer-attach {
		background: var(--input-bg);
		border: none;
		padding: 12px 10px;
		border-radius: 8px 0 0 8px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-attach:hover:not(:disabled) { color: var(--accent); }

	.composer-attach:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.composer-input-wrapper {
		flex: 1;
		position: relative;
		background: var(--input-bg);
		overflow: hidden;
	}

	.composer-input-overlay {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		padding: 12px 16px;
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		color: var(--text-normal);
		pointer-events: none;
		white-space: nowrap;
		overflow: hidden;
	}

	.composer-input {
		position: relative;
		width: 100%;
		padding: 12px 16px;
		border: none;
		background: transparent;
		color: transparent;
		caret-color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		outline: none;
		min-height: 20px;
	}

	.composer-input::placeholder { color: var(--text-dim); }
	.composer-input::selection { background: rgba(88, 101, 242, 0.3); }
	.composer-input-wrapper:focus-within { box-shadow: 0 0 0 2px var(--accent); }
	.composer-input-wrapper:has(.composer-input:disabled) { opacity: 0.5; }

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

	/* ───── Disconnected state ───── */

	.composer-disconnected {
		border-radius: 8px;
		opacity: 0.5;
		display: flex;
		align-items: center;
	}

	.connecting-message {
		padding: 12px 16px;
		font-size: 15px;
		color: var(--text-dim);
		line-height: 20px;
		user-select: none;
	}

	.animated-ellipsis::after {
		content: '';
		animation: ellipsis-cycle 1.5s steps(4, end) infinite;
	}

	@keyframes ellipsis-cycle {
		0%   { content: ''; }
		25%  { content: '.'; }
		50%  { content: '..'; }
		75%  { content: '...'; }
	}

	/* ───── Image preview ───── */

	.image-preview {
		position: relative;
		display: inline-flex;
		margin-bottom: 8px;
		border-radius: 8px;
		overflow: hidden;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		max-width: 200px;
	}

	.preview-thumb {
		max-width: 200px;
		max-height: 150px;
		object-fit: contain;
		display: block;
	}

	.remove-preview {
		position: absolute;
		top: 4px;
		right: 4px;
		width: 22px;
		height: 22px;
		border-radius: 50%;
		border: none;
		background: rgba(0, 0, 0, 0.6);
		color: #fff;
		cursor: pointer;
		display: grid;
		place-items: center;
		padding: 0;
		transition: background-color 150ms ease;
	}

	.remove-preview:hover {
		background: rgba(0, 0, 0, 0.85);
	}

	.sr-only {
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border-width: 0;
	}

	/* ───── Message images ───── */

	.message-image-link {
		display: block;
		margin-top: 4px;
		max-width: 400px;
		border-radius: 8px;
		overflow: hidden;
		border: none;
		background: none;
		padding: 0;
		cursor: pointer;
	}

	.message-image {
		display: block;
		max-width: 100%;
		max-height: 300px;
		border-radius: 8px;
		object-fit: contain;
		cursor: pointer;
		transition: opacity 150ms ease;
	}

	.message-image-link:hover .message-image {
		opacity: 0.85;
	}

	.link-previews {
		display: flex;
		flex-direction: column;
		gap: 4px;
		margin-top: 4px;
	}

	/* ───── Edited label ───── */

	.edited-label {
		font-size: 11px;
		color: var(--text-muted);
		margin-left: 4px;
	}

	/* ───── Inline edit mode ───── */

	.edit-container {
		margin-top: 2px;
	}

	.edit-input {
		width: 100%;
		box-sizing: border-box;
		min-height: 44px;
		padding: 8px 12px;
		border-radius: 8px;
		border: 1px solid var(--accent);
		background: var(--bg-primary);
		color: var(--text-normal);
		font-family: inherit;
		font-size: 15px;
		line-height: 1.375;
		resize: vertical;
	}

	.edit-input:focus {
		outline: none;
		border-color: var(--accent);
		box-shadow: 0 0 0 2px rgba(88, 101, 242, 0.3);
	}

	.edit-actions {
		margin-top: 4px;
	}

	.edit-hint {
		font-size: 12px;
		color: var(--text-muted);
	}

	.edit-link-btn {
		background: none;
		border: none;
		padding: 0;
		color: var(--accent);
		font-size: 12px;
		cursor: pointer;
		font-family: inherit;
	}

	.edit-link-btn:hover {
		text-decoration: underline;
	}

	/* ───── Reply action bar ───── */

	.dm-message-actions {
		position: absolute;
		top: -14px;
		right: 16px;
		display: none;
		gap: 2px;
		padding: 2px 4px;
		border-radius: 4px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		box-shadow: 0 1px 4px rgba(0, 0, 0, 0.25);
		z-index: 2;
	}

	.message:hover .dm-message-actions { display: flex; }

	.dm-action-btn {
		display: grid;
		place-items: center;
		width: 28px;
		height: 28px;
		border: none;
		border-radius: 4px;
		background: transparent;
		color: var(--text-muted);
		cursor: pointer;
		transition: background-color 100ms ease, color 100ms ease;
	}

	.dm-action-btn:hover {
		background: var(--bg-message-hover);
		color: var(--text-normal);
	}

	.dm-action-btn-danger:hover {
		color: #ed4245;
		background: rgba(237, 66, 69, 0.1);
	}

	/* ───── Reply highlight ───── */

	:global(.reply-highlight) {
		animation: dm-reply-highlight-fade 1.5s ease-out;
	}

	@keyframes dm-reply-highlight-fade {
		0%   { background: color-mix(in srgb, var(--accent) 15%, transparent); }
		100% { background: transparent; }
	}

	@media (prefers-reduced-motion: reduce) {
		:global(.reply-highlight) {
			animation: none;
			background: color-mix(in srgb, var(--accent) 10%, transparent);
		}
	}
</style>
