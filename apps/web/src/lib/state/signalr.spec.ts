import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ChatHubService, SignalRCallbacks } from '$lib/services/chat-hub.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { ServerStore } from './server-store.svelte.js';
import type { ChannelStore } from './channel-store.svelte.js';
import type { MessageStore } from './message-store.svelte.js';
import type { DmStore } from './dm-store.svelte.js';
import type { FriendStore } from './friend-store.svelte.js';
import type { VoiceStore } from './voice-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

// Mock isTokenExpired so we control its behavior
vi.mock('$lib/auth/session.js', () => ({
	isTokenExpired: vi.fn().mockReturnValue(false),
}));

// Mock navigation — we don't need goHome in these tests but the module imports it
vi.mock('./navigation.svelte.js', () => ({
	goHome: vi.fn(),
}));

import { setupSignalR } from './signalr.svelte.js';
import { isTokenExpired } from '$lib/auth/session.js';

// Captures the callbacks passed to hub.start so we can invoke them in tests
let capturedCallbacks: SignalRCallbacks;
let capturedTokenFactory: () => string | Promise<string>;

function createMockHub(overrides: Partial<ChatHubService> = {}): ChatHubService {
	return {
		start: vi.fn().mockImplementation(async (tokenFactory: () => string | Promise<string>, callbacks: SignalRCallbacks) => {
			capturedTokenFactory = tokenFactory;
			capturedCallbacks = callbacks;
		}),
		stop: vi.fn().mockResolvedValue(undefined),
		isConnected: true,
		joinChannel: vi.fn().mockResolvedValue(undefined),
		joinDmChannel: vi.fn().mockResolvedValue(undefined),
		joinServer: vi.fn().mockResolvedValue(undefined),
		leaveDmChannel: vi.fn().mockResolvedValue(undefined),
		leaveServer: vi.fn().mockResolvedValue(undefined),
		...overrides,
	} as unknown as ChatHubService;
}

function createMockAuth(): AuthStore {
	return {
		idToken: 'valid-token',
		refreshToken: vi.fn().mockResolvedValue('refreshed-token'),
		signOut: vi.fn(),
	} as unknown as AuthStore;
}

function createMockServers(): ServerStore {
	return {
		selectedServerId: null,
		servers: [],
		members: [],
		customEmojis: [],
		loadMembers: vi.fn().mockResolvedValue(undefined),
		loadCustomEmojis: vi.fn().mockResolvedValue(undefined),
		loadServerPresence: vi.fn(),
		loadCategories: vi.fn().mockResolvedValue(undefined),
		loadNotificationPreferences: vi.fn().mockResolvedValue(undefined),
		handleMemberBanned: vi.fn(),
		handleMemberUnbanned: vi.fn(),
		handleMemberJoined: vi.fn(),
		handleMemberLeft: vi.fn(),
		handleMemberRoleChanged: vi.fn(),
		handleServerNameChanged: vi.fn(),
		handleServerIconChanged: vi.fn(),
		handleServerDeleted: vi.fn(),
		handleServerDescriptionChanged: vi.fn(),
		handleUserStatusChanged: vi.fn(),
		handleCategoryCreated: vi.fn(),
		handleCategoryRenamed: vi.fn(),
		handleCategoryDeleted: vi.fn(),
		handleCategoryOrderChanged: vi.fn(),
		handleCustomEmojiAdded: vi.fn(),
		handleCustomEmojiUpdated: vi.fn(),
		handleCustomEmojiDeleted: vi.fn(),
	} as unknown as ServerStore;
}

function createMockChannels(): ChannelStore {
	return {
		selectedChannelId: null,
		channels: [],
		loadChannels: vi.fn().mockResolvedValue(undefined),
		handleChannelNameChanged: vi.fn(),
		handleChannelDeleted: vi.fn(),
		handleChannelDescriptionChanged: vi.fn(),
		handleChannelOrderChanged: vi.fn().mockResolvedValue(undefined),
		handleChannelOverrideUpdated: vi.fn().mockResolvedValue(undefined),
		handleMentionReceived: vi.fn(),
	} as unknown as ChannelStore;
}

function createMockMessages(): MessageStore {
	return {
		messages: [],
		hasMoreMessages: false,
		loadMessages: vi.fn().mockResolvedValue(undefined),
		handleIncomingMessage: vi.fn(),
		handleTyping: vi.fn(),
		handleStoppedTyping: vi.fn(),
		handleReactionUpdate: vi.fn(),
		handleMessageDeleted: vi.fn(),
		handleChannelPurged: vi.fn(),
		handleMessageEdited: vi.fn(),
		handleMessagePinned: vi.fn(),
		handleMessageUnpinned: vi.fn(),
		patchLinkPreviews: vi.fn(),
	} as unknown as MessageStore;
}

function createMockDms(): DmStore {
	return {
		activeDmChannelId: null,
		dmMessages: [],
		dmTypingUsers: [],
		loadDmConversations: vi.fn().mockResolvedValue(undefined),
		handleIncomingDm: vi.fn(),
		handleDmTyping: vi.fn(),
		handleDmStoppedTyping: vi.fn(),
		handleDmMessageDeleted: vi.fn(),
		handleDmMessageEdited: vi.fn(),
		handleDmReactionUpdate: vi.fn(),
		patchDmLinkPreviews: vi.fn(),
	} as unknown as DmStore;
}

function createMockFriends(): FriendStore {
	return {
		loadFriends: vi.fn().mockResolvedValue(undefined),
		loadFriendRequests: vi.fn().mockResolvedValue(undefined),
	} as unknown as FriendStore;
}

function createMockVoice(): VoiceStore {
	return {
		activeVoiceChannelId: null,
		activeCall: null,
		teardownOnDisconnect: vi.fn(),
		handleUserJoinedVoice: vi.fn(),
		handleUserLeftVoice: vi.fn(),
		handleVoiceStateUpdated: vi.fn(),
		handleIncomingCall: vi.fn(),
		handleCallAccepted: vi.fn().mockResolvedValue(undefined),
		handleCallDeclined: vi.fn(),
		handleCallEnded: vi.fn(),
		handleCallMissed: vi.fn(),
		checkActiveCall: vi.fn().mockResolvedValue(undefined),
	} as unknown as VoiceStore;
}

function createMockUI(): UIStore {
	return {
		isHubConnected: false,
		showFriendsPanel: false,
		userPresence: new Map<string, string>(),
		setTransientError: vi.fn(),
	} as unknown as UIStore;
}

describe('setupSignalR', () => {
	let hub: ChatHubService;
	let auth: AuthStore;
	let servers: ServerStore;
	let channels: ChannelStore;
	let messages: MessageStore;
	let dms: DmStore;
	let friends: FriendStore;
	let voice: VoiceStore;
	let ui: UIStore;

	beforeEach(() => {
		vi.clearAllMocks();
		hub = createMockHub();
		auth = createMockAuth();
		servers = createMockServers();
		channels = createMockChannels();
		messages = createMockMessages();
		dms = createMockDms();
		friends = createMockFriends();
		voice = createMockVoice();
		ui = createMockUI();
	});

	it('calls hub.start with a token factory and callbacks', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(hub.start).toHaveBeenCalledOnce();
		expect(typeof capturedTokenFactory).toBe('function');
		expect(capturedCallbacks).toBeDefined();
	});

	it('sets isHubConnected after start', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(ui.isHubConnected).toBe(true);
	});

	it('checks for active call after connecting', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(voice.checkActiveCall).toHaveBeenCalledOnce();
	});

	describe('token factory', () => {
		it('returns existing token when not expired', async () => {
			vi.mocked(isTokenExpired).mockReturnValue(false);
			auth.idToken = 'my-token';

			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();
			expect(token).toBe('my-token');
		});

		it('refreshes token when current token is expired', async () => {
			vi.mocked(isTokenExpired).mockReturnValue(true);
			auth.idToken = 'expired-token';

			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();
			expect(auth.refreshToken).toHaveBeenCalledOnce();
			expect(token).toBe('refreshed-token');
		});

		it('returns empty string when refresh returns null', async () => {
			vi.mocked(isTokenExpired).mockReturnValue(true);
			auth.idToken = 'expired-token';
			vi.mocked(auth.refreshToken).mockResolvedValue(null as any);

			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();
			expect(token).toBe('');
		});

		it('refreshes token when idToken is null', async () => {
			auth.idToken = null as any;

			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();
			expect(auth.refreshToken).toHaveBeenCalledOnce();
		});
	});

	describe('post-start channel/DM joins', () => {
		it('joins selected channel if one is active', async () => {
			channels.selectedChannelId = 'ch-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			expect(hub.joinChannel).toHaveBeenCalledWith('ch-1');
		});

		it('does not join channel when none selected', async () => {
			channels.selectedChannelId = null;
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			expect(hub.joinChannel).not.toHaveBeenCalled();
		});

		it('joins active DM channel if one is active', async () => {
			dms.activeDmChannelId = 'dm-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-1');
		});

		it('does not join DM channel when none active', async () => {
			dms.activeDmChannelId = null;
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			expect(hub.joinDmChannel).not.toHaveBeenCalled();
		});
	});

	describe('onReconnecting callback', () => {
		it('sets isHubConnected to false', async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			ui.isHubConnected = true;
			capturedCallbacks.onReconnecting!();
			expect(ui.isHubConnected).toBe(false);
		});

		it('tears down voice when active voice channel exists', async () => {
			voice.activeVoiceChannelId = 'vc-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onReconnecting!();
			expect(voice.teardownOnDisconnect).toHaveBeenCalledOnce();
			expect(ui.setTransientError).toHaveBeenCalledWith(
				'Voice disconnected due to network interruption.'
			);
		});

		it('tears down voice when active call exists', async () => {
			voice.activeCall = { callId: 'call-1' } as any;
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onReconnecting!();
			expect(voice.teardownOnDisconnect).toHaveBeenCalledOnce();
		});

		it('does not tear down voice when no active voice session', async () => {
			voice.activeVoiceChannelId = null;
			voice.activeCall = null;
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onReconnecting!();
			expect(voice.teardownOnDisconnect).not.toHaveBeenCalled();
		});
	});

	describe('onReconnected callback', () => {
		it('sets isHubConnected to true', async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			ui.isHubConnected = false;
			await capturedCallbacks.onReconnected!();
			expect(ui.isHubConnected).toBe(true);
		});

		it('re-joins server and channel on reconnect', async () => {
			servers.selectedServerId = 'srv-1';
			channels.selectedChannelId = 'ch-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			vi.mocked(hub.joinServer).mockClear();
			vi.mocked(hub.joinChannel).mockClear();

			await capturedCallbacks.onReconnected!();
			expect(hub.joinServer).toHaveBeenCalledWith('srv-1');
			expect(hub.joinChannel).toHaveBeenCalledWith('ch-1');
		});

		it('reloads messages for the active channel on reconnect', async () => {
			channels.selectedChannelId = 'ch-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

			await capturedCallbacks.onReconnected!();
			expect(messages.loadMessages).toHaveBeenCalledWith('ch-1');
		});

		it('re-joins active DM channel on reconnect', async () => {
			dms.activeDmChannelId = 'dm-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			vi.mocked(hub.joinDmChannel).mockClear();

			await capturedCallbacks.onReconnected!();
			expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-1');
		});
	});

	describe('onClose callback', () => {
		it('sets isHubConnected to false', async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onClose!();
			expect(ui.isHubConnected).toBe(false);
		});

		it('tears down voice on close when active', async () => {
			voice.activeVoiceChannelId = 'vc-1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onClose!();
			expect(voice.teardownOnDisconnect).toHaveBeenCalledOnce();
		});
	});

	describe('message callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onMessage to messages.handleIncomingMessage', () => {
			const msg = { id: 'msg-1' } as any;
			capturedCallbacks.onMessage(msg);
			expect(messages.handleIncomingMessage).toHaveBeenCalledWith(msg);
		});

		it('routes onUserTyping to messages.handleTyping', () => {
			capturedCallbacks.onUserTyping('ch-1', 'Alice');
			expect(messages.handleTyping).toHaveBeenCalledWith('ch-1', 'Alice');
		});

		it('routes onUserStoppedTyping to messages.handleStoppedTyping', () => {
			capturedCallbacks.onUserStoppedTyping('ch-1', 'Alice');
			expect(messages.handleStoppedTyping).toHaveBeenCalledWith('ch-1', 'Alice');
		});

		it('routes onReactionUpdated to messages.handleReactionUpdate', () => {
			const update = { messageId: 'm1', channelId: 'c1', reactions: [] } as any;
			capturedCallbacks.onReactionUpdated(update);
			expect(messages.handleReactionUpdate).toHaveBeenCalledWith(update);
		});

		it('routes onMessageDeleted to messages.handleMessageDeleted', () => {
			const event = { messageId: 'm1', channelId: 'c1' };
			capturedCallbacks.onMessageDeleted!(event);
			expect(messages.handleMessageDeleted).toHaveBeenCalledWith(event);
		});

		it('routes onMessageEdited to messages.handleMessageEdited', () => {
			const event = { messageId: 'm1', channelId: 'c1', body: 'new', editedAt: '2024-01-01' };
			capturedCallbacks.onMessageEdited!(event);
			expect(messages.handleMessageEdited).toHaveBeenCalledWith(event);
		});

		it('routes onChannelPurged to messages.handleChannelPurged', () => {
			const event = { channelId: 'c1' };
			capturedCallbacks.onChannelPurged!(event);
			expect(messages.handleChannelPurged).toHaveBeenCalledWith(event);
		});
	});

	describe('friend callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onFriendRequestReceived to friends.loadFriendRequests', () => {
			capturedCallbacks.onFriendRequestReceived!({} as any);
			expect(friends.loadFriendRequests).toHaveBeenCalled();
		});

		it('routes onFriendRequestAccepted to load friends and requests', () => {
			capturedCallbacks.onFriendRequestAccepted!({} as any);
			expect(friends.loadFriends).toHaveBeenCalled();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
		});

		it('routes onFriendRemoved to friends.loadFriends', () => {
			capturedCallbacks.onFriendRemoved!({} as any);
			expect(friends.loadFriends).toHaveBeenCalled();
		});
	});

	describe('DM callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onReceiveDm to dms.handleIncomingDm with showFriendsPanel', () => {
			ui.showFriendsPanel = true;
			const msg = { id: 'dm1' } as any;
			capturedCallbacks.onReceiveDm!(msg);
			expect(dms.handleIncomingDm).toHaveBeenCalledWith(msg, true);
		});

		it('routes onDmTyping to dms.handleDmTyping', () => {
			capturedCallbacks.onDmTyping!('dm-ch', 'Bob');
			expect(dms.handleDmTyping).toHaveBeenCalledWith('dm-ch', 'Bob');
		});

		it('routes onDmConversationOpened to dms.loadDmConversations', () => {
			capturedCallbacks.onDmConversationOpened!({ dmChannelId: 'dm-1', participant: {} } as any);
			expect(dms.loadDmConversations).toHaveBeenCalled();
		});
	});

	describe('link preview callback', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('patches channel messages when channelId matches selected channel', () => {
			channels.selectedChannelId = 'ch-1';
			const event = {
				messageId: 'm1',
				channelId: 'ch-1',
				linkPreviews: [{ url: 'https://example.com' }],
			} as any;
			capturedCallbacks.onLinkPreviewsReady!(event);
			expect(messages.patchLinkPreviews).toHaveBeenCalledWith('m1', event.linkPreviews);
		});

		it('does not patch channel messages when channelId does not match', () => {
			channels.selectedChannelId = 'ch-2';
			const event = {
				messageId: 'm1',
				channelId: 'ch-1',
				linkPreviews: [],
			} as any;
			capturedCallbacks.onLinkPreviewsReady!(event);
			expect(messages.patchLinkPreviews).not.toHaveBeenCalled();
		});

		it('patches DM messages when dmChannelId matches active DM', () => {
			dms.activeDmChannelId = 'dm-1';
			const event = {
				messageId: 'm1',
				dmChannelId: 'dm-1',
				linkPreviews: [{ url: 'https://example.com' }],
			} as any;
			capturedCallbacks.onLinkPreviewsReady!(event);
			expect(dms.patchDmLinkPreviews).toHaveBeenCalledWith('m1', event.linkPreviews);
		});
	});

	describe('kicked/banned from server callbacks', () => {
		beforeEach(async () => {
			servers.servers = [
				{ serverId: 's1', name: 'Server 1' },
				{ serverId: 's2', name: 'Server 2' },
			] as any;
			servers.selectedServerId = 's1';
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('removes the server and shows error when kicked', async () => {
			await capturedCallbacks.onKickedFromServer!({ serverId: 's1', serverName: 'Server 1' });
			expect(servers.servers).toEqual([{ serverId: 's2', name: 'Server 2' }]);
			expect(ui.setTransientError).toHaveBeenCalledWith('You were kicked from "Server 1".');
		});

		it('removes the server and shows error when banned', async () => {
			await capturedCallbacks.onBannedFromServer!({ serverId: 's1', serverName: 'Server 1' });
			expect(servers.servers).toEqual([{ serverId: 's2', name: 'Server 2' }]);
			expect(ui.setTransientError).toHaveBeenCalledWith('You were banned from "Server 1".');
		});

		it('selects the next server and loads its data when kicked from selected server', async () => {
			await capturedCallbacks.onKickedFromServer!({ serverId: 's1', serverName: 'Server 1' });
			expect(servers.selectedServerId).toBe('s2');
			expect(channels.loadChannels).toHaveBeenCalledWith('s2');
			expect(servers.loadMembers).toHaveBeenCalledWith('s2');
		});

		it('clears messages and channels when kicked from selected server', async () => {
			messages.messages = [{ id: 'm1' }] as any;
			messages.hasMoreMessages = true;
			channels.channels = [{ id: 'c1' }] as any;

			await capturedCallbacks.onKickedFromServer!({ serverId: 's1', serverName: 'Server 1' });
			expect(channels.channels).toEqual([]);
			expect(messages.messages).toEqual([]);
			expect(messages.hasMoreMessages).toBe(false);
			expect(servers.members).toEqual([]);
		});
	});

	describe('presence callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('sets presence for online/idle users', () => {
			capturedCallbacks.onUserPresenceChanged!({ userId: 'u1', status: 'online' });
			expect(ui.userPresence.get('u1')).toBe('online');

			capturedCallbacks.onUserPresenceChanged!({ userId: 'u1', status: 'idle' });
			expect(ui.userPresence.get('u1')).toBe('idle');
		});

		it('removes presence for offline users', () => {
			ui.userPresence.set('u1', 'online');
			capturedCallbacks.onUserPresenceChanged!({ userId: 'u1', status: 'offline' });
			expect(ui.userPresence.has('u1')).toBe(false);
		});
	});

	describe('voice callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onUserJoinedVoice to voice store', () => {
			const event = { channelId: 'vc-1', userId: 'u1', displayName: 'Alice' } as any;
			capturedCallbacks.onUserJoinedVoice!(event);
			expect(voice.handleUserJoinedVoice).toHaveBeenCalledWith(event);
		});

		it('routes onUserLeftVoice to voice store', () => {
			const event = { channelId: 'vc-1', userId: 'u1' };
			capturedCallbacks.onUserLeftVoice!(event);
			expect(voice.handleUserLeftVoice).toHaveBeenCalledWith(event);
		});

		it('routes onIncomingCall to voice store', () => {
			const event = { callId: 'c1', dmChannelId: 'dm-1', callerUserId: 'u1', callerDisplayName: 'Bob' } as any;
			capturedCallbacks.onIncomingCall!(event);
			expect(voice.handleIncomingCall).toHaveBeenCalledWith(event);
		});

		it('routes onCallEnded to voice store', () => {
			const event = { callId: 'c1', dmChannelId: 'dm-1', endReason: 'ended' } as any;
			capturedCallbacks.onCallEnded!(event);
			expect(voice.handleCallEnded).toHaveBeenCalledWith(event);
		});
	});

	describe('account deletion callback', () => {
		it('calls auth.signOut on account deletion', async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			capturedCallbacks.onAccountDeleted!();
			expect(auth.signOut).toHaveBeenCalledOnce();
		});
	});

	describe('server/channel setting callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onServerNameChanged to servers store', () => {
			const event = { serverId: 's1', name: 'New Name' };
			capturedCallbacks.onServerNameChanged!(event);
			expect(servers.handleServerNameChanged).toHaveBeenCalledWith(event);
		});

		it('routes onChannelNameChanged to channels store with selectedServerId', () => {
			servers.selectedServerId = 'srv-1';
			const event = { serverId: 'srv-1', channelId: 'ch-1', name: 'New Channel' };
			capturedCallbacks.onChannelNameChanged!(event);
			expect(channels.handleChannelNameChanged).toHaveBeenCalledWith(event, 'srv-1');
		});

		it('routes onChannelDeleted to channels store with selectedServerId', () => {
			servers.selectedServerId = 'srv-1';
			const event = { serverId: 'srv-1', channelId: 'ch-1' };
			capturedCallbacks.onChannelDeleted!(event);
			expect(channels.handleChannelDeleted).toHaveBeenCalledWith(event, 'srv-1');
		});
	});

	describe('category callbacks', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('routes onCategoryCreated to servers store', () => {
			const event = { serverId: 's1', categoryId: 'cat-1', name: 'General', position: 0 };
			capturedCallbacks.onCategoryCreated!(event);
			expect(servers.handleCategoryCreated).toHaveBeenCalledWith(event);
		});

		it('routes onCategoryDeleted to servers store and updates channels', () => {
			const existingChannels = [{ id: 'ch-existing' }] as any;
			channels.channels = existingChannels;
			const updatedChannels = [{ id: 'ch-1' }] as any;
			vi.mocked(servers.handleCategoryDeleted).mockReturnValue(updatedChannels);
			const event = { serverId: 's1', categoryId: 'cat-1' };
			capturedCallbacks.onCategoryDeleted!(event);
			expect(servers.handleCategoryDeleted).toHaveBeenCalledWith(event, existingChannels);
			expect(channels.channels).toEqual(updatedChannels);
		});

		it('does not update channels when handleCategoryDeleted returns undefined', () => {
			vi.mocked(servers.handleCategoryDeleted).mockReturnValue(undefined);
			channels.channels = [{ id: 'original' }] as any;
			capturedCallbacks.onCategoryDeleted!({ serverId: 's1', categoryId: 'cat-1' });
			expect(channels.channels).toEqual([{ id: 'original' }]);
		});

		it('reloads categories when channel order changes for selected server', async () => {
			servers.selectedServerId = 's1';
			await capturedCallbacks.onChannelOrderChanged!({ serverId: 's1' });
			expect(channels.handleChannelOrderChanged).toHaveBeenCalled();
			expect(servers.loadCategories).toHaveBeenCalled();
		});

		it('does not reload categories when channel order changes for different server', async () => {
			servers.selectedServerId = 's2';
			vi.mocked(servers.loadCategories).mockClear();
			await capturedCallbacks.onChannelOrderChanged!({ serverId: 's1' });
			expect(channels.handleChannelOrderChanged).toHaveBeenCalled();
			expect(servers.loadCategories).not.toHaveBeenCalled();
		});
	});
});
