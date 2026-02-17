import { getContext, setContext } from 'svelte';
import { tick } from 'svelte';
import type {
	MemberServer,
	Channel,
	Message,
	Member,
	UserProfile,
	Friend,
	FriendRequest,
	UserSearchResult,
	DmConversation,
	DirectMessage,
	ServerInvite
} from '$lib/types/index.js';
import { ApiClient, ApiError } from '$lib/api/client.js';
import { ChatHubService } from '$lib/services/chat-hub.js';
import type { ReactionUpdate } from '$lib/services/chat-hub.js';
import {
	persistToken,
	loadStoredToken,
	clearSession as clearStoredSession,
	isTokenExpired,
	isSessionExpired
} from '$lib/auth/session.js';
import { initGoogleIdentity, renderGoogleButton, requestFreshToken } from '$lib/auth/google.js';

const CTX_KEY = Symbol('app-state');

/** Retrieve the `AppState` from the component tree context. */
export function getAppState(): AppState {
	return getContext<AppState>(CTX_KEY);
}

/**
 * Create, install, and return the root `AppState`.
 * Must be called exactly once in a root layout or page component.
 */
export function createAppState(apiBaseUrl: string, googleClientId: string): AppState {
	const state = new AppState(apiBaseUrl, googleClientId);
	setContext(CTX_KEY, state);
	return state;
}

/**
 * Central application state powered by Svelte 5 runes.
 *
 * Owns all domain data, loading flags, and orchestration logic.
 * UI components read from this object via `getAppState()` and call
 * its methods to trigger side-effects.
 */
export class AppState {
	/* ───── domain data ───── */
	servers = $state<MemberServer[]>([]);
	channels = $state<Channel[]>([]);
	messages = $state<Message[]>([]);
	members = $state<Member[]>([]);
	friends = $state<Friend[]>([]);
	incomingRequests = $state<FriendRequest[]>([]);
	outgoingRequests = $state<FriendRequest[]>([]);
	userSearchResults = $state<UserSearchResult[]>([]);

	/* ───── DM data ───── */
	dmConversations = $state<DmConversation[]>([]);
	dmMessages = $state<DirectMessage[]>([]);
	activeDmChannelId = $state<string | null>(null);
	dmTypingUsers = $state<string[]>([]);
	unreadDmCounts = $state<Map<string, number>>(new Map());

	/* ───── mention tracking ───── */
	/** channelId → count of unread mentions */
	channelMentionCounts = $state<Map<string, number>>(new Map());
	/** channelId → serverId mapping for aggregation */
	channelServerMap = $state<Map<string, string>>(new Map());

	/* ───── invite data ───── */
	serverInvites = $state<ServerInvite[]>([]);

	/* ───── selection ───── */
	selectedServerId = $state<string | null>(null);
	selectedChannelId = $state<string | null>(null);

	/* ───── auth ───── */
	idToken = $state<string | null>(null);
	me = $state<UserProfile | null>(null);
	status = $state('Signed out');
	error = $state<string | null>(null);

	/* ───── loading flags ───── */
	isInitialLoading = $state(true);
	isLoadingServers = $state(false);
	isLoadingChannels = $state(false);
	isLoadingMessages = $state(false);
	isLoadingOlderMessages = $state(false);
	hasMoreMessages = $state(false);
	isLoadingMe = $state(false);
	isLoadingMembers = $state(false);
	isSending = $state(false);
	isJoining = $state(false);
	isCreatingServer = $state(false);
	isCreatingChannel = $state(false);
	isUploadingAvatar = $state(false);
	isLoadingFriends = $state(false);
	isSearchingUsers = $state(false);
	isLoadingDmConversations = $state(false);
	isLoadingDmMessages = $state(false);
	isSendingDm = $state(false);
	isLoadingInvites = $state(false);
	isCreatingInvite = $state(false);

	/* ───── UI toggles ───── */
	showCreateServer = $state(false);
	showCreateChannel = $state(false);
	showInvitePanel = $state(false);
	showFriendsPanel = $state(false);
	showAlphaNotification = $state(false);
	friendsTab = $state<'all' | 'pending' | 'add'>('all');
	friendSearchQuery = $state('');
	settingsOpen = $state(false);
	settingsCategory = $state<'profile' | 'account'>('profile');
	serverSettingsOpen = $state(false);
	isUpdatingServerName = $state(false);
	isUpdatingChannelName = $state(false);

	/* ───── mobile navigation ───── */
	mobileNavOpen = $state(false);
	mobileMembersOpen = $state(false);

	/* ───── form fields ───── */
	newServerName = $state('');
	newChannelName = $state('');
	messageBody = $state('');
	dmMessageBody = $state('');
	pendingMentions = $state<Map<string, string>>(new Map());

	/* ───── image attachments ───── */
	pendingImage = $state<File | null>(null);
	pendingImagePreview = $state<string | null>(null);
	pendingDmImage = $state<File | null>(null);
	pendingDmImagePreview = $state<string | null>(null);

	/* ───── image lightbox ───── */
	lightboxImageUrl = $state<string | null>(null);

	/* ───── real-time ───── */
	typingUsers = $state<string[]>([]);
	isHubConnected = $state(false);

	/* ───── reply state ───── */
	replyingTo = $state<{ messageId: string; authorName: string; bodyPreview: string; context: 'channel' | 'dm' } | null>(null);

	/* ───── derived ───── */
	readonly isSignedIn = $derived(Boolean(this.idToken));

	readonly isGlobalAdmin = $derived(Boolean(this.me?.user.isGlobalAdmin));

	readonly currentServerRole = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.role ?? null
	);

	readonly canManageChannels = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly canKickMembers = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly canManageInvites = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly canDeleteServer = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner'
	);

	readonly canDeleteChannel = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly selectedServerName = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.name ?? 'Codec'
	);

	readonly effectiveDisplayName = $derived(
		this.me?.user.effectiveDisplayName ?? this.me?.user.displayName ?? ''
	);

	readonly selectedChannelName = $derived(
		this.channels.find((c) => c.id === this.selectedChannelId)?.name ?? null
	);

	readonly activeDmParticipant = $derived(
		this.dmConversations.find((c) => c.id === this.activeDmChannelId)?.participant ?? null
	);

	readonly homeBadgeCount = $derived(
		this.incomingRequests.length + this.unreadDmCounts.size
	);

	serverMentionCount(serverId: string): number {
		let total = 0;
		for (const [channelId, count] of this.channelMentionCounts) {
			if (this.channelServerMap.get(channelId) === serverId) {
				total += count;
			}
		}
		return total;
	}

	channelMentionCount(channelId: string): number {
		return this.channelMentionCounts.get(channelId) ?? 0;
	}

	/* ───── internals ───── */
	private api: ApiClient;
	private hub: ChatHubService;
	private refreshPromise: Promise<string | null> | null = null;

	constructor(
		private readonly apiBaseUrl: string,
		private readonly googleClientId: string
	) {
		this.api = new ApiClient(apiBaseUrl, () => this.refreshToken());
		this.hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);
	}

	/**
	 * Attempt to silently refresh the Google ID token.
	 * Concurrent calls are deduplicated so only one refresh runs at a time.
	 * Signs the user out if the refresh fails.
	 */
	private async refreshToken(): Promise<string | null> {
		if (this.refreshPromise) return this.refreshPromise;

		this.refreshPromise = (async () => {
			try {
				const freshToken = await requestFreshToken();
				this.idToken = freshToken;
				persistToken(freshToken);
				return freshToken;
			} catch {
				await this.signOut();
				return null;
			} finally {
				this.refreshPromise = null;
			}
		})();

		return this.refreshPromise;
	}

	/* ═══════════════════ Settings ═══════════════════ */

	openSettings(): void {
		this.settingsOpen = true;
		this.settingsCategory = 'profile';
	}

	closeSettings(): void {
		this.settingsOpen = false;
	}

	openServerSettings(): void {
		this.serverSettingsOpen = true;
	}

	closeServerSettings(): void {
		this.serverSettingsOpen = false;
	}

	/** Dismiss the alpha notification banner. */
	dismissAlphaNotification(): void {
		this.showAlphaNotification = false;
	}

	/* ═══════════════════ Auth ═══════════════════ */

	/** Bootstrap auth: restore session or show sign-in UI. */
	init(): void {
		if (!isSessionExpired()) {
			const stored = loadStoredToken();
			if (stored && !isTokenExpired(stored)) {
				this.handleCredential(stored);
				return;
			}
		}
		clearStoredSession();
		this.isInitialLoading = false;
		this.renderSignIn();
	}

	private renderSignIn(): void {
		initGoogleIdentity(this.googleClientId, (token) => this.handleCredential(token), {
			renderButtonId: 'google-button',
			autoSelect: true
		});
	}

	async handleCredential(token: string): Promise<void> {
		this.idToken = token;
		this.status = 'Signed in';
		this.isInitialLoading = true;
		persistToken(token);
		await Promise.all([
			this.loadMe(),
			this.loadServers(),
			this.startSignalR()
		]);
		this.isInitialLoading = false;
		this.showAlphaNotification = true;
	}

	async signOut(): Promise<void> {
		await this.hub.stop();
		clearStoredSession();

		this.isInitialLoading = true;
		this.isHubConnected = false;
		this.idToken = null;
		this.me = null;
		this.status = 'Signed out';
		this.servers = [];
		this.channels = [];
		this.messages = [];
		this.hasMoreMessages = false;
		this.members = [];
		this.friends = [];
		this.incomingRequests = [];
		this.outgoingRequests = [];
		this.userSearchResults = [];
		this.dmConversations = [];
		this.dmMessages = [];
		this.activeDmChannelId = null;
		this.dmTypingUsers = [];
		this.dmMessageBody = '';
		this.unreadDmCounts = new Map();
		this.channelMentionCounts = new Map();
		this.channelServerMap = new Map();
		this.selectedServerId = null;
		this.selectedChannelId = null;
		this.serverInvites = [];
		this.showInvitePanel = false;
		this.typingUsers = [];
		this.error = null;
		this.showFriendsPanel = false;
		this.friendsTab = 'all';
		this.friendSearchQuery = '';
		this.settingsOpen = false;
		this.serverSettingsOpen = false;
		this.replyingTo = null;
		this.lightboxImageUrl = null;

		await tick();
		renderGoogleButton('google-button');
	}

	/* ═══════════════════ Data loading ═══════════════════ */

	async loadMe(): Promise<void> {
		if (!this.idToken) return;
		this.error = null;
		this.isLoadingMe = true;
		try {
			this.me = await this.api.getMe(this.idToken);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingMe = false;
		}
	}

	async loadServers(): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingServers = true;
		try {
			this.servers = await this.api.getServers(this.idToken);
			this.selectedServerId = this.servers[0]?.serverId ?? null;
			if (this.selectedServerId) {
				await this.loadChannels(this.selectedServerId);
				await this.loadMembers(this.selectedServerId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingServers = false;
		}
	}

	async loadChannels(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingChannels = true;
		try {
			this.channels = await this.api.getChannels(this.idToken, serverId);

			// Keep channel→server mapping up to date for mention badge aggregation
			const mapNext = new Map(this.channelServerMap);
			for (const ch of this.channels) {
				mapNext.set(ch.id, serverId);
			}
			this.channelServerMap = mapNext;

			const previousChannelId = this.selectedChannelId;
			this.selectedChannelId = this.channels[0]?.id ?? null;

			// Clear mention badge for the auto-selected channel
			if (this.selectedChannelId) {
				const mentionNext = new Map(this.channelMentionCounts);
				mentionNext.delete(this.selectedChannelId);
				this.channelMentionCounts = mentionNext;
			}

			if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
			if (this.selectedChannelId) await this.hub.joinChannel(this.selectedChannelId);

			if (this.selectedChannelId) {
				await this.loadMessages(this.selectedChannelId);
			} else {
				this.messages = [];
				this.hasMoreMessages = false;
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingChannels = false;
		}
	}

	async loadMessages(channelId: string): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingMessages = true;
		try {
			const result = await this.api.getMessages(this.idToken, channelId, { limit: 100 });
			this.messages = result.messages;
			this.hasMoreMessages = result.hasMore;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingMessages = false;
		}
	}

	/** Load older messages before the earliest currently loaded message. */
	async loadOlderMessages(): Promise<void> {
		if (!this.idToken || !this.selectedChannelId || !this.hasMoreMessages || this.isLoadingOlderMessages) return;
		const oldest = this.messages[0];
		if (!oldest) return;

		this.isLoadingOlderMessages = true;
		try {
			const result = await this.api.getMessages(this.idToken, this.selectedChannelId, {
				before: oldest.createdAt,
				limit: 100
			});
			this.messages = [...result.messages, ...this.messages];
			this.hasMoreMessages = result.hasMore;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingOlderMessages = false;
		}
	}

	async loadMembers(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingMembers = true;
		try {
			this.members = await this.api.getMembers(this.idToken, serverId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingMembers = false;
		}
	}

	/* ═══════════════════ Actions ═══════════════════ */

	async selectChannel(channelId: string): Promise<void> {
		const previousChannelId = this.selectedChannelId;
		this.selectedChannelId = channelId;
		this.typingUsers = [];
		this.pendingMentions = new Map();
		this.replyingTo = null;
		this.mobileNavOpen = false;

		// Clear mention badge for this channel
		const next = new Map(this.channelMentionCounts);
		next.delete(channelId);
		this.channelMentionCounts = next;

		if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
		await this.hub.joinChannel(channelId);
		await this.loadMessages(channelId);
	}

	async sendMessage(): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;

		const body = this.resolveMentions(this.messageBody.trim());
		const imageFile = this.pendingImage;
		const replyToMessageId = this.replyingTo?.context === 'channel' ? this.replyingTo.messageId : null;

		if (!body && !imageFile) {
			this.error = 'Message body or image is required.';
			return;
		}

		this.isSending = true;
		try {
			let imageUrl: string | null = null;
			if (imageFile) {
				const result = await this.api.uploadImage(this.idToken, imageFile);
				imageUrl = result.imageUrl;
			}

			await this.api.sendMessage(this.idToken, this.selectedChannelId, body, imageUrl, replyToMessageId);
			this.messageBody = '';
			this.pendingMentions = new Map();
			this.clearPendingImage();
			this.replyingTo = null;

			if (this.me) {
				this.hub.clearTyping(this.selectedChannelId, this.effectiveDisplayName);
			}

			// If SignalR is not connected, fall back to full reload.
			if (!this.hub.isConnected) {
				await this.loadMessages(this.selectedChannelId);
			}
		} catch (e) {
			this.setError(e);
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

	async createServer(): Promise<void> {
		const name = this.newServerName.trim();
		if (!name) {
			this.error = 'Server name is required.';
			return;
		}
		if (!this.idToken) return;

		this.isCreatingServer = true;
		try {
			const created = await this.api.createServer(this.idToken, name);
			this.newServerName = '';
			this.showCreateServer = false;
			await this.loadServers();
			await this.selectServer(created.id);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isCreatingServer = false;
		}
	}

	async createChannel(): Promise<void> {
		const name = this.newChannelName.trim();
		if (!name) {
			this.error = 'Channel name is required.';
			return;
		}
		if (!this.idToken || !this.selectedServerId) return;

		this.isCreatingChannel = true;
		try {
			const created = await this.api.createChannel(this.idToken, this.selectedServerId, name);
			this.newChannelName = '';
			this.showCreateChannel = false;
			await this.loadChannels(this.selectedServerId);
			await this.selectChannel(created.id);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isCreatingChannel = false;
		}
	}

	async updateServerName(name: string): Promise<void> {
		if (!name.trim()) {
			this.error = 'Server name is required.';
			return;
		}
		if (!this.idToken || !this.selectedServerId) return;

		this.isUpdatingServerName = true;
		try {
			await this.api.updateServer(this.idToken, this.selectedServerId, name.trim());
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUpdatingServerName = false;
		}
	}

	async updateChannelName(channelId: string, name: string): Promise<void> {
		if (!name.trim()) {
			this.error = 'Channel name is required.';
			return;
		}
		if (!this.idToken || !this.selectedServerId) return;

		this.isUpdatingChannelName = true;
		try {
			await this.api.updateChannel(this.idToken, this.selectedServerId, channelId, name.trim());
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUpdatingChannelName = false;
		}
	}

	/** Delete a server. Requires Owner role or global admin privileges. */
	async deleteServer(serverId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.deleteServer(this.idToken, serverId);
			this.servers = this.servers.filter((s) => s.serverId !== serverId);
			if (this.selectedServerId === serverId) {
				this.goHome();
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Delete a channel from the current server. Requires Owner/Admin role or global admin privileges. */
	async deleteChannel(channelId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteChannel(this.idToken, this.selectedServerId, channelId);
			this.channels = this.channels.filter((c) => c.id !== channelId);
			if (this.selectedChannelId === channelId) {
				const firstChannel = this.channels[0];
				if (firstChannel) {
					this.selectedChannelId = firstChannel.id;
					await this.loadMessages(firstChannel.id);
				} else {
					this.selectedChannelId = null;
					this.messages = [];
				}
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/* ═══════════════════ Server Moderation ═══════════════════ */

	/** Kick a member from the currently selected server. */
	async kickMember(userId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.kickMember(this.idToken, this.selectedServerId, userId);
			await this.loadMembers(this.selectedServerId);
		} catch (e) {
			this.setError(e);
		}
	}

	/* ═══════════════════ Server Invites ═══════════════════ */

	/** Load active invites for the currently selected server. */
	async loadInvites(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isLoadingInvites = true;
		try {
			this.serverInvites = await this.api.getInvites(this.idToken, this.selectedServerId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingInvites = false;
		}
	}

	/** Create a new invite for the currently selected server. */
	async createInvite(options?: { maxUses?: number | null; expiresInHours?: number | null }): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isCreatingInvite = true;
		try {
			const invite = await this.api.createInvite(this.idToken, this.selectedServerId, options);
			this.serverInvites = [invite, ...this.serverInvites];
		} catch (e) {
			this.setError(e);
		} finally {
			this.isCreatingInvite = false;
		}
	}

	/** Revoke an invite from the currently selected server. */
	async revokeInvite(inviteId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.revokeInvite(this.idToken, this.selectedServerId, inviteId);
			this.serverInvites = this.serverInvites.filter((i) => i.id !== inviteId);
		} catch (e) {
			this.setError(e);
		}
	}

	/** Join a server via invite code. */
	async joinViaInvite(code: string): Promise<void> {
		if (!this.idToken) return;
		this.isJoining = true;
		try {
			const result = await this.api.joinViaInvite(this.idToken, code);
			await this.hub.joinServer(result.serverId);
			await this.loadServers();
			await this.selectServer(result.serverId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isJoining = false;
		}
	}

	/* ═══════════════════ Avatar ═══════════════════ */

	/** Upload a custom global avatar and refresh the local profile. */
	async uploadAvatar(file: File): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.error = null;
		try {
			const { avatarUrl } = await this.api.uploadAvatar(this.idToken, file);
			if (this.me) {
				this.me = {
					...this.me,
					user: { ...this.me.user, avatarUrl }
				};
			}
			// Refresh member list so the sidebar picks up the new avatar.
			if (this.selectedServerId) {
				await this.loadMembers(this.selectedServerId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	/** Remove the custom global avatar and revert to the Google profile picture. */
	async deleteAvatar(): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.error = null;
		try {
			const { avatarUrl } = await this.api.deleteAvatar(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: { ...this.me.user, avatarUrl }
				};
			}
			if (this.selectedServerId) {
				await this.loadMembers(this.selectedServerId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	/* ═══════════════════ Nickname ═══════════════════ */

	/** Set or update the current user's nickname. */
	async setNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		this.error = null;
		try {
			const result = await this.api.setNickname(this.idToken, nickname);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						nickname: result.nickname,
						effectiveDisplayName: result.effectiveDisplayName
					}
				};
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Remove the current user's nickname, reverting to the Google display name. */
	async removeNickname(): Promise<void> {
		if (!this.idToken) return;
		this.error = null;
		try {
			const result = await this.api.removeNickname(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						nickname: result.nickname,
						effectiveDisplayName: result.effectiveDisplayName
					}
				};
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Upload a server-specific avatar for the current user. */
	async uploadServerAvatar(serverId: string, file: File): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.error = null;
		try {
			await this.api.uploadServerAvatar(this.idToken, serverId, file);
			await this.loadMembers(serverId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	/** Remove the server-specific avatar for the current user. */
	async deleteServerAvatar(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isUploadingAvatar = true;
		this.error = null;
		try {
			await this.api.deleteServerAvatar(this.idToken, serverId);
			await this.loadMembers(serverId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingAvatar = false;
		}
	}

	handleComposerInput(): void {
		if (!this.selectedChannelId || !this.me) return;
		this.hub.emitTyping(this.selectedChannelId, this.effectiveDisplayName);
	}

	/* ═══════════════════ Image Attachments ═══════════════════ */

	private static readonly ALLOWED_IMAGE_TYPES = new Set([
		'image/jpeg',
		'image/png',
		'image/webp',
		'image/gif'
	]);

	/** Attach an image file to the channel message composer. */
	attachImage(file: File): void {
		if (!AppState.ALLOWED_IMAGE_TYPES.has(file.type)) {
			this.error = 'Unsupported image type. Allowed: JPG, PNG, WebP, GIF.';
			return;
		}
		if (file.size > 10 * 1024 * 1024) {
			this.error = 'Image must be under 10 MB.';
			return;
		}
		this.pendingImage = file;
		this.pendingImagePreview = URL.createObjectURL(file);
	}

	/** Remove the pending image attachment from the channel message composer. */
	clearPendingImage(): void {
		if (this.pendingImagePreview) {
			URL.revokeObjectURL(this.pendingImagePreview);
		}
		this.pendingImage = null;
		this.pendingImagePreview = null;
	}

	/** Attach an image file to the DM message composer. */
	attachDmImage(file: File): void {
		if (!AppState.ALLOWED_IMAGE_TYPES.has(file.type)) {
			this.error = 'Unsupported image type. Allowed: JPG, PNG, WebP, GIF.';
			return;
		}
		if (file.size > 10 * 1024 * 1024) {
			this.error = 'Image must be under 10 MB.';
			return;
		}
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

	/* ═══════════════════ Image Lightbox ═══════════════════ */

	/** Open the full-screen image lightbox for a given URL. */
	openImagePreview(url: string): void {
		this.lightboxImageUrl = url;
	}

	/** Close the image lightbox. */
	closeImagePreview(): void {
		this.lightboxImageUrl = null;
	}

	async toggleReaction(messageId: string, emoji: string): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;
		try {
			await this.api.toggleReaction(this.idToken, this.selectedChannelId, messageId, emoji);
			// Real-time update arrives via SignalR; fall back to reload if disconnected.
			if (!this.hub.isConnected) {
				await this.loadMessages(this.selectedChannelId);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Delete a channel message owned by the current user. */
	async deleteMessage(messageId: string): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;
		try {
			await this.api.deleteMessage(this.idToken, this.selectedChannelId, messageId);
			// Real-time update arrives via SignalR; fall back to local removal if disconnected.
			if (!this.hub.isConnected) {
				this.messages = this.messages.filter((m) => m.id !== messageId);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Delete a DM message owned by the current user. */
	async deleteDmMessage(messageId: string): Promise<void> {
		if (!this.idToken || !this.activeDmChannelId) return;
		try {
			await this.api.deleteDmMessage(this.idToken, this.activeDmChannelId, messageId);
			// Real-time update arrives via SignalR; fall back to local removal if disconnected.
			if (!this.hub.isConnected) {
				this.dmMessages = this.dmMessages.filter((m) => m.id !== messageId);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Edit a channel message owned by the current user. */
	async editMessage(messageId: string, newBody: string): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;
		try {
			await this.api.editMessage(this.idToken, this.selectedChannelId, messageId, newBody);
			// Real-time update arrives via SignalR; fall back to local update if disconnected.
			if (!this.hub.isConnected) {
				this.messages = this.messages.map((m) =>
					m.id === messageId ? { ...m, body: newBody, editedAt: new Date().toISOString() } : m
				);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Edit a DM message owned by the current user. */
	async editDmMessage(messageId: string, newBody: string): Promise<void> {
		if (!this.idToken || !this.activeDmChannelId) return;
		try {
			await this.api.editDmMessage(this.idToken, this.activeDmChannelId, messageId, newBody);
			// Real-time update arrives via SignalR; fall back to local update if disconnected.
			if (!this.hub.isConnected) {
				this.dmMessages = this.dmMessages.map((m) =>
					m.id === messageId ? { ...m, body: newBody, editedAt: new Date().toISOString() } : m
				);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/* ═══════════════════ Replies ═══════════════════ */

	/** Activate the reply composer bar for a given message. */
	startReply(messageId: string, authorName: string, bodyPreview: string, context: 'channel' | 'dm' = 'channel'): void {
		this.replyingTo = { messageId, authorName, bodyPreview, context };
	}

	/** Clear the reply-in-progress state. */
	cancelReply(): void {
		this.replyingTo = null;
	}

	/* ═══════════════════ Friends ═══════════════════ */

	/** Navigate to the friends panel (Home view). */
	goHome(): void {
		this.showFriendsPanel = true;
		this.selectedServerId = null;
		this.selectedChannelId = null;
		this.channels = [];
		this.messages = [];
		this.hasMoreMessages = false;
		this.members = [];
		this.mobileNavOpen = false;
		this.loadFriends();
		this.loadFriendRequests();
		this.loadDmConversations();
	}

	/** Navigate back to a server view. */
	async selectServer(serverId: string): Promise<void> {
		this.showFriendsPanel = false;
		this.showInvitePanel = false;
		this.selectedServerId = serverId;
		this.mobileNavOpen = false;

		// Clean up active DM state so incoming DMs correctly increment unread badges.
		if (this.activeDmChannelId) {
			await this.hub.leaveDmChannel(this.activeDmChannelId);
			this.activeDmChannelId = null;
			this.dmMessages = [];
			this.dmTypingUsers = [];
		}

		await this.loadChannels(serverId);
		await this.loadMembers(serverId);
	}

	async loadFriends(): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingFriends = true;
		try {
			this.friends = await this.api.getFriends(this.idToken);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingFriends = false;
		}
	}

	async loadFriendRequests(): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingFriends = true;
		try {
			const [incoming, outgoing] = await Promise.all([
				this.api.getFriendRequests(this.idToken, 'received'),
				this.api.getFriendRequests(this.idToken, 'sent')
			]);
			this.incomingRequests = incoming;
			this.outgoingRequests = outgoing;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingFriends = false;
		}
	}

	async sendFriendRequest(recipientUserId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.sendFriendRequest(this.idToken, recipientUserId);
			await this.loadFriendRequests();
			// Refresh search results to update relationship status.
			if (this.friendSearchQuery.trim().length >= 2) {
				await this.searchUsers(this.friendSearchQuery);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	async acceptFriendRequest(requestId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.respondToFriendRequest(this.idToken, requestId, 'accept');
			await this.loadFriends();
			await this.loadFriendRequests();
		} catch (e) {
			this.setError(e);
		}
	}

	async declineFriendRequest(requestId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.respondToFriendRequest(this.idToken, requestId, 'decline');
			await this.loadFriendRequests();
		} catch (e) {
			this.setError(e);
		}
	}

	async cancelFriendRequest(requestId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.cancelFriendRequest(this.idToken, requestId);
			await this.loadFriendRequests();
			if (this.friendSearchQuery.trim().length >= 2) {
				await this.searchUsers(this.friendSearchQuery);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	async removeFriend(friendshipId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.removeFriend(this.idToken, friendshipId);
			await this.loadFriends();
		} catch (e) {
			this.setError(e);
		}
	}

	async searchUsers(query: string): Promise<void> {
		if (!this.idToken) return;
		this.friendSearchQuery = query;
		if (query.trim().length < 2) {
			this.userSearchResults = [];
			return;
		}
		this.isSearchingUsers = true;
		try {
			this.userSearchResults = await this.api.searchUsers(this.idToken, query);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isSearchingUsers = false;
		}
	}

	/* ═══════════════════ Direct Messages ═══════════════════ */

	async loadDmConversations(): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingDmConversations = true;
		try {
			this.dmConversations = await this.api.getDmConversations(this.idToken);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingDmConversations = false;
		}
	}

	async selectDmConversation(dmChannelId: string): Promise<void> {
		const previousDmId = this.activeDmChannelId;
		this.activeDmChannelId = dmChannelId;
		this.dmTypingUsers = [];
		this.dmMessageBody = '';
		this.replyingTo = null;
		this.mobileNavOpen = false;

		// Clear unread for this conversation.
		if (this.unreadDmCounts.has(dmChannelId)) {
			const next = new Map(this.unreadDmCounts);
			next.delete(dmChannelId);
			this.unreadDmCounts = next;
		}

		if (previousDmId) await this.hub.leaveDmChannel(previousDmId);
		await this.hub.joinDmChannel(dmChannelId);
		await this.loadDmMessages(dmChannelId);
	}

	async loadDmMessages(dmChannelId: string): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingDmMessages = true;
		try {
			const result = await this.api.getDmMessages(this.idToken, dmChannelId);
			this.dmMessages = result.messages;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingDmMessages = false;
		}
	}

	async sendDmMessage(): Promise<void> {
		if (!this.idToken || !this.activeDmChannelId) return;

		const body = this.dmMessageBody.trim();
		const imageFile = this.pendingDmImage;
		const replyToDirectMessageId = this.replyingTo?.context === 'dm' ? this.replyingTo.messageId : null;

		if (!body && !imageFile) {
			this.error = 'Message body or image is required.';
			return;
		}

		this.isSendingDm = true;
		try {
			let imageUrl: string | null = null;
			if (imageFile) {
				const result = await this.api.uploadImage(this.idToken, imageFile);
				imageUrl = result.imageUrl;
			}

			await this.api.sendDm(this.idToken, this.activeDmChannelId, body, imageUrl, replyToDirectMessageId);
			this.dmMessageBody = '';
			this.clearPendingDmImage();
			this.replyingTo = null;

			if (this.me) {
				this.hub.clearDmTyping(this.activeDmChannelId, this.effectiveDisplayName);
			}

			// If SignalR is not connected, fall back to full reload.
			if (!this.hub.isConnected) {
				await this.loadDmMessages(this.activeDmChannelId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isSendingDm = false;
		}
	}

	/** Open or create a DM conversation with a friend. */
	async openDmWithUser(userId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			const result = await this.api.createOrResumeDm(this.idToken, userId);
			await this.loadDmConversations();
			await this.selectDmConversation(result.id);
		} catch (e) {
			this.setError(e);
		}
	}

	async closeDmConversation(dmChannelId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			await this.api.closeDmConversation(this.idToken, dmChannelId);
			this.dmConversations = this.dmConversations.filter((c) => c.id !== dmChannelId);
			if (this.activeDmChannelId === dmChannelId) {
				await this.hub.leaveDmChannel(dmChannelId);
				this.activeDmChannelId = null;
				this.dmMessages = [];
				this.dmTypingUsers = [];
				this.dmMessageBody = '';
			}
		} catch (e) {
			this.setError(e);
		}
	}

	handleDmComposerInput(): void {
		if (!this.activeDmChannelId || !this.me) return;
		this.hub.emitDmTyping(this.activeDmChannelId, this.effectiveDisplayName);
	}

	/* ═══════════════════ SignalR ═══════════════════ */

	private async startSignalR(): Promise<void> {
		await this.hub.start(
			async () => {
				if (this.idToken && !isTokenExpired(this.idToken)) return this.idToken;
				const fresh = await this.refreshToken();
				return fresh ?? '';
			},
			{
			onReconnecting: () => {
				this.isHubConnected = false;
			},
			onReconnected: () => {
				this.isHubConnected = true;
			},
			onClose: () => {
				this.isHubConnected = false;
			},
			onMessage: (msg) => {
				if (msg.channelId === this.selectedChannelId) {
					if (!this.messages.some((m) => m.id === msg.id)) {
						this.messages = [...this.messages, { ...msg, linkPreviews: msg.linkPreviews ?? [], mentions: msg.mentions ?? [], replyContext: msg.replyContext ?? null }];
					}
				}
			},
			onUserTyping: (channelId, displayName) => {
				if (channelId === this.selectedChannelId && !this.typingUsers.includes(displayName)) {
					this.typingUsers = [...this.typingUsers, displayName];
				}
			},
			onUserStoppedTyping: (channelId, displayName) => {
				if (channelId === this.selectedChannelId) {
					this.typingUsers = this.typingUsers.filter((u) => u !== displayName);
				}
			},
			onReactionUpdated: (update: ReactionUpdate) => {
				if (update.channelId === this.selectedChannelId) {
					this.messages = this.messages.map((m) =>
						m.id === update.messageId ? { ...m, reactions: update.reactions } : m
					);
				}
			},
			onFriendRequestReceived: () => {
				this.loadFriendRequests();
			},
			onFriendRequestAccepted: () => {
				this.loadFriends();
				this.loadFriendRequests();
			},
			onFriendRequestDeclined: () => {
				this.loadFriendRequests();
			},
			onFriendRequestCancelled: () => {
				this.loadFriendRequests();
			},
			onFriendRemoved: () => {
				this.loadFriends();
			},
			onReceiveDm: (msg) => {
				if (this.showFriendsPanel && msg.dmChannelId === this.activeDmChannelId) {
					if (!this.dmMessages.some((m) => m.id === msg.id)) {
						this.dmMessages = [...this.dmMessages, { ...msg, linkPreviews: msg.linkPreviews ?? [], replyContext: msg.replyContext ?? null }];
					}
				} else {
					// Increment unread count for this channel.
					const next = new Map(this.unreadDmCounts);
					next.set(msg.dmChannelId, (next.get(msg.dmChannelId) ?? 0) + 1);
					this.unreadDmCounts = next;
				}
				// Refresh conversation list to update last message preview and order.
				this.loadDmConversations();
			},
			onDmTyping: (dmChannelId, displayName) => {
				if (dmChannelId === this.activeDmChannelId && !this.dmTypingUsers.includes(displayName)) {
					this.dmTypingUsers = [...this.dmTypingUsers, displayName];
				}
			},
			onDmStoppedTyping: (dmChannelId, displayName) => {
				if (dmChannelId === this.activeDmChannelId) {
					this.dmTypingUsers = this.dmTypingUsers.filter((u) => u !== displayName);
				}
			},
			onDmConversationOpened: () => {
				// A new DM conversation was opened by another user — refresh the list.
				this.loadDmConversations();
			},
			onKickedFromServer: async (event) => {
				// Leave the server's SignalR group since we're no longer a member.
				try {
					await this.hub.leaveServer(event.serverId);
				} catch (error) {
					console.error('Failed to leave server after being kicked:', error);
					// Proceed with local cleanup even if leaving the hub group fails.
					this.setTransientError('There was a problem updating your connection after being kicked. Some data may be out of date.');
				}

				// Remove the server from the local list and navigate away if needed.
				this.servers = this.servers.filter((s) => s.serverId !== event.serverId);
				if (this.selectedServerId === event.serverId) {
					this.selectedServerId = this.servers[0]?.serverId ?? null;
					this.channels = [];
					this.messages = [];
					this.hasMoreMessages = false;
					this.members = [];
					if (this.selectedServerId) {
						this.loadChannels(this.selectedServerId);
						this.loadMembers(this.selectedServerId);
					}
				}
				this.setTransientError(`You were kicked from "${event.serverName}".`);
			},
			onLinkPreviewsReady: (event) => {
				// Patch link previews into the matching channel message.
				if (event.channelId && event.channelId === this.selectedChannelId) {
					this.messages = this.messages.map((m) =>
						m.id === event.messageId
							? { ...m, linkPreviews: event.linkPreviews }
							: m
					);
				}
				// Patch link previews into the matching DM message.
				if (event.dmChannelId && event.dmChannelId === this.activeDmChannelId) {
					this.dmMessages = this.dmMessages.map((m) =>
						m.id === event.messageId
							? { ...m, linkPreviews: event.linkPreviews }
							: m
					);
				}
			},
			onMentionReceived: (event) => {
				// Track channel→server mapping for badge aggregation
				this.channelServerMap.set(event.channelId, event.serverId);

				// Don't increment badge for the channel the user is currently viewing
				if (event.channelId === this.selectedChannelId) return;

				const next = new Map(this.channelMentionCounts);
				next.set(event.channelId, (next.get(event.channelId) ?? 0) + 1);
				this.channelMentionCounts = next;
			},
			onMemberJoined: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.loadMembers(event.serverId);
				}
			},
			onMemberLeft: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.loadMembers(event.serverId);
				}
			},
			onMessageDeleted: (event) => {
				if (event.channelId === this.selectedChannelId) {
					this.messages = this.messages.filter((m) => m.id !== event.messageId);
				}
			},
			onDmMessageDeleted: (event) => {
				if (event.dmChannelId === this.activeDmChannelId) {
					this.dmMessages = this.dmMessages.filter((m) => m.id !== event.messageId);
				}
			},
			onMessageEdited: (event) => {
				if (event.channelId === this.selectedChannelId) {
					this.messages = this.messages.map((m) =>
						m.id === event.messageId ? { ...m, body: event.body, editedAt: event.editedAt } : m
					);
				}
			},
			onDmMessageEdited: (event) => {
				if (event.dmChannelId === this.activeDmChannelId) {
					this.dmMessages = this.dmMessages.map((m) =>
						m.id === event.messageId ? { ...m, body: event.body, editedAt: event.editedAt } : m
					);
				}
			},
			onServerNameChanged: (event) => {
				// Update server name in the local list
				this.servers = this.servers.map((s) =>
					s.serverId === event.serverId ? { ...s, name: event.name } : s
				);
			},
			onChannelNameChanged: (event) => {
				// Update channel name in the local list if it's for the current server
				if (event.serverId === this.selectedServerId) {
					this.channels = this.channels.map((c) =>
						c.id === event.channelId ? { ...c, name: event.name } : c
					);
				}
			},
			onServerDeleted: (event) => {
				this.servers = this.servers.filter((s) => s.serverId !== event.serverId);
				if (this.selectedServerId === event.serverId) {
					this.selectedServerId = null;
					this.selectedChannelId = null;
					this.channels = [];
					this.messages = [];
					this.members = [];
				}
			},
			onChannelDeleted: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.channels = this.channels.filter((c) => c.id !== event.channelId);
					if (this.selectedChannelId === event.channelId) {
						const next = this.channels[0];
						this.selectedChannelId = next?.id ?? null;
						this.messages = [];
					}
				}
			}
		});

		this.isHubConnected = this.hub.isConnected;

		if (this.selectedChannelId) {
			await this.hub.joinChannel(this.selectedChannelId);
		}
		if (this.activeDmChannelId) {
			await this.hub.joinDmChannel(this.activeDmChannelId);
		}
	}

	async destroy(): Promise<void> {
		await this.hub.stop();
	}

	/* ───── helpers ───── */

	private transientErrorTimer: ReturnType<typeof setTimeout> | null = null;

	private setTransientError(message: string, durationMs = 5000): void {
		if (this.transientErrorTimer) clearTimeout(this.transientErrorTimer);
		this.error = message;
		this.transientErrorTimer = setTimeout(() => {
			if (this.error === message) this.error = null;
			this.transientErrorTimer = null;
		}, durationMs);
	}

	private setError(e: unknown): void {
		if (e instanceof ApiError) {
			this.error = e.message;
		} else if (e instanceof Error) {
			this.error = e.message;
		} else {
			this.error = 'An unexpected error occurred.';
		}
	}
}
