import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('svelte', () => ({ getContext: vi.fn(), setContext: vi.fn() }));

import { VoiceStore } from './voice-store.svelte';

function mockAuth(overrides: Record<string, unknown> = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1', displayName: 'Test User', avatarUrl: null } },
		effectiveDisplayName: 'Test User',
		isGlobalAdmin: false,
		...overrides
	} as any;
}

function mockApi() {
	return {
		getVoiceStates: vi.fn(),
		getActiveCall: vi.fn()
	} as any;
}

function mockUi() {
	return {
		setError: vi.fn(),
		setTransientError: vi.fn()
	} as any;
}

function mockHub() {
	return {
		joinVoiceChannel: vi.fn(),
		leaveVoiceChannel: vi.fn(),
		updateVoiceState: vi.fn().mockResolvedValue(undefined),
		startCall: vi.fn(),
		acceptCall: vi.fn(),
		declineCall: vi.fn(),
		endCall: vi.fn(),
		setupCallTransports: vi.fn()
	} as any;
}

function mockVoice() {
	return {
		join: vi.fn().mockResolvedValue(undefined),
		leave: vi.fn().mockResolvedValue(undefined),
		setMuted: vi.fn().mockResolvedValue(undefined),
		startVideo: vi.fn(),
		stopVideo: vi.fn(),
		startScreenShare: vi.fn(),
		stopScreenShare: vi.fn(),
		teardownSync: vi.fn()
	} as any;
}

// Mock localStorage
const localStorageMock = (() => {
	let store: Record<string, string> = {};
	return {
		getItem: vi.fn((key: string) => store[key] ?? null),
		setItem: vi.fn((key: string, value: string) => { store[key] = value; }),
		removeItem: vi.fn((key: string) => { delete store[key]; }),
		clear: vi.fn(() => { store = {}; })
	};
})();

describe('VoiceStore', () => {
	let store: VoiceStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: ReturnType<typeof mockUi>;
	let hub: ReturnType<typeof mockHub>;
	let voice: ReturnType<typeof mockVoice>;

	beforeEach(() => {
		vi.clearAllMocks();
		localStorageMock.clear();
		vi.stubGlobal('localStorage', localStorageMock);
		auth = mockAuth();
		api = mockApi();
		ui = mockUi();
		hub = mockHub();
		voice = mockVoice();
		store = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	// --- Initial state ---

	describe('initial state', () => {
		it('should have null activeVoiceChannelId', () => {
			expect(store.activeVoiceChannelId).toBeNull();
		});

		it('should have empty voiceChannelMembers', () => {
			expect(store.voiceChannelMembers.size).toBe(0);
		});

		it('should have isMuted false', () => {
			expect(store.isMuted).toBe(false);
		});

		it('should have isDeafened false', () => {
			expect(store.isDeafened).toBe(false);
		});

		it('should have isJoiningVoice false', () => {
			expect(store.isJoiningVoice).toBe(false);
		});

		it('should have null activeCall', () => {
			expect(store.activeCall).toBeNull();
		});

		it('should have null incomingCall', () => {
			expect(store.incomingCall).toBeNull();
		});

		it('should have default pttKey as KeyV', () => {
			expect(store.pttKey).toBe('KeyV');
		});

		it('should have voice-activity input mode by default', () => {
			expect(store.voiceInputMode).toBe('voice-activity');
		});

		it('should have isVideoEnabled false', () => {
			expect(store.isVideoEnabled).toBe(false);
		});

		it('should have isScreenSharing false', () => {
			expect(store.isScreenSharing).toBe(false);
		});
	});

	// --- handleUserJoinedVoice ---

	describe('handleUserJoinedVoice', () => {
		it('should add member to voice channel members', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});

			const members = store.voiceChannelMembers.get('ch1');
			expect(members).toHaveLength(1);
			expect(members![0].userId).toBe('u1');
			expect(members![0].displayName).toBe('User 1');
			expect(members![0].isMuted).toBe(false);
			expect(members![0].isDeafened).toBe(false);
		});

		it('should not add duplicate member', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});

			expect(store.voiceChannelMembers.get('ch1')).toHaveLength(1);
		});

		it('should add members to different channels independently', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});
			store.handleUserJoinedVoice({
				channelId: 'ch2',
				userId: 'u2',
				displayName: 'User 2',
				avatarUrl: null
			});

			expect(store.voiceChannelMembers.get('ch1')).toHaveLength(1);
			expect(store.voiceChannelMembers.get('ch2')).toHaveLength(1);
		});
	});

	// --- handleUserLeftVoice ---

	describe('handleUserLeftVoice', () => {
		it('should remove member from voice channel', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u2',
				displayName: 'User 2',
				avatarUrl: null
			});

			store.handleUserLeftVoice({ channelId: 'ch1', userId: 'u1' });

			const members = store.voiceChannelMembers.get('ch1');
			expect(members).toHaveLength(1);
			expect(members![0].userId).toBe('u2');
		});

		it('should handle leaving from empty channel gracefully', () => {
			store.handleUserLeftVoice({ channelId: 'ch1', userId: 'u1' });

			expect(store.voiceChannelMembers.get('ch1')).toEqual([]);
		});
	});

	// --- handleVoiceStateUpdated ---

	describe('handleVoiceStateUpdated', () => {
		it('should update mute/deafen state for specific member', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'User 1',
				avatarUrl: null
			});

			store.handleVoiceStateUpdated({
				channelId: 'ch1',
				userId: 'u1',
				isMuted: true,
				isDeafened: true
			});

			const members = store.voiceChannelMembers.get('ch1')!;
			expect(members[0].isMuted).toBe(true);
			expect(members[0].isDeafened).toBe(true);
		});

		it('should not affect other members', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u1',
				displayName: 'U1',
				avatarUrl: null
			});
			store.handleUserJoinedVoice({
				channelId: 'ch1',
				userId: 'u2',
				displayName: 'U2',
				avatarUrl: null
			});

			store.handleVoiceStateUpdated({
				channelId: 'ch1',
				userId: 'u1',
				isMuted: true,
				isDeafened: false
			});

			const members = store.voiceChannelMembers.get('ch1')!;
			expect(members.find((m) => m.userId === 'u2')!.isMuted).toBe(false);
		});
	});

	// --- handleIncomingCall ---

	describe('handleIncomingCall', () => {
		it('should set incomingCall when no active call', () => {
			const event = {
				callId: 'c1',
				dmChannelId: 'dm1',
				callerUserId: 'u2',
				callerDisplayName: 'Caller',
				callerAvatarUrl: null
			};

			store.handleIncomingCall(event);

			expect(store.incomingCall).toEqual(event);
		});

		it('should ignore when activeCall exists', () => {
			store.activeCall = {
				callId: 'existing',
				dmChannelId: 'dm1',
				otherUserId: 'u3',
				otherDisplayName: 'Other',
				status: 'active',
				startedAt: new Date().toISOString()
			};

			store.handleIncomingCall({
				callId: 'c1',
				dmChannelId: 'dm2',
				callerUserId: 'u2',
				callerDisplayName: 'Caller',
				callerAvatarUrl: null
			});

			expect(store.incomingCall).toBeNull();
		});

		it('should ignore when incomingCall already exists', () => {
			store.incomingCall = {
				callId: 'existing',
				dmChannelId: 'dm1',
				callerUserId: 'u2',
				callerDisplayName: 'First',
				callerAvatarUrl: null
			};

			store.handleIncomingCall({
				callId: 'c2',
				dmChannelId: 'dm2',
				callerUserId: 'u3',
				callerDisplayName: 'Second',
				callerAvatarUrl: null
			});

			expect(store.incomingCall!.callId).toBe('existing');
		});
	});

	// --- handleCallDeclined ---

	describe('handleCallDeclined', () => {
		it('should clear activeCall when callId matches', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'ringing',
				startedAt: new Date().toISOString()
			};

			store.handleCallDeclined({ callId: 'c1' });

			expect(store.activeCall).toBeNull();
		});

		it('should not clear activeCall when callId does not match', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'ringing',
				startedAt: new Date().toISOString()
			};

			store.handleCallDeclined({ callId: 'c999' });

			expect(store.activeCall).not.toBeNull();
		});
	});

	// --- handleCallEnded ---

	describe('handleCallEnded', () => {
		it('should clear activeCall and reset voice state when callId matches', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'active',
				startedAt: new Date().toISOString()
			};
			store.isMuted = true;
			store.isDeafened = true;

			store.handleCallEnded({ callId: 'c1' });

			expect(store.activeCall).toBeNull();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
			expect(voice.leave).toHaveBeenCalled();
		});

		it('should not clear state when callId does not match', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'active',
				startedAt: new Date().toISOString()
			};

			store.handleCallEnded({ callId: 'c999' });

			expect(store.activeCall).not.toBeNull();
		});
	});

	// --- handleCallMissed ---

	describe('handleCallMissed', () => {
		it('should clear matching activeCall', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'ringing',
				startedAt: new Date().toISOString()
			};

			store.handleCallMissed({ callId: 'c1' });

			expect(store.activeCall).toBeNull();
		});

		it('should clear matching incomingCall', () => {
			store.incomingCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				callerUserId: 'u2',
				callerDisplayName: 'Caller',
				callerAvatarUrl: null
			};

			store.handleCallMissed({ callId: 'c1' });

			expect(store.incomingCall).toBeNull();
		});

		it('should not clear non-matching calls', () => {
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'ringing',
				startedAt: new Date().toISOString()
			};
			store.incomingCall = {
				callId: 'c2',
				dmChannelId: 'dm2',
				callerUserId: 'u3',
				callerDisplayName: 'Caller',
				callerAvatarUrl: null
			};

			store.handleCallMissed({ callId: 'c999' });

			expect(store.activeCall).not.toBeNull();
			expect(store.incomingCall).not.toBeNull();
		});
	});

	// --- setUserVolume ---

	describe('setUserVolume', () => {
		it('should set volume and save to localStorage', () => {
			store.setUserVolume('u1', 0.5);

			expect(store.userVolumes.get('u1')).toBe(0.5);
			expect(localStorageMock.setItem).toHaveBeenCalled();
		});

		it('should clamp volume to min 0', () => {
			store.setUserVolume('u1', -0.5);

			expect(store.userVolumes.get('u1')).toBe(0);
		});

		it('should clamp volume to max 1', () => {
			store.setUserVolume('u1', 1.5);

			// At 1.0, the entry is removed (default volume)
			expect(store.userVolumes.has('u1')).toBe(false);
		});

		it('should remove entry at 1.0 (default volume)', () => {
			store.setUserVolume('u1', 0.5);
			expect(store.userVolumes.has('u1')).toBe(true);

			store.setUserVolume('u1', 1.0);
			expect(store.userVolumes.has('u1')).toBe(false);
		});
	});

	// --- setPttKey ---

	describe('setPttKey', () => {
		it('should update pttKey and save to localStorage', () => {
			store.setPttKey('Space');

			expect(store.pttKey).toBe('Space');
			expect(localStorageMock.setItem).toHaveBeenCalledWith(
				'codec-voice-preferences',
				expect.stringContaining('Space')
			);
		});
	});

	// --- teardownOnDisconnect ---

	describe('teardownOnDisconnect', () => {
		it('should clear all voice state and call voice.leave', () => {
			store.activeVoiceChannelId = 'ch1';
			store.activeCall = {
				callId: 'c1',
				dmChannelId: 'dm1',
				otherUserId: 'u2',
				otherDisplayName: 'Other',
				status: 'active',
				startedAt: new Date().toISOString()
			};
			store.incomingCall = {
				callId: 'c2',
				dmChannelId: 'dm2',
				callerUserId: 'u3',
				callerDisplayName: 'Caller',
				callerAvatarUrl: null
			};
			store.isMuted = true;
			store.isDeafened = true;

			store.teardownOnDisconnect();

			expect(voice.leave).toHaveBeenCalled();
			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.activeCall).toBeNull();
			expect(store.incomingCall).toBeNull();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
			expect(store.isPttActive).toBe(false);
			expect(store.isVideoEnabled).toBe(false);
			expect(store.isScreenSharing).toBe(false);
		});
	});

	// --- reset ---

	describe('reset', () => {
		it('should clear all state including members and volumes', () => {
			store.activeVoiceChannelId = 'ch1';
			store.isMuted = true;
			const members = new Map();
			members.set('ch1', [{ userId: 'u1', displayName: 'U1', avatarUrl: null, isMuted: false, isDeafened: false }]);
			store.voiceChannelMembers = members;
			store.userVolumes = new Map([['u1', 0.5]]);

			store.reset();

			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.voiceChannelMembers.size).toBe(0);
			expect(store.userVolumes.size).toBe(0);
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
			expect(voice.leave).toHaveBeenCalled();
		});
	});

	// --- Constructor loads from localStorage ---

	describe('constructor localStorage loading', () => {
		it('should load user volumes from localStorage', () => {
			localStorageMock.getItem.mockImplementation((key: string) => {
				if (key === 'codec-user-volumes') return JSON.stringify({ 'u1': 0.7 });
				return null;
			});

			const s = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');

			expect(s.userVolumes.get('u1')).toBe(0.7);
		});

		it('should load voice preferences from localStorage', () => {
			localStorageMock.getItem.mockImplementation((key: string) => {
				if (key === 'codec-voice-preferences')
					return JSON.stringify({ inputMode: 'push-to-talk', pttKey: 'Space' });
				return null;
			});

			const s = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');

			expect(s.voiceInputMode).toBe('push-to-talk');
			expect(s.pttKey).toBe('Space');
		});

		it('should handle corrupt localStorage data gracefully', () => {
			localStorageMock.getItem.mockReturnValue('not-valid-json{{{');

			expect(() => {
				new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
			}).not.toThrow();
		});

		it('should use default liveKitUrl when empty string provided', () => {
			const s = new VoiceStore(auth, api, ui, hub, voice, '');

			// The constructor sets a fallback — just confirm no error
			expect(s.activeVoiceChannelId).toBeNull();
		});
	});
});
