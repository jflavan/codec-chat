import { getContext, setContext } from 'svelte';
import { tick } from 'svelte';
import { SvelteMap } from 'svelte/reactivity';
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
	ServerInvite,
	VoiceChannelMember,
	CustomEmoji,
	SearchFilters,
	PaginatedSearchResults,
	PresenceStatus,
	AuthResponse,
	TokenRefreshResponse,
	ChannelCategory,
	AuditLogEntry,
	NotificationPreferences,
	PinnedMessage,
	MessagePinnedEvent,
	MessageUnpinnedEvent,
	Webhook,
	WebhookDelivery
} from '$lib/types/index.js';
import { ApiClient, ApiError } from '$lib/api/client.js';
import { ChatHubService } from '$lib/services/chat-hub.js';
import type { ReactionUpdate, DmReactionUpdate } from '$lib/services/chat-hub.js';
import { VoiceService } from '$lib/services/voice-service.js';
import {
	persistToken,
	loadStoredToken,
	clearSession as clearStoredSession,
	isTokenExpired,
	isSessionExpired,
	type AuthType,
	setAuthType,
	getAuthType,
	persistRefreshToken,
	loadStoredRefreshToken
} from '$lib/auth/session.js';
import {
	initGoogleIdentity,
	renderGoogleButton,
	requestFreshToken,
	consumeRedirectCredential
} from '$lib/auth/google.js';
import { getTheme, applyTheme, type ThemeId } from '$lib/utils/theme.js';

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
	isPurgingChannel = $state(false);
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

	/* ───── categories ───── */
	categories = $state<ChannelCategory[]>([]);

	/* ───── audit log ───── */
	auditLogEntries = $state<AuditLogEntry[]>([]);
	hasMoreAuditLog = $state(false);
	isLoadingAuditLog = $state(false);

	/* ───── notification preferences ───── */
	notificationPreferences = $state<NotificationPreferences | null>(null);

	/* ───── pinned messages ───── */
	pinnedMessages = $state<PinnedMessage[]>([]);
	showPinnedPanel = $state(false);
	readonly pinnedMessageIds = $derived(new Set(this.pinnedMessages.map((p) => p.messageId)));
	readonly pinnedMessageCount = $derived(this.pinnedMessages.length);

	/* ───── webhooks ───── */
	webhooks = $state<Webhook[]>([]);
	webhookDeliveries = $state<WebhookDelivery[]>([]);
	isLoadingWebhooks = $state(false);
	isCreatingWebhook = $state(false);
	selectedWebhookId = $state<string | null>(null);
	isLoadingDeliveries = $state(false);

	/* ───── selection ───── */
	selectedServerId = $state<string | null>(null);
	selectedChannelId = $state<string | null>(null);

	/* ───── auth ───── */
	idToken = $state<string | null>(null);
	me = $state<UserProfile | null>(null);
	status = $state('Signed out');
	error = $state<string | null>(null);
	authType: AuthType = $state<AuthType>('google');
	needsNickname = $state(false);
	needsLinking = $state(false);
	linkingEmail = $state('');
	pendingGoogleCredential = $state('');
	emailVerified = $state(true);

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
	showFriendsPanel = $state(false);
	showAlphaNotification = $state(false);
	friendsTab = $state<'all' | 'pending' | 'add'>('all');
	friendSearchQuery = $state('');
	settingsOpen = $state(false);
	settingsCategory = $state<'profile' | 'account' | 'voice-audio' | 'appearance'>('profile');
	theme = $state<ThemeId>(getTheme());
	bugReportOpen = $state(false);
	serverSettingsOpen = $state(false);
	serverSettingsCategory = $state<'general' | 'channels' | 'invites' | 'webhooks' | 'emojis' | 'members' | 'audit-log'>('general');
	isUpdatingServerName = $state(false);
	isUpdatingChannelName = $state(false);
	isUploadingServerIcon = $state(false);
	customEmojis = $state<CustomEmoji[]>([]);
	isUploadingEmoji = $state(false);

	/* ───── search ───── */
	isSearchOpen = $state(false);
	searchQuery = $state('');
	searchFilters = $state<SearchFilters>({});
	searchResults = $state<PaginatedSearchResults | null>(null);
	isSearching = $state(false);
	highlightedMessageId = $state<string | null>(null);

	/* ───── mobile navigation ───── */
	mobileNavOpen = $state(false);
	mobileMembersOpen = $state(false);

	/* ───── form fields ───── */
	newServerName = $state('');
	newChannelName = $state('');
	newChannelType = $state<'text' | 'voice'>('text');
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
	ignoredReactionUpdates = $state<Map<string, string[]>>(new Map());
	pendingReactionKeys = $state<Set<string>>(new Set());

	/* ───── presence ───── */
	userPresence = $state(new SvelteMap<string, PresenceStatus>());

	/* ───── voice ───── */
	activeVoiceChannelId = $state<string | null>(null);
	/** Map of channelId → list of connected members, for sidebar display. */
	voiceChannelMembers = $state<Map<string, VoiceChannelMember[]>>(new Map());
	isMuted = $state(false);
	isDeafened = $state(false);
	isJoiningVoice = $state(false);
	/** Per-user volume levels: userId -> 0.0-1.0. Loaded from localStorage on init. */
	userVolumes = $state<Map<string, number>>(new Map());
	/** Input mode: 'voice-activity' (always-on mic) or 'push-to-talk'. */
	voiceInputMode = $state<'voice-activity' | 'push-to-talk'>('voice-activity');
	/** KeyboardEvent.code for push-to-talk key. */
	pttKey = $state('KeyV');
	/** True while the PTT key is held down. */
	isPttActive = $state(false);

	/* ───── calls ───── */
	activeCall = $state<{
		callId: string;
		dmChannelId: string;
		otherUserId: string;
		otherDisplayName: string;
		otherAvatarUrl?: string | null;
		status: 'ringing' | 'active';
		startedAt: string;
		answeredAt?: string;
	} | null>(null);

	incomingCall = $state<{
		callId: string;
		dmChannelId: string;
		callerUserId: string;
		callerDisplayName: string;
		callerAvatarUrl?: string | null;
	} | null>(null);

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

	readonly canManageRoles = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly canPinMessages = $derived(
		this.isGlobalAdmin || this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly selectedServerName = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.name ?? 'Codec'
	);

	readonly selectedServerIconUrl = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.iconUrl ?? null
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
	private voice: VoiceService = new VoiceService();
	private refreshPromise: Promise<string | null> | null = null;
	private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
	private audioContext: AudioContext | null = null;
	private audioNodes = new Map<
		string,
		{ element: HTMLAudioElement; source: MediaStreamAudioSourceNode; gain: GainNode }
	>();
	/** participantId -> userId lookup for volume. Built during attach. */
	private audioParticipantUserMap = new Map<string, string>();
	private pttKeydownHandler: ((e: KeyboardEvent) => void) | null = null;
	private pttKeyupHandler: ((e: KeyboardEvent) => void) | null = null;

	constructor(
		private readonly apiBaseUrl: string,
		private readonly googleClientId: string
	) {
		this.api = new ApiClient(apiBaseUrl, () => this.refreshToken());
		this.hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);
	}

	/**
	 * Attempt to silently refresh the auth token.
	 * For Google: uses One Tap. For local/GitHub/Discord: uses refresh token.
	 * Concurrent calls are deduplicated so only one refresh runs at a time.
	 * Signs the user out if the refresh fails.
	 */
	private async refreshToken(): Promise<string | null> {
		if (this.refreshPromise) return this.refreshPromise;

		this.refreshPromise = (async () => {
			try {
				if (this.authType === 'local' || this.authType === 'github' || this.authType === 'discord') {
					const success = await this.refreshAccessToken();
					return success ? this.idToken : null;
				}
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

	setTheme(id: ThemeId): void {
		this.theme = id;
		applyTheme(id);
	}

	openServerSettings(): void {
		this.serverSettingsOpen = true;
		this.serverSettingsCategory = 'general';
	}

	closeServerSettings(): void {
		this.serverSettingsOpen = false;
	}

	/** Dismiss the alpha notification banner. */
	dismissAlphaNotification(): void {
		this.showAlphaNotification = false;
	}

	/** Submit a bug report to GitHub via the API proxy. */
	async submitBugReport(title: string, description: string, userAgent: string, currentPage: string): Promise<{ issueUrl: string }> {
		if (!this.idToken) throw new Error('Not authenticated');
		return this.api.submitBugReport(this.idToken, title, description, userAgent, currentPage);
	}

	/* ═══════════════════ Auth ═══════════════════ */

	/** Bootstrap auth: restore session or show sign-in UI. */
	init(): void {
		applyTheme(this.theme);
		this._loadUserVolumes();
		this._loadVoicePreferences();
		this.authType = getAuthType();

		// Check for credential from Google redirect flow (Android PWA)
		const redirectCredential = consumeRedirectCredential();
		if (redirectCredential) {
			this.handleCredential(redirectCredential);
			return;
		}

		if (!isSessionExpired()) {
			const stored = loadStoredToken();
			if (stored && !isTokenExpired(stored)) {
				this.handleCredential(stored);
				return;
			}
			// For local/oauth auth, try refresh token
			if (this.authType === 'local' || this.authType === 'github' || this.authType === 'discord') {
				this.refreshAccessToken().then((success) => {
					if (success && this.idToken) {
						this.handleCredential(this.idToken);
					} else {
						this.isInitialLoading = false;
						this.renderSignIn();
					}
				});
				return;
			}
		}
		clearStoredSession();
		this.isInitialLoading = false;
		this.renderSignIn();
	}

	private renderSignIn(): void {
		initGoogleIdentity(this.googleClientId, (token) => this.handleCredential(token), {
			renderButtonIds: ['google-button', 'login-google-button'],
			autoSelect: true
		});
	}

	async handleCredential(token: string): Promise<void> {
		this.idToken = token;
		this.status = 'Signed in';
		this.isInitialLoading = true;
		persistToken(token);

		// Load user profile first to ensure user is created and auto-joined to default server
		await this.loadMe();

		// Check if local auth user needs email verification
		if (this.authType === 'local' && this.me && !this.me.user.emailVerified) {
			this.emailVerified = false;
			this.isInitialLoading = false;
			return;
		}

		// Check if this Google sign-in needs account linking
		if (this.me?.needsLinking) {
			this.needsLinking = true;
			this.linkingEmail = this.me.email ?? '';
			this.pendingGoogleCredential = token;
			this.isInitialLoading = false;
			return;
		}

		// Check if this is a new Google user who needs to set a nickname
		if (this.me?.isNewUser) {
			this.needsNickname = true;
			this.isInitialLoading = false;
			return;
		}

		// Then load all other data in parallel
		await Promise.all([
			this.loadServers(),
			this.loadFriends(),
			this.loadFriendRequests(),
			this.loadDmConversations(),
			this.startSignalR()
		]);
		this.isInitialLoading = false;
		this.showAlphaNotification = true;
	}

	async register(email: string, password: string, nickname: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.api.register(email, password, nickname, recaptchaToken);
	}

	async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
		return this.api.login(email, password, recaptchaToken);
	}

	async linkGoogle(email: string, password: string, googleCredential: string): Promise<AuthResponse> {
		return this.api.linkGoogle(email, password, googleCredential);
	}

	async handleOAuthCallback(provider: 'github' | 'discord', code: string): Promise<void> {
		const response = await this.api.oauthCallback(provider, code);
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType(provider);
		this.authType = provider;

		if (response.isNewUser) {
			this.needsNickname = true;
			this.isInitialLoading = false;
			return;
		}

		this.isInitialLoading = true;
		await this.loadMe();

		await Promise.all([
			this.loadServers(),
			this.loadFriends(),
			this.loadFriendRequests(),
			this.loadDmConversations(),
			this.startSignalR()
		]);
		this.isInitialLoading = false;
		this.showAlphaNotification = true;
	}

	async handleLocalAuth(response: AuthResponse): Promise<void> {
		this.idToken = response.accessToken;
		this.status = 'Signed in';
		persistToken(response.accessToken);
		persistRefreshToken(response.refreshToken);
		setAuthType('local');
		this.authType = 'local';
		this.emailVerified = response.user.emailVerified ?? false;

		if (!this.emailVerified) {
			this.isInitialLoading = false;
			return;
		}

		this.isInitialLoading = true;
		await this.loadMe();

		await Promise.all([
			this.loadServers(),
			this.loadFriends(),
			this.loadFriendRequests(),
			this.loadDmConversations(),
			this.startSignalR()
		]);
		this.isInitialLoading = false;
		this.showAlphaNotification = true;
	}

	async resendVerification(): Promise<void> {
		if (!this.idToken) return;
		await this.api.resendVerification(this.idToken);
	}

	async checkEmailVerified(): Promise<boolean> {
		if (!this.idToken) return false;
		try {
			const profile = await this.api.getMe(this.idToken);
			if (profile.user.emailVerified) {
				this.emailVerified = true;
				this.me = profile;
				this.isInitialLoading = true;
				await Promise.all([
					this.loadServers(),
					this.loadFriends(),
					this.loadFriendRequests(),
					this.loadDmConversations(),
					this.startSignalR()
				]);
				this.isInitialLoading = false;
				this.showAlphaNotification = true;
				return true;
			}
		} catch {
			// ignore
		}
		return false;
	}

	async confirmNickname(nickname: string): Promise<void> {
		if (!this.idToken) return;
		await this.api.setNickname(this.idToken, nickname);
		this.needsNickname = false;
		this.isInitialLoading = true;

		await Promise.all([
			this.loadMe(),
			this.loadServers(),
			this.loadFriends(),
			this.loadFriendRequests(),
			this.loadDmConversations(),
			this.startSignalR()
		]);
		this.isInitialLoading = false;
		this.showAlphaNotification = true;
	}

	async refreshAccessToken(): Promise<boolean> {
		const refreshToken = loadStoredRefreshToken();
		if (!refreshToken) return false;

		try {
			const response = await this.api.refreshToken(refreshToken);
			this.idToken = response.accessToken;
			persistToken(response.accessToken);
			persistRefreshToken(response.refreshToken);
			return true;
		} catch {
			clearStoredSession();
			return false;
		}
	}

	async signOut(): Promise<void> {
		await this.hub.stop();

		// Revoke refresh token server-side before clearing local state
		const refreshToken = loadStoredRefreshToken();
		if (refreshToken) {
			try {
				await this.api.logout(refreshToken);
			} catch {
				// Best-effort: proceed with local cleanup even if server revocation fails
			}
		}

		clearStoredSession();

		this.isInitialLoading = false;
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
		this.webhooks = [];
		this.webhookDeliveries = [];
		this.selectedWebhookId = null;
		this.customEmojis = [];
		this.categories = [];
		this.auditLogEntries = [];
		this.hasMoreAuditLog = false;
		this.notificationPreferences = null;
		this.typingUsers = [];
		this.ignoredReactionUpdates = new Map();
		this.pendingReactionKeys = new Set();
		this.error = null;
		this.showFriendsPanel = false;
		this.friendsTab = 'all';
		this.friendSearchQuery = '';
		this.settingsOpen = false;
		this.serverSettingsOpen = false;
		this.replyingTo = null;
		this.lightboxImageUrl = null;
		this.userPresence = new SvelteMap();
		this.activeVoiceChannelId = null;
		this.voiceChannelMembers = new Map();
		this.isMuted = false;
		this.isDeafened = false;
		this.isJoiningVoice = false;
		this._cleanupRemoteAudio();

		await tick();
		renderGoogleButton('google-button');
		renderGoogleButton('login-google-button');
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
				await this.loadCustomEmojis(this.selectedServerId);
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
			const firstTextChannel = this.channels.find((c) => c.type !== 'voice') ?? this.channels[0] ?? null;
			this.selectedChannelId = firstTextChannel?.id ?? null;

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

			// Load voice state membership for each voice channel
			await this._loadAllVoiceStates();
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

	async loadCustomEmojis(serverId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			this.customEmojis = await this.api.getCustomEmojis(this.idToken, serverId);
		} catch (e) {
			this.setError(e);
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
		this.pinnedMessages = [];
		this.showPinnedPanel = false;

		// Clear mention badge for this channel
		const next = new Map(this.channelMentionCounts);
		next.delete(channelId);
		this.channelMentionCounts = next;

		if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
		await this.hub.joinChannel(channelId);
		await this.loadMessages(channelId);
		await this.loadPinnedMessages(channelId);
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

	/** Persist a new server display order for the current user. */
	async reorderServers(serverIds: string[]): Promise<void> {
		if (!this.idToken) return;
		// Optimistically reorder the local list.
		const ordered: typeof this.servers = [];
		for (let i = 0; i < serverIds.length; i++) {
			const s = this.servers.find((srv) => srv.serverId === serverIds[i]);
			if (s) ordered.push({ ...s, sortOrder: i });
		}
		this.servers = ordered;

		try {
			await this.api.reorderServers(this.idToken, serverIds);
		} catch (e) {
			this.setError(e);
			// Reload original order on failure.
			await this.loadServers();
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
			const created = await this.api.createChannel(this.idToken, this.selectedServerId, name, this.newChannelType);
			this.newChannelName = '';
			this.newChannelType = 'text';
			this.showCreateChannel = false;
			await this.loadChannels(this.selectedServerId);
			if (created.type !== 'voice') {
				await this.selectChannel(created.id);
			}
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
			await this.api.updateServer(this.idToken, this.selectedServerId, { name: name.trim() });
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUpdatingServerName = false;
		}
	}

	/** Upload or update the server icon image. */
	async uploadServerIcon(file: File): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;

		this.isUploadingServerIcon = true;
		try {
			await this.api.uploadServerIcon(this.idToken, this.selectedServerId, file);
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingServerIcon = false;
		}
	}

	/** Remove the server icon. */
	async removeServerIcon(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;

		this.isUploadingServerIcon = true;
		try {
			await this.api.deleteServerIcon(this.idToken, this.selectedServerId);
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUploadingServerIcon = false;
		}
	}

	async uploadCustomEmoji(name: string, file: File): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isUploadingEmoji = true;
		try {
			const emoji = await this.api.uploadCustomEmoji(this.idToken, this.selectedServerId, name, file);
			this.customEmojis = [...this.customEmojis, emoji];
		} finally {
			this.isUploadingEmoji = false;
		}
	}

	async renameCustomEmoji(emojiId: string, name: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.renameCustomEmoji(this.idToken, this.selectedServerId, emojiId, name);
			this.customEmojis = this.customEmojis.map(e => e.id === emojiId ? { ...e, name } : e);
		} catch (e) {
			this.setError(e);
		}
	}

	async deleteCustomEmoji(emojiId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteCustomEmoji(this.idToken, this.selectedServerId, emojiId);
			this.customEmojis = this.customEmojis.filter(e => e.id !== emojiId);
		} catch (e) {
			this.setError(e);
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
			await this.api.updateChannel(this.idToken, this.selectedServerId, channelId, { name: name.trim() });
			// Update will be reflected via SignalR event
		} catch (e) {
			this.setError(e);
		} finally {
			this.isUpdatingChannelName = false;
		}
	}

	/** Delete a server. Requires Owner role or global admin privileges. */
	isDeletingServer = $state(false);

	async deleteServer(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isDeletingServer = true;
		try {
			await this.api.deleteServer(this.idToken, serverId);
			this.servers = this.servers.filter((s) => s.serverId !== serverId);
			this.serverSettingsOpen = false;
			if (this.selectedServerId === serverId) {
				this.goHome();
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isDeletingServer = false;
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

	/** Change a member's role in the currently selected server. */
	async updateMemberRole(userId: string, role: 'Admin' | 'Member'): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateMemberRole(this.idToken, this.selectedServerId, userId, role);
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

	/* ═══════════════════ Webhooks ═══════════════════ */

	/** Load webhooks for the currently selected server. */
	async loadWebhooks(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isLoadingWebhooks = true;
		try {
			this.webhooks = await this.api.getWebhooks(this.idToken, this.selectedServerId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingWebhooks = false;
		}
	}

	/** Create a new webhook for the currently selected server. */
	async createWebhook(data: {
		name: string;
		url: string;
		secret?: string;
		eventTypes: string[];
	}): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isCreatingWebhook = true;
		try {
			const webhook = await this.api.createWebhook(this.idToken, this.selectedServerId, data);
			this.webhooks = [webhook, ...this.webhooks];
		} catch (e) {
			this.setError(e);
		} finally {
			this.isCreatingWebhook = false;
		}
	}

	/** Update an existing webhook. */
	async updateWebhook(
		webhookId: string,
		data: { name?: string; url?: string; secret?: string; eventTypes?: string[]; isActive?: boolean }
	): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			const updated = await this.api.updateWebhook(this.idToken, this.selectedServerId, webhookId, data);
			this.webhooks = this.webhooks.map((w) => (w.id === webhookId ? updated : w));
		} catch (e) {
			this.setError(e);
		}
	}

	/** Delete a webhook from the currently selected server. */
	async deleteWebhook(webhookId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteWebhook(this.idToken, this.selectedServerId, webhookId);
			this.webhooks = this.webhooks.filter((w) => w.id !== webhookId);
			if (this.selectedWebhookId === webhookId) {
				this.selectedWebhookId = null;
				this.webhookDeliveries = [];
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Load delivery logs for a specific webhook. */
	async loadWebhookDeliveries(webhookId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isLoadingDeliveries = true;
		this.selectedWebhookId = webhookId;
		try {
			this.webhookDeliveries = await this.api.getWebhookDeliveries(
				this.idToken,
				this.selectedServerId,
				webhookId
			);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingDeliveries = false;
		}
	}

	/* ═══════════════════ Categories ═══════════════════ */

	async loadCategories(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			this.categories = await this.api.getCategories(this.idToken, this.selectedServerId);
		} catch (e) {
			this.setError(e);
		}
	}

	async createCategory(name: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			const created = await this.api.createCategory(this.idToken, this.selectedServerId, name);
			this.categories = [...this.categories, created];
		} catch (e) {
			this.setError(e);
		}
	}

	async renameCategory(categoryId: string, name: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.renameCategory(this.idToken, this.selectedServerId, categoryId, name);
			this.categories = this.categories.map((c) =>
				c.id === categoryId ? { ...c, name } : c
			);
		} catch (e) {
			this.setError(e);
		}
	}

	async deleteCategory(categoryId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.deleteCategory(this.idToken, this.selectedServerId, categoryId);
			this.categories = this.categories.filter((c) => c.id !== categoryId);
			this.channels = this.channels.map((ch) =>
				ch.categoryId === categoryId ? { ...ch, categoryId: undefined } : ch
			);
		} catch (e) {
			this.setError(e);
		}
	}

	async saveChannelOrder(channels: { channelId: string; categoryId?: string; position: number }[]): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateChannelOrder(this.idToken, this.selectedServerId, channels);
		} catch (e) {
			this.setError(e);
		}
	}

	async saveCategoryOrder(categories: { categoryId: string; position: number }[]): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateCategoryOrder(this.idToken, this.selectedServerId, categories);
		} catch (e) {
			this.setError(e);
		}
	}

	/* ═══════════════════ Descriptions ═══════════════════ */

	async updateServerDescription(description: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateServer(this.idToken, this.selectedServerId, { description });
		} catch (e) {
			this.setError(e);
		}
	}

	async updateChannelDescription(channelId: string, description: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			await this.api.updateChannel(this.idToken, this.selectedServerId, channelId, { description });
		} catch (e) {
			this.setError(e);
		}
	}

	/* ═══════════════════ Audit Log ═══════════════════ */

	async loadAuditLog(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		this.isLoadingAuditLog = true;
		this.auditLogEntries = [];
		try {
			const result = await this.api.getAuditLog(this.idToken, this.selectedServerId, { limit: 50 });
			this.auditLogEntries = result.entries;
			this.hasMoreAuditLog = result.hasMore;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingAuditLog = false;
		}
	}

	async loadOlderAuditLog(): Promise<void> {
		if (!this.idToken || !this.selectedServerId || !this.hasMoreAuditLog || this.isLoadingAuditLog) return;
		const last = this.auditLogEntries[this.auditLogEntries.length - 1];
		if (!last) return;

		this.isLoadingAuditLog = true;
		try {
			const result = await this.api.getAuditLog(this.idToken, this.selectedServerId, {
				before: last.createdAt,
				limit: 50
			});
			this.auditLogEntries = [...this.auditLogEntries, ...result.entries];
			this.hasMoreAuditLog = result.hasMore;
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingAuditLog = false;
		}
	}

	/* ═══════════════════ Notification Preferences ═══════════════════ */

	async loadNotificationPreferences(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		try {
			this.notificationPreferences = await this.api.getNotificationPreferences(this.idToken, this.selectedServerId);
		} catch (e) {
			this.setError(e);
		}
	}

	async toggleServerMute(): Promise<void> {
		if (!this.idToken || !this.selectedServerId) return;
		const current = this.notificationPreferences?.serverMuted ?? false;
		const next = !current;
		if (this.notificationPreferences) {
			this.notificationPreferences = { ...this.notificationPreferences, serverMuted: next };
		}
		try {
			await this.api.muteServer(this.idToken, this.selectedServerId, next);
		} catch (e) {
			// Revert on failure
			if (this.notificationPreferences) {
				this.notificationPreferences = { ...this.notificationPreferences, serverMuted: current };
			}
			this.setError(e);
		}
	}

	async toggleChannelMute(channelId: string): Promise<void> {
		if (!this.idToken || !this.selectedServerId || !this.notificationPreferences) return;
		const override = this.notificationPreferences.channelOverrides.find((o) => o.channelId === channelId);
		const current = override?.isMuted ?? false;
		const next = !current;

		// Optimistic update
		const overrides = this.notificationPreferences.channelOverrides.filter((o) => o.channelId !== channelId);
		this.notificationPreferences = {
			...this.notificationPreferences,
			channelOverrides: [...overrides, { channelId, isMuted: next }]
		};

		try {
			await this.api.muteChannel(this.idToken, this.selectedServerId, channelId, next);
		} catch (e) {
			// Revert on failure
			await this.loadNotificationPreferences();
			this.setError(e);
		}
	}

	get isServerMuted(): boolean {
		return this.notificationPreferences?.serverMuted ?? false;
	}

	isChannelMuted(channelId: string): boolean {
		return this.notificationPreferences?.channelOverrides.find((o) => o.channelId === channelId)?.isMuted ?? false;
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

	/* ═══════════════════ Status ═══════════════════ */

	/** Set or update the current user's custom status message. */
	async setStatus(statusText?: string | null, statusEmoji?: string | null): Promise<void> {
		if (!this.idToken) return;
		this.error = null;
		try {
			const result = await this.api.setStatus(this.idToken, statusText, statusEmoji);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						statusText: result.statusText,
						statusEmoji: result.statusEmoji
					}
				};
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/** Clear the current user's custom status message. */
	async clearStatus(): Promise<void> {
		if (!this.idToken) return;
		this.error = null;
		try {
			const result = await this.api.clearStatus(this.idToken);
			if (this.me) {
				this.me = {
					...this.me,
					user: {
						...this.me.user,
						statusText: result.statusText,
						statusEmoji: result.statusEmoji
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

	private static reactionToggleKey(messageId: string, emoji: string): string {
		return `${messageId}:${emoji}`;
	}

	private static serializeReactionSnapshot(reactions: ReadonlyArray<Message['reactions'][number]>): string {
		return JSON.stringify(
			reactions
				.map((reaction) => ({
					emoji: reaction.emoji,
					count: reaction.count,
					userIds: [...reaction.userIds].sort()
				}))
				.sort((reactionA, reactionB) => reactionA.emoji.localeCompare(reactionB.emoji))
		);
	}

	private _setReactionPending(reactionKey: string, pending: boolean): void {
		const next = new Set(this.pendingReactionKeys);
		if (pending) {
			next.add(reactionKey);
		} else {
			next.delete(reactionKey);
		}
		this.pendingReactionKeys = next;
	}

	private _updateMessageReactions(messageId: string, reactions: Message['reactions']): void {
		this.messages = this.messages.map((message) =>
			message.id === messageId ? { ...message, reactions } : message
		);
	}

	private _updateDmMessageReactions(messageId: string, reactions: DirectMessage['reactions']): void {
		this.dmMessages = this.dmMessages.map((message) =>
			message.id === messageId ? { ...message, reactions } : message
		);
	}

	private _rememberReactionUpdate(messageId: string, reactions: Message['reactions']): void {
		const serialized = AppState.serializeReactionSnapshot(reactions);
		const next = new Map(this.ignoredReactionUpdates);
		next.set(messageId, [...(next.get(messageId) ?? []), serialized]);
		this.ignoredReactionUpdates = next;
	}

	private _matchAndRemoveReactionSnapshot(
		messageId: string,
		reactions: Message['reactions']
	): boolean {
		const queue = this.ignoredReactionUpdates.get(messageId);
		if (!queue?.length) {
			return false;
		}

		const serialized = AppState.serializeReactionSnapshot(reactions);
		const matchedIndex = queue.indexOf(serialized);
		if (matchedIndex === -1) {
			return false;
		}

		const next = new Map(this.ignoredReactionUpdates);
		const remaining = queue.filter((snapshot, index) => {
			void snapshot;
			return index !== matchedIndex;
		});
		if (remaining.length > 0) {
			next.set(messageId, remaining);
		} else {
			next.delete(messageId);
		}
		this.ignoredReactionUpdates = next;

		return true;
	}

	/** True when a specific message/emoji reaction toggle request is still in flight. */
	isReactionPending(messageId: string, emoji: string): boolean {
		return this.pendingReactionKeys.has(AppState.reactionToggleKey(messageId, emoji));
	}

	async toggleReaction(messageId: string, emoji: string): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;
		const normalizedEmoji = emoji.trim();
		if (!normalizedEmoji) return;
		const reactionKey = AppState.reactionToggleKey(messageId, normalizedEmoji);
		if (this.pendingReactionKeys.has(reactionKey)) return;
		this._setReactionPending(reactionKey, true);
		try {
			const result = await this.api.toggleReaction(
				this.idToken,
				this.selectedChannelId,
				messageId,
				normalizedEmoji
			);
			this._updateMessageReactions(messageId, result.reactions);
			this._rememberReactionUpdate(messageId, result.reactions);
			// Real-time update arrives via SignalR; fall back to reload if disconnected.
			if (!this.hub.isConnected) {
				await this.loadMessages(this.selectedChannelId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this._setReactionPending(reactionKey, false);
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
				this.pinnedMessages = this.pinnedMessages.filter((p) => p.messageId !== messageId);
			}
		} catch (e) {
			this.setError(e);
		}
	}

	/* ───── pinning ───── */

	async loadPinnedMessages(channelId?: string): Promise<void> {
		const cid = channelId ?? this.selectedChannelId;
		if (!cid || !this.idToken) return;
		try {
			this.pinnedMessages = await this.api.getPinnedMessages(this.idToken, cid);
		} catch {
			this.pinnedMessages = [];
		}
	}

	async pinMessage(messageId: string): Promise<void> {
		if (!this.selectedChannelId || !this.idToken) return;
		try {
			await this.api.pinMessage(this.idToken, this.selectedChannelId, messageId);
		} catch (e) {
			this.setError(e);
		}
	}

	async unpinMessage(messageId: string): Promise<void> {
		if (!this.selectedChannelId || !this.idToken) return;
		try {
			await this.api.unpinMessage(this.idToken, this.selectedChannelId, messageId);
			this.pinnedMessages = this.pinnedMessages.filter((p) => p.messageId !== messageId);
		} catch (e) {
			this.setError(e);
		}
	}

	togglePinnedPanel(): void {
		this.showPinnedPanel = !this.showPinnedPanel;
		if (this.showPinnedPanel) {
			this.loadPinnedMessages();
		}
	}

	/** Purge all messages from a channel. Global admin only. */
	async purgeChannel(channelId: string): Promise<void> {
		if (!this.idToken) return;
		this.isPurgingChannel = true;
		try {
			await this.api.purgeChannel(this.idToken, channelId);
			if (!this.hub.isConnected && channelId === this.selectedChannelId) {
				this.messages = [];
				this.hasMoreMessages = false;
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isPurgingChannel = false;
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

	/** Toggle a reaction on a DM message. */
	async toggleDmReaction(messageId: string, emoji: string): Promise<void> {
		if (!this.idToken || !this.activeDmChannelId) return;
		const normalizedEmoji = emoji.trim();
		if (!normalizedEmoji) return;
		const reactionKey = AppState.reactionToggleKey(messageId, normalizedEmoji);
		if (this.pendingReactionKeys.has(reactionKey)) return;
		this._setReactionPending(reactionKey, true);
		try {
			const result = await this.api.toggleDmReaction(
				this.idToken,
				this.activeDmChannelId,
				messageId,
				normalizedEmoji
			);
			this._updateDmMessageReactions(messageId, result.reactions);
			this._rememberReactionUpdate(messageId, result.reactions);
			if (!this.hub.isConnected) {
				await this.loadDmMessages(this.activeDmChannelId);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this._setReactionPending(reactionKey, false);
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
		this.customEmojis = [];
		this.mobileNavOpen = false;
		this.loadFriends();
		this.loadFriendRequests();
		this.loadDmConversations();
	}

	/** Navigate back to a server view. */
	async selectServer(serverId: string): Promise<void> {
		this.showFriendsPanel = false;
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
		await this.loadCustomEmojis(serverId);
		this.loadServerPresence(serverId);
		this.loadCategories();
		this.loadNotificationPreferences();
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
		this.loadDmPresence();
	}

	async loadServerPresence(serverId: string): Promise<void> {
		if (!this.idToken) return;
		try {
			const entries = await this.api.getServerPresence(this.idToken, serverId);
			for (const entry of entries) {
				this.userPresence.set(entry.userId, entry.status);
			}
		} catch (e) {
			console.warn('Failed to load server presence:', e);
		}
	}

	async loadDmPresence(): Promise<void> {
		if (!this.idToken) return;
		try {
			const entries = await this.api.getDmPresence(this.idToken);
			for (const entry of entries) {
				this.userPresence.set(entry.userId, entry.status);
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

	/* ═══════════════════ Voice ═══════════════════ */

	async joinVoiceChannel(channelId: string): Promise<void> {
		if (this.isJoiningVoice) return;

		// Leave existing voice session first
		if (this.activeVoiceChannelId) {
			await this.leaveVoiceChannel();
		}

		this.isJoiningVoice = true;
		try {
			const members = await this.voice.join(channelId, this.hub, {
				onNewTrack: (pid, track) => {
					const userId = this._findUserIdByParticipant(pid);
					this._attachRemoteAudio(pid, userId, track);
				},
				onTrackEnded: (pid) => this._detachRemoteAudio(pid),
			});

			this.activeVoiceChannelId = channelId;
			this.isMuted = false;
			this.isDeafened = false;

			if (this.voiceInputMode === 'push-to-talk') {
				this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			}

			// Seed the local member map with what the server returned (excludes self),
			// then add ourselves so the UI always shows the joining user. The
			// onUserJoinedVoice callback may have already added us due to the hub
			// sending UserJoinedVoice to the caller before returning — merge rather
			// than overwrite to avoid erasing that entry.
			const memberMap = new Map(this.voiceChannelMembers);
			const existing = memberMap.get(channelId) ?? [];
			const merged = [...members];
			// Add any members already present from real-time events (e.g. self)
			for (const m of existing) {
				if (!merged.some((e) => e.participantId === m.participantId)) {
					merged.push(m);
				}
			}
			// Ensure self is always in the list
			if (this.me && !merged.some((m) => m.userId === this.me!.user.id)) {
				merged.push({
					userId: this.me.user.id,
					displayName: this.effectiveDisplayName,
					avatarUrl: this.me.user.avatarUrl ?? null,
					isMuted: false,
					isDeafened: false,
					participantId: '',
				});
			}
			memberMap.set(channelId, merged);
			this.voiceChannelMembers = memberMap;
		} catch (e) {
			console.error('[Voice] Failed to join voice channel:', e);

			// Clean up any partial join state on both server and client.
			try { await this.hub.leaveVoiceChannel(); } catch { /* ignore */ }
			await this.voice.leave();
			this._cleanupRemoteAudio();
			this._removePttListeners();
			this.isPttActive = false;
			this.activeVoiceChannelId = null;

			if (e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'PermissionDeniedError')) {
				const isSystemDenied = e.message?.includes('Permission denied by system');
				const message = isSystemDenied
					? 'Microphone access was denied by your operating system. On macOS, go to System Settings → Privacy & Security → Microphone and enable your browser.'
					: 'Microphone access is required to join a voice channel. Please allow microphone access in your browser and try again.';
				this.setError(new Error(message));
			} else if (e instanceof DOMException && e.name === 'NotFoundError') {
				this.setError(new Error('No microphone found. Please connect a microphone to join voice channels.'));
			} else {
				this.setError(e);
			}
		} finally {
			this.isJoiningVoice = false;
		}
	}

	async leaveVoiceChannel(): Promise<void> {
		if (!this.activeVoiceChannelId) return;
		const channelId = this.activeVoiceChannelId;

		try {
			await this.hub.leaveVoiceChannel();
		} catch {
			// ignore SignalR errors on leave
		}

		await this.voice.leave();
		this._cleanupRemoteAudio();
		this.activeVoiceChannelId = null;
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this._removePttListeners();

		// Remove self from the local members map
		if (this.me) {
			const memberMap = new Map(this.voiceChannelMembers);
			const currentMembers = memberMap.get(channelId) ?? [];
			memberMap.set(channelId, currentMembers.filter((m) => m.userId !== this.me!.user.id));
			this.voiceChannelMembers = memberMap;
		}
	}

	async toggleMute(): Promise<void> {
		this.isMuted = !this.isMuted;
		if (this.voiceInputMode === 'push-to-talk') {
			// In PTT mode, mute button toggles between "PTT active" and "fully muted"
			if (this.isMuted) {
				this._removePttListeners();
				this.voice.setMuted(true);
				this.isPttActive = false;
			} else {
				this.voice.setMuted(true); // still muted until PTT key held
				this._registerPttListeners();
			}
		} else {
			this.voice.setMuted(this.isMuted);
		}
		await this.hub.updateVoiceState(this.isMuted, this.isDeafened);
	}

	async toggleDeafen(): Promise<void> {
		this.isDeafened = !this.isDeafened;
		// Deafening also mutes the microphone
		if (this.isDeafened && !this.isMuted) {
			this.isMuted = true;
			this.voice.setMuted(true);
		} else if (!this.isDeafened && this.isMuted) {
			// Undeafening does not auto-unmute; keep current mute state
		}
		// Mute/unmute remote audio based on deafened state
		for (const [pid, nodes] of this.audioNodes) {
			const userId = this.audioParticipantUserMap.get(pid);
			nodes.gain.gain.value = this.isDeafened ? 0 : (this.userVolumes.get(userId ?? '') ?? 1.0);
		}
		await this.hub.updateVoiceState(this.isMuted, this.isDeafened);
	}

	private async _loadAllVoiceStates(): Promise<void> {
		if (!this.idToken) return;
		const voiceChannels = this.channels.filter((c) => c.type === 'voice');
		const memberMap = new Map(this.voiceChannelMembers);

		await Promise.all(
			voiceChannels.map(async (ch) => {
				try {
					const members = await this.api.getVoiceStates(this.idToken!, ch.id);
					memberMap.set(ch.id, members);
				} catch {
					// ignore individual failures
				}
			})
		);

		this.voiceChannelMembers = memberMap;
	}

	private _attachRemoteAudio(participantId: string, userId: string, track: MediaStreamTrack): void {
		this._detachRemoteAudio(participantId); // guard against re-attach
		if (!this.audioContext) {
			this.audioContext = new AudioContext();
		}
		if (this.audioContext.state === 'suspended') {
			this.audioContext.resume().catch(() => {});
		}

		// Chrome desktop requires the track to be consumed by an <audio> element
		// to activate the WebRTC decoding pipeline. We mute the element so it
		// doesn't bypass the Web Audio API gain chain (which controls volume &
		// deafen), then use createMediaStreamSource for gain-controlled output.
		const stream = new MediaStream([track]);
		const el = new Audio();
		el.srcObject = stream;
		el.muted = true;
		el.play().catch(() => {});

		const source = this.audioContext.createMediaStreamSource(stream);
		const gain = this.audioContext.createGain();
		const volume = this.isDeafened ? 0 : (this.userVolumes.get(userId) ?? 1.0);
		gain.gain.value = volume;
		source.connect(gain);
		gain.connect(this.audioContext.destination);
		this.audioNodes.set(participantId, { element: el, source, gain });
		this.audioParticipantUserMap.set(participantId, userId);
	}

	private _detachRemoteAudio(participantId: string): void {
		const nodes = this.audioNodes.get(participantId);
		if (nodes) {
			nodes.source.disconnect();
			nodes.gain.disconnect();
			nodes.element.pause();
			nodes.element.srcObject = null;
			this.audioNodes.delete(participantId);
		}
		this.audioParticipantUserMap.delete(participantId);
	}

	private _cleanupRemoteAudio(): void {
		for (const nodes of this.audioNodes.values()) {
			nodes.source.disconnect();
			nodes.gain.disconnect();
			nodes.element.pause();
			nodes.element.srcObject = null;
		}
		this.audioNodes.clear();
		this.audioParticipantUserMap.clear();
	}

	private _findUserIdByParticipant(participantId: string): string {
		if (!this.activeVoiceChannelId) return '';
		const members = this.voiceChannelMembers.get(this.activeVoiceChannelId) ?? [];
		return members.find((m) => m.participantId === participantId)?.userId ?? '';
	}

	private _loadUserVolumes(): void {
		try {
			const raw = localStorage.getItem('codec-user-volumes');
			if (raw) {
				const parsed = JSON.parse(raw) as Record<string, number>;
				this.userVolumes = new Map(Object.entries(parsed));
			}
		} catch {
			// ignore corrupt data
		}
	}

	private _saveUserVolumes(): void {
		const obj: Record<string, number> = {};
		for (const [k, v] of this.userVolumes) {
			obj[k] = v;
		}
		localStorage.setItem('codec-user-volumes', JSON.stringify(obj));
	}

	private _loadVoicePreferences(): void {
		try {
			const raw = localStorage.getItem('codec-voice-preferences');
			if (raw) {
				const parsed = JSON.parse(raw) as { inputMode?: string; pttKey?: string };
				if (parsed.inputMode === 'voice-activity' || parsed.inputMode === 'push-to-talk') {
					this.voiceInputMode = parsed.inputMode;
				}
				if (parsed.pttKey) {
					this.pttKey = parsed.pttKey;
				}
			}
		} catch {
			// ignore corrupt data
		}
	}

	private _saveVoicePreferences(): void {
		localStorage.setItem('codec-voice-preferences', JSON.stringify({
			inputMode: this.voiceInputMode,
			pttKey: this.pttKey,
		}));
	}

	private _registerPttListeners(): void {
		this._removePttListeners();

		this.pttKeydownHandler = (e: KeyboardEvent) => {
			if (e.code !== this.pttKey || e.repeat) return;
			// Don't activate PTT while typing in text fields
			const tag = (document.activeElement as HTMLElement)?.tagName;
			if (tag === 'INPUT' || tag === 'TEXTAREA' || (document.activeElement as HTMLElement)?.isContentEditable) return;

			this.isPttActive = true;
			this.voice.setMuted(false);
		};

		this.pttKeyupHandler = (e: KeyboardEvent) => {
			if (e.code !== this.pttKey) return;

			this.isPttActive = false;
			this.voice.setMuted(true);
		};

		window.addEventListener('keydown', this.pttKeydownHandler);
		window.addEventListener('keyup', this.pttKeyupHandler);
	}

	private _removePttListeners(): void {
		if (this.pttKeydownHandler) {
			window.removeEventListener('keydown', this.pttKeydownHandler);
			this.pttKeydownHandler = null;
		}
		if (this.pttKeyupHandler) {
			window.removeEventListener('keyup', this.pttKeyupHandler);
			this.pttKeyupHandler = null;
		}
	}

	setUserVolume(userId: string, volume: number): void {
		const clamped = Math.max(0, Math.min(1, volume));
		const updated = new Map(this.userVolumes);
		if (clamped === 1.0) {
			updated.delete(userId); // default = no entry
		} else {
			updated.set(userId, clamped);
		}
		this.userVolumes = updated;
		this._saveUserVolumes();

		// Apply to any active audio node for this user
		if (!this.isDeafened) {
			for (const [pid, uid] of this.audioParticipantUserMap) {
				if (uid === userId) {
					const nodes = this.audioNodes.get(pid);
					if (nodes) nodes.gain.gain.value = clamped;
				}
			}
		}
	}

	resetUserVolume(userId: string): void {
		this.setUserVolume(userId, 1.0);
	}

	setVoiceInputMode(mode: 'voice-activity' | 'push-to-talk'): void {
		this.voiceInputMode = mode;
		this._saveVoicePreferences();

		if (this.activeVoiceChannelId) {
			if (mode === 'push-to-talk') {
				// Switch to PTT: pause producer, register listeners
				this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			} else {
				// Switch to VA: resume producer (if not manually muted), remove listeners
				this._removePttListeners();
				this.isPttActive = false;
				if (!this.isMuted) {
					this.voice.setMuted(false);
				}
			}
		}
	}

	setPttKey(code: string): void {
		this.pttKey = code;
		this._saveVoicePreferences();
	}

	/* ═══════════════════ DM Voice Calls ═══════════════════ */

	async startCall(dmChannelId: string): Promise<void> {
		if (this.activeCall || this.incomingCall) return;

		// Leave any active server voice channel.
		if (this.activeVoiceChannelId) {
			await this.leaveVoiceChannel();
		}

		try {
			const result = await this.hub.startCall(dmChannelId);
			this.activeCall = {
				callId: result.callId,
				dmChannelId,
				otherUserId: result.recipientUserId,
				otherDisplayName: result.recipientDisplayName,
				otherAvatarUrl: result.recipientAvatarUrl,
				status: 'ringing',
				startedAt: new Date().toISOString(),
			};
		} catch (e) {
			this.setError(e);
		}
	}

	async acceptCall(callId: string): Promise<void> {
		if (!this.incomingCall || this.incomingCall.callId !== callId) return;

		// Leave any active server voice channel.
		if (this.activeVoiceChannelId) {
			await this.leaveVoiceChannel();
		}

		const caller = this.incomingCall;
		this.incomingCall = null;

		try {
			const result = await this.hub.acceptCall(callId);

			if ('alreadyHandled' in result && result.alreadyHandled) return;

			this.activeCall = {
				callId,
				dmChannelId: caller.dmChannelId,
				otherUserId: caller.callerUserId,
				otherDisplayName: caller.callerDisplayName,
				otherAvatarUrl: caller.callerAvatarUrl,
				status: 'active',
				startedAt: new Date().toISOString(),
				answeredAt: new Date().toISOString(),
			};

			// Set up WebRTC with the transport options from AcceptCall.
			await this.voice.joinWithOptions(
				{
					routerRtpCapabilities: result.routerRtpCapabilities,
					sendTransportOptions: result.sendTransportOptions,
					recvTransportOptions: result.recvTransportOptions,
					iceServers: result.iceServers as any,
				},
				this.hub,
				{
					onNewTrack: (pid, track) => {
						this._attachRemoteAudio(pid, caller.callerUserId, track);
					},
					onTrackEnded: (pid) => this._detachRemoteAudio(pid),
				}
			);

			this.isMuted = false;
			this.isDeafened = false;

			if (this.voiceInputMode === 'push-to-talk') {
				this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			}
		} catch (e) {
			console.error('[Voice] Failed to accept call:', e);
			this.activeCall = null;
			try { await this.hub.endCall(); } catch { /* ignore */ }
			await this.voice.leave();
			this._cleanupRemoteAudio();
			this.setError(e);
		}
	}

	async declineCall(callId: string): Promise<void> {
		if (!this.incomingCall || this.incomingCall.callId !== callId) return;
		this.incomingCall = null;

		try {
			await this.hub.declineCall(callId);
		} catch {
			// ignore
		}
	}

	async endCall(): Promise<void> {
		if (!this.activeCall) return;
		this.activeCall = null;

		try {
			await this.hub.endCall();
		} catch {
			// ignore
		}

		await this.voice.leave();
		this._cleanupRemoteAudio();
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this._removePttListeners();
	}

	async checkActiveCall(): Promise<void> {
		if (!this.idToken) return;
		const call = await this.api.getActiveCall(this.idToken);
		if (!call) return;

		if (call.status === 'ringing') {
			if (call.callerUserId === this.me?.user.id) {
				// We initiated the call — restore ringing state.
				this.activeCall = {
					callId: call.id,
					dmChannelId: call.dmChannelId,
					otherUserId: call.otherUserId,
					otherDisplayName: call.otherDisplayName,
					otherAvatarUrl: call.otherAvatarUrl,
					status: 'ringing',
					startedAt: call.startedAt,
				};
			} else {
				// We are the recipient — show incoming call.
				this.incomingCall = {
					callId: call.id,
					dmChannelId: call.dmChannelId,
					callerUserId: call.otherUserId,
					callerDisplayName: call.otherDisplayName,
					callerAvatarUrl: call.otherAvatarUrl,
				};
			}
		}
		// Active calls after refresh can't be rejoined without re-establishing WebRTC,
		// so just end them cleanly.
		if (call.status === 'active') {
			try { await this.hub.endCall(); } catch { /* ignore */ }
		}
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
				// Tear down voice — SignalR group membership is lost, so the voice
				// session cannot recover without a full re-join.
				if (this.activeVoiceChannelId || this.activeCall) {
					this.voice.leave();
					this._cleanupRemoteAudio();
					this._removePttListeners();
					this.activeVoiceChannelId = null;
					this.activeCall = null;
					this.incomingCall = null;
					this.isMuted = false;
					this.isDeafened = false;
					this.isPttActive = false;
					this.setTransientError('Voice disconnected due to network interruption.');
				}
				if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
				this.reconnectTimer = setTimeout(() => {
					if (!this.isHubConnected) window.location.reload();
				}, 5000);
			},
			onReconnected: async () => {
				this.isHubConnected = true;
				if (this.reconnectTimer) {
					clearTimeout(this.reconnectTimer);
					this.reconnectTimer = null;
				}

				// Re-join SignalR groups lost during disconnection.
				if (this.selectedServerId) {
					await this.hub.joinServer(this.selectedServerId).catch(() => {});
				}
				if (this.selectedChannelId) {
					await this.hub.joinChannel(this.selectedChannelId).catch(() => {});
					// Reload messages to catch anything missed while disconnected.
					await this.loadMessages(this.selectedChannelId).catch(() => {});
				}
				if (this.activeDmChannelId) {
					await this.hub.joinDmChannel(this.activeDmChannelId).catch(() => {});
				}
			},
			onClose: (error) => {
				this.isHubConnected = false;
				if (this.reconnectTimer) {
					clearTimeout(this.reconnectTimer);
					this.reconnectTimer = null;
				}
				if (this.activeVoiceChannelId) {
					this.voice.leave();
					this._cleanupRemoteAudio();
					this._removePttListeners();
					this.activeVoiceChannelId = null;
					this.isMuted = false;
					this.isDeafened = false;
					this.isPttActive = false;
				}
				if (error) window.location.reload();
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
				if (this._matchAndRemoveReactionSnapshot(update.messageId, update.reactions)) {
					return;
				}
				if (update.channelId === this.selectedChannelId) {
					this._updateMessageReactions(update.messageId, update.reactions);
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
				const mapCopy = new Map(this.channelServerMap);
				mapCopy.set(event.channelId, event.serverId);
				this.channelServerMap = mapCopy;

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
			onMemberRoleChanged: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.loadMembers(event.serverId);
				}
				// Update the caller's own role in the servers list if they were promoted/demoted
				if (event.userId === this.me?.user.id) {
					this.servers = this.servers.map((s) =>
						s.serverId === event.serverId ? { ...s, role: event.newRole } : s
					);
				}
			},
			onMessageDeleted: (event) => {
				if (event.channelId === this.selectedChannelId) {
					this.messages = this.messages.filter((m) => m.id !== event.messageId);
					this.pinnedMessages = this.pinnedMessages.filter((p) => p.messageId !== event.messageId);
				}
			},
			onChannelPurged: (event) => {
				if (event.channelId === this.selectedChannelId) {
					this.messages = [];
					this.hasMoreMessages = false;
					this.pinnedMessages = [];
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
			onDmReactionUpdated: (update) => {
				if (this._matchAndRemoveReactionSnapshot(update.messageId, update.reactions)) {
					return;
				}
				if (update.dmChannelId === this.activeDmChannelId) {
					this._updateDmMessageReactions(update.messageId, update.reactions);
				}
			},
			onServerNameChanged: (event) => {
				// Update server name in the local list
				this.servers = this.servers.map((s) =>
					s.serverId === event.serverId ? { ...s, name: event.name } : s
				);
			},
			onServerIconChanged: (event) => {
				// Update server icon in the local list
				this.servers = this.servers.map((s) =>
					s.serverId === event.serverId ? { ...s, iconUrl: event.iconUrl } : s
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
					this.serverSettingsOpen = false;
					this.goHome();
				}
			},
			onChannelDeleted: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.channels = this.channels.filter((c) => c.id !== event.channelId);
					if (this.selectedChannelId === event.channelId) {
						const next = this.channels.find((c) => c.type !== 'voice');
						this.selectedChannelId = next?.id ?? null;
						this.messages = [];
					}
				}
			},
			onUserJoinedVoice: (event) => {
				const memberMap = new Map(this.voiceChannelMembers);
				const members = [...(memberMap.get(event.channelId) ?? [])];
				if (!members.some((m) => m.participantId === event.participantId)) {
					members.push({
						userId: event.userId,
						displayName: event.displayName,
						avatarUrl: event.avatarUrl ?? null,
						isMuted: false,
						isDeafened: false,
						participantId: event.participantId,
					});
					memberMap.set(event.channelId, members);
					this.voiceChannelMembers = memberMap;
				}
			},
			onUserLeftVoice: (event) => {
				const memberMap = new Map(this.voiceChannelMembers);
				const members = (memberMap.get(event.channelId) ?? []).filter(
					(m) => m.participantId !== event.participantId
				);
				memberMap.set(event.channelId, members);
				this.voiceChannelMembers = memberMap;
			},
			onVoiceStateUpdated: (event) => {
				const memberMap = new Map(this.voiceChannelMembers);
				const members = (memberMap.get(event.channelId) ?? []).map((m) =>
					m.userId === event.userId
						? { ...m, isMuted: event.isMuted, isDeafened: event.isDeafened }
						: m
				);
				memberMap.set(event.channelId, members);
				this.voiceChannelMembers = memberMap;
			},
			onUserPresenceChanged: (event) => {
				if (event.status === 'offline') {
					this.userPresence.delete(event.userId);
				} else {
					this.userPresence.set(event.userId, event.status);
				}
			},
			onUserStatusChanged: (event) => {
				// Update member list with new status
				this.members = this.members.map((m) =>
					m.userId === event.userId
						? { ...m, statusText: event.statusText, statusEmoji: event.statusEmoji }
						: m
				);
				// Update own profile if it's our status
				if (this.me && event.userId === this.me.user.id) {
					this.me = {
						...this.me,
						user: {
							...this.me.user,
							statusText: event.statusText,
							statusEmoji: event.statusEmoji
						}
					};
				}
			},
			onNewProducer: async (event) => {
				// Handle both server voice channels and DM calls.
				const inVoiceChannel = this.activeVoiceChannelId === event.channelId;
				const inDmCall = this.activeCall?.status === 'active';
				if (inVoiceChannel || inDmCall) {
					await this.voice.consumeProducer(event.producerId, event.participantId, this.hub, {
						onNewTrack: (pid, track) => this._attachRemoteAudio(pid, event.userId, track),
						onTrackEnded: (pid) => this._detachRemoteAudio(pid),
					});
				}
			},
			onIncomingCall: (event) => {
				// Don't show incoming call if we already have one or are in a call.
				if (this.activeCall || this.incomingCall) return;
				this.incomingCall = event;
			},
			onCallAccepted: async (event) => {
				if (!this.activeCall || this.activeCall.callId !== event.callId) return;

				try {
					// Caller: set up our transports now.
					const transportResult = await this.hub.setupCallTransports(event.callId);

					this.activeCall = { ...this.activeCall, status: 'active', answeredAt: new Date().toISOString() };

					await this.voice.joinWithOptions(
						{
							routerRtpCapabilities: transportResult.routerRtpCapabilities,
							sendTransportOptions: transportResult.sendTransportOptions,
							recvTransportOptions: transportResult.recvTransportOptions,
							members: transportResult.members as any,
							iceServers: transportResult.iceServers as any,
						},
						this.hub,
						{
							onNewTrack: (pid, track) => {
								this._attachRemoteAudio(pid, this.activeCall!.otherUserId, track);
							},
							onTrackEnded: (pid) => this._detachRemoteAudio(pid),
						}
					);

					this.isMuted = false;
					this.isDeafened = false;

					if (this.voiceInputMode === 'push-to-talk') {
						this.voice.setMuted(true);
						this.isPttActive = false;
						this._registerPttListeners();
					}
				} catch (e) {
					console.error('[Voice] Failed to set up call as caller:', e);
					this.activeCall = null;
					await this.voice.leave();
					this._cleanupRemoteAudio();
					this.setError(e);
				}
			},
			onCallDeclined: (event) => {
				if (this.activeCall?.callId === event.callId) {
					this.activeCall = null;
				}
			},
			onCallEnded: (event) => {
				if (this.activeCall?.callId === event.callId) {
					this.activeCall = null;
					this.voice.leave();
					this._cleanupRemoteAudio();
					this.isMuted = false;
					this.isDeafened = false;
					this.isPttActive = false;
					this._removePttListeners();
				}
			},
			onCallMissed: (event) => {
				if (this.activeCall?.callId === event.callId) {
					this.activeCall = null;
				}
				if (this.incomingCall?.callId === event.callId) {
					this.incomingCall = null;
				}
			},
			onCustomEmojiAdded: (event) => {
				if (this.selectedServerId === event.serverId) {
					if (!this.customEmojis.some(e => e.id === event.emoji.id)) {
						this.customEmojis = [...this.customEmojis, event.emoji];
					}
				}
			},
			onCustomEmojiUpdated: (event) => {
				this.customEmojis = this.customEmojis.map(e =>
					e.id === event.emojiId ? { ...e, name: event.name } : e
				);
			},
			onCustomEmojiDeleted: (event) => {
				this.customEmojis = this.customEmojis.filter(e => e.id !== event.emojiId);
			},
			onServerDescriptionChanged: (event) => {
				this.servers = this.servers.map((s) =>
					s.serverId === event.serverId ? { ...s, description: event.description } : s
				);
			},
			onChannelDescriptionChanged: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.channels = this.channels.map((c) =>
						c.id === event.channelId ? { ...c, description: event.description } : c
					);
				}
			},
			onMessagePinned: (event: MessagePinnedEvent) => {
				if (event.channelId === this.selectedChannelId) {
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
			},
			onMessageUnpinned: (event: MessageUnpinnedEvent) => {
				if (event.channelId === this.selectedChannelId) {
					this.pinnedMessages = this.pinnedMessages.filter((p) => p.messageId !== event.messageId);
				}
			},
			onCategoryCreated: (event) => {
				if (event.serverId === this.selectedServerId) {
					if (!this.categories.some((c) => c.id === event.categoryId)) {
						this.categories = [...this.categories, {
							id: event.categoryId,
							serverId: event.serverId,
							name: event.name,
							position: event.position
						}];
					}
				}
			},
			onCategoryRenamed: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.categories = this.categories.map((c) =>
						c.id === event.categoryId ? { ...c, name: event.name } : c
					);
				}
			},
			onCategoryDeleted: (event) => {
				if (event.serverId === this.selectedServerId) {
					this.categories = this.categories.filter((c) => c.id !== event.categoryId);
					this.channels = this.channels.map((ch) =>
						ch.categoryId === event.categoryId ? { ...ch, categoryId: undefined } : ch
					);
				}
			},
			onChannelOrderChanged: async (event) => {
				if (event.serverId === this.selectedServerId) {
					await this.loadChannels(event.serverId).catch(() => {});
					await this.loadCategories().catch(() => {});
				}
			},
			onCategoryOrderChanged: async (event) => {
				if (event.serverId === this.selectedServerId) {
					await this.loadCategories().catch(() => {});
				}
			},
		});

		this.isHubConnected = this.hub.isConnected;

		if (this.selectedChannelId) {
			await this.hub.joinChannel(this.selectedChannelId);
		}
		if (this.activeDmChannelId) {
			await this.hub.joinDmChannel(this.activeDmChannelId);
		}

		// Check for active call on initial connect.
		await this.checkActiveCall();
	}

	/** Synchronous voice cleanup for beforeunload (stops mic tracks immediately). */
	teardownVoiceSync(): void {
		this.voice.teardownSync();
		this._cleanupRemoteAudio();
		this._removePttListeners();
		this.activeCall = null;
		this.incomingCall = null;
		this.audioContext?.close().catch(() => {});
		this.audioContext = null;
	}

	async destroy(): Promise<void> {
		if (this.activeCall) {
			await this.endCall();
		}
		if (this.activeVoiceChannelId) {
			await this.leaveVoiceChannel();
		}
		this.audioContext?.close().catch(() => {});
		this.audioContext = null;
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

	/* ───── Search ───── */

	toggleSearch(): void {
		this.isSearchOpen = !this.isSearchOpen;
		if (!this.isSearchOpen) {
			this.searchQuery = '';
			this.searchFilters = {};
			this.searchResults = null;
		}
	}

	async searchMessages(query: string, filters: SearchFilters = {}): Promise<void> {
		if (!this.idToken || query.trim().length < 2) {
			this.searchResults = null;
			return;
		}

		this.searchQuery = query;
		this.searchFilters = filters;
		this.isSearching = true;

		try {
			if (this.selectedServerId) {
				this.searchResults = await this.api.searchServerMessages(
					this.idToken,
					this.selectedServerId,
					query,
					filters
				);
			} else if (this.activeDmChannelId) {
				this.searchResults = await this.api.searchDmMessages(
					this.idToken,
					query,
					filters
				);
			}
		} catch (e) {
			this.setError(e);
		} finally {
			this.isSearching = false;
		}
	}

	async searchPage(page: number): Promise<void> {
		await this.searchMessages(this.searchQuery, { ...this.searchFilters, page });
	}

	async jumpToMessage(messageId: string, channelId: string, isDm: boolean): Promise<void> {
		if (!this.idToken) return;

		try {
			// Switch to the correct view if needed
			if (isDm) {
				if (this.activeDmChannelId !== channelId) {
					await this.selectDmConversation(channelId);
				}
			} else {
				if (this.selectedChannelId !== channelId) {
					await this.selectChannel(channelId);
				}
			}

			// Fetch the around-window for the target message
			const result = isDm
				? await this.api.getDmMessagesAround(this.idToken, channelId, messageId)
				: await this.api.getMessagesAround(this.idToken, channelId, messageId);

			// Replace messages with the around-window
			if (isDm) {
				this.dmMessages = result.messages as unknown as DirectMessage[];
			} else {
				this.messages = result.messages;
				this.hasMoreMessages = result.hasMoreBefore;
			}

			// Highlight the target message
			this.highlightedMessageId = messageId;
			setTimeout(() => { this.highlightedMessageId = null; }, 2000);
		} catch (e) {
			this.setError(e);
		}
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
