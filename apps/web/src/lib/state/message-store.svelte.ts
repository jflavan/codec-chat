// apps/web/src/lib/state/message-store.svelte.ts
import { getContext, setContext } from 'svelte';
import type {
	Message,
	SearchFilters,
	PaginatedSearchResults,
	PinnedMessage,
	LinkPreview,
	MessagePinnedEvent,
	MessageUnpinnedEvent,
	DirectMessage
} from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type {
	ChatHubService,
	ReactionUpdate,
	MessageDeletedEvent,
	ChannelPurgedEvent,
	MessageEditedEvent
} from '$lib/services/chat-hub.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';
import { validateImage, validateFile } from '$lib/utils/attachments.js';
import { rememberReactionUpdate, matchAndRemoveReactionSnapshot } from '$lib/utils/reactions.js';

const MESSAGE_KEY = Symbol('message-store');

export function createMessageStore(
	auth: AuthStore,
	channels: ChannelStore,
	api: ApiClient,
	ui: UIStore,
	hub: ChatHubService
): MessageStore {
	const store = new MessageStore(auth, channels, api, ui, hub);
	setContext(MESSAGE_KEY, store);
	return store;
}

export function getMessageStore(): MessageStore {
	return getContext<MessageStore>(MESSAGE_KEY);
}

export class MessageStore {
	/* ───── dependencies ───── */
	private readonly auth!: AuthStore;
	private readonly channels!: ChannelStore;
	private readonly api!: ApiClient;
	private readonly ui!: UIStore;
	private readonly hub!: ChatHubService;

	/* ───── $state fields ───── */
	messages = $state<Message[]>([]);
	isPurgingChannel = $state(false);
	typingUsers = $state<string[]>([]);
	pendingImage = $state<File | null>(null);
	pendingImagePreview = $state<string | null>(null);
	pendingFile = $state<File | null>(null);
	messageBody = $state('');
	pendingMentions = $state<Map<string, string>>(new Map());
	replyingTo = $state<{
		messageId: string;
		authorName: string;
		bodyPreview: string;
		context: 'channel';
	} | null>(null);

	/* ───── search ───── */
	isSearchOpen = $state(false);
	searchQuery = $state('');
	searchFilters = $state<SearchFilters>({});
	searchResults = $state<PaginatedSearchResults | null>(null);
	isSearching = $state(false);
	highlightedMessageId = $state<string | null>(null);

	/* ───── pinned messages ───── */
	pinnedMessages = $state<PinnedMessage[]>([]);
	showPinnedPanel = $state(false);

	/* ───── loading flags ───── */
	isSending = $state(false);
	isLoadingMessages = $state(false);
	isLoadingOlderMessages = $state(false);
	hasMoreMessages = $state(false);

	/* ───── derived ───── */
	pinnedMessageIds = $derived(new Set(this.pinnedMessages.map((p) => p.messageId)));
	pinnedMessageCount = $derived(this.pinnedMessages.length);

	/* ───── callbacks (wired by +page.svelte to avoid circular deps) ───── */
	getSelectedServerId: (() => string | null) | null = null;
	getActiveDmChannelId: (() => string | null) | null = null;
	onSelectChannel: ((channelId: string) => Promise<void>) | null = null;
	onSelectDmConversation: ((channelId: string) => Promise<void>) | null = null;
	onSetDmMessages: ((messages: DirectMessage[]) => void) | null = null;

	/** Incremented on each loadMessages call to discard stale responses after fast channel switches. */
	private loadGeneration = 0;

	constructor(
		auth: AuthStore,
		channels: ChannelStore,
		api: ApiClient,
		ui: UIStore,
		hub: ChatHubService
	) {
		this.auth = auth;
		this.channels = channels;
		this.api = api;
		this.ui = ui;
		this.hub = hub;
	}

	/* ═══════════════════ Message Loading ═══════════════════ */

	async loadMessages(channelId: string): Promise<void> {
		if (!this.auth.idToken) return;
		const gen = ++this.loadGeneration;
		this.isLoadingMessages = true;
		try {
			const result = await this.api.getMessages(this.auth.idToken, channelId, { limit: 100 });
			// Discard stale response if user switched channels during the fetch.
			if (gen !== this.loadGeneration) return;
			this.messages = result.messages;
			this.hasMoreMessages = result.hasMore;
		} catch (e) {
			if (gen === this.loadGeneration) this.ui.setError(e);
		} finally {
			if (gen === this.loadGeneration) this.isLoadingMessages = false;
		}
	}

	async loadOlderMessages(): Promise<void> {
		if (
			!this.auth.idToken ||
			!this.channels.selectedChannelId ||
			!this.hasMoreMessages ||
			this.isLoadingOlderMessages
		)
			return;
		const oldest = this.messages[0];
		if (!oldest) return;

		this.isLoadingOlderMessages = true;
		try {
			const result = await this.api.getMessages(
				this.auth.idToken,
				this.channels.selectedChannelId,
				{
					before: oldest.createdAt,
					limit: 100
				}
			);
			this.messages = [...result.messages, ...this.messages];
			this.hasMoreMessages = result.hasMore;
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingOlderMessages = false;
		}
	}

	/* ═══════════════════ Send Message ═══════════════════ */

	async sendMessage(): Promise<void> {
		if (!this.auth.idToken || !this.channels.selectedChannelId) return;

		const body = this.resolveMentions(this.messageBody.trim());
		const imageFile = this.pendingImage;
		const file = this.pendingFile;
		const replyToMessageId =
			this.replyingTo?.context === 'channel' ? this.replyingTo.messageId : null;

		if (!body && !imageFile && !file) {
			this.ui.error = 'Message body, image, or file is required.';
			return;
		}

		this.isSending = true;
		try {
			let imageUrl: string | null = null;
			if (imageFile) {
				const result = await this.api.uploadImage(this.auth.idToken, imageFile);
				imageUrl = result.imageUrl;
			}

			let fileFields: {
				fileUrl: string;
				fileName: string;
				fileSize: number;
				fileContentType: string;
			} | null = null;
			if (file) {
				fileFields = await this.api.uploadFile(this.auth.idToken, file);
			}

			await this.api.sendMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				body,
				imageUrl,
				replyToMessageId,
				fileFields
			);
			this.messageBody = '';
			this.pendingMentions = new Map();
			this.clearPendingImage();
			this.clearPendingFile();
			this.replyingTo = null;

			if (this.auth.me) {
				this.hub.clearTyping(
					this.channels.selectedChannelId,
					this.auth.effectiveDisplayName
				);
			}

			// If SignalR is not connected, fall back to full reload.
			if (!this.hub.isConnected) {
				await this.loadMessages(this.channels.selectedChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSending = false;
		}
	}

	resolveMentions(text: string): string {
		let result = text;
		// Convert @here keyword to wire token
		result = result.replaceAll('@here', '<@here>');
		for (const [displayName, userId] of this.pendingMentions) {
			result = result.replaceAll(`@${displayName}`, `<@${userId}>`);
		}
		return result;
	}

	/* ═══════════════════ Composer ═══════════════════ */

	handleComposerInput(): void {
		if (!this.channels.selectedChannelId || !this.auth.me) return;
		this.hub.emitTyping(this.channels.selectedChannelId, this.auth.effectiveDisplayName);
	}

	/* ═══════════════════ GIF Messages ═══════════════════ */

	/** Send a GIF as a message using the GIPHY URL as the image attachment. */
	async sendGifMessage(gifUrl: string): Promise<void> {
		if (!this.auth.idToken || !this.channels.selectedChannelId) return;

		const replyToMessageId =
			this.replyingTo?.context === 'channel' ? this.replyingTo.messageId : null;

		this.isSending = true;
		try {
			await this.api.sendMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				'',
				gifUrl,
				replyToMessageId,
				null
			);
			this.replyingTo = null;

			if (this.auth.me) {
				this.hub.clearTyping(
					this.channels.selectedChannelId,
					this.auth.effectiveDisplayName
				);
			}

			if (!this.hub.isConnected) {
				await this.loadMessages(this.channels.selectedChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSending = false;
		}
	}

	/* ═══════════════════ Image Attachments ═══════════════════ */

	attachImage(file: File): void {
		const error = validateImage(file);
		if (error) { this.ui.error = error; return; }
		this.pendingImage = file;
		this.pendingImagePreview = URL.createObjectURL(file);
	}

	clearPendingImage(): void {
		if (this.pendingImagePreview) {
			URL.revokeObjectURL(this.pendingImagePreview);
		}
		this.pendingImage = null;
		this.pendingImagePreview = null;
	}

	/* ═══════════════════ File Attachments ═══════════════════ */

	attachFile(file: File): void {
		const error = validateFile(file);
		if (error) { this.ui.error = error; return; }
		this.pendingFile = file;
	}

	clearPendingFile(): void {
		this.pendingFile = null;
	}

	/* ═══════════════════ Reply ═══════════════════ */

	startReply(messageId: string, authorName: string, bodyPreview: string): void {
		this.replyingTo = { messageId, authorName, bodyPreview, context: 'channel' };
	}

	cancelReply(): void {
		this.replyingTo = null;
	}

	/* ═══════════════════ Edit / Delete ═══════════════════ */

	async editMessage(messageId: string, newBody: string): Promise<void> {
		if (!this.auth.idToken || !this.channels.selectedChannelId) return;
		try {
			await this.api.editMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				messageId,
				newBody
			);
			// Real-time update arrives via SignalR; fall back to local update if disconnected.
			if (!this.hub.isConnected) {
				this.messages = this.messages.map((m) =>
					m.id === messageId
						? { ...m, body: newBody, editedAt: new Date().toISOString() }
						: m
				);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async deleteMessage(messageId: string): Promise<void> {
		if (!this.auth.idToken || !this.channels.selectedChannelId) return;
		try {
			await this.api.deleteMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				messageId
			);
			// Real-time update arrives via SignalR; fall back to local removal if disconnected.
			if (!this.hub.isConnected) {
				this.messages = this.messages.filter((m) => m.id !== messageId);
				this.pinnedMessages = this.pinnedMessages.filter(
					(p) => p.messageId !== messageId
				);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ Reactions ═══════════════════ */

	private _updateMessageReactions(
		messageId: string,
		reactions: Message['reactions']
	): void {
		this.messages = this.messages.map((message) =>
			message.id === messageId ? { ...message, reactions } : message
		);
	}

	async toggleReaction(messageId: string, emoji: string): Promise<void> {
		if (!this.auth.idToken || !this.channels.selectedChannelId) return;
		const normalizedEmoji = emoji.trim();
		if (!normalizedEmoji) return;
		const reactionKey = UIStore.reactionToggleKey(messageId, normalizedEmoji);
		if (this.ui.pendingReactionKeys.has(reactionKey)) return;
		this.ui.setReactionPending(reactionKey, true);
		try {
			const result = await this.api.toggleReaction(
				this.auth.idToken,
				this.channels.selectedChannelId,
				messageId,
				normalizedEmoji
			);
			this._updateMessageReactions(messageId, result.reactions);
			rememberReactionUpdate(this.ui, messageId, result.reactions);
			// Real-time update arrives via SignalR; fall back to reload if disconnected.
			if (!this.hub.isConnected) {
				await this.loadMessages(this.channels.selectedChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.ui.setReactionPending(reactionKey, false);
		}
	}

	/* ═══════════════════ Pinned Messages ═══════════════════ */

	async loadPinnedMessages(channelId?: string): Promise<void> {
		const cid = channelId ?? this.channels.selectedChannelId;
		if (!cid || !this.auth.idToken) return;
		try {
			this.pinnedMessages = await this.api.getPinnedMessages(this.auth.idToken, cid);
		} catch {
			this.pinnedMessages = [];
		}
	}

	async pinMessage(messageId: string): Promise<void> {
		if (!this.channels.selectedChannelId || !this.auth.idToken) return;
		try {
			await this.api.pinMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				messageId
			);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async unpinMessage(messageId: string): Promise<void> {
		if (!this.channels.selectedChannelId || !this.auth.idToken) return;
		try {
			await this.api.unpinMessage(
				this.auth.idToken,
				this.channels.selectedChannelId,
				messageId
			);
			this.pinnedMessages = this.pinnedMessages.filter(
				(p) => p.messageId !== messageId
			);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	togglePinnedPanel(): void {
		this.showPinnedPanel = !this.showPinnedPanel;
		if (this.showPinnedPanel) {
			this.loadPinnedMessages();
		}
	}

	/* ═══════════════════ Purge ═══════════════════ */

	async purgeChannel(channelId: string): Promise<void> {
		if (!this.auth.idToken) return;
		this.isPurgingChannel = true;
		try {
			await this.api.purgeChannel(this.auth.idToken, channelId);
			if (!this.hub.isConnected && channelId === this.channels.selectedChannelId) {
				this.messages = [];
				this.hasMoreMessages = false;
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isPurgingChannel = false;
		}
	}

	/* ═══════════════════ Search ═══════════════════ */

	toggleSearch(): void {
		this.isSearchOpen = !this.isSearchOpen;
		if (!this.isSearchOpen) {
			this.searchQuery = '';
			this.searchFilters = {};
			this.searchResults = null;
		}
	}

	async searchMessages(
		query: string,
		filters: SearchFilters = {}
	): Promise<void> {
		if (!this.auth.idToken || query.trim().length < 2) {
			this.searchResults = null;
			return;
		}

		this.searchQuery = query;
		this.searchFilters = filters;
		this.isSearching = true;

		try {
			const selectedServerId = this.getSelectedServerId?.() ?? null;
			const activeDmChannelId = this.getActiveDmChannelId?.() ?? null;

			if (selectedServerId) {
				this.searchResults = await this.api.searchServerMessages(
					this.auth.idToken,
					selectedServerId,
					query,
					filters
				);
			} else if (activeDmChannelId) {
				this.searchResults = await this.api.searchDmMessages(
					this.auth.idToken,
					query,
					filters
				);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSearching = false;
		}
	}

	async searchPage(page: number): Promise<void> {
		await this.searchMessages(this.searchQuery, { ...this.searchFilters, page });
	}

	async jumpToMessage(
		messageId: string,
		channelId: string,
		isDm: boolean
	): Promise<void> {
		if (!this.auth.idToken) return;

		try {
			// Switch to the correct view if needed
			if (isDm) {
				if (this.getActiveDmChannelId?.() !== channelId) {
					await this.onSelectDmConversation?.(channelId);
				}
			} else {
				if (this.channels.selectedChannelId !== channelId) {
					await this.onSelectChannel?.(channelId);
				}
			}

			// Fetch the around-window for the target message
			const result = isDm
				? await this.api.getDmMessagesAround(this.auth.idToken, channelId, messageId)
				: await this.api.getMessagesAround(this.auth.idToken, channelId, messageId);

			// Replace messages with the around-window
			if (isDm) {
				this.onSetDmMessages?.(result.messages as unknown as DirectMessage[]);
			} else {
				this.messages = result.messages;
				this.hasMoreMessages = result.hasMoreBefore;
			}

			// Highlight the target message
			this.highlightedMessageId = messageId;
			setTimeout(() => {
				this.highlightedMessageId = null;
			}, 2000);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	handleIncomingMessage(msg: Message): void {
		if (msg.channelId === this.channels.selectedChannelId) {
			if (!this.messages.some((m) => m.id === msg.id)) {
				this.messages = [
					...this.messages,
					{
						...msg,
						linkPreviews: msg.linkPreviews ?? [],
						mentions: msg.mentions ?? [],
						replyContext: msg.replyContext ?? null
					}
				];
			}
		}
	}

	handleTyping(channelId: string, displayName: string): void {
		if (
			channelId === this.channels.selectedChannelId &&
			!this.typingUsers.includes(displayName)
		) {
			this.typingUsers = [...this.typingUsers, displayName];
		}
	}

	handleStoppedTyping(channelId: string, displayName: string): void {
		if (channelId === this.channels.selectedChannelId) {
			this.typingUsers = this.typingUsers.filter((u) => u !== displayName);
		}
	}

	handleReactionUpdate(update: ReactionUpdate): void {
		if (matchAndRemoveReactionSnapshot(this.ui, update.messageId, update.reactions)) {
			return;
		}
		if (update.channelId === this.channels.selectedChannelId) {
			this._updateMessageReactions(update.messageId, update.reactions);
		}
	}

	handleMessageDeleted(event: MessageDeletedEvent): void {
		if (event.channelId === this.channels.selectedChannelId) {
			this.messages = this.messages.filter((m) => m.id !== event.messageId);
			this.pinnedMessages = this.pinnedMessages.filter(
				(p) => p.messageId !== event.messageId
			);
		}
	}

	handleChannelPurged(event: ChannelPurgedEvent): void {
		if (event.channelId === this.channels.selectedChannelId) {
			this.messages = [];
			this.hasMoreMessages = false;
			this.pinnedMessages = [];
		}
	}

	handleMessageEdited(event: MessageEditedEvent): void {
		if (event.channelId === this.channels.selectedChannelId) {
			this.messages = this.messages.map((m) =>
				m.id === event.messageId
					? { ...m, body: event.body, editedAt: event.editedAt }
					: m
			);
		}
	}

	handleMessagePinned(event: MessagePinnedEvent): void {
		if (event.channelId === this.channels.selectedChannelId) {
			if (this.showPinnedPanel) {
				this.loadPinnedMessages();
			} else {
				const msg = this.messages.find((m) => m.id === event.messageId);
				if (msg) {
					this.pinnedMessages = [
						{
							messageId: event.messageId,
							channelId: event.channelId,
							pinnedBy: event.pinnedBy,
							pinnedAt: event.pinnedAt,
							message: msg
						},
						...this.pinnedMessages
					];
				} else {
					this.loadPinnedMessages();
				}
			}
		}
	}

	handleMessageUnpinned(event: MessageUnpinnedEvent): void {
		if (event.channelId === this.channels.selectedChannelId) {
			this.pinnedMessages = this.pinnedMessages.filter(
				(p) => p.messageId !== event.messageId
			);
		}
	}

	patchLinkPreviews(messageId: string, previews: LinkPreview[]): void {
		this.messages = this.messages.map((m) =>
			m.id === messageId ? { ...m, linkPreviews: previews } : m
		);
	}

	/* ═══════════════════ Reset ═══════════════════ */

	reset(): void {
		this.messages = [];
		this.isPurgingChannel = false;
		this.typingUsers = [];
		this.pendingImage = null;
		this.pendingImagePreview = null;
		this.pendingFile = null;
		this.messageBody = '';
		this.pendingMentions = new Map();
		this.replyingTo = null;
		this.isSearchOpen = false;
		this.searchQuery = '';
		this.searchFilters = {};
		this.searchResults = null;
		this.isSearching = false;
		this.highlightedMessageId = null;
		this.pinnedMessages = [];
		this.showPinnedPanel = false;
		this.isSending = false;
		this.isLoadingMessages = false;
		this.isLoadingOlderMessages = false;
		this.hasMoreMessages = false;
	}
}
