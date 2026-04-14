<script lang="ts">
	import { onMount, tick, untrack } from 'svelte';
	import { getAuthStore } from '$lib/state/auth-store.svelte.js';
	import { getUIStore } from '$lib/state/ui-store.svelte.js';
	import { getServerStore } from '$lib/state/server-store.svelte.js';
	import { getMessageStore } from '$lib/state/message-store.svelte.js';
	import { getDmStore } from '$lib/state/dm-store.svelte.js';
	import { getVoiceStore } from '$lib/state/voice-store.svelte.js';
	import { formatTime, formatMessageTimestamp, formatDateSeparator, isDifferentDay } from '$lib/utils/format.js';
	import LinkifiedText from '$lib/components/chat/LinkifiedText.svelte';
	import LinkPreviewCard from '$lib/components/chat/LinkPreviewCard.svelte';
	import FileCard from '$lib/components/chat/FileCard.svelte';
	import YouTubeEmbed from '$lib/components/chat/YouTubeEmbed.svelte';
	import ReplyReference from '$lib/components/chat/ReplyReference.svelte';
	import ReplyComposerBar from '$lib/components/chat/ReplyComposerBar.svelte';
	import ComposerOverlay from '$lib/components/chat/ComposerOverlay.svelte';
	import MessageActionBar from '$lib/components/chat/MessageActionBar.svelte';
	import ReactionBar from '$lib/components/chat/ReactionBar.svelte';
	import { extractYouTubeUrls } from '$lib/utils/youtube.js';
	import EmojiPicker from '$lib/components/chat/EmojiPicker.svelte';
	import GifPicker from '$lib/components/chat/GifPicker.svelte';
	import DmCallHeader from '$lib/components/voice/DmCallHeader.svelte';
	import VideoGrid from '$lib/components/voice/VideoGrid.svelte';
	import SearchPanel from '$lib/components/search/SearchPanel.svelte';
	import { recordEmojiUse, getFrequentEmojis } from '$lib/utils/emoji-frequency.js';
	import { isTouchDevice } from '$lib/utils/dom.js';

	const auth = getAuthStore();
	const ui = getUIStore();
	const servers = getServerStore();
	const msgStore = getMessageStore();
	const dms = getDmStore();
	const voice = getVoiceStore();
	const BOTTOM_THRESHOLD = 50;

	let container: HTMLDivElement;
	let dmInputEl = $state<HTMLInputElement>(undefined!);
	let dmFileInputEl = $state<HTMLInputElement>(undefined!);
	let dmOverlayEl = $state<HTMLDivElement>(undefined!);
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

	function handleReply(message: typeof dms.dmMessages[0]): void {
		dms.startReply(message.id, message.authorName, message.body?.slice(0, 100) ?? '');
	}

	/* ───── Inline message editing ───── */
	let editingDmMessageId = $state<string | null>(null);
	let editDmBody = $state('');

	function startDmEdit(message: typeof dms.dmMessages[0]): void {
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
		const msg = dms.dmMessages.find((m) => m.id === editingDmMessageId);
		if (trimmed === msg?.body) {
			cancelDmEdit();
			return;
		}
		await dms.editDmMessage(editingDmMessageId, trimmed);
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
		if (!dms.isLoadingDmMessages && dms.dmMessages.length > 0) {
			tick().then(() => {
				requestAnimationFrame(() => scrollToBottom(true));
			});
		}

		// Re-scroll when images or embeds finish loading and expand the scroll
		// height. The 'load' event doesn't bubble, so capture phase is required.
		function handleContentLoad() {
			if (isLockedToBottom && !isAtBottom()) {
				scrollToBottom(true);
			}
		}
		container?.addEventListener('load', handleContentLoad, true);

		// Keep the feed pinned to the bottom when the container shrinks (e.g. the
		// composer grows taller as the user adds line breaks). Without this, the
		// visible area shrinks from the bottom and recent messages scroll out of view.
		const resizeObserver = new ResizeObserver(() => {
			if (isLockedToBottom && !isAtBottom()) {
				scrollToBottom(true);
			}
		});
		if (container) resizeObserver.observe(container);

		return () => {
			container?.removeEventListener('load', handleContentLoad, true);
			resizeObserver.disconnect();
		};
	});

	// Scroll to highlighted message from search jump
	$effect(() => {
		const targetId = msgStore.highlightedMessageId;
		if (targetId && container) {
			setTimeout(() => {
				const el = container?.querySelector(`[data-message-id="${CSS.escape(targetId)}"]`);
				if (el) {
					el.scrollIntoView({ behavior: 'smooth', block: 'center' });
				}
			}, 100);
		}
	});

	// Reset scroll on channel change
	$effect(() => {
		const channelId = dms.activeDmChannelId;
		if (channelId !== previousChannelId) {
			previousChannelId = channelId;
			isLockedToBottom = true;
			unreadCount = 0;
			previousMessageCount = 0;
		}
	});

	// Auto-scroll when locked, or track unread when unlocked
	$effect(() => {
		const count = dms.dmMessages.length;
		const loading = dms.isLoadingDmMessages;

		// While messages are being fetched, the array still holds stale data from
		// the previous conversation. Don't sync previousMessageCount here — the
		// reset effect already set it to 0 for the new conversation.
		if (loading) return;

		if (count === 0) {
			previousMessageCount = 0;
			return;
		}

		const newMessages = count - previousMessageCount;
		previousMessageCount = count;
		if (newMessages <= 0) return;

		untrack(() => {
			if (isLockedToBottom) {
				// Wait for the next animation frame so the browser finishes layout
				// for the newly rendered messages before we measure scrollHeight.
				tick().then(() => {
					requestAnimationFrame(() => scrollToBottom(newMessages > 3));
				});
			} else {
				unreadCount += newMessages;
			}
		});
	});

	async function handleDmSubmit(e: SubmitEvent) {
		e.preventDefault();
		await dms.sendDmMessage();
		dmInputEl?.focus();
	}

	function handleDmFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (file) {
			if (file.type.startsWith('image/')) {
				dms.attachDmImage(file);
			} else {
				dms.attachDmFile(file);
			}
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
					dms.attachDmImage(file);
				}
				return;
			}
		}
	}

	function handleDmKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape' && dms.replyingTo?.context === 'dm') {
			e.preventDefault();
			dms.cancelReply();
		}
	}

	function handleDmInput(): void {
		dms.handleDmComposerInput();
		syncDmOverlayScroll();
	}

	function syncDmOverlayScroll(): void {
		if (dmOverlayEl && dmInputEl) {
			dmOverlayEl.scrollLeft = dmInputEl.scrollLeft;
		}
	}

	/* ───── Emoji / GIF picker ───── */
	let showDmPicker = $state(false);
	let dmPickerTab = $state<'emoji' | 'gif'>('emoji');
	let dmQuickEmojis = $state(getFrequentEmojis(8));

	function handleDmEmojiInsert(emoji: string) {
		recordEmojiUse(emoji);
		dmQuickEmojis = getFrequentEmojis(8);
		if (!dmInputEl) return;
		const start = dmInputEl.selectionStart ?? dms.dmMessageBody.length;
		const end = dmInputEl.selectionEnd ?? start;
		const before = dms.dmMessageBody.slice(0, start);
		const after = dms.dmMessageBody.slice(end);
		dms.dmMessageBody = before + emoji + after;
		const newPos = start + emoji.length;
		requestAnimationFrame(() => {
			dmInputEl.focus();
			dmInputEl.setSelectionRange(newPos, newPos);
		});
	}

	function handleDmGifSelect(gifUrl: string) {
		showDmPicker = false;
		dmPickerTab = 'emoji';
		dms.sendDmGifMessage(gifUrl);
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
		if (!dms.activeDmChannelId) return;
		const file = e.dataTransfer?.files[0];
		if (file) {
			if (file.type.startsWith('image/')) {
				dms.attachDmImage(file);
			} else {
				dms.attachDmFile(file);
			}
		}
	}
</script>

<div class="dm-chat-wrapper" class:search-open={msgStore.isSearchOpen}>
<main
	class="dm-chat"
	aria-label="Direct message conversation"
	ondragenter={handleDragEnter}
	ondragover={handleDragOver}
	ondragleave={handleDragLeave}
	ondrop={handleDrop}
>
	<header class="dm-header">
		<button class="mobile-nav-btn" onclick={() => { ui.mobileNavOpen = true; }} aria-label="Open navigation">
			<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
				<path d="M3 5h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 1 1 0-2zm0 4h14a1 1 0 1 1 0 2H3a1 1 0 0 1 0-2z"/>
			</svg>
		</button>
		<div class="dm-header-left">
			{#if dms.activeDmParticipant?.avatarUrl}
				<img class="header-avatar" src={dms.activeDmParticipant.avatarUrl} alt="" />
			{:else if dms.activeDmParticipant}
				<div class="header-avatar-placeholder" aria-hidden="true">
					{dms.activeDmParticipant.displayName.slice(0, 1).toUpperCase()}
				</div>
			{/if}
			<h1 class="dm-participant-name">
				{dms.activeDmParticipant?.displayName ?? 'Select a conversation'}
			</h1>
		</div>
		<div class="dm-header-right">
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
			<button
				class="call-btn-header"
				disabled={!!voice.activeCall || !!voice.incomingCall}
				onclick={() => { if (dms.activeDmChannelId) voice.startCall(dms.activeDmChannelId); }}
				aria-label="Start voice call"
				title="Start voice call"
			>
				<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
					<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
				</svg>
			</button>
		</div>
	</header>

	{#if voice.activeCall && voice.activeCall.dmChannelId === dms.activeDmChannelId}
		<DmCallHeader />
		<VideoGrid />
	{/if}

	<div class="dm-body">
		{#if isDragOver && dms.activeDmChannelId}
			<div class="drop-overlay" aria-hidden="true">
				<div class="drop-overlay-content">
					<svg width="48" height="48" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
						<path d="M19 7v2.99s-1.99.01-2 0V7h-3s.01-1.99 0-2h3V2h2v3h3v2h-3zm-3 4V8h-3V5H5c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2v-8h-3zM5 19l3-4 2 3 3-4 4 5H5z"/>
					</svg>
					<span class="drop-overlay-text">Drop image to upload</span>
				</div>
			</div>
		{/if}
		{#if ui.error}
			<div class="error-banner" role="alert">{ui.error}</div>
		{/if}

		<!-- Message feed -->
		<div class="feed-wrapper">
		<div class="message-feed" bind:this={container} onscroll={handleScroll}>
			{#if dms.isLoadingDmMessages}
				<p class="muted feed-status">Loading messages…</p>
			{:else if dms.dmMessages.length === 0}
				<p class="muted feed-status">
					No messages yet. Say hi to {dms.activeDmParticipant?.displayName ?? 'your friend'}!
				</p>
			{:else}
				{#each dms.dmMessages as message, i (message.id)}
					{@const prev = i > 0 ? dms.dmMessages[i - 1] : null}
					{@const newDay = !prev || isDifferentDay(prev.createdAt, message.createdAt)}
					{#if newDay}
						<div class="date-separator" role="separator">
							<span class="date-separator-label">{formatDateSeparator(message.createdAt)}</span>
						</div>
					{/if}
					{#if message.messageType === 1}
						<div class="system-message voice-call-event">
							<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" class="call-event-icon" class:missed={message.body === 'missed'}>
								<path d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>
							</svg>
							<span class="call-event-text">
								{#if message.body === 'missed'}
									Missed voice call
								{:else if message.body?.startsWith('call:')}
									{@const secs = parseInt(message.body.split(':')[1] ?? '0')}
									Voice call — {Math.floor(secs / 60)}m {secs % 60}s
								{:else}
									Voice call
								{/if}
							</span>
							<time class="call-event-time">{formatTime(message.createdAt)}</time>
						</div>
					{:else}
					{@const isGrouped = !newDay && prev?.authorUserId === message.authorUserId && prev?.authorName === message.authorName}
					{@const ytUrls = message.body ? extractYouTubeUrls(message.body) : []}
					{@const coveredIds = new Set((message.linkPreviews ?? []).map((lp) => { const m = /[\w-]{11}/.exec(lp.url); return m?.[0] ?? ''; }).filter(Boolean))}
					{@const uncoveredYt = ytUrls.filter((yt) => !coveredIds.has(yt.videoId))}				<div data-message-id={message.id} class:reply-highlight={highlightedMessageId === message.id} class:search-highlight={msgStore.highlightedMessageId === message.id}>					<article class="message" class:grouped={isGrouped}>
					<!-- Floating action bar -->
					<MessageActionBar
						isOwnMessage={message.authorUserId === auth.me?.user.id}
						canDelete={message.authorUserId === auth.me?.user.id}
						onReply={() => handleReply(message)}
						onReact={(emoji) => dms.toggleDmReaction(message.id, emoji)}
						onEdit={() => startDmEdit(message)}
						onDelete={() => dms.deleteDmMessage(message.id)}
						isReactionPending={(emoji) => ui.isReactionPending(message.id, emoji)}
					/>

					{#if !isGrouped}
						<div class="message-avatar-col">
							{#if message.authorAvatarUrl}
								<img class="message-avatar-img" src={message.authorAvatarUrl} alt="" />
							{:else}
								<div class="message-avatar" class:deleted-avatar={!message.authorUserId} aria-hidden="true">
									{message.authorUserId ? message.authorName.slice(0, 1).toUpperCase() : '?'}
								</div>
							{/if}
						</div>
						<div class="message-content">
							{#if message.replyContext}
								<ReplyReference replyContext={message.replyContext} onClickGoToOriginal={() => scrollToMessage(message.replyContext!.messageId)} />
							{/if}
								<div class="message-header">
									<strong class="message-author" class:deleted-user={!message.authorUserId}>{message.authorUserId ? message.authorName : 'Deleted User'}</strong>
									<time class="message-time">{formatMessageTimestamp(message.createdAt)}</time>
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
								<p class="message-body"><LinkifiedText text={message.body} customEmojis={servers.customEmojis} /></p>
							{/if}
							{#if message.imageUrl}
								<button type="button" class="message-image-link" onclick={() => ui.openImagePreview(message.imageUrl!)}>
									<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
								</button>
							{/if}
							{#if message.fileUrl && message.fileName}
								<FileCard fileUrl={message.fileUrl} fileName={message.fileName} fileSize={message.fileSize} fileContentType={message.fileContentType} />
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
							{#if (message.reactions ?? []).length > 0}
								<ReactionBar
									reactions={message.reactions}
									currentUserId={auth.me?.user.id ?? null}
									onToggle={(emoji) => dms.toggleDmReaction(message.id, emoji)}
									isPending={(emoji) => ui.isReactionPending(message.id, emoji)}
									customEmojis={servers.customEmojis}
								/>
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
									<LinkifiedText text={message.body} customEmojis={servers.customEmojis} />
									{#if message.editedAt}
										<span class="edited-label">(edited)</span>
									{/if}
								</p>
							{/if}
							{#if message.imageUrl}
								<button type="button" class="message-image-link" onclick={() => ui.openImagePreview(message.imageUrl!)}>
									<img src={message.imageUrl} alt="Uploaded attachment" class="message-image" loading="lazy" />
								</button>
							{/if}
							{#if message.fileUrl && message.fileName}
								<FileCard fileUrl={message.fileUrl} fileName={message.fileName} fileSize={message.fileSize} fileContentType={message.fileContentType} />
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
							{#if (message.reactions ?? []).length > 0}
								<ReactionBar
									reactions={message.reactions}
									currentUserId={auth.me?.user.id ?? null}
									onToggle={(emoji) => dms.toggleDmReaction(message.id, emoji)}
									isPending={(emoji) => ui.isReactionPending(message.id, emoji)}
									customEmojis={servers.customEmojis}
								/>
							{/if}
							</div>
						{/if}
					</article>
					</div>
					{/if}
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

		<!-- Typing indicator -->
		{#if dms.dmTypingUsers.length > 0}
			<div class="typing-indicator" aria-live="polite">
				<span class="typing-dots" aria-hidden="true">
					<span class="dot"></span><span class="dot"></span><span class="dot"></span>
				</span>
				<span class="typing-text">
					{#if dms.dmTypingUsers.length === 1}
						<strong>{dms.dmTypingUsers[0]}</strong> is typing…
					{:else}
						Several people are typing…
					{/if}
				</span>
			</div>
		{/if}
	</div>

		<!-- Composer -->
		<form class="composer" onsubmit={handleDmSubmit}>
		{#if dms.replyingTo?.context === 'dm'}
			<ReplyComposerBar authorName={dms.replyingTo.authorName} bodyPreview={dms.replyingTo.bodyPreview} onCancel={() => dms.cancelReply()} />
		{/if}
		{#if dms.pendingDmImagePreview}
			<div class="image-preview">
				<img src={dms.pendingDmImagePreview} alt="Attachment preview" class="preview-thumb" />
				<button
					type="button"
					class="remove-preview"
					onclick={() => dms.clearPendingDmImage()}
					aria-label="Remove image"
				>
					<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
					</svg>
				</button>
			</div>
		{/if}
		{#if dms.pendingDmFile}
			<div class="file-preview">
				<svg class="file-preview-icon" width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-1 2l5 5h-5V4zM6 20V4h7v5h5v11H6z"/></svg>
				<span class="file-preview-name">{dms.pendingDmFile.name}</span>
				<button
					type="button"
					class="remove-preview"
					onclick={() => dms.clearPendingDmFile()}
					aria-label="Remove file"
				>
					<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
					</svg>
				</button>
			</div>
		{/if}
		{#if isTouchDevice && ui.isHubConnected && !showDmPicker && !dms.replyingTo && dms.activeDmChannelId}
			<div class="quick-emoji-bar" role="toolbar" aria-label="Quick emoji">
				{#each dmQuickEmojis as emoji (emoji)}
					{@const customMatch = emoji.startsWith(':') && emoji.endsWith(':')
						? servers.customEmojis.find((c) => `:${c.name}:` === emoji)
						: undefined}
					<button
						type="button"
						class="quick-emoji-btn"
						onclick={() => handleDmEmojiInsert(emoji)}
					>{#if customMatch}<img src={customMatch.imageUrl} alt={customMatch.name} width="22" height="22" class="quick-emoji-img" />{:else}{emoji}{/if}</button>
				{/each}
				<button
					type="button"
					class="quick-emoji-btn quick-emoji-more"
					onclick={() => { showDmPicker = true; dmPickerTab = 'emoji'; }}
					aria-label="Open emoji picker"
					title="More emojis"
				>
					<svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"/>
					</svg>
				</button>
			</div>
		{/if}

		{#if !ui.isHubConnected}
			<div class="composer-row">
				<div class="composer-input-wrapper composer-disconnected">
					<span class="connecting-message" aria-live="polite">Codec connecting<span class="animated-ellipsis"></span></span>
				</div>
			</div>
		{:else}
			<div class="composer-row">
			<input type="file" class="sr-only" bind:this={dmFileInputEl} onchange={handleDmFileSelect} />
			<button
				class="composer-attach"
				type="button"
				onclick={() => dmFileInputEl?.click()}
				disabled={!dms.activeDmChannelId || dms.isSendingDm}
				aria-label="Attach file"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M10 3a1 1 0 0 1 1 1v5h5a1 1 0 1 1 0 2h-5v5a1 1 0 1 1-2 0v-5H4a1 1 0 1 1 0-2h5V4a1 1 0 0 1 1-1z"/>
				</svg>
			</button>
			<div class="composer-input-wrapper">
				<div class="composer-input-overlay" bind:this={dmOverlayEl} aria-hidden="true"><ComposerOverlay text={dms.dmMessageBody} customEmojis={servers.customEmojis} /></div>
				<input
					bind:this={dmInputEl}
					class="composer-input"
					type="text"
					inputmode="text"
					autocomplete="off"
					placeholder={dms.activeDmParticipant ? `Message @${dms.activeDmParticipant.displayName}` : 'Select a conversation…'}
					bind:value={dms.dmMessageBody}
					disabled={!dms.activeDmChannelId || dms.isSendingDm}
					oninput={handleDmInput}
					onkeydown={handleDmKeydown}
					onpaste={handleDmPaste}
				/>
			</div>
			<button
				class="composer-emoji"
				type="button"
				onclick={() => { showDmPicker = !showDmPicker; if (showDmPicker) dmPickerTab = 'emoji'; }}
				disabled={!dms.activeDmChannelId || dms.isSendingDm}
				aria-label="Add emoji or GIF"
				title="Add emoji or GIF"
			>
				<svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1Zm0 1a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm-2.5 4a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5Zm5 0a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM4.5 9.5a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 .383.82A3.98 3.98 0 0 1 8 11.5a3.98 3.98 0 0 1-2.883-1.68A.5.5 0 0 1 5 9.5h-1Z"/>
				</svg>
			</button>
			<button
				class="composer-send"
				type="submit"
				disabled={!dms.activeDmChannelId || (!dms.dmMessageBody.trim() && !dms.pendingDmImage && !dms.pendingDmFile) || dms.isSendingDm}
				aria-label="Send message"
			>
				<svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
					<path d="M2.5 2.3a.75.75 0 0 1 .8-.05l14 7a.75.75 0 0 1 0 1.34l-14 7A.75.75 0 0 1 2.2 17l1.9-6.5a.5.5 0 0 1 .47-.35h4.68a.75.75 0 0 0 0-1.5H4.57a.5.5 0 0 1-.47-.35L2.2 1.8a.75.75 0 0 1 .3-.8z"/>
				</svg>
			</button>
			</div>
			{#if showDmPicker}
				<div class="composer-picker-wrapper">
					<div class="picker-backdrop" role="presentation" onclick={() => { showDmPicker = false; }} onkeydown={(e) => { if (e.key === 'Escape') showDmPicker = false; }}></div>
					<div class="picker-container" role="dialog" aria-label="Emoji and GIF picker">
						<div class="picker-tab-bar">
							<button
								class="picker-tab"
								class:active={dmPickerTab === 'emoji'}
								type="button"
								onclick={() => (dmPickerTab = 'emoji')}
							>Emoji</button>
							<button
								class="picker-tab"
								class:active={dmPickerTab === 'gif'}
								type="button"
								onclick={() => (dmPickerTab = 'gif')}
							>GIFs</button>
						</div>
						<div class="picker-body">
							{#if dmPickerTab === 'emoji'}
								<EmojiPicker
									mode="insert"
									embedded={true}
									onSelect={handleDmEmojiInsert}
									onClose={() => { showDmPicker = false; }}
									customEmojis={servers.customEmojis}
								/>
							{:else}
								<GifPicker
									onSelect={handleDmGifSelect}
									onClose={() => { showDmPicker = false; }}
								/>
							{/if}
						</div>
					</div>
				</div>
			{/if}
		{/if}
		</form>
	</div>
</main>
{#if msgStore.isSearchOpen}
	<SearchPanel isDm />
{/if}
</div>

<style>
	.dm-chat-wrapper {
		display: flex;
		height: 100%;
		overflow: hidden;
	}

	.dm-chat {
		background: var(--bg-primary);
		display: flex;
		flex-direction: column;
		overflow: hidden;
		position: relative;
		flex: 1;
		min-width: 0;
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
			min-width: 44px;
			min-height: 44px;
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
		padding: 16px 0 26px;
		scrollbar-width: thin;
		scrollbar-color: var(--border) transparent;
	}

	.feed-status { padding: 16px; text-align: center; }
	.muted { color: var(--text-muted); }

	.date-separator {
		display: flex;
		align-items: center;
		margin: 8px 16px;
		gap: 8px;
	}

	.date-separator::before,
	.date-separator::after {
		content: '';
		flex: 1;
		height: 1px;
		background: var(--border);
	}

	.date-separator-label {
		font-size: 12px;
		font-weight: 600;
		color: var(--text-muted);
		white-space: nowrap;
		padding: 2px 4px;
	}

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

	.message-author.deleted-user {
		color: var(--text-muted);
		font-style: italic;
	}

	.deleted-avatar {
		opacity: 0.5;
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
		position: absolute;
		bottom: 0;
		left: 0;
		right: 0;
		padding: 2px 16px 4px;
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 13px;
		color: var(--text-muted);
		background: var(--bg-primary);
		min-height: 20px;
		z-index: 2;
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
		padding: 8px 16px 24px;
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
		box-sizing: border-box;
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
		box-sizing: border-box;
		margin: 0;
		padding: 12px 16px;
		border: none;
		background: transparent;
		color: transparent;
		caret-color: var(--text-normal);
		font-size: 15px;
		font-family: inherit;
		line-height: 20px;
		outline: none;
		height: 44px;
	}

	.composer-input::placeholder { color: var(--text-dim); }
	.composer-input::selection { background: rgba(var(--accent-rgb, 0, 255, 102), 0.3); }
	.composer-input-wrapper:focus-within { box-shadow: 0 0 0 2px var(--accent); }
	.composer-input-wrapper:has(.composer-input:disabled) { opacity: 0.5; }

	.composer-send {
		background: var(--input-bg);
		border: none;
		box-sizing: border-box;
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

	.composer-emoji {
		background: var(--input-bg);
		border: none;
		box-sizing: border-box;
		padding: 9px 6px;
		color: var(--text-muted);
		cursor: pointer;
		display: grid;
		place-items: center;
		flex-shrink: 0;
		transition: color 150ms ease;
	}

	.composer-emoji:hover:not(:disabled) { color: var(--accent); }

	.composer-emoji:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.composer-picker-wrapper {
		position: relative;
	}

	.picker-backdrop {
		position: fixed;
		inset: 0;
		z-index: 99;
	}

	.picker-container {
		position: absolute;
		z-index: 100;
		bottom: calc(100% + 4px);
		right: 0;
		width: 352px;
		max-height: 420px;
		display: flex;
		flex-direction: column;
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: 8px;
		box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
		overflow: hidden;
	}

	.picker-tab-bar {
		display: flex;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}

	.picker-tab {
		flex: 1;
		padding: 8px 12px;
		background: none;
		border: none;
		border-bottom: 2px solid transparent;
		color: var(--text-muted);
		font-size: 0.85rem;
		font-weight: 600;
		cursor: pointer;
		transition: color 0.15s, border-color 0.15s;
	}

	.picker-tab:hover { color: var(--text-primary); }

	.picker-tab.active {
		color: var(--accent);
		border-bottom-color: var(--accent);
	}

	.picker-body {
		flex: 1;
		min-height: 0;
		overflow: hidden;
		display: flex;
		flex-direction: column;
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

	.file-preview {
		position: relative;
		display: inline-flex;
		align-items: center;
		gap: 8px;
		margin-bottom: 8px;
		padding: 8px 32px 8px 12px;
		border-radius: 8px;
		border: 1px solid var(--border);
		background: var(--bg-secondary);
		max-width: 300px;
	}

	.file-preview-icon {
		flex-shrink: 0;
		color: var(--text-muted);
	}

	.file-preview-name {
		font-size: 13px;
		color: var(--text-normal);
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
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
		box-shadow: 0 0 0 2px rgba(var(--accent-rgb, 0, 255, 102), 0.3);
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

	/* ───── Reply / search highlight ───── */

	:global(.reply-highlight) {
		animation: dm-reply-highlight-fade 1.5s ease-out;
	}

	:global(.search-highlight) {
		animation: dm-search-highlight-fade 2s ease-out;
	}

	@keyframes dm-reply-highlight-fade {
		0%   { background: color-mix(in srgb, var(--accent) 15%, transparent); }
		100% { background: transparent; }
	}

	@keyframes dm-search-highlight-fade {
		0%   { background: color-mix(in srgb, var(--accent) 15%, transparent); }
		100% { background: transparent; }
	}

	@media (prefers-reduced-motion: reduce) {
		:global(.reply-highlight),
		:global(.search-highlight) {
			animation: none;
			background: color-mix(in srgb, var(--accent) 10%, transparent);
		}
	}

	/* ───── Search button ───── */

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

	/* ───── Call button in header ───── */

	.dm-header-right {
		margin-left: auto;
		display: flex;
		align-items: center;
	}
	.call-btn-header {
		background: none;
		border: none;
		color: var(--text-muted);
		cursor: pointer;
		padding: 6px;
		border-radius: 4px;
		display: grid;
		place-items: center;
		transition: color 150ms ease, background 150ms ease;
	}
	.call-btn-header:hover:not(:disabled) {
		color: var(--text-normal);
		background: var(--bg-message-hover);
	}
	.call-btn-header:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	/* ───── Voice call system messages ───── */

	.system-message.voice-call-event {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 8px;
		padding: 8px 16px;
		color: var(--text-muted);
		font-size: 13px;
	}
	.call-event-icon {
		color: var(--success);
		flex-shrink: 0;
	}
	.call-event-icon.missed {
		color: var(--danger);
	}
	.call-event-time {
		font-size: 11px;
		color: var(--text-dim);
	}

	/* ───── Mobile adjustments ───── */

	.quick-emoji-bar {
		display: none;
	}

	@media (max-width: 768px) {
		.system-message.voice-call-event {
			background: var(--bg-secondary);
			border-radius: 8px;
			margin: 4px 16px;
			padding: 10px 16px;
			font-size: 14px;
		}

		.dm-chat .composer-input {
			font-size: 16px;
			height: 44px;
		}

		.dm-chat .composer-attach,
		.dm-chat .composer-send {
			min-width: 44px;
			min-height: 44px;
		}

		.composer-emoji {
			min-width: 44px;
			min-height: 44px;
		}

		.dm-chat .composer {
			padding: 0 16px calc(16px + env(safe-area-inset-bottom, 0));
		}

		/* Quick emoji bar */
		.quick-emoji-bar {
			display: flex;
			align-items: center;
			gap: 2px;
			padding: 6px 8px;
			overflow-x: auto;
			-webkit-overflow-scrolling: touch;
			scrollbar-width: none;
		}

		.quick-emoji-bar::-webkit-scrollbar {
			display: none;
		}

		.quick-emoji-btn {
			flex-shrink: 0;
			display: grid;
			place-items: center;
			width: 40px;
			height: 36px;
			background: var(--bg-tertiary);
			border: 1px solid var(--border);
			border-radius: 8px;
			cursor: pointer;
			font-size: 20px;
			line-height: 1;
			padding: 0;
			transition: background 0.12s, transform 0.1s;
		}

		.quick-emoji-btn:active {
			transform: scale(0.92);
			background: var(--bg-message-hover);
		}

		.quick-emoji-more {
			color: var(--text-muted);
			font-size: 16px;
		}

		.quick-emoji-img {
			object-fit: contain;
		}

		/* Bottom sheet picker */
		.picker-container {
			position: fixed;
			bottom: 0;
			left: 0;
			right: 0;
			top: unset;
			width: 100%;
			max-height: 60vh;
			border-radius: 12px 12px 0 0;
			padding-bottom: env(safe-area-inset-bottom);
			animation: slide-up 200ms ease;
		}

		.picker-backdrop {
			background: rgba(0, 0, 0, 0.5);
		}

		.picker-tab {
			padding: 12px 16px;
			font-size: 0.95rem;
			min-height: 44px;
		}
	}

	@keyframes slide-up {
		from { transform: translateY(100%); }
		to { transform: translateY(0); }
	}
</style>
