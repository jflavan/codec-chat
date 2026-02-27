import { Router } from 'express';
import type { Worker, WebRtcTransport, Producer, Consumer, DtlsParameters, MediaKind, RtpParameters, RtpCapabilities } from 'mediasoup/types';
import { MEDIA_CODECS, WEBRTC_TRANSPORT_OPTIONS } from './worker.js';

interface Participant {
  sendTransport?: WebRtcTransport;
  recvTransport?: WebRtcTransport;
  producer?: Producer;
  consumers: Map<string, Consumer>;
}

interface Room {
  router: Awaited<ReturnType<Worker['createRouter']>>;
  participants: Map<string, Participant>;
}

/** In-memory room registry: roomId (= channelId) → Room */
const rooms = new Map<string, Room>();

function getOrCreateParticipant(room: Room, participantId: string): Participant {
  let p = room.participants.get(participantId);
  if (!p) {
    p = { consumers: new Map() };
    room.participants.set(participantId, p);
  }
  return p;
}

export function createRoomRouter(worker: Worker): Router {
  const router = Router();

  /* ── POST /rooms/:roomId ── create room (idempotent) ── */
  router.post('/rooms/:roomId', async (req, res) => {
    const { roomId } = req.params;
    let room = rooms.get(roomId);
    if (!room) {
      const sfuRouter = await worker.createRouter({ mediaCodecs: MEDIA_CODECS });
      room = { router: sfuRouter, participants: new Map() };
      rooms.set(roomId, room);
    }
    res.json({ routerRtpCapabilities: room.router.rtpCapabilities });
  });

  /* ── DELETE /rooms/:roomId ── close room ── */
  router.delete('/rooms/:roomId', (req, res) => {
    const { roomId } = req.params;
    const room = rooms.get(roomId);
    if (room) {
      room.router.close();
      rooms.delete(roomId);
    }
    res.status(204).send();
  });

  /* ── POST /rooms/:roomId/transports ── create a WebRTC transport ── */
  router.post('/rooms/:roomId/transports', async (req, res) => {
    const { roomId } = req.params;
    const { participantId, direction } = req.body as { participantId: string; direction: 'send' | 'recv' };

    if (!participantId || typeof participantId !== 'string') {
      return res.status(400).json({ error: 'participantId is required and must be a string' });
    }
    if (direction !== 'send' && direction !== 'recv') {
      return res.status(400).json({ error: 'direction must be "send" or "recv"' });
    }

    const room = rooms.get(roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });

    const transport = await room.router.createWebRtcTransport(WEBRTC_TRANSPORT_OPTIONS);

    const participant = getOrCreateParticipant(room, participantId);
    if (direction === 'send') {
      participant.sendTransport = transport;
    } else {
      participant.recvTransport = transport;
    }

    res.json({
      id: transport.id,
      iceParameters: transport.iceParameters,
      iceCandidates: transport.iceCandidates,
      dtlsParameters: transport.dtlsParameters,
    });
  });

  /* ── POST /rooms/:roomId/transports/:transportId/connect ── */
  router.post('/rooms/:roomId/transports/:transportId/connect', async (req, res) => {
    const { roomId, transportId } = req.params;
    const { participantId, dtlsParameters } = req.body as { participantId: string; dtlsParameters: DtlsParameters };

    const room = rooms.get(roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });

    // Only allow connecting a transport that belongs to the calling participant.
    const participant = room.participants.get(participantId);
    let transport: WebRtcTransport | undefined;
    if (participant?.sendTransport?.id === transportId) {
      transport = participant.sendTransport;
    } else if (participant?.recvTransport?.id === transportId) {
      transport = participant.recvTransport;
    }

    if (!transport) return res.status(403).json({ error: 'Transport not found or not owned by participant' });

    await transport.connect({ dtlsParameters });
    res.status(200).json({ connected: true });
  });

  /* ── POST /rooms/:roomId/transports/:transportId/produce ── */
  router.post('/rooms/:roomId/transports/:transportId/produce', async (req, res) => {
    const { roomId, transportId } = req.params;
    const { participantId, kind, rtpParameters } = req.body as { participantId: string; kind: MediaKind; rtpParameters: RtpParameters };

    const room = rooms.get(roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });

    // Only allow producing on a send transport that belongs to the calling participant.
    const participant = room.participants.get(participantId);
    if (!participant || participant.sendTransport?.id !== transportId) {
      return res.status(403).json({ error: 'Transport not found or not owned by participant' });
    }

    const producer = await participant.sendTransport.produce({ kind, rtpParameters });
    participant.producer = producer;

    producer.on('transportclose', () => { producer.close(); });

    res.json({ producerId: producer.id });
  });

  /* ── POST /rooms/:roomId/consumers ── create a consumer for a producer ── */
  router.post('/rooms/:roomId/consumers', async (req, res) => {
    const { roomId } = req.params;
    const { producerId, transportId, rtpCapabilities, participantId } = req.body;

    const room = rooms.get(roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });

    // Ensure the producer exists in this room before attempting to consume it.
    // This prevents cross-room consumption even if router.canConsume() were to allow it.
    let producerInRoom = false;
    for (const participant of room.participants.values()) {
      if (participant.producer?.id === producerId) {
        producerInRoom = true;
        break;
      }
    }
    if (!producerInRoom) return res.status(404).json({ error: 'Producer not found in this room' });

    if (!room.router.canConsume({ producerId, rtpCapabilities })) {
      return res.status(400).json({ error: 'Cannot consume: incompatible RTP capabilities' });
    }

    // Validate that the recv transport belongs to the requesting participant.
    if (!participantId) {
      return res.status(403).json({ error: 'participantId is required' });
    }
    const consumerParticipant = room.participants.get(participantId);
    if (!consumerParticipant || consumerParticipant.recvTransport?.id !== transportId) {
      return res.status(403).json({ error: 'Recv transport not found or not owned by participant' });
    }

    const consumer = await consumerParticipant.recvTransport!.consume({
      producerId,
      rtpCapabilities,
      paused: true, // Client resumes after setting up the track.
    });

    consumerParticipant.consumers.set(consumer.id, consumer);
    consumer.on('transportclose', () => { consumer.close(); });
    consumer.on('producerclose', () => { consumer.close(); });

    res.json({
      id: consumer.id,
      producerId,
      kind: consumer.kind,
      rtpParameters: consumer.rtpParameters,
    });
  });

  /* ── DELETE /rooms/:roomId/participants/:participantId ── remove participant ── */
  router.delete('/rooms/:roomId/participants/:participantId', (req, res) => {
    const { roomId, participantId } = req.params;
    const room = rooms.get(roomId);
    if (!room) return res.status(204).send();

    const participant = room.participants.get(participantId);
    if (participant) {
      participant.producer?.close();
      participant.sendTransport?.close();
      participant.recvTransport?.close();
      for (const consumer of participant.consumers.values()) consumer.close();
      room.participants.delete(participantId);
    }

    // Clean up empty rooms.
    if (room.participants.size === 0) {
      room.router.close();
      rooms.delete(roomId);
    }

    res.status(204).send();
  });

  return router;
}
