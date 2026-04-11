import {
	Room,
	RoomEvent,
	Track,
	type RemoteTrackPublication,
	type RemoteParticipant,
	type LocalParticipant
} from 'livekit-client';

export type VoiceServiceCallbacks = {
	onTrackSubscribed: (userId: string, track: MediaStreamTrack, label: string) => void;
	onTrackUnsubscribed: (userId: string, label: string) => void;
	onDisconnected: () => void;
};

/**
 * Manages the LiveKit WebRTC connection for a voice channel or DM call.
 *
 * Call `join()` to connect to a LiveKit room and `leave()` to disconnect cleanly.
 * The owning state layer wires callbacks into reactive UI.
 */
export class VoiceService {
	private room: Room | null = null;
	private callbacks: VoiceServiceCallbacks | null = null;

	async join(token: string, serverUrl: string, callbacks: VoiceServiceCallbacks): Promise<void> {
		this.callbacks = callbacks;

		this.room = new Room({
			adaptiveStream: true,
			dynacast: true
		});

		this.room.on(
			RoomEvent.TrackSubscribed,
			(track, publication: RemoteTrackPublication, participant: RemoteParticipant) => {
				const userId = participant.identity;
				const label = this._trackLabel(track, publication);
				callbacks.onTrackSubscribed(userId, track.mediaStreamTrack, label);
			}
		);

		this.room.on(
			RoomEvent.TrackUnsubscribed,
			(track, publication: RemoteTrackPublication, participant: RemoteParticipant) => {
				const userId = participant.identity;
				const label = this._trackLabel(track, publication);
				callbacks.onTrackUnsubscribed(userId, label);
			}
		);

		this.room.on(RoomEvent.Disconnected, () => {
			callbacks.onDisconnected();
		});

		await this.room.connect(serverUrl, token);
		await this.room.localParticipant.setMicrophoneEnabled(true);
	}

	async setMuted(muted: boolean): Promise<void> {
		if (!this.room) return;
		await this.room.localParticipant.setMicrophoneEnabled(!muted);
	}

	async startVideo(): Promise<MediaStreamTrack> {
		if (!this.room) throw new Error('Not in a voice session');
		await this.room.localParticipant.setCameraEnabled(true);
		const pub = this.room.localParticipant.getTrackPublication(Track.Source.Camera);
		if (!pub?.track?.mediaStreamTrack) throw new Error('Failed to get camera track');
		return pub.track.mediaStreamTrack;
	}

	async stopVideo(): Promise<void> {
		if (!this.room) return;
		await this.room.localParticipant.setCameraEnabled(false);
	}

	async startScreenShare(): Promise<MediaStreamTrack> {
		if (!this.room) throw new Error('Not in a voice session');
		await this.room.localParticipant.setScreenShareEnabled(true);
		const pub = this.room.localParticipant.getTrackPublication(Track.Source.ScreenShare);
		if (!pub?.track?.mediaStreamTrack) throw new Error('Failed to get screen share track');
		return pub.track.mediaStreamTrack;
	}

	async stopScreenShare(): Promise<void> {
		if (!this.room) return;
		await this.room.localParticipant.setScreenShareEnabled(false);
	}

	get isVideoActive(): boolean {
		if (!this.room) return false;
		const pub = this.room.localParticipant.getTrackPublication(Track.Source.Camera);
		return pub?.track !== undefined && !pub.isMuted;
	}

	get isScreenShareActive(): boolean {
		if (!this.room) return false;
		const pub = this.room.localParticipant.getTrackPublication(Track.Source.ScreenShare);
		return pub?.track !== undefined && !pub.isMuted;
	}

	async leave(): Promise<void> {
		if (this.room) {
			await this.room.disconnect();
			this.room = null;
		}
		this.callbacks = null;
	}

	/** Synchronous cleanup for beforeunload — disconnects immediately. */
	teardownSync(): void {
		if (this.room) {
			this.room.disconnect();
			this.room = null;
		}
		this.callbacks = null;
	}

	/** Get the local participant for volume/audio manipulation. */
	get localParticipant(): LocalParticipant | null {
		return this.room?.localParticipant ?? null;
	}

	private _trackLabel(
		track: { kind: Track.Kind; source: Track.Source },
		_publication: RemoteTrackPublication
	): string {
		if (track.source === Track.Source.ScreenShare || track.source === Track.Source.ScreenShareAudio)
			return 'screen';
		if (track.source === Track.Source.Camera) return 'video';
		if (track.kind === Track.Kind.Audio) return 'audio';
		return 'video';
	}
}
