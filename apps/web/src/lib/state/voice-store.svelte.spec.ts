import { describe, it, expect, beforeEach, vi } from 'vitest';
import { VoiceStore } from './voice-store.svelte.js';
import { UIStore } from './ui-store.svelte.js';

function mockAuth(overrides = {}) {
	return {
		idToken: 'test-token',
		me: { user: { id: 'user-1' } },
		isGlobalAdmin: false,
		effectiveDisplayName: 'TestUser',
		...overrides
	} as any;
}

function mockApi() {
	return {
		getVoiceStates: vi.fn().mockResolvedValue([]),
		getActiveCall: vi.fn().mockResolvedValue(null)
	} as any;
}

function mockHub() {
	return {
		joinVoiceChannel: vi.fn().mockResolvedValue({ token: 'lk-token', members: [] }),
		leaveVoiceChannel: vi.fn().mockResolvedValue(undefined),
		updateVoiceState: vi.fn().mockResolvedValue(undefined),
		startCall: vi.fn().mockResolvedValue({ callId: 'call-1', recipientUserId: 'u2', recipientDisplayName: 'User2' }),
		acceptCall: vi.fn().mockResolvedValue({ token: 'lk-call-token' }),
		declineCall: vi.fn().mockResolvedValue(undefined),
		endCall: vi.fn().mockResolvedValue(undefined),
		setupCallTransports: vi.fn().mockResolvedValue({ token: 'lk-caller-token' })
	} as any;
}

function mockVoice() {
	return {
		join: vi.fn().mockResolvedValue(undefined),
		leave: vi.fn().mockResolvedValue(undefined),
		setMuted: vi.fn().mockResolvedValue(undefined),
		startVideo: vi.fn().mockResolvedValue({ kind: 'video' }),
		stopVideo: vi.fn().mockResolvedValue(undefined),
		startScreenShare: vi.fn().mockResolvedValue({ kind: 'video', addEventListener: vi.fn() }),
		stopScreenShare: vi.fn().mockResolvedValue(undefined),
		teardownSync: vi.fn()
	} as any;
}

describe('VoiceStore', () => {
	let store: VoiceStore;
	let auth: ReturnType<typeof mockAuth>;
	let api: ReturnType<typeof mockApi>;
	let ui: UIStore;
	let hub: ReturnType<typeof mockHub>;
	let voice: ReturnType<typeof mockVoice>;

	beforeEach(() => {
		localStorage.clear();
		auth = mockAuth();
		api = mockApi();
		ui = new UIStore();
		hub = mockHub();
		voice = mockVoice();
		store = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
	});

	describe('initial state', () => {
		it('starts with no active voice channel', () => {
			expect(store.activeVoiceChannelId).toBe(null);
		});

		it('starts unmuted and undeafened', () => {
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});

		it('starts with voice-activity input mode', () => {
			expect(store.voiceInputMode).toBe('voice-activity');
		});

		it('starts with default PTT key', () => {
			expect(store.pttKey).toBe('KeyV');
		});

		it('starts with no active call', () => {
			expect(store.activeCall).toBe(null);
			expect(store.incomingCall).toBe(null);
		});

		it('starts with video/screen sharing disabled', () => {
			expect(store.isVideoEnabled).toBe(false);
			expect(store.isScreenSharing).toBe(false);
		});
	});

	describe('setUserVolume', () => {
		it('sets user volume and persists', () => {
			store.setUserVolume('u2', 0.5);
			expect(store.userVolumes.get('u2')).toBe(0.5);

			const stored = JSON.parse(localStorage.getItem('codec-user-volumes')!);
			expect(stored.u2).toBe(0.5);
		});

		it('clamps volume to 0-1 range', () => {
			store.setUserVolume('u2', 2.0);
			expect(store.userVolumes.get('u2')).toBeUndefined(); // 1.0 removes from map

			store.setUserVolume('u2', -0.5);
			expect(store.userVolumes.get('u2')).toBe(0);
		});

		it('removes user at default volume (1.0)', () => {
			store.setUserVolume('u2', 0.5);
			store.setUserVolume('u2', 1.0);
			expect(store.userVolumes.has('u2')).toBe(false);
		});
	});

	describe('resetUserVolume', () => {
		it('resets user volume to 1.0', () => {
			store.setUserVolume('u2', 0.3);
			store.resetUserVolume('u2');
			expect(store.userVolumes.has('u2')).toBe(false);
		});
	});

	describe('setVoiceInputMode', () => {
		it('changes input mode and persists', async () => {
			await store.setVoiceInputMode('push-to-talk');
			expect(store.voiceInputMode).toBe('push-to-talk');

			const stored = JSON.parse(localStorage.getItem('codec-voice-preferences')!);
			expect(stored.inputMode).toBe('push-to-talk');
		});
	});

	describe('setPttKey', () => {
		it('changes PTT key and persists', () => {
			store.setPttKey('Space');
			expect(store.pttKey).toBe('Space');

			const stored = JSON.parse(localStorage.getItem('codec-voice-preferences')!);
			expect(stored.pttKey).toBe('Space');
		});
	});

	describe('loadAllVoiceStates', () => {
		it('loads voice states for voice channels only', async () => {
			const channels = [
				{ id: 'ch-1', type: 'text' },
				{ id: 'ch-2', type: 'voice' },
				{ id: 'ch-3', type: 'voice' }
			] as any;
			const members = [{ userId: 'u2', displayName: 'User2' }];
			api.getVoiceStates.mockResolvedValue(members);

			await store.loadAllVoiceStates(channels);

			expect(api.getVoiceStates).toHaveBeenCalledTimes(2);
			expect(store.voiceChannelMembers.get('ch-2')).toEqual(members);
		});
	});

	describe('SignalR handlers', () => {
		it('handleUserJoinedVoice adds member', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u2',
				displayName: 'User2',
				avatarUrl: null
			});

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members).toHaveLength(1);
			expect(members![0].userId).toBe('u2');
		});

		it('handleUserJoinedVoice does not duplicate', () => {
			store.voiceChannelMembers = new Map([['ch-1', [{ userId: 'u2', displayName: 'User2', avatarUrl: null, isMuted: false, isDeafened: false }]]]);

			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u2',
				displayName: 'User2',
				avatarUrl: null
			});

			expect(store.voiceChannelMembers.get('ch-1')).toHaveLength(1);
		});

		it('handleUserLeftVoice removes member', () => {
			store.voiceChannelMembers = new Map([
				['ch-1', [
					{ userId: 'u2', displayName: 'User2', avatarUrl: null, isMuted: false, isDeafened: false },
					{ userId: 'u3', displayName: 'User3', avatarUrl: null, isMuted: false, isDeafened: false }
				]]
			]);

			store.handleUserLeftVoice({ channelId: 'ch-1', userId: 'u2' });

			expect(store.voiceChannelMembers.get('ch-1')).toHaveLength(1);
			expect(store.voiceChannelMembers.get('ch-1')![0].userId).toBe('u3');
		});

		it('handleVoiceStateUpdated updates mute/deafen state', () => {
			store.voiceChannelMembers = new Map([
				['ch-1', [{ userId: 'u2', displayName: 'User2', avatarUrl: null, isMuted: false, isDeafened: false }]]
			]);

			store.handleVoiceStateUpdated({
				channelId: 'ch-1',
				userId: 'u2',
				isMuted: true,
				isDeafened: true
			});

			const member = store.voiceChannelMembers.get('ch-1')![0];
			expect(member.isMuted).toBe(true);
			expect(member.isDeafened).toBe(true);
		});

		it('handleIncomingCall sets incoming call', () => {
			const event = {
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u2',
				callerDisplayName: 'User2'
			} as any;

			store.handleIncomingCall(event);

			expect(store.incomingCall).toEqual(event);
		});

		it('handleIncomingCall ignores when already in call', () => {
			store.activeCall = { callId: 'call-0' } as any;

			store.handleIncomingCall({ callId: 'call-1' } as any);

			expect(store.incomingCall).toBe(null);
		});

		it('handleCallDeclined clears active call', () => {
			store.activeCall = { callId: 'call-1' } as any;

			store.handleCallDeclined({ callId: 'call-1', dmChannelId: 'dm-1' });

			expect(store.activeCall).toBe(null);
		});

		it('handleCallDeclined ignores wrong call', () => {
			store.activeCall = { callId: 'call-1' } as any;

			store.handleCallDeclined({ callId: 'call-2', dmChannelId: 'dm-1' });

			expect(store.activeCall).not.toBe(null);
		});

		it('handleCallEnded cleans up call state', () => {
			store.activeCall = { callId: 'call-1' } as any;

			store.handleCallEnded({ callId: 'call-1', dmChannelId: 'dm-1', endReason: 'normal' });

			expect(store.activeCall).toBe(null);
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});

		it('handleCallMissed clears both active and incoming', () => {
			store.activeCall = { callId: 'call-1' } as any;
			store.incomingCall = { callId: 'call-1' } as any;

			store.handleCallMissed({ callId: 'call-1', dmChannelId: 'dm-1' });

			expect(store.activeCall).toBe(null);
			expect(store.incomingCall).toBe(null);
		});
	});

	describe('localStorage restoration', () => {
		it('restores user volumes from localStorage', () => {
			localStorage.setItem('codec-user-volumes', JSON.stringify({ u2: 0.3, u3: 0.7 }));
			const restored = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
			expect(restored.userVolumes.get('u2')).toBe(0.3);
			expect(restored.userVolumes.get('u3')).toBe(0.7);
		});

		it('restores voice preferences from localStorage', () => {
			localStorage.setItem('codec-voice-preferences', JSON.stringify({ inputMode: 'push-to-talk', pttKey: 'Space' }));
			const restored = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
			expect(restored.voiceInputMode).toBe('push-to-talk');
			expect(restored.pttKey).toBe('Space');
		});

		it('handles corrupt localStorage gracefully', () => {
			localStorage.setItem('codec-user-volumes', 'not-json');
			localStorage.setItem('codec-voice-preferences', 'bad');
			const restored = new VoiceStore(auth, api, ui, hub, voice, 'ws://localhost:7880');
			expect(restored.userVolumes.size).toBe(0);
			expect(restored.voiceInputMode).toBe('voice-activity');
		});
	});

	describe('teardownOnDisconnect', () => {
		it('resets all voice state', () => {
			store.activeVoiceChannelId = 'ch-1';
			store.activeCall = { callId: 'call-1' } as any;
			store.incomingCall = { callId: 'call-2' } as any;
			store.isMuted = true;
			store.isDeafened = true;
			store.isVideoEnabled = true;
			store.isScreenSharing = true;

			store.teardownOnDisconnect();

			expect(store.activeVoiceChannelId).toBe(null);
			expect(store.activeCall).toBe(null);
			expect(store.incomingCall).toBe(null);
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
			expect(store.isVideoEnabled).toBe(false);
			expect(store.isScreenSharing).toBe(false);
			expect(voice.leave).toHaveBeenCalled();
		});
	});

	describe('reset', () => {
		it('resets all state including members', () => {
			store.voiceChannelMembers = new Map([['ch-1', []]]);
			store.userVolumes = new Map([['u2', 0.5]]);

			store.reset();

			expect(store.voiceChannelMembers.size).toBe(0);
			expect(store.userVolumes.size).toBe(0);
		});
	});
});
