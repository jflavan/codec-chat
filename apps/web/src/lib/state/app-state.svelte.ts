import { getContext, setContext } from 'svelte';
import { tick } from 'svelte';
import type {
	MemberServer,
	DiscoverServer,
	Channel,
	Message,
	Member,
	UserProfile
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
import { initGoogleIdentity, renderGoogleButton } from '$lib/auth/google.js';

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
	discoverServers = $state<DiscoverServer[]>([]);
	channels = $state<Channel[]>([]);
	messages = $state<Message[]>([]);
	members = $state<Member[]>([]);

	/* ───── selection ───── */
	selectedServerId = $state<string | null>(null);
	selectedChannelId = $state<string | null>(null);

	/* ───── auth ───── */
	idToken = $state<string | null>(null);
	me = $state<UserProfile | null>(null);
	status = $state('Signed out');
	error = $state<string | null>(null);

	/* ───── loading flags ───── */
	isLoadingServers = $state(false);
	isLoadingDiscover = $state(false);
	isLoadingChannels = $state(false);
	isLoadingMessages = $state(false);
	isLoadingMe = $state(false);
	isLoadingMembers = $state(false);
	isSending = $state(false);
	isJoining = $state(false);
	isCreatingServer = $state(false);
	isCreatingChannel = $state(false);
	isUploadingAvatar = $state(false);

	/* ───── UI toggles ───── */
	showCreateServer = $state(false);
	showCreateChannel = $state(false);

	/* ───── form fields ───── */
	newServerName = $state('');
	newChannelName = $state('');
	messageBody = $state('');

	/* ───── real-time ───── */
	typingUsers = $state<string[]>([]);

	/* ───── derived ───── */
	readonly isSignedIn = $derived(Boolean(this.idToken));

	readonly currentServerRole = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.role ?? null
	);

	readonly canManageChannels = $derived(
		this.currentServerRole === 'Owner' || this.currentServerRole === 'Admin'
	);

	readonly selectedServerName = $derived(
		this.servers.find((s) => s.serverId === this.selectedServerId)?.name ?? 'Codec'
	);

	readonly selectedChannelName = $derived(
		this.channels.find((c) => c.id === this.selectedChannelId)?.name ?? null
	);

	/* ───── internals ───── */
	private api: ApiClient;
	private hub: ChatHubService;

	constructor(
		private readonly apiBaseUrl: string,
		private readonly googleClientId: string
	) {
		this.api = new ApiClient(apiBaseUrl);
		this.hub = new ChatHubService(`${apiBaseUrl}/hubs/chat`);
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
		this.renderSignIn();
	}

	private renderSignIn(): void {
		initGoogleIdentity(this.googleClientId, (token) => this.handleCredential(token), {
			renderButtonId: 'google-button',
			autoSelect: true
		});
	}

	handleCredential(token: string): void {
		this.idToken = token;
		this.status = 'Signed in';
		persistToken(token);
		this.loadMe();
		this.loadServers();
		this.loadDiscoverServers();
		this.startSignalR(token);
	}

	async signOut(): Promise<void> {
		await this.hub.stop();
		clearStoredSession();

		this.idToken = null;
		this.me = null;
		this.status = 'Signed out';
		this.servers = [];
		this.discoverServers = [];
		this.channels = [];
		this.messages = [];
		this.members = [];
		this.selectedServerId = null;
		this.selectedChannelId = null;
		this.typingUsers = [];
		this.error = null;

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

	async loadDiscoverServers(): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingDiscover = true;
		try {
			this.discoverServers = await this.api.getDiscoverServers(this.idToken);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingDiscover = false;
		}
	}

	async loadChannels(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isLoadingChannels = true;
		try {
			this.channels = await this.api.getChannels(this.idToken, serverId);
			const previousChannelId = this.selectedChannelId;
			this.selectedChannelId = this.channels[0]?.id ?? null;

			if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
			if (this.selectedChannelId) await this.hub.joinChannel(this.selectedChannelId);

			if (this.selectedChannelId) {
				await this.loadMessages(this.selectedChannelId);
			} else {
				this.messages = [];
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
			this.messages = await this.api.getMessages(this.idToken, channelId);
		} catch (e) {
			this.setError(e);
		} finally {
			this.isLoadingMessages = false;
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

	async selectServer(serverId: string): Promise<void> {
		this.selectedServerId = serverId;
		await this.loadChannels(serverId);
		await this.loadMembers(serverId);
	}

	async selectChannel(channelId: string): Promise<void> {
		const previousChannelId = this.selectedChannelId;
		this.selectedChannelId = channelId;
		this.typingUsers = [];

		if (previousChannelId) await this.hub.leaveChannel(previousChannelId);
		await this.hub.joinChannel(channelId);
		await this.loadMessages(channelId);
	}

	async sendMessage(): Promise<void> {
		if (!this.idToken || !this.selectedChannelId) return;

		const body = this.messageBody.trim();
		if (!body) {
			this.error = 'Message body is required.';
			return;
		}

		this.isSending = true;
		try {
			await this.api.sendMessage(this.idToken, this.selectedChannelId, body);
			this.messageBody = '';

			if (this.me) {
				this.hub.clearTyping(this.selectedChannelId, this.me.user.displayName);
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

	async joinServer(serverId: string): Promise<void> {
		if (!this.idToken) return;
		this.isJoining = true;
		try {
			await this.api.joinServer(this.idToken, serverId);
			await this.loadServers();
			await this.loadDiscoverServers();
		} catch (e) {
			this.setError(e);
		} finally {
			this.isJoining = false;
		}
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
			await this.loadDiscoverServers();
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
		this.hub.emitTyping(this.selectedChannelId, this.me.user.displayName);
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

	/* ═══════════════════ SignalR ═══════════════════ */

	private async startSignalR(token: string): Promise<void> {
		await this.hub.start(token, {
			onMessage: (msg) => {
				if (msg.channelId === this.selectedChannelId) {
					if (!this.messages.some((m) => m.id === msg.id)) {
						this.messages = [...this.messages, msg];
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
			}
		});

		if (this.selectedChannelId) {
			await this.hub.joinChannel(this.selectedChannelId);
		}
	}

	async destroy(): Promise<void> {
		await this.hub.stop();
	}

	/* ───── helpers ───── */

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
