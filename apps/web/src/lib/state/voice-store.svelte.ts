// apps/web/src/lib/state/voice-store.svelte.ts
import { getContext, setContext } from 'svelte';
import { env } from '$env/dynamic/public';
import type { VoiceChannelMember, Channel } from '$lib/types/index.js';
import type { ApiClient } from '$lib/api/client.js';
import type { ChatHubService } from '$lib/services/chat-hub.js';
import type {
	UserJoinedVoiceEvent,
	UserLeftVoiceEvent,
	VoiceStateUpdatedEvent,
	IncomingCallEvent,
	CallAcceptedEvent,
	CallDeclinedEvent,
	CallEndedEvent,
	CallMissedEvent
} from '$lib/services/chat-hub.js';
import type { VoiceService } from '$lib/services/voice-service.js';
import type { AuthStore } from './auth-store.svelte.js';
import type { UIStore } from './ui-store.svelte.js';

const VOICE_KEY = Symbol('voice-store');

export function createVoiceStore(
	auth: AuthStore,
	api: ApiClient,
	ui: UIStore,
	hub: ChatHubService,
	voice: VoiceService
): VoiceStore {
	const store = new VoiceStore(auth, api, ui, hub, voice);
	setContext(VOICE_KEY, store);
	return store;
}

export function getVoiceStore(): VoiceStore {
	return getContext<VoiceStore>(VOICE_KEY);
}

export class VoiceStore {
	/* ───── $state fields ───── */
	activeVoiceChannelId = $state<string | null>(null);
	/** Map of channelId -> list of connected members, for sidebar display. */
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
	/** Whether the local camera is enabled. */
	isVideoEnabled = $state(false);
	/** Whether the local screen share is enabled. */
	isScreenSharing = $state(false);
	/** Local video track for self-preview (camera). */
	localVideoTrack = $state<MediaStreamTrack | null>(null);
	/** Local screen share track for self-preview. */
	localScreenTrack = $state<MediaStreamTrack | null>(null);
	/** Handler for screen share 'ended' event, stored for cleanup. */
	private _screenEndedHandler: (() => void) | null = null;
	/** Remote video tracks: "userId:label" -> { track, label } */
	remoteVideoTracks = $state<Map<string, { track: MediaStreamTrack; label: string }>>(new Map());

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

	/* ───── private fields ───── */
	private audioContext: AudioContext | null = null;
	private audioNodes = new Map<
		string,
		{ element: HTMLAudioElement; source: MediaStreamAudioSourceNode; gain: GainNode }
	>();
	private pttKeydownHandler: ((e: KeyboardEvent) => void) | null = null;
	private pttKeyupHandler: ((e: KeyboardEvent) => void) | null = null;
	/** The LiveKit server URL from env. */
	private liveKitUrl: string;

	constructor(
		private readonly auth: AuthStore,
		private readonly api: ApiClient,
		private readonly ui: UIStore,
		private readonly hub: ChatHubService,
		private readonly voice: VoiceService
	) {
		this._loadUserVolumes();
		this._loadVoicePreferences();
		this.liveKitUrl = env.PUBLIC_LIVEKIT_URL || 'ws://localhost:7880';
	}

	/* ��══════════════════ Core Voice ═══════════════════ */

	async joinVoiceChannel(channelId: string): Promise<void> {
		if (this.isJoiningVoice) return;

		// Leave existing voice session first
		if (this.activeVoiceChannelId) {
			await this.leaveVoiceChannel();
		}

		this.isJoiningVoice = true;
		try {
			// Get token from hub (also persists VoiceState server-side)
			const result = await this.hub.joinVoiceChannel(channelId);
			const members = result.members as VoiceChannelMember[];

			// Connect to LiveKit room
			await this.voice.join(result.token, this.liveKitUrl, {
				onTrackSubscribed: (userId, track, label) => {
					if (label === 'audio') {
						this._attachRemoteAudio(userId, track);
					} else {
						this._attachRemoteVideo(userId, track, label);
					}
				},
				onTrackUnsubscribed: (userId, label) => {
					if (label === 'audio') {
						this._detachRemoteAudio(userId);
					} else {
						this._detachRemoteVideo(userId, label);
					}
				},
				onDisconnected: () => {
					// LiveKit disconnected unexpectedly
					this._cleanupVoiceState();
				}
			});

			this.activeVoiceChannelId = channelId;
			this.isMuted = false;
			this.isDeafened = false;

			if (this.voiceInputMode === 'push-to-talk') {
				await this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			}

			// Seed the local member map
			const memberMap = new Map(this.voiceChannelMembers);
			const existing = memberMap.get(channelId) ?? [];
			const merged = [...members];
			for (const m of existing) {
				if (!merged.some((e) => e.userId === m.userId)) {
					merged.push(m);
				}
			}
			// Ensure self is always in the list
			if (this.auth.me && !merged.some((m) => m.userId === this.auth.me!.user.id)) {
				merged.push({
					userId: this.auth.me.user.id,
					displayName: this.auth.effectiveDisplayName,
					avatarUrl: this.auth.me.user.avatarUrl ?? null,
					isMuted: false,
					isDeafened: false
				});
			}
			memberMap.set(channelId, merged);
			this.voiceChannelMembers = memberMap;
		} catch (e) {
			console.error('[Voice] Failed to join voice channel:', e);

			// Clean up any partial join state
			try {
				await this.hub.leaveVoiceChannel();
			} catch {
				/* ignore */
			}
			await this.voice.leave();
			this._cleanupRemoteAudio();
			this._cleanupRemoteVideo();
			this._removePttListeners();
			this.isPttActive = false;
			this.isVideoEnabled = false;
			this.isScreenSharing = false;
			this.localVideoTrack = null;
			this.localScreenTrack = null;
			this.activeVoiceChannelId = null;

			if (
				e instanceof DOMException &&
				(e.name === 'NotAllowedError' || e.name === 'PermissionDeniedError')
			) {
				const isSystemDenied = e.message?.includes('Permission denied by system');
				const message = isSystemDenied
					? 'Microphone access was denied by your operating system. On macOS, go to System Settings → Privacy & Security → Microphone and enable your browser.'
					: 'Microphone access is required to join a voice channel. Please allow microphone access in your browser and try again.';
				this.ui.setError(new Error(message));
			} else if (e instanceof DOMException && e.name === 'NotFoundError') {
				this.ui.setError(
					new Error(
						'No microphone found. Please connect a microphone to join voice channels.'
					)
				);
			} else {
				this.ui.setError(e);
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
		this._cleanupRemoteVideo();
		this.activeVoiceChannelId = null;
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this.isVideoEnabled = false;
		this.isScreenSharing = false;
		this.localVideoTrack = null;
		this.localScreenTrack = null;
		this._removePttListeners();

		// Remove self from the local members map
		if (this.auth.me) {
			const memberMap = new Map(this.voiceChannelMembers);
			const currentMembers = memberMap.get(channelId) ?? [];
			memberMap.set(
				channelId,
				currentMembers.filter((m) => m.userId !== this.auth.me!.user.id)
			);
			this.voiceChannelMembers = memberMap;
		}
	}

	async toggleMute(): Promise<void> {
		this.isMuted = !this.isMuted;
		if (this.voiceInputMode === 'push-to-talk') {
			if (this.isMuted) {
				this._removePttListeners();
				await this.voice.setMuted(true);
				this.isPttActive = false;
			} else {
				await this.voice.setMuted(true); // still muted until PTT key held
				this._registerPttListeners();
			}
		} else {
			await this.voice.setMuted(this.isMuted);
		}
		await this.hub.updateVoiceState(this.isMuted, this.isDeafened);
	}

	async toggleDeafen(): Promise<void> {
		this.isDeafened = !this.isDeafened;
		if (this.isDeafened && !this.isMuted) {
			this.isMuted = true;
			await this.voice.setMuted(true);
		}
		// Mute/unmute remote audio based on deafened state
		for (const [userId, nodes] of this.audioNodes) {
			nodes.gain.gain.value = this.isDeafened ? 0 : (this.userVolumes.get(userId) ?? 1.0);
		}
		await this.hub.updateVoiceState(this.isMuted, this.isDeafened);
	}

	async loadAllVoiceStates(channels: Channel[]): Promise<void> {
		if (!this.auth.idToken) return;
		const voiceChannels = channels.filter((c) => c.type === 'voice');
		const memberMap = new Map(this.voiceChannelMembers);

		await Promise.all(
			voiceChannels.map(async (ch) => {
				try {
					const members = await this.api.getVoiceStates(this.auth.idToken!, ch.id);
					memberMap.set(ch.id, members);
				} catch {
					// ignore individual failures
				}
			})
		);

		this.voiceChannelMembers = memberMap;
	}

	/* ═══════════════════ Remote Audio ═══════════════════ */

	private _attachRemoteAudio(userId: string, track: MediaStreamTrack): void {
		this._detachRemoteAudio(userId); // guard against re-attach
		if (!this.audioContext) {
			this.audioContext = new AudioContext();
		}
		if (this.audioContext.state === 'suspended') {
			this.audioContext.resume().catch(() => {});
		}

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
		this.audioNodes.set(userId, { element: el, source, gain });
	}

	private _detachRemoteAudio(userId: string): void {
		const nodes = this.audioNodes.get(userId);
		if (nodes) {
			nodes.source.disconnect();
			nodes.gain.disconnect();
			nodes.element.pause();
			nodes.element.srcObject = null;
			this.audioNodes.delete(userId);
		}
	}

	private _cleanupRemoteAudio(): void {
		for (const nodes of this.audioNodes.values()) {
			nodes.source.disconnect();
			nodes.gain.disconnect();
			nodes.element.pause();
			nodes.element.srcObject = null;
		}
		this.audioNodes.clear();
	}

	/* ═══════════════════ User Volumes ═══════════════════ */

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

	setUserVolume(userId: string, volume: number): void {
		const clamped = Math.max(0, Math.min(1, volume));
		const updated = new Map(this.userVolumes);
		if (clamped === 1.0) {
			updated.delete(userId);
		} else {
			updated.set(userId, clamped);
		}
		this.userVolumes = updated;
		this._saveUserVolumes();

		if (!this.isDeafened) {
			const nodes = this.audioNodes.get(userId);
			if (nodes) nodes.gain.gain.value = clamped;
		}
	}

	resetUserVolume(userId: string): void {
		this.setUserVolume(userId, 1.0);
	}

	/* ═══════════════════ Voice Preferences ═══════════════════ */

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
		localStorage.setItem(
			'codec-voice-preferences',
			JSON.stringify({
				inputMode: this.voiceInputMode,
				pttKey: this.pttKey
			})
		);
	}

	async setVoiceInputMode(mode: 'voice-activity' | 'push-to-talk'): Promise<void> {
		this.voiceInputMode = mode;
		this._saveVoicePreferences();

		if (this.activeVoiceChannelId) {
			if (mode === 'push-to-talk') {
				await this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			} else {
				this._removePttListeners();
				this.isPttActive = false;
				if (!this.isMuted) {
					await this.voice.setMuted(false);
				}
			}
		}
	}

	setPttKey(code: string): void {
		this.pttKey = code;
		this._saveVoicePreferences();
	}

	/* ═══════════════════ PTT Listeners ═══════════════════ */

	private _registerPttListeners(): void {
		this._removePttListeners();

		this.pttKeydownHandler = async (e: KeyboardEvent) => {
			if (e.code !== this.pttKey || e.repeat) return;
			const tag = (document.activeElement as HTMLElement)?.tagName;
			if (
				tag === 'INPUT' ||
				tag === 'TEXTAREA' ||
				(document.activeElement as HTMLElement)?.isContentEditable
			)
				return;

			this.isPttActive = true;
			await this.voice.setMuted(false);
		};

		this.pttKeyupHandler = async (e: KeyboardEvent) => {
			if (e.code !== this.pttKey) return;

			this.isPttActive = false;
			await this.voice.setMuted(true);
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

	/* ═══════════════════ Video & Screen Share ═══════════════════ */

	async toggleVideo(): Promise<void> {
		if (!this.activeVoiceChannelId && !this.activeCall) return;

		if (this.isVideoEnabled) {
			await this.voice.stopVideo();
			this.isVideoEnabled = false;
			this.localVideoTrack = null;
		} else {
			try {
				const track = await this.voice.startVideo();
				this.isVideoEnabled = true;
				this.localVideoTrack = track;
			} catch (e) {
				console.error('[Video] Failed to start camera:', e);
				if (
					e instanceof DOMException &&
					(e.name === 'NotAllowedError' || e.name === 'PermissionDeniedError')
				) {
					this.ui.setError(
						new Error(
							'Camera access is required. Please allow camera access in your browser and try again.'
						)
					);
				} else if (e instanceof DOMException && e.name === 'NotFoundError') {
					this.ui.setError(
						new Error('No camera found. Please connect a camera to enable video.')
					);
				} else {
					this.ui.setError(e);
				}
			}
		}
	}

	async toggleScreenShare(): Promise<void> {
		if (!this.activeVoiceChannelId && !this.activeCall) return;

		if (this.isScreenSharing) {
			this._cleanupScreenEndedHandler();
			await this.voice.stopScreenShare();
			this.isScreenSharing = false;
			this.localScreenTrack = null;
		} else {
			try {
				const track = await this.voice.startScreenShare();
				this.isScreenSharing = true;
				this.localScreenTrack = track;

				this._cleanupScreenEndedHandler();
				this._screenEndedHandler = () => {
					this.isScreenSharing = false;
					this.localScreenTrack = null;
					this._screenEndedHandler = null;
				};
				track.addEventListener('ended', this._screenEndedHandler);
			} catch (e) {
				if (e instanceof DOMException && e.name === 'NotAllowedError') return;
				console.error('[Video] Failed to start screen share:', e);
				this.ui.setError(e);
			}
		}
	}

	private _cleanupScreenEndedHandler(): void {
		if (this._screenEndedHandler && this.localScreenTrack) {
			this.localScreenTrack.removeEventListener('ended', this._screenEndedHandler);
		}
		this._screenEndedHandler = null;
	}

	private _attachRemoteVideo(
		userId: string,
		track: MediaStreamTrack,
		label: string
	): void {
		const key = `${userId}:${label}`;
		const updated = new Map(this.remoteVideoTracks);
		updated.set(key, { track, label });
		this.remoteVideoTracks = updated;
	}

	private _detachRemoteVideo(userId: string, label: string): void {
		const key = `${userId}:${label}`;
		const updated = new Map(this.remoteVideoTracks);
		updated.delete(key);
		this.remoteVideoTracks = updated;
	}

	private _cleanupRemoteVideo(): void {
		this.remoteVideoTracks = new Map();
	}

	/* ═══════════════════ DM Voice Calls ═══════════════════ */

	async startCall(dmChannelId: string): Promise<void> {
		if (this.activeCall || this.incomingCall) return;

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
				startedAt: new Date().toISOString()
			};
		} catch (e) {
			this.ui.setError(e);
		}
	}

	async acceptCall(callId: string): Promise<void> {
		if (!this.incomingCall || this.incomingCall.callId !== callId) return;

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
				answeredAt: new Date().toISOString()
			};

			// Connect to LiveKit room with the token from the hub
			await this.voice.join(result.token, this.liveKitUrl, {
				onTrackSubscribed: (userId, track, label) => {
					if (label === 'audio') {
						this._attachRemoteAudio(userId, track);
					} else {
						this._attachRemoteVideo(userId, track, label);
					}
				},
				onTrackUnsubscribed: (userId, label) => {
					if (label === 'audio') {
						this._detachRemoteAudio(userId);
					} else {
						this._detachRemoteVideo(userId, label);
					}
				},
				onDisconnected: () => {
					this._cleanupVoiceState();
				}
			});

			this.isMuted = false;
			this.isDeafened = false;

			if (this.voiceInputMode === 'push-to-talk') {
				await this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			}
		} catch (e) {
			console.error('[Voice] Failed to accept call:', e);
			this.activeCall = null;
			try {
				await this.hub.endCall();
			} catch {
				/* ignore */
			}
			await this.voice.leave();
			this._cleanupRemoteAudio();
			this.ui.setError(e);
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
		this._cleanupRemoteVideo();
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this.isVideoEnabled = false;
		this.isScreenSharing = false;
		this.localVideoTrack = null;
		this.localScreenTrack = null;
		this._removePttListeners();
	}

	async checkActiveCall(): Promise<void> {
		if (!this.auth.idToken) return;
		const call = await this.api.getActiveCall(this.auth.idToken);
		if (!call) return;

		if (call.status === 'ringing') {
			if (call.callerUserId === this.auth.me?.user.id) {
				this.activeCall = {
					callId: call.id,
					dmChannelId: call.dmChannelId,
					otherUserId: call.otherUserId,
					otherDisplayName: call.otherDisplayName,
					otherAvatarUrl: call.otherAvatarUrl,
					status: 'ringing',
					startedAt: call.startedAt
				};
			} else {
				this.incomingCall = {
					callId: call.id,
					dmChannelId: call.dmChannelId,
					callerUserId: call.otherUserId,
					callerDisplayName: call.otherDisplayName,
					callerAvatarUrl: call.otherAvatarUrl
				};
			}
		}
		// Active calls after refresh can't be rejoined, so end them cleanly.
		if (call.status === 'active') {
			try {
				await this.hub.endCall();
			} catch {
				/* ignore */
			}
		}
	}

	/* ═══════════════════ SignalR Handlers ═══════════════════ */

	handleUserJoinedVoice(event: UserJoinedVoiceEvent): void {
		const memberMap = new Map(this.voiceChannelMembers);
		const members = [...(memberMap.get(event.channelId) ?? [])];
		if (!members.some((m) => m.userId === event.userId)) {
			members.push({
				userId: event.userId,
				displayName: event.displayName,
				avatarUrl: event.avatarUrl ?? null,
				isMuted: false,
				isDeafened: false
			});
			memberMap.set(event.channelId, members);
			this.voiceChannelMembers = memberMap;
		}
	}

	handleUserLeftVoice(event: UserLeftVoiceEvent): void {
		const memberMap = new Map(this.voiceChannelMembers);
		const members = (memberMap.get(event.channelId) ?? []).filter(
			(m) => m.userId !== event.userId
		);
		memberMap.set(event.channelId, members);
		this.voiceChannelMembers = memberMap;
	}

	handleVoiceStateUpdated(event: VoiceStateUpdatedEvent): void {
		const memberMap = new Map(this.voiceChannelMembers);
		const members = (memberMap.get(event.channelId) ?? []).map((m) =>
			m.userId === event.userId
				? { ...m, isMuted: event.isMuted, isDeafened: event.isDeafened }
				: m
		);
		memberMap.set(event.channelId, members);
		this.voiceChannelMembers = memberMap;
	}

	handleIncomingCall(event: IncomingCallEvent): void {
		if (this.activeCall || this.incomingCall) return;
		this.incomingCall = event;
	}

	async handleCallAccepted(event: CallAcceptedEvent): Promise<void> {
		if (!this.activeCall || this.activeCall.callId !== event.callId) return;

		try {
			// Caller: get token and connect to LiveKit.
			const transportResult = await this.hub.setupCallTransports(event.callId);

			this.activeCall = {
				...this.activeCall,
				status: 'active',
				answeredAt: new Date().toISOString()
			};

			await this.voice.join(transportResult.token, this.liveKitUrl, {
				onTrackSubscribed: (userId, track, label) => {
					if (label === 'audio') {
						this._attachRemoteAudio(userId, track);
					} else {
						this._attachRemoteVideo(userId, track, label);
					}
				},
				onTrackUnsubscribed: (userId, label) => {
					if (label === 'audio') {
						this._detachRemoteAudio(userId);
					} else {
						this._detachRemoteVideo(userId, label);
					}
				},
				onDisconnected: () => {
					this._cleanupVoiceState();
				}
			});

			this.isMuted = false;
			this.isDeafened = false;

			if (this.voiceInputMode === 'push-to-talk') {
				await this.voice.setMuted(true);
				this.isPttActive = false;
				this._registerPttListeners();
			}
		} catch (e) {
			console.error('[Voice] Failed to set up call as caller:', e);
			this.activeCall = null;
			await this.voice.leave();
			this._cleanupRemoteAudio();
			this._cleanupRemoteVideo();
			this.ui.setError(e);
		}
	}

	handleCallDeclined(event: CallDeclinedEvent): void {
		if (this.activeCall?.callId === event.callId) {
			this.activeCall = null;
		}
	}

	handleCallEnded(event: CallEndedEvent): void {
		if (this.activeCall?.callId === event.callId) {
			this.activeCall = null;
			this.voice.leave();
			this._cleanupRemoteAudio();
			this._cleanupRemoteVideo();
			this.isMuted = false;
			this.isDeafened = false;
			this.isPttActive = false;
			this.isVideoEnabled = false;
			this.isScreenSharing = false;
			this.localVideoTrack = null;
			this.localScreenTrack = null;
			this._removePttListeners();
		}
	}

	handleCallMissed(event: CallMissedEvent): void {
		if (this.activeCall?.callId === event.callId) {
			this.activeCall = null;
		}
		if (this.incomingCall?.callId === event.callId) {
			this.incomingCall = null;
		}
	}

	/* ═══════════════════ Internal Cleanup ═══════════════════ */

	private async _cleanupVoiceState(): Promise<void> {
		// Notify the server so the VoiceState row is removed and other users
		// see this user leave. Without this, a LiveKit disconnect while SignalR
		// stays connected would leave a ghost entry in the voice channel sidebar.
		try {
			if (this.activeCall) {
				await this.hub.endCall();
			} else if (this.activeVoiceChannelId) {
				await this.hub.leaveVoiceChannel();
			}
		} catch {
			/* SignalR may also be disconnected — ignore */
		}

		this._cleanupRemoteAudio();
		this._cleanupRemoteVideo();
		this._removePttListeners();
		this.activeVoiceChannelId = null;
		this.activeCall = null;
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this.isVideoEnabled = false;
		this.isScreenSharing = false;
		this.localVideoTrack = null;
		this.localScreenTrack = null;
	}

	/* ═══════════════════ Teardown ═══════════════════ */

	teardownOnDisconnect(): void {
		this.voice.leave();
		this._cleanupRemoteAudio();
		this._cleanupRemoteVideo();
		this._removePttListeners();
		this.activeVoiceChannelId = null;
		this.activeCall = null;
		this.incomingCall = null;
		this.isMuted = false;
		this.isDeafened = false;
		this.isPttActive = false;
		this.isVideoEnabled = false;
		this.isScreenSharing = false;
		this.localVideoTrack = null;
		this.localScreenTrack = null;
	}

	/** Synchronous voice cleanup for beforeunload (disconnects LiveKit immediately). */
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
	}

	reset(): void {
		this.teardownOnDisconnect();
		this.voiceChannelMembers = new Map();
		this.userVolumes = new Map();
	}
}
