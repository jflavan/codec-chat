import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));
vi.mock('$lib/auth/session.js', () => ({
	isTokenExpired: vi.fn().mockReturnValue(false)
}));
vi.mock('./navigation.svelte.js', () => ({
	goHome: vi.fn()
}));

import { setupSignalR } from './signalr.svelte';

// The hub.start() call captures all the callbacks. We test that setupSignalR
// wires callbacks correctly and performs post-start setup.

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

	beforeEach(() => {
		vi.clearAllMocks();
		capturedCallbacks = null;

		hub = {
			start: vi.fn(async (_tokenFactory: any, callbacks: any) => {
				capturedCallbacks = callbacks;
			}),
			isConnected: true,
			joinChannel: vi.fn().mockResolvedValue(undefined),
			joinDmChannel: vi.fn().mockResolvedValue(undefined),
			joinServer: vi.fn().mockResolvedValue(undefined),
			leaveServer: vi.fn().mockResolvedValue(undefined)
		};

		auth = {
			idToken: 'test-token',
			me: { user: { id: 'u1' } },
			refreshToken: vi.fn().mockResolvedValue('new-token'),
			signOut: vi.fn()
		};

		servers = {
			selectedServerId: null,
			servers: [],
			members: [],
			discordImport: null,
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
			handleMentionReceived: vi.fn()
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
			loadDmConversations: vi.fn(),
			handleDmMessageDeleted: vi.fn(),
			handleDmMessageEdited: vi.fn(),
			handleDmReactionUpdate: vi.fn(),
			patchDmLinkPreviews: vi.fn()
		};

		friends = {
			loadFriends: vi.fn(),
			loadFriendRequests: vi.fn()
		};

		voice = {
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
			checkActiveCall: vi.fn().mockResolvedValue(undefined)
		};

		ui = {
			isHubConnected: false,
			showFriendsPanel: false,
			userPresence: new Map(),
			setTransientError: vi.fn()
		};
	});

	it('should call hub.start with a token factory and callbacks', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(hub.start).toHaveBeenCalledTimes(1);
		expect(capturedCallbacks).not.toBeNull();
	});

	it('should set isHubConnected after start', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(ui.isHubConnected).toBe(true);
	});

	it('should check for active call after start', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(voice.checkActiveCall).toHaveBeenCalled();
	});

	it('should join selected channel after start', async () => {
		channels.selectedChannelId = 'ch1';

		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(hub.joinChannel).toHaveBeenCalledWith('ch1');
	});

	it('should join active DM channel after start', async () => {
		dms.activeDmChannelId = 'dm1';

		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(hub.joinDmChannel).toHaveBeenCalledWith('dm1');
	});

	it('should not join channel or DM when none selected', async () => {
		await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);

		expect(hub.joinChannel).not.toHaveBeenCalled();
		expect(hub.joinDmChannel).not.toHaveBeenCalled();
	});

	// --- Callback wiring tests ---

	describe('callback wiring', () => {
		beforeEach(async () => {
			await setupSignalR(hub, auth, servers, channels, messages, dms, friends, voice, ui);
		});

		it('onMessage should delegate to messages.handleIncomingMessage', () => {
			const msg = { id: 'm1' };
			capturedCallbacks.onMessage(msg);
			expect(messages.handleIncomingMessage).toHaveBeenCalledWith(msg);
		});

		it('onUserJoinedVoice should delegate to voice.handleUserJoinedVoice', () => {
			const event = { channelId: 'ch1', userId: 'u1' };
			capturedCallbacks.onUserJoinedVoice(event);
			expect(voice.handleUserJoinedVoice).toHaveBeenCalledWith(event);
		});

		it('onIncomingCall should delegate to voice.handleIncomingCall', () => {
			const event = { callId: 'c1' };
			capturedCallbacks.onIncomingCall(event);
			expect(voice.handleIncomingCall).toHaveBeenCalledWith(event);
		});

		it('onAccountDeleted should call auth.signOut', () => {
			capturedCallbacks.onAccountDeleted();
			expect(auth.signOut).toHaveBeenCalled();
		});

		it('onFriendRequestReceived should reload friend requests', () => {
			capturedCallbacks.onFriendRequestReceived();
			expect(friends.loadFriendRequests).toHaveBeenCalled();
		});

		it('onServerNameChanged should delegate to servers', () => {
			const event = { serverId: 's1', name: 'New' };
			capturedCallbacks.onServerNameChanged(event);
			expect(servers.handleServerNameChanged).toHaveBeenCalledWith(event);
		});

		it('onReconnecting should set isHubConnected false', () => {
			ui.isHubConnected = true;
			capturedCallbacks.onReconnecting();
			expect(ui.isHubConnected).toBe(false);
		});

		it('onReconnecting should teardown voice if active', () => {
			voice.activeVoiceChannelId = 'ch1';
			capturedCallbacks.onReconnecting();
			expect(voice.teardownOnDisconnect).toHaveBeenCalled();
			expect(ui.setTransientError).toHaveBeenCalledWith(
				'Voice disconnected due to network interruption.'
			);
		});

		it('onReconnected should set isHubConnected true', async () => {
			ui.isHubConnected = false;
			await capturedCallbacks.onReconnected();
			expect(ui.isHubConnected).toBe(true);
		});

		it('onClose should set isHubConnected false', () => {
			ui.isHubConnected = true;
			capturedCallbacks.onClose();
			expect(ui.isHubConnected).toBe(false);
		});

		it('onUserPresenceChanged should update presence map', () => {
			capturedCallbacks.onUserPresenceChanged({ userId: 'u1', status: 'online' });
			expect(ui.userPresence.get('u1')).toBe('online');
		});

		it('onUserPresenceChanged should remove offline users', () => {
			ui.userPresence.set('u1', 'online');
			capturedCallbacks.onUserPresenceChanged({ userId: 'u1', status: 'offline' });
			expect(ui.userPresence.has('u1')).toBe(false);
		});

		it('onReceiveDm should delegate to dms.handleIncomingDm', () => {
			const msg = { id: 'dm-m1' };
			capturedCallbacks.onReceiveDm(msg);
			expect(dms.handleIncomingDm).toHaveBeenCalledWith(msg, ui.showFriendsPanel);
		});
	});
});
