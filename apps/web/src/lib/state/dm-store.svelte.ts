// apps/web/src/lib/state/dm-store.svelte.ts
import { getContext, setContext } from 'svelte';
import type {
	DmConversation,
	DirectMessage,
	PresenceStatus,
	LinkPreview
} from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type {
	DmReactionUpdate,
	DmMessageDeletedEvent,
	DmMessageEditedEvent
} from '$lib/services/chat-hub.js';
import type { AuthStore } from './auth-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';
import type { UIStore as UIStoreType } from './ui-store.svelte.js';
import { validateImage, validateFile } from '$lib/utils/attachments.js';
import { rememberReactionUpdate, matchAndRemoveReactionSnapshot } from '$lib/utils/reactions.js';

const DM_KEY = Symbol('dm-store');

export function createDmStore(
	auth: AuthStore,
	api: ApiClient,
	ui: UIStoreType,
	hub: ChatHubService
): DmStore {
	const store = new DmStore(auth, api, ui, hub);
	setContext(DM_KEY, store);
	return store;
}

export function getDmStore(): DmStore {
	return getContext<DmStore>(DM_KEY);
}

export class DmStore {
	/* ═══════════════════ $state fields ═══════════════════ */

	dmConversations = $state<DmConversation[]>([]);
	dmMessages = $state<DirectMessage[]>([]);
	activeDmChannelId = $state<string | null>(null);
	dmTypingUsers = $state<string[]>([]);
	unreadDmCounts = $state<Map<string, number>>(new Map());
	dmMessageBody = $state('');
	isLoadingDmConversations = $state(false);
	isLoadingDmMessages = $state(false);
	isSendingDm = $state(false);
	pendingDmImage = $state<File | null>(null);
	pendingDmImagePreview = $state<string | null>(null);
	pendingDmFile = $state<File | null>(null);
	replyingTo = $state<{
		messageId: string;
		authorName: string;
		bodyPreview: string;
		context: 'dm';
	} | null>(null);

	/** Incremented on each loadDmMessages call to discard stale responses after fast DM switches. */
	private loadGeneration = 0;

	/* ═══════════════════ $derived ═══════════════════ */

	activeDmParticipant = $derived(
		this.dmConversations.find((c) => c.id === this.activeDmChannelId)?.participant ?? null
	);

	/* ═══════════════════ Constructor ═══════════════════ */

	constructor(
		private readonly auth: AuthStore,
		private readonly api: ApiClient,
		private readonly ui: UIStoreType,
		private readonly hub: ChatHubService
	) {}

	/* ═══════════════════ DM Conversations ═══════════════════ */

	async loadDmConversations(): Promise<void> {
		if (!this.auth.idToken) return;
		this.isLoadingDmConversations = true;
		try {
			this.dmConversations = await this.api.getDmConversations(this.auth.idToken);
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isLoadingDmConversations = false;
		}
		this.loadDmPresence();
	}

	async loadDmPresence(): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			const entries = await this.api.getDmPresence(this.auth.idToken);
			for (const entry of entries) {
				this.ui.userPresence.set(entry.userId, entry.status);
			}
		} catch (e) {
			console.warn('Failed to load DM presence:', e);
		}
	}

	async selectDmConversation(dmChannelId: string): Promise<void> {
		const previousDmId = this.activeDmChannelId;
		this.activeDmChannelId = dmChannelId;
		this.dmTypingUsers = [];
		this.dmMessageBody = '';
		this.replyingTo = null;
		this.ui.mobileNavOpen = false;

		// Clear unread for this conversation.
		if (this.unreadDmCounts.has(dmChannelId)) {
			const next = new Map(this.unreadDmCounts);
			next.delete(dmChannelId);
			this.unreadDmCounts = next;
		}

		if (previousDmId) await this.hub.leaveDmChannel(previousDmId);
		await this.hub.joinDmChannel(dmChannelId);

		try {
			await this.loadDmMessages(dmChannelId);
		} catch {
			// On failure, clear messages so stale data from the old conversation isn't shown.
			if (this.activeDmChannelId === dmChannelId) {
				this.dmMessages = [];
			}
		}
	}

	async loadDmMessages(dmChannelId: string): Promise<void> {
		if (!this.auth.idToken) return;
		const gen = ++this.loadGeneration;
		this.isLoadingDmMessages = true;
		try {
			const result = await this.api.getDmMessages(this.auth.idToken, dmChannelId);
			// Discard stale response if user switched conversations during the fetch.
			if (gen !== this.loadGeneration) return;
			this.dmMessages = result.messages;
		} catch (e) {
			if (gen === this.loadGeneration) this.ui.setError(e);
			throw e; // Re-throw so selectDmConversation can handle failure
		} finally {
			if (gen === this.loadGeneration) this.isLoadingDmMessages = false;
		}
	}

	async sendDmMessage(): Promise<void> {
		if (!this.auth.idToken || !this.activeDmChannelId) return;

		const body = this.dmMessageBody.trim();
		const imageFile = this.pendingDmImage;
		const file = this.pendingDmFile;
		const replyToDirectMessageId =
			this.replyingTo?.context === 'dm' ? this.replyingTo.messageId : null;

		if (!body && !imageFile && !file) {
			this.ui.error = 'Message body, image, or file is required.';
			return;
		}

		this.isSendingDm = true;
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

			await this.api.sendDm(
				this.auth.idToken,
				this.activeDmChannelId,
				body,
				imageUrl,
				replyToDirectMessageId,
				fileFields
			);
			this.dmMessageBody = '';
			this.clearPendingDmImage();
			this.clearPendingDmFile();
			this.replyingTo = null;

			if (this.auth.me) {
				this.hub.clearDmTyping(this.activeDmChannelId, this.auth.effectiveDisplayName);
			}

			// If SignalR is not connected, fall back to full reload.
			if (!this.hub.isConnected) {
				await this.loadDmMessages(this.activeDmChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSendingDm = false;
		}
	}

	/** Send a GIF as a DM using the GIPHY URL as the image attachment. */
	async sendDmGifMessage(gifUrl: string): Promise<void> {
		if (!this.auth.idToken || !this.activeDmChannelId) return;

		const replyToDirectMessageId =
			this.replyingTo?.context === 'dm' ? this.replyingTo.messageId : null;

		this.isSendingDm = true;
		try {
			await this.api.sendDm(
				this.auth.idToken,
				this.activeDmChannelId,
				'',
				gifUrl,
				replyToDirectMessageId,
				null
			);
			this.replyingTo = null;

			if (this.auth.me) {
				this.hub.clearDmTyping(this.activeDmChannelId, this.auth.effectiveDisplayName);
			}

			if (!this.hub.isConnected) {
				await this.loadDmMessages(this.activeDmChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.isSendingDm = false;
		}
	}

	/** Open or create a DM conversation with a friend. */
	async openDmWithUser(userId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			const result = await this.api.createOrResumeDm(this.auth.idToken, userId);
			await this.loadDmConversations();
			await this.selectDmConversation(result.id);
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async closeDmConversation(dmChannelId: string): Promise<void> {
		if (!this.auth.idToken) return;
		try {
			await this.api.closeDmConversation(this.auth.idToken, dmChannelId);
			this.dmConversations = this.dmConversations.filter((c) => c.id !== dmChannelId);
			if (this.activeDmChannelId === dmChannelId) {
				await this.hub.leaveDmChannel(dmChannelId);
				this.activeDmChannelId = null;
				this.dmMessages = [];
				this.dmTypingUsers = [];
				this.dmMessageBody = '';
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	handleDmComposerInput(): void {
		if (!this.activeDmChannelId || !this.auth.me) return;
		this.hub.emitDmTyping(this.activeDmChannelId, this.auth.effectiveDisplayName);
	}

	/* ═══════════════════ DM Message Actions ═══════════════════ */

	/** Delete a DM message owned by the current user. */
	async deleteDmMessage(messageId: string): Promise<void> {
		if (!this.auth.idToken || !this.activeDmChannelId) return;
		try {
			await this.api.deleteDmMessage(this.auth.idToken, this.activeDmChannelId, messageId);
			// Real-time update arrives via SignalR; fall back to local removal if disconnected.
			if (!this.hub.isConnected) {
				this.dmMessages = this.dmMessages.filter((m) => m.id !== messageId);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Edit a DM message owned by the current user. */
	async editDmMessage(messageId: string, newBody: string): Promise<void> {
		if (!this.auth.idToken || !this.activeDmChannelId) return;
		try {
			await this.api.editDmMessage(
				this.auth.idToken,
				this.activeDmChannelId,
				messageId,
				newBody
			);
			// Real-time update arrives via SignalR; fall back to local update if disconnected.
			if (!this.hub.isConnected) {
				this.dmMessages = this.dmMessages.map((m) =>
					m.id === messageId
						? { ...m, body: newBody, editedAt: new Date().toISOString() }
						: m
				);
			}
		} catch (e) {
			this.ui.setError(e);
		}
	}

	/** Toggle a reaction on a DM message. */
	async toggleDmReaction(messageId: string, emoji: string): Promise<void> {
		if (!this.auth.idToken || !this.activeDmChannelId) return;
		const normalizedEmoji = emoji.trim();
		if (!normalizedEmoji) return;
		const reactionKey = UIStore.reactionToggleKey(messageId, normalizedEmoji);
		if (this.ui.pendingReactionKeys.has(reactionKey)) return;
		this.ui.setReactionPending(reactionKey, true);
		try {
			const result = await this.api.toggleDmReaction(
				this.auth.idToken,
				this.activeDmChannelId,
				messageId,
				normalizedEmoji
			);
			this._updateDmMessageReactions(messageId, result.reactions);
			rememberReactionUpdate(this.ui, messageId, result.reactions);
			if (!this.hub.isConnected) {
				await this.loadDmMessages(this.activeDmChannelId);
			}
		} catch (e) {
			this.ui.setError(e);
		} finally {
			this.ui.setReactionPending(reactionKey, false);
		}
	}

	/* ═══════════════════ Image Attachments ═══════════════════ */

	/** Attach an image file to the DM message composer. */
	attachDmImage(file: File): void {
		const error = validateImage(file);
		if (error) { this.ui.error = error; return; }
		this.pendingDmImage = file;
		this.pendingDmImagePreview = URL.createObjectURL(file);
	}

	/** Remove the pending image attachment from the DM message composer. */
	clearPendingDmImage(): void {
		if (this.pendingDmImagePreview) {
			URL.revokeObjectURL(this.pendingDmImagePreview);
		}
		this.pendingDmImage = null;
		this.pendingDmImagePreview = null;
	}

	/* ═══════════════════ File Attachments ═══════════════════ */

	/** Attach a non-image file to the DM message composer. */
	attachDmFile(file: File): void {
		const error = validateFile(file);
		if (error) { this.ui.error = error; return; }
		this.pendingDmFile = file;
	}

	/** Remove the pending file attachment from the DM message composer. */
	clearPendingDmFile(): void {
		this.pendingDmFile = null;
	}

	/* ═══════════════════ Replies ═══════════════════ */

	/** Activate the reply composer bar for a given DM message. */
	startReply(messageId: string, authorName: string, bodyPreview: string): void {
		this.replyingTo = { messageId, authorName, bodyPreview, context: 'dm' };
	}

	/** Cancel the active reply. */
	cancelReply(): void {
		this.replyingTo = null;
	}

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	/** Handle an incoming DM from the SignalR hub. */
	handleIncomingDm(msg: DirectMessage, isViewingDms: boolean): void {
		if (isViewingDms && msg.dmChannelId === this.activeDmChannelId) {
			if (!this.dmMessages.some((m) => m.id === msg.id)) {
				this.dmMessages = [
					...this.dmMessages,
					{
						...msg,
						linkPreviews: msg.linkPreviews ?? [],
						replyContext: msg.replyContext ?? null
					}
				];
			}
		} else {
			// Increment unread count for this channel.
			const next = new Map(this.unreadDmCounts);
			next.set(msg.dmChannelId, (next.get(msg.dmChannelId) ?? 0) + 1);
			this.unreadDmCounts = next;
		}
		// Refresh conversation list to update last message preview and order.
		this.loadDmConversations();
	}

	/** Handle a DM typing indicator from SignalR. */
	handleDmTyping(dmChannelId: string, displayName: string): void {
		if (
			dmChannelId === this.activeDmChannelId &&
			!this.dmTypingUsers.includes(displayName)
		) {
			this.dmTypingUsers = [...this.dmTypingUsers, displayName];
		}
	}

	/** Handle a DM stopped-typing indicator from SignalR. */
	handleDmStoppedTyping(dmChannelId: string, displayName: string): void {
		if (dmChannelId === this.activeDmChannelId) {
			this.dmTypingUsers = this.dmTypingUsers.filter((u) => u !== displayName);
		}
	}

	/** Handle a DM message deletion event from SignalR. */
	handleDmMessageDeleted(event: DmMessageDeletedEvent): void {
		if (event.dmChannelId === this.activeDmChannelId) {
			this.dmMessages = this.dmMessages.filter((m) => m.id !== event.messageId);
		}
	}

	/** Handle a DM message edited event from SignalR. */
	handleDmMessageEdited(event: DmMessageEditedEvent): void {
		if (event.dmChannelId === this.activeDmChannelId) {
			this.dmMessages = this.dmMessages.map((m) =>
				m.id === event.messageId
					? { ...m, body: event.body, editedAt: event.editedAt }
					: m
			);
		}
	}

	/** Handle a DM reaction update event from SignalR. */
	handleDmReactionUpdate(update: DmReactionUpdate): void {
		if (matchAndRemoveReactionSnapshot(this.ui, update.messageId, update.reactions)) {
			return;
		}
		if (update.dmChannelId === this.activeDmChannelId) {
			this._updateDmMessageReactions(update.messageId, update.reactions);
		}
	}

	/** Patch link previews into a matching DM message. */
	patchDmLinkPreviews(messageId: string, previews: LinkPreview[]): void {
		this.dmMessages = this.dmMessages.map((m) =>
			m.id === messageId ? { ...m, linkPreviews: previews } : m
		);
	}

	/* ═══════════════════ Reaction Helpers (private) ═══════════════════ */

	private _updateDmMessageReactions(
		messageId: string,
		reactions: DirectMessage['reactions']
	): void {
		this.dmMessages = this.dmMessages.map((message) =>
			message.id === messageId ? { ...message, reactions } : message
		);
	}

	/* ═══════════════════ Reset ═══════════════════ */

	reset(): void {
		this.dmConversations = [];
		this.dmMessages = [];
		this.activeDmChannelId = null;
		this.dmTypingUsers = [];
		this.unreadDmCounts = new Map();
		this.dmMessageBody = '';
		this.isLoadingDmConversations = false;
		this.isLoadingDmMessages = false;
		this.isSendingDm = false;
		this.pendingDmImage = null;
		this.pendingDmImagePreview = null;
		this.pendingDmFile = null;
		this.replyingTo = null;
	}
}
