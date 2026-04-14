import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock session module before import
vi.mock('$lib/auth/session.js', () => ({
	isTokenExpired: vi.fn().mockReturnValue(false)
}));

import { setupSignalR } from './signalr.svelte.js';

describe('setupSignalR', () => {
	let hub: any;
	let auth: any;
	let servers: any;
	let channels: any;
	let messages: any;
	let dms: any;
	let friends: any;
	let voice: any;
	let ui: any;
	let capturedCallbacks: any;
	let capturedTokenFactory: any;

	beforeEach(() => {
		capturedCallbacks = null;
		capturedTokenFactory = null;

		hub = {
			start: vi.fn().mockImplementation(async (tokenFactory: any, callbacks: any) => {
				capturedTokenFactory = tokenFactory;
				capturedCallbacks = callbacks;
			}),
			joinChannel: vi.fn().mockResolvedValue(undefined),
			joinDmChannel: vi.fn().mockResolvedValue(undefined),
			isConnected: true
		};

		auth = {
			idToken: 'test-token',
			refreshToken: vi.fn().mockResolvedValue('refreshed-token'),
			signOut: vi.fn().mockResolvedValue(undefined),
			me: { user: { id: 'user-1' } }
		};

		servers = {
			selectedServerId: null,
			discordImport: null,
			servers: [],
			members: [],
			handleMemberBanned: vi.fn(),
			handleMemberUnbanned: vi.fn(),
			handleMemberJoined: vi.fn(),
			handleMemberLeft: vi.fn(),
			handleMemberRoleChanged: vi.fn(),
			handleServerNameChanged: vi.fn(),
			handleServerIconChanged: vi.fn(),
			handleServerDeleted: vi.fn(),
			handleServerDescriptionChanged: vi.fn(),
			handleCustomEmojiAdded: vi.fn(),
			handleCustomEmojiUpdated: vi.fn(),
			handleCustomEmojiDeleted: vi.fn(),
			handleCategoryCreated: vi.fn(),
			handleCategoryRenamed: vi.fn(),
			handleCategoryDeleted: vi.fn(),
			handleCategoryOrderChanged: vi.fn(),
			handleUserStatusChanged: vi.fn(),
			loadCategories: vi.fn().mockResolvedValue(undefined)
		};

		channels = {
			selectedChannelId: null,
			channels: [],
			handleChannelNameChanged: vi.fn(),
			handleChannelDeleted: vi.fn(),
			handleChannelDescriptionChanged: vi.fn(),
			handleChannelOrderChanged: vi.fn().mockResolvedValue(undefined),
			handleChannelOverrideUpdated: vi.fn().mockResolvedValue(undefined),
			handleMentionReceived: vi.fn(),
			loadChannels: vi.fn().mockResolvedValue(undefined)
		};

		messages = {
			messages: [],
			hasMoreMessages: false,
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
			loadMessages: vi.fn().mockResolvedValue(undefined)
		};

		dms = {
			activeDmChannelId: null,
			handleIncomingDm: vi.fn(),
			handleDmTyping: vi.fn(),
			handleDmStoppedTyping: vi.fn(),
			handleDmMessageDeleted: vi.fn(),
			handleDmMessageEdited: vi.fn(),
			handleDmReactionUpdate: vi.fn(),
			loadDmConversations: vi.fn().mockResolvedValue(undefined),
			patchDmLinkPreviews: vi.fn()
		};

		friends = {
			loadFriends: vi.fn().mockResolvedValue(undefined),
			loadFriendRequests: vi.fn().mockResolvedValue(undefined)
		};

		voice = {
			activeVoiceChannelId: null,
			activeCall: null,
			handleUserJoinedVoice: vi.fn(),
			handleUserLeftVoice: vi.fn(),
			handleVoiceStateUpdated: vi.fn(),
			handleIncomingCall: vi.fn(),
			handleCallAccepted: vi.fn().mockResolvedValue(undefined),
			handleCallDeclined: vi.fn(),
			handleCallEnded: vi.fn(),
			handleCallMissed: vi.fn(),
			teardownOnDisconnect: vi.fn(),
			checkActiveCall: vi.fn().mockResolvedValue(undefined)
		};

		ui = {
			isHubConnected: false,
			showFriendsPanel: false,
			userPresence: new Map(),
			setTransientError: vi.fn()
		};
	});

	it('calls hub.start with token factory and callbacks', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(hub.start).toHaveBeenCalledTimes(1);
		expect(capturedTokenFactory).toBeDefined();
		expect(capturedCallbacks).toBeDefined();
	});

	it('sets isHubConnected after start', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(ui.isHubConnected).toBe(true);
	});

	it('checks for active calls after start', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(voice.checkActiveCall).toHaveBeenCalled();
	});

	it('joins active channel after start', async () => {
		channels.selectedChannelId = 'ch-1';
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(hub.joinChannel).toHaveBeenCalledWith('ch-1');
	});

	it('joins active DM channel after start', async () => {
		dms.activeDmChannelId = 'dm-1';
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		expect(hub.joinDmChannel).toHaveBeenCalledWith('dm-1');
	});

	describe('token factory', () => {
		it('returns current token when valid', async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();
			expect(token).toBe('test-token');
		});

		it('refreshes token when expired', async () => {
			const { isTokenExpired } = await import('$lib/auth/session.js');
			vi.mocked(isTokenExpired).mockReturnValue(true);
			auth.idToken = 'expired-token';

			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
			const token = await capturedTokenFactory();

			expect(auth.refreshToken).toHaveBeenCalled();
			expect(token).toBe('refreshed-token');
		});
	});

	describe('callback wiring', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('onMessage delegates to messages store', () => {
			const msg = { id: 'msg-1', channelId: 'ch-1' };
			capturedCallbacks.onMessage(msg);
			expect(messages.handleIncomingMessage).toHaveBeenCalledWith(msg);
		});

		it('onUserTyping delegates to messages store', () => {
			capturedCallbacks.onUserTyping('ch-1', 'Alice');
			expect(messages.handleTyping).toHaveBeenCalledWith('ch-1', 'Alice');
		});

		it('onReceiveDm delegates to dms store', () => {
			const msg = { id: 'dm-msg-1' };
			capturedCallbacks.onReceiveDm(msg);
			expect(dms.handleIncomingDm).toHaveBeenCalledWith(msg, ui.showFriendsPanel);
		});

		it('onFriendRequestReceived reloads friend requests', () => {
			capturedCallbacks.onFriendRequestReceived();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
		});

		it('onFriendRequestAccepted reloads friends and requests', () => {
			capturedCallbacks.onFriendRequestAccepted();
			expect(friends.loadFriends).toHaveBeenCalled();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
		});

		it('onServerNameChanged delegates to servers store', () => {
			const event = { serverId: 'srv-1', name: 'New' };
			capturedCallbacks.onServerNameChanged(event);
			expect(servers.handleServerNameChanged).toHaveBeenCalledWith(event);
		});

		it('onChannelNameChanged delegates to channels store with server id', () => {
			const event = { channelId: 'ch-1', serverId: 'srv-1', name: 'New' };
			capturedCallbacks.onChannelNameChanged(event);
			expect(channels.handleChannelNameChanged).toHaveBeenCalledWith(event, servers.selectedServerId);
		});

		it('onUserJoinedVoice delegates to voice store', () => {
			const event = { channelId: 'ch-1', userId: 'u2' };
			capturedCallbacks.onUserJoinedVoice(event);
			expect(voice.handleUserJoinedVoice).toHaveBeenCalledWith(event);
		});

		it('onReconnecting sets hub disconnected and tears down voice', () => {
			voice.activeVoiceChannelId = 'ch-1';
			capturedCallbacks.onReconnecting();
			expect(ui.isHubConnected).toBe(false);
			expect(voice.teardownOnDisconnect).toHaveBeenCalled();
		});

		it('onReconnecting does not tear down voice if not in voice', () => {
			voice.activeVoiceChannelId = null;
			voice.activeCall = null;
			capturedCallbacks.onReconnecting();
			expect(voice.teardownOnDisconnect).not.toHaveBeenCalled();
		});

		it('onClose sets hub disconnected', () => {
			capturedCallbacks.onClose();
			expect(ui.isHubConnected).toBe(false);
		});

		it('onUserPresenceChanged updates presence map', () => {
			capturedCallbacks.onUserPresenceChanged({ userId: 'u2', status: 'online' });
			expect(ui.userPresence.get('u2')).toBe('online');
		});

		it('onUserPresenceChanged removes offline users', () => {
			ui.userPresence.set('u2', 'online');
			capturedCallbacks.onUserPresenceChanged({ userId: 'u2', status: 'offline' });
			expect(ui.userPresence.has('u2')).toBe(false);
		});

		it('onAccountDeleted signs out', () => {
			capturedCallbacks.onAccountDeleted();
			expect(auth.signOut).toHaveBeenCalled();
		});

		it('onMentionReceived delegates to channels', () => {
			const event = { channelId: 'ch-1', serverId: 'srv-1' };
			capturedCallbacks.onMentionReceived(event);
			expect(channels.handleMentionReceived).toHaveBeenCalledWith(event);
		});
	});
});
