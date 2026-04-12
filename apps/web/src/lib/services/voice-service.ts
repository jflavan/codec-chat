import { Device } from 'mediasoup-client';
import type { Transport, Producer, Consumer, TransportOptions, RtpCapabilities } from 'mediasoup-client/types';
import type { ChatHubService } from './chat-hub.js';
import type { VoiceChannelMember } from '$lib/types/index.js';

export type VoiceServiceCallbacks = {
	onNewTrack: (participantId: string, track: MediaStreamTrack, label: string) => void;
	onTrackEnded: (participantId: string, label: string) => void;
};

type MemberWithProducers = VoiceChannelMember & {
	producerId?: string;
	videoProducerId?: string;
	screenProducerId?: string;
};

/**
 * Manages the mediasoup-client WebRTC connection for a voice channel.
 *
 * Call `join()` to enter a voice channel and `leave()` to exit cleanly.
 * The owning state layer wires callbacks into reactive UI.
 */
export class VoiceService {
	private device: Device | null = null;
	private sendTransport: Transport | null = null;
	private recvTransport: Transport | null = null;
	private producer: Producer | null = null;
	private videoProducer: Producer | null = null;
	private screenProducer: Producer | null = null;
	private consumers = new Map<string, Consumer>();
	private localStream: MediaStream | null = null;
	private videoStream: MediaStream | null = null;
	private screenStream: MediaStream | null = null;
	/** Tracks producerIds already consumed to prevent duplicates from the
	 *  group-join / member-snapshot race window. */
	private consumedProducerIds = new Set<string>();
	/** Maps consumerId → label for track type identification. */
	private consumerLabels = new Map<string, string>();
	/** Stored hub and callbacks for producing after join. */
	private _hub: ChatHubService | null = null;
	/** The label to use for the next produce call. Set before transport.produce(). */
	private _pendingProduceLabel: string | null = null;

	async join(
		channelId: string,
		hub: ChatHubService,
		callbacks: VoiceServiceCallbacks
	): Promise<VoiceChannelMember[]> {
		this._hub = hub;
		// Request microphone access first — if denied or unavailable this throws
		// immediately before doing any network work (SignalR, mediasoup).
		this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
		const audioTrack = this.localStream.getAudioTracks()[0];

		this.device = new Device();

		const result = await hub.joinVoiceChannel(channelId);
		const routerRtpCapabilities = result.routerRtpCapabilities as RtpCapabilities;
		const sendTransportOptions = result.sendTransportOptions as TransportOptions;
		const recvTransportOptions = result.recvTransportOptions as TransportOptions;
		const members = result.members as MemberWithProducers[];
		const iceServers = (result as Record<string, unknown>).iceServers as
			| { urls: string[]; username: string; credential: string }[]
			| undefined;

		await this.device.load({ routerRtpCapabilities });

		// Send transport: microphone audio → SFU
		// Capture transport references into locals so event handlers don't rely on
		// `this.sendTransport` / `this.recvTransport`, which could be nulled by leave().
		const sendTransport = this.device.createSendTransport({
			...sendTransportOptions,
			...(iceServers ? { iceServers } : {}),
		});
		this.sendTransport = sendTransport;
		sendTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
			hub.connectTransport(sendTransport.id, dtlsParameters)
				.then(callback)
				.catch(errback);
		});
		sendTransport.on('produce', ({ kind, rtpParameters }, callback, errback) => {
			const label = this._pendingProduceLabel ?? kind;
			this._pendingProduceLabel = null;
			hub.produce(sendTransport.id, rtpParameters, label)
				.then((producerId) => callback({ id: producerId }))
				.catch(errback);
		});

		// Recv transport: SFU audio → speakers
		const recvTransport = this.device.createRecvTransport({
			...recvTransportOptions,
			...(iceServers ? { iceServers } : {}),
		});
		this.recvTransport = recvTransport;
		recvTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
			hub.connectTransport(recvTransport.id, dtlsParameters)
				.then(callback)
				.catch(errback);
		});

		this.producer = await sendTransport.produce({ track: audioTrack });

		// Consume any already-connected participants
		for (const member of members) {
			if (member.producerId) {
				await this.consumeProducer(member.producerId, member.participantId, hub, callbacks, 'audio');
			}
			if (member.videoProducerId) {
				await this.consumeProducer(member.videoProducerId, member.participantId, hub, callbacks, 'video');
			}
			if (member.screenProducerId) {
				await this.consumeProducer(member.screenProducerId, member.participantId, hub, callbacks, 'screen');
			}
		}

		return members;
	}

	/**
	 * Join a voice session with pre-fetched transport options (for DM calls).
	 * Skips the hub.joinVoiceChannel() call since transports are already created.
	 */
	async joinWithOptions(
		options: {
			routerRtpCapabilities: object;
			sendTransportOptions: object;
			recvTransportOptions: object;
			members?: MemberWithProducers[];
			iceServers?: { urls: string[]; username: string; credential: string }[];
		},
		hub: ChatHubService,
		callbacks: VoiceServiceCallbacks
	): Promise<VoiceChannelMember[]> {
		this._hub = hub;
		this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
		const audioTrack = this.localStream.getAudioTracks()[0];

		this.device = new Device();
		const routerRtpCapabilities = options.routerRtpCapabilities as RtpCapabilities;
		const sendTransportOptions = options.sendTransportOptions as TransportOptions;
		const recvTransportOptions = options.recvTransportOptions as TransportOptions;
		const members = (options.members ?? []) as MemberWithProducers[];
		const iceServers = options.iceServers;

		await this.device.load({ routerRtpCapabilities });

		const sendTransport = this.device.createSendTransport({
			...sendTransportOptions,
			...(iceServers ? { iceServers } : {}),
		});
		this.sendTransport = sendTransport;
		sendTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
			hub.connectTransport(sendTransport.id, dtlsParameters).then(callback).catch(errback);
		});
		sendTransport.on('produce', ({ kind, rtpParameters }, callback, errback) => {
			const label = this._pendingProduceLabel ?? kind;
			this._pendingProduceLabel = null;
			hub.produce(sendTransport.id, rtpParameters, label)
				.then((producerId) => callback({ id: producerId }))
				.catch(errback);
		});

		const recvTransport = this.device.createRecvTransport({
			...recvTransportOptions,
			...(iceServers ? { iceServers } : {}),
		});
		this.recvTransport = recvTransport;
		recvTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
			hub.connectTransport(recvTransport.id, dtlsParameters).then(callback).catch(errback);
		});

		this.producer = await sendTransport.produce({ track: audioTrack });

		for (const member of members) {
			if (member.producerId) {
				await this.consumeProducer(member.producerId, member.participantId, hub, callbacks, 'audio');
			}
			if (member.videoProducerId) {
				await this.consumeProducer(member.videoProducerId, member.participantId, hub, callbacks, 'video');
			}
			if (member.screenProducerId) {
				await this.consumeProducer(member.screenProducerId, member.participantId, hub, callbacks, 'screen');
			}
		}

		return members;
	}

	async consumeProducer(
		producerId: string,
		participantId: string,
		hub: ChatHubService,
		callbacks: VoiceServiceCallbacks,
		label: string = 'audio'
	): Promise<void> {
		if (!this.device || !this.recvTransport) return;
		// Guard against the double-consume race: group join happens before the member snapshot
		// is taken, so a NewProducer event and the initial member list can both reference the
		// same producer. Skip if already consuming this producer.
		if (this.consumedProducerIds.has(producerId)) return;
		this.consumedProducerIds.add(producerId);

		const raw = await hub.consume(producerId, this.recvTransport.id, this.device.rtpCapabilities);

		const consumer = await this.recvTransport.consume({
			id: raw.id,
			producerId: raw.producerId,
			kind: raw.kind as 'audio' | 'video',
			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			rtpParameters: raw.rtpParameters as any,
		});

		await consumer.resume();
		this.consumers.set(consumer.id, consumer);
		this.consumerLabels.set(consumer.id, label);
		callbacks.onNewTrack(participantId, consumer.track, label);

		const cleanupConsumer = () => {
			const consumerLabel = this.consumerLabels.get(consumer.id) ?? 'audio';
			callbacks.onTrackEnded(participantId, consumerLabel);
			this.consumers.delete(consumer.id);
			this.consumerLabels.delete(consumer.id);
			this.consumedProducerIds.delete(consumer.producerId);
		};
		consumer.on('transportclose', cleanupConsumer);
		consumer.on('trackended', cleanupConsumer);
	}

	/** Close consumers for a specific producer (when it's stopped remotely). */
	closeConsumerByProducerId(producerId: string): void {
		for (const [consumerId, consumer] of this.consumers) {
			if (consumer.producerId === producerId) {
				consumer.close();
				this.consumers.delete(consumerId);
				this.consumerLabels.delete(consumerId);
				this.consumedProducerIds.delete(producerId);
				break;
			}
		}
	}

	setMuted(muted: boolean): void {
		if (this.producer) {
			if (muted) this.producer.pause();
			else this.producer.resume();
		}
	}

	/** Start camera video producer. Returns the local video track for preview. */
	async startVideo(): Promise<MediaStreamTrack> {
		if (!this.sendTransport || !this._hub) throw new Error('Not in a voice session');

		this.videoStream = await navigator.mediaDevices.getUserMedia({
			video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } },
		});
		const videoTrack = this.videoStream.getVideoTracks()[0];

		this._pendingProduceLabel = 'video';
		this.videoProducer = await this.sendTransport.produce({ track: videoTrack });

		// Auto-stop when the user stops the track via browser UI
		videoTrack.addEventListener('ended', () => {
			this.stopVideo();
		});

		return videoTrack;
	}

	/** Stop camera video producer. Guards against double invocation from track 'ended' events. */
	async stopVideo(): Promise<void> {
		const producer = this.videoProducer;
		if (!producer && !this.videoStream) return; // already stopped
		if (producer) {
			this.videoProducer = null;
			producer.close();
		}
		if (this.videoStream) {
			for (const track of this.videoStream.getTracks()) track.stop();
			this.videoStream = null;
		}
		if (this._hub) {
			await this._hub.stopProducing('video');
		}
	}

	/** Start screen share producer. Returns the local screen track for preview. */
	async startScreenShare(): Promise<MediaStreamTrack> {
		if (!this.sendTransport || !this._hub) throw new Error('Not in a voice session');

		this.screenStream = await navigator.mediaDevices.getDisplayMedia({
			video: { frameRate: { ideal: 30 } },
			audio: false,
		});
		const screenTrack = this.screenStream.getVideoTracks()[0];

		this._pendingProduceLabel = 'screen';
		this.screenProducer = await this.sendTransport.produce({ track: screenTrack });

		// Auto-stop when the user clicks "Stop sharing" in the browser UI
		screenTrack.addEventListener('ended', () => {
			this.stopScreenShare();
		});

		return screenTrack;
	}

	/** Stop screen share producer. Guards against double invocation from track 'ended' events. */
	async stopScreenShare(): Promise<void> {
		const producer = this.screenProducer;
		if (!producer && !this.screenStream) return; // already stopped
		if (producer) {
			this.screenProducer = null;
			producer.close();
		}
		if (this.screenStream) {
			for (const track of this.screenStream.getTracks()) track.stop();
			this.screenStream = null;
		}
		if (this._hub) {
			await this._hub.stopProducing('screen');
		}
	}

	async leave(): Promise<void> {
		this.consumedProducerIds.clear();
		this.consumerLabels.clear();
		this.producer?.close();
		this.producer = null;
		this.videoProducer?.close();
		this.videoProducer = null;
		this.screenProducer?.close();
		this.screenProducer = null;

		for (const consumer of this.consumers.values()) {
			consumer.close();
		}
		this.consumers.clear();

		this.sendTransport?.close();
		this.sendTransport = null;
		this.recvTransport?.close();
		this.recvTransport = null;

		if (this.localStream) {
			for (const track of this.localStream.getTracks()) {
				track.stop();
			}
			this.localStream = null;
		}
		if (this.videoStream) {
			for (const track of this.videoStream.getTracks()) track.stop();
			this.videoStream = null;
		}
		if (this.screenStream) {
			for (const track of this.screenStream.getTracks()) track.stop();
			this.screenStream = null;
		}

		this.device = null;
		this._hub = null;
	}

	/** Synchronous cleanup for beforeunload — stops mic tracks immediately. */
	teardownSync(): void {
		if (this.localStream) {
			for (const track of this.localStream.getTracks()) {
				track.stop();
			}
			this.localStream = null;
		}
		if (this.videoStream) {
			for (const track of this.videoStream.getTracks()) track.stop();
			this.videoStream = null;
		}
		if (this.screenStream) {
			for (const track of this.screenStream.getTracks()) track.stop();
			this.screenStream = null;
		}
		this.sendTransport?.close();
		this.sendTransport = null;
		this.recvTransport?.close();
		this.recvTransport = null;
		this.producer = null;
		this.videoProducer = null;
		this.screenProducer = null;
		this.consumers.clear();
		this.consumedProducerIds.clear();
		this.consumerLabels.clear();
		this.device = null;
		this._hub = null;
	}
}
