import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('svelte', () => ({
	getContext: vi.fn(),
	setContext: vi.fn()
}));

vi.mock('$env/dynamic/public', () => ({
	env: { PUBLIC_LIVEKIT_URL: 'ws://test:7880' }
}));

// Must import after mocking
const { VoiceStore } = await import('./voice-store.svelte.js');

type VoiceChannelMember = import('$lib/types/index.js').VoiceChannelMember;
type UserJoinedVoiceEvent = import('$lib/services/chat-hub.js').UserJoinedVoiceEvent;
type UserLeftVoiceEvent = import('$lib/services/chat-hub.js').UserLeftVoiceEvent;
type VoiceStateUpdatedEvent = import('$lib/services/chat-hub.js').VoiceStateUpdatedEvent;
type CallDeclinedEvent = import('$lib/services/chat-hub.js').CallDeclinedEvent;
type CallEndedEvent = import('$lib/services/chat-hub.js').CallEndedEvent;
type CallMissedEvent = import('$lib/services/chat-hub.js').CallMissedEvent;
type IncomingCallEvent = import('$lib/services/chat-hub.js').IncomingCallEvent;

function createMocks() {
	const auth = {
		idToken: 'test-token',
		me: {
			user: { id: 'me-id', displayName: 'Me', avatarUrl: null }
		},
		effectiveDisplayName: 'Me'
	} as any;

	const api = {
		getVoiceStates: vi.fn().mockResolvedValue([])
	} as any;

	const ui = { setError: vi.fn() } as any;

	const hub = {
		joinVoiceChannel: vi.fn().mockResolvedValue({
			token: 'lk-token',
			members: [] as VoiceChannelMember[]
		}),
		leaveVoiceChannel: vi.fn().mockResolvedValue(undefined),
		updateVoiceState: vi.fn().mockResolvedValue(undefined),
		startCall: vi.fn().mockResolvedValue({
			callId: 'call-1',
			recipientUserId: 'u-2',
			recipientDisplayName: 'Bob',
			recipientAvatarUrl: null
		}),
		acceptCall: vi.fn().mockResolvedValue({ token: 'lk-call-token' }),
		declineCall: vi.fn().mockResolvedValue(undefined),
		endCall: vi.fn().mockResolvedValue(undefined),
		setupCallTransports: vi.fn().mockResolvedValue({ token: 'lk-caller-token' })
	} as any;

	const voice = {
		join: vi.fn().mockResolvedValue(undefined),
		leave: vi.fn().mockResolvedValue(undefined),
		setMuted: vi.fn().mockResolvedValue(undefined),
		startVideo: vi.fn().mockResolvedValue({ kind: 'video' }),
		stopVideo: vi.fn().mockResolvedValue(undefined),
		startScreenShare: vi.fn().mockResolvedValue({
			kind: 'video',
			addEventListener: vi.fn(),
			removeEventListener: vi.fn()
		}),
		stopScreenShare: vi.fn().mockResolvedValue(undefined),
		teardownSync: vi.fn()
	} as any;

	return { auth, api, ui, hub, voice };
}

describe('VoiceStore', () => {
	let store: InstanceType<typeof VoiceStore>;
	let auth: ReturnType<typeof createMocks>['auth'];
	let api: ReturnType<typeof createMocks>['api'];
	let ui: ReturnType<typeof createMocks>['ui'];
	let hub: ReturnType<typeof createMocks>['hub'];
	let voice: ReturnType<typeof createMocks>['voice'];

	beforeEach(() => {
		vi.clearAllMocks();
		localStorage.clear();
		const mocks = createMocks();
		auth = mocks.auth;
		api = mocks.api;
		ui = mocks.ui;
		hub = mocks.hub;
		voice = mocks.voice;
		store = new VoiceStore(auth, api, ui, hub, voice);
	});

	describe('initial state', () => {
		it('has null activeVoiceChannelId', () => {
			expect(store.activeVoiceChannelId).toBeNull();
		});

		it('has empty voiceChannelMembers', () => {
			expect(store.voiceChannelMembers.size).toBe(0);
		});

		it('has isMuted false', () => {
			expect(store.isMuted).toBe(false);
		});

		it('has isDeafened false', () => {
			expect(store.isDeafened).toBe(false);
		});

		it('has isJoiningVoice false', () => {
			expect(store.isJoiningVoice).toBe(false);
		});

		it('has no active call', () => {
			expect(store.activeCall).toBeNull();
		});

		it('has no incoming call', () => {
			expect(store.incomingCall).toBeNull();
		});

		it('has voice-activity input mode by default', () => {
			expect(store.voiceInputMode).toBe('voice-activity');
		});

		it('has KeyV as default ptt key', () => {
			expect(store.pttKey).toBe('KeyV');
		});

		it('has video and screen share disabled', () => {
			expect(store.isVideoEnabled).toBe(false);
			expect(store.isScreenSharing).toBe(false);
		});
	});

	describe('joinVoiceChannel', () => {
		it('joins a voice channel via hub and voice service', async () => {
			const members: VoiceChannelMember[] = [
				{ userId: 'u-1', displayName: 'Alice', avatarUrl: null, isMuted: false, isDeafened: false }
			];
			hub.joinVoiceChannel.mockResolvedValue({ token: 'lk-token', members });

			await store.joinVoiceChannel('ch-1');

			expect(hub.joinVoiceChannel).toHaveBeenCalledWith('ch-1');
			expect(voice.join).toHaveBeenCalledWith('lk-token', 'ws://test:7880', expect.any(Object));
			expect(store.activeVoiceChannelId).toBe('ch-1');
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
			expect(store.isJoiningVoice).toBe(false);
		});

		it('populates voiceChannelMembers with returned members and self', async () => {
			const members: VoiceChannelMember[] = [
				{ userId: 'u-1', displayName: 'Alice', avatarUrl: null, isMuted: false, isDeafened: false }
			];
			hub.joinVoiceChannel.mockResolvedValue({ token: 'lk-token', members });

			await store.joinVoiceChannel('ch-1');

			const channelMembers = store.voiceChannelMembers.get('ch-1');
			expect(channelMembers).toBeDefined();
			expect(channelMembers!.length).toBe(2); // Alice + self
			expect(channelMembers!.some((m: VoiceChannelMember) => m.userId === 'me-id')).toBe(true);
			expect(channelMembers!.some((m: VoiceChannelMember) => m.userId === 'u-1')).toBe(true);
		});

		it('does not add self if already in members', async () => {
			const members: VoiceChannelMember[] = [
				{ userId: 'me-id', displayName: 'Me', avatarUrl: null, isMuted: false, isDeafened: false }
			];
			hub.joinVoiceChannel.mockResolvedValue({ token: 'lk-token', members });

			await store.joinVoiceChannel('ch-1');

			const channelMembers = store.voiceChannelMembers.get('ch-1');
			expect(channelMembers!.filter((m: VoiceChannelMember) => m.userId === 'me-id').length).toBe(1);
		});

		it('does not join if already joining', async () => {
			store.isJoiningVoice = true;

			await store.joinVoiceChannel('ch-1');

			expect(hub.joinVoiceChannel).not.toHaveBeenCalled();
		});

		it('leaves existing channel before joining new one', async () => {
			// First join
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			// Second join should leave first
			await store.joinVoiceChannel('ch-2');

			expect(hub.leaveVoiceChannel).toHaveBeenCalled();
			expect(voice.leave).toHaveBeenCalled();
			expect(store.activeVoiceChannelId).toBe('ch-2');
		});

		it('handles join failure with DOMException NotAllowedError', async () => {
			const err = new DOMException('Permission denied', 'NotAllowedError');
			voice.join.mockRejectedValue(err);

			await store.joinVoiceChannel('ch-1');

			expect(ui.setError).toHaveBeenCalled();
			const errorArg = ui.setError.mock.calls[0][0];
			expect(errorArg.message).toContain('Microphone access');
			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.isJoiningVoice).toBe(false);
		});

		it('handles join failure with DOMException NotFoundError', async () => {
			const err = new DOMException('No device', 'NotFoundError');
			voice.join.mockRejectedValue(err);

			await store.joinVoiceChannel('ch-1');

			expect(ui.setError).toHaveBeenCalled();
			const errorArg = ui.setError.mock.calls[0][0];
			expect(errorArg.message).toContain('No microphone found');
		});

		it('handles generic join failure', async () => {
			const err = new Error('network fail');
			voice.join.mockRejectedValue(err);

			await store.joinVoiceChannel('ch-1');

			expect(ui.setError).toHaveBeenCalledWith(err);
			expect(store.activeVoiceChannelId).toBeNull();
		});
	});

	describe('leaveVoiceChannel', () => {
		it('leaves the current voice channel', async () => {
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			await store.leaveVoiceChannel();

			expect(hub.leaveVoiceChannel).toHaveBeenCalled();
			expect(voice.leave).toHaveBeenCalled();
			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});

		it('removes self from voiceChannelMembers', async () => {
			await store.joinVoiceChannel('ch-1');

			await store.leaveVoiceChannel();

			const members = store.voiceChannelMembers.get('ch-1') ?? [];
			expect(members.some((m: VoiceChannelMember) => m.userId === 'me-id')).toBe(false);
		});

		it('does nothing when not in a voice channel', async () => {
			await store.leaveVoiceChannel();

			expect(hub.leaveVoiceChannel).not.toHaveBeenCalled();
			expect(voice.leave).not.toHaveBeenCalled();
		});
	});

	describe('toggleMute', () => {
		it('toggles mute state', async () => {
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			await store.toggleMute();

			expect(store.isMuted).toBe(true);
			expect(voice.setMuted).toHaveBeenCalledWith(true);
			expect(hub.updateVoiceState).toHaveBeenCalledWith(true, false);
		});

		it('toggles mute back to unmuted', async () => {
			await store.joinVoiceChannel('ch-1');
			await store.toggleMute(); // mute
			vi.clearAllMocks();

			await store.toggleMute(); // unmute

			expect(store.isMuted).toBe(false);
			expect(voice.setMuted).toHaveBeenCalledWith(false);
			expect(hub.updateVoiceState).toHaveBeenCalledWith(false, false);
		});
	});

	describe('toggleDeafen', () => {
		it('deafening also mutes', async () => {
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			await store.toggleDeafen();

			expect(store.isDeafened).toBe(true);
			expect(store.isMuted).toBe(true);
			expect(voice.setMuted).toHaveBeenCalledWith(true);
			expect(hub.updateVoiceState).toHaveBeenCalledWith(true, true);
		});

		it('un-deafening does not auto-unmute', async () => {
			await store.joinVoiceChannel('ch-1');
			await store.toggleDeafen(); // deafen (also mutes)
			vi.clearAllMocks();

			await store.toggleDeafen(); // un-deafen

			expect(store.isDeafened).toBe(false);
			// isMuted remains true because toggleDeafen does not unmute
			expect(store.isMuted).toBe(true);
		});
	});

	describe('handleUserJoinedVoice', () => {
		it('adds user to voiceChannelMembers for the channel', () => {
			const event: UserJoinedVoiceEvent = {
				channelId: 'ch-1',
				userId: 'u-2',
				displayName: 'Bob',
				avatarUrl: null
			};

			store.handleUserJoinedVoice(event);

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members).toBeDefined();
			expect(members!.length).toBe(1);
			expect(members![0].userId).toBe('u-2');
			expect(members![0].isMuted).toBe(false);
			expect(members![0].isDeafened).toBe(false);
		});

		it('does not add duplicate user', () => {
			const event: UserJoinedVoiceEvent = {
				channelId: 'ch-1',
				userId: 'u-2',
				displayName: 'Bob',
				avatarUrl: null
			};

			store.handleUserJoinedVoice(event);
			store.handleUserJoinedVoice(event);

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members!.length).toBe(1);
		});
	});

	describe('handleUserLeftVoice', () => {
		it('removes user from voiceChannelMembers', () => {
			// First add user
			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u-2',
				displayName: 'Bob',
				avatarUrl: null
			});

			store.handleUserLeftVoice({ channelId: 'ch-1', userId: 'u-2' });

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members).toEqual([]);
		});

		it('handles removing user from empty channel gracefully', () => {
			store.handleUserLeftVoice({ channelId: 'ch-1', userId: 'u-nonexistent' });

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members).toEqual([]);
		});
	});

	describe('handleVoiceStateUpdated', () => {
		it('updates muted/deafened state for the user', () => {
			// Add user first
			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u-2',
				displayName: 'Bob',
				avatarUrl: null
			});

			const event: VoiceStateUpdatedEvent = {
				channelId: 'ch-1',
				userId: 'u-2',
				isMuted: true,
				isDeafened: true
			};

			store.handleVoiceStateUpdated(event);

			const members = store.voiceChannelMembers.get('ch-1');
			expect(members![0].isMuted).toBe(true);
			expect(members![0].isDeafened).toBe(true);
		});

		it('does not change other users in same channel', () => {
			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u-1',
				displayName: 'Alice',
				avatarUrl: null
			});
			store.handleUserJoinedVoice({
				channelId: 'ch-1',
				userId: 'u-2',
				displayName: 'Bob',
				avatarUrl: null
			});

			store.handleVoiceStateUpdated({
				channelId: 'ch-1',
				userId: 'u-2',
				isMuted: true,
				isDeafened: false
			});

			const members = store.voiceChannelMembers.get('ch-1')!;
			const alice = members.find((m: VoiceChannelMember) => m.userId === 'u-1')!;
			const bob = members.find((m: VoiceChannelMember) => m.userId === 'u-2')!;
			expect(alice.isMuted).toBe(false);
			expect(bob.isMuted).toBe(true);
		});
	});

	describe('handleIncomingCall', () => {
		it('sets incomingCall when no active or incoming call', () => {
			const event: IncomingCallEvent = {
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u-2',
				callerDisplayName: 'Bob',
				callerAvatarUrl: null
			};

			store.handleIncomingCall(event);

			expect(store.incomingCall).toEqual(event);
		});

		it('ignores incoming call when active call exists', async () => {
			// Set up an active call
			await store.startCall('dm-1');

			store.handleIncomingCall({
				callId: 'call-2',
				dmChannelId: 'dm-2',
				callerUserId: 'u-3',
				callerDisplayName: 'Charlie',
				callerAvatarUrl: null
			});

			expect(store.incomingCall).toBeNull();
		});

		it('ignores incoming call when another incoming call exists', () => {
			store.handleIncomingCall({
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u-2',
				callerDisplayName: 'Bob'
			});

			store.handleIncomingCall({
				callId: 'call-2',
				dmChannelId: 'dm-2',
				callerUserId: 'u-3',
				callerDisplayName: 'Charlie'
			});

			expect(store.incomingCall!.callId).toBe('call-1');
		});
	});

	describe('handleCallDeclined', () => {
		it('clears activeCall when call is declined', async () => {
			await store.startCall('dm-1');
			expect(store.activeCall).not.toBeNull();

			store.handleCallDeclined({ callId: 'call-1', dmChannelId: 'dm-1' });

			expect(store.activeCall).toBeNull();
		});

		it('ignores decline for different call', async () => {
			await store.startCall('dm-1');

			store.handleCallDeclined({ callId: 'other-call', dmChannelId: 'dm-1' });

			expect(store.activeCall).not.toBeNull();
		});
	});

	describe('handleCallEnded', () => {
		it('clears activeCall and cleans up voice', async () => {
			await store.startCall('dm-1');
			vi.clearAllMocks();

			store.handleCallEnded({
				callId: 'call-1',
				dmChannelId: 'dm-1',
				endReason: 'ended',
				durationSeconds: 60
			});

			expect(store.activeCall).toBeNull();
			expect(voice.leave).toHaveBeenCalled();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});
	});

	describe('handleCallMissed', () => {
		it('clears activeCall if matching', async () => {
			await store.startCall('dm-1');

			store.handleCallMissed({ callId: 'call-1', dmChannelId: 'dm-1' });

			expect(store.activeCall).toBeNull();
		});

		it('clears incomingCall if matching', () => {
			store.handleIncomingCall({
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u-2',
				callerDisplayName: 'Bob'
			});

			store.handleCallMissed({ callId: 'call-1', dmChannelId: 'dm-1' });

			expect(store.incomingCall).toBeNull();
		});
	});

	describe('loadAllVoiceStates', () => {
		it('loads voice states for all voice channels', async () => {
			const members: VoiceChannelMember[] = [
				{ userId: 'u-1', displayName: 'Alice', avatarUrl: null, isMuted: false, isDeafened: false }
			];
			api.getVoiceStates.mockResolvedValue(members);

			const channels = [
				{ id: 'ch-1', name: 'General', serverId: 's-1', type: 'voice' as const, position: 0 },
				{ id: 'ch-2', name: 'Text', serverId: 's-1', type: 'text' as const, position: 1 }
			];

			await store.loadAllVoiceStates(channels);

			expect(api.getVoiceStates).toHaveBeenCalledTimes(1); // Only voice channel
			expect(api.getVoiceStates).toHaveBeenCalledWith('test-token', 'ch-1');
			expect(store.voiceChannelMembers.get('ch-1')).toEqual(members);
		});

		it('does nothing when no idToken', async () => {
			auth.idToken = null;

			await store.loadAllVoiceStates([]);

			expect(api.getVoiceStates).not.toHaveBeenCalled();
		});
	});

	describe('setUserVolume', () => {
		it('stores volume and persists to localStorage', () => {
			store.setUserVolume('u-1', 0.5);

			expect(store.userVolumes.get('u-1')).toBe(0.5);
			const stored = JSON.parse(localStorage.getItem('codec-user-volumes')!);
			expect(stored['u-1']).toBe(0.5);
		});

		it('clamps volume to 0-1 range', () => {
			store.setUserVolume('u-1', 1.5);
			expect(store.userVolumes.get('u-1')).toBeUndefined(); // 1.0 removes entry

			store.setUserVolume('u-2', -0.5);
			expect(store.userVolumes.get('u-2')).toBe(0);
		});

		it('removes entry when volume is 1.0 (default)', () => {
			store.setUserVolume('u-1', 0.5);
			expect(store.userVolumes.has('u-1')).toBe(true);

			store.setUserVolume('u-1', 1.0);
			expect(store.userVolumes.has('u-1')).toBe(false);
		});
	});

	describe('resetUserVolume', () => {
		it('resets volume to 1.0 (removes entry)', () => {
			store.setUserVolume('u-1', 0.3);
			expect(store.userVolumes.has('u-1')).toBe(true);

			store.resetUserVolume('u-1');
			expect(store.userVolumes.has('u-1')).toBe(false);
		});
	});

	describe('voice preferences', () => {
		it('loads preferences from localStorage on construction', () => {
			localStorage.setItem(
				'codec-voice-preferences',
				JSON.stringify({ inputMode: 'push-to-talk', pttKey: 'Space' })
			);

			const store2 = new VoiceStore(auth, api, ui, hub, voice);

			expect(store2.voiceInputMode).toBe('push-to-talk');
			expect(store2.pttKey).toBe('Space');
		});

		it('ignores invalid preferences in localStorage', () => {
			localStorage.setItem('codec-voice-preferences', 'not-json');

			const store2 = new VoiceStore(auth, api, ui, hub, voice);

			expect(store2.voiceInputMode).toBe('voice-activity');
			expect(store2.pttKey).toBe('KeyV');
		});

		it('setPttKey updates key and persists', () => {
			store.setPttKey('Space');

			expect(store.pttKey).toBe('Space');
			const stored = JSON.parse(localStorage.getItem('codec-voice-preferences')!);
			expect(stored.pttKey).toBe('Space');
		});
	});

	describe('teardownOnDisconnect', () => {
		it('resets all voice state', async () => {
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			store.teardownOnDisconnect();

			expect(voice.leave).toHaveBeenCalled();
			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.activeCall).toBeNull();
			expect(store.incomingCall).toBeNull();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});
	});

	describe('reset', () => {
		it('clears all state including members and volumes', async () => {
			await store.joinVoiceChannel('ch-1');
			store.setUserVolume('u-1', 0.5);
			vi.clearAllMocks();

			store.reset();

			expect(store.activeVoiceChannelId).toBeNull();
			expect(store.voiceChannelMembers.size).toBe(0);
			expect(store.userVolumes.size).toBe(0);
		});
	});

	describe('startCall', () => {
		it('starts a call via hub', async () => {
			await store.startCall('dm-1');

			expect(hub.startCall).toHaveBeenCalledWith('dm-1');
			expect(store.activeCall).not.toBeNull();
			expect(store.activeCall!.callId).toBe('call-1');
			expect(store.activeCall!.status).toBe('ringing');
			expect(store.activeCall!.dmChannelId).toBe('dm-1');
		});

		it('does nothing if already in a call', async () => {
			await store.startCall('dm-1');
			vi.clearAllMocks();

			await store.startCall('dm-2');

			expect(hub.startCall).not.toHaveBeenCalled();
		});

		it('does nothing if incoming call exists', () => {
			store.handleIncomingCall({
				callId: 'call-x',
				dmChannelId: 'dm-x',
				callerUserId: 'u-x',
				callerDisplayName: 'X'
			});

			store.startCall('dm-1');

			expect(hub.startCall).not.toHaveBeenCalled();
		});

		it('leaves voice channel before starting call', async () => {
			await store.joinVoiceChannel('ch-1');
			vi.clearAllMocks();

			await store.startCall('dm-1');

			expect(hub.leaveVoiceChannel).toHaveBeenCalled();
		});
	});

	describe('declineCall', () => {
		it('clears incomingCall and notifies hub', async () => {
			store.handleIncomingCall({
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u-2',
				callerDisplayName: 'Bob'
			});

			await store.declineCall('call-1');

			expect(store.incomingCall).toBeNull();
			expect(hub.declineCall).toHaveBeenCalledWith('call-1');
		});

		it('does nothing for non-matching call', async () => {
			store.handleIncomingCall({
				callId: 'call-1',
				dmChannelId: 'dm-1',
				callerUserId: 'u-2',
				callerDisplayName: 'Bob'
			});

			await store.declineCall('call-other');

			expect(store.incomingCall).not.toBeNull();
			expect(hub.declineCall).not.toHaveBeenCalled();
		});
	});

	describe('endCall', () => {
		it('ends active call and cleans up', async () => {
			await store.startCall('dm-1');
			vi.clearAllMocks();

			await store.endCall();

			expect(hub.endCall).toHaveBeenCalled();
			expect(voice.leave).toHaveBeenCalled();
			expect(store.activeCall).toBeNull();
			expect(store.isMuted).toBe(false);
			expect(store.isDeafened).toBe(false);
		});

		it('does nothing when no active call', async () => {
			await store.endCall();

			expect(hub.endCall).not.toHaveBeenCalled();
		});
	});

	describe('toggleScreenShare', () => {
		it('does nothing when not in a voice channel or call', async () => {
			await store.toggleScreenShare();

			expect(voice.startScreenShare).not.toHaveBeenCalled();
			expect(store.isScreenSharing).toBe(false);
		});

		it('starts screen share and registers ended listener on the track', async () => {
			await store.joinVoiceChannel('ch-1');

			await store.toggleScreenShare();

			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			const track = await (voice.startScreenShare.mock.results[0].value as Promise<any>);
			expect(voice.startScreenShare).toHaveBeenCalledOnce();
			expect(store.isScreenSharing).toBe(true);
			expect(track.addEventListener).toHaveBeenCalledWith('ended', expect.any(Function));
		});

		it('removes the exact ended listener when stopping screen share', async () => {
			await store.joinVoiceChannel('ch-1');
			await store.toggleScreenShare();

			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			const track = await (voice.startScreenShare.mock.results[0].value as Promise<any>);
			const registeredHandler = track.addEventListener.mock.calls[0][1] as () => void;

			await store.toggleScreenShare();

			expect(voice.stopScreenShare).toHaveBeenCalledOnce();
			expect(store.isScreenSharing).toBe(false);
			expect(track.removeEventListener).toHaveBeenCalledWith('ended', registeredHandler);
		});

		it('cleans up previous listener before registering a new one on repeated screen shares', async () => {
			await store.joinVoiceChannel('ch-1');

			await store.toggleScreenShare();
			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			const track = await (voice.startScreenShare.mock.results[0].value as Promise<any>);
			const firstHandler = track.addEventListener.mock.calls[0][1] as () => void;

			await store.toggleScreenShare(); // stop — removes firstHandler from track

			expect(track.removeEventListener).toHaveBeenCalledWith('ended', firstHandler);
			(track.removeEventListener as ReturnType<typeof vi.fn>).mockClear();

			await store.toggleScreenShare(); // start again — no stale handler to remove
			expect(track.removeEventListener).not.toHaveBeenCalled(); // previous was already cleaned up
			expect(track.addEventListener).toHaveBeenCalledTimes(2); // once per start
		});
	});
});
