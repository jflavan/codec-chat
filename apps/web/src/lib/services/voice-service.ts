import { Device } from 'mediasoup-client';
import type { Transport, Producer, Consumer, TransportOptions, RtpCapabilities } from 'mediasoup-client/types';
import type { ChatHubService } from './chat-hub.js';
import type { VoiceChannelMember } from '$lib/types/index.js';

export type VoiceServiceCallbacks = {
	onNewTrack: (participantId: string, track: MediaStreamTrack) => void;
	onTrackEnded: (participantId: string) => void;
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
	private consumers = new Map<string, Consumer>();
	private localStream: MediaStream | null = null;
	/** Tracks producerIds already consumed to prevent duplicates from the
	 *  group-join / member-snapshot race window. */
	private consumedProducerIds = new Set<string>();

	async join(
		channelId: string,
		hub: ChatHubService,
		callbacks: VoiceServiceCallbacks
	): Promise<VoiceChannelMember[]> {
		// Request microphone access first — if denied or unavailable this throws
		// immediately before doing any network work (SignalR, mediasoup).
		this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
		const audioTrack = this.localStream.getAudioTracks()[0];

		this.device = new Device();

		const result = await hub.joinVoiceChannel(channelId);
		const routerRtpCapabilities = result.routerRtpCapabilities as RtpCapabilities;
		const sendTransportOptions = result.sendTransportOptions as TransportOptions;
		const recvTransportOptions = result.recvTransportOptions as TransportOptions;
		const members = result.members as (VoiceChannelMember & { producerId?: string })[];

		await this.device.load({ routerRtpCapabilities });

		// Send transport: microphone audio → SFU
		// Capture transport references into locals so event handlers don't rely on
		// `this.sendTransport` / `this.recvTransport`, which could be nulled by leave().
		const sendTransport = this.device.createSendTransport(sendTransportOptions);
		this.sendTransport = sendTransport;
		sendTransport.on('connect', ({ dtlsParameters }, callback, errback) => {
			hub.connectTransport(sendTransport.id, dtlsParameters)
				.then(callback)
				.catch(errback);
		});
		sendTransport.on('produce', ({ kind, rtpParameters }, callback, errback) => {
			hub.produce(sendTransport.id, rtpParameters)
				.then((producerId) => callback({ id: producerId }))
				.catch(errback);
		});

		// Recv transport: SFU audio → speakers
		const recvTransport = this.device.createRecvTransport(recvTransportOptions);
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
				await this.consumeProducer(member.producerId, member.participantId, hub, callbacks);
			}
		}

		return members;
	}

	async consumeProducer(
		producerId: string,
		participantId: string,
		hub: ChatHubService,
		callbacks: VoiceServiceCallbacks
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
			kind: raw.kind as 'audio',
			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			rtpParameters: raw.rtpParameters as any,
		});

		await consumer.resume();
		this.consumers.set(consumer.id, consumer);
		callbacks.onNewTrack(participantId, consumer.track);

		const cleanupConsumer = () => {
			callbacks.onTrackEnded(participantId);
			this.consumers.delete(consumer.id);
			this.consumedProducerIds.delete(consumer.producerId);
		};
		consumer.on('transportclose', cleanupConsumer);
		consumer.on('trackended', cleanupConsumer);
	}

	setMuted(muted: boolean): void {
		if (this.producer) {
			if (muted) this.producer.pause();
			else this.producer.resume();
		}
	}

	async leave(): Promise<void> {
		this.consumedProducerIds.clear();
		this.producer?.close();
		this.producer = null;

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

		this.device = null;
	}
}
