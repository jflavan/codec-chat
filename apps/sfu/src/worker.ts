import * as mediasoup from 'mediasoup';
import type { Worker } from 'mediasoup/types';

/** Codec capabilities for the router: Opus audio + VP8 video. */
export const MEDIA_CODECS = [
  {
    kind: 'audio' as const,
    mimeType: 'audio/opus',
    clockRate: 48000,
    channels: 2,
  },
  {
    kind: 'video' as const,
    mimeType: 'video/VP8',
    clockRate: 90000,
  },
];

/** Announced IP for ICE candidates. Defaults to localhost for development. */
const ANNOUNCED_IP = process.env.ANNOUNCED_IP ?? '127.0.0.1';

if (process.env.NODE_ENV === 'production' && ANNOUNCED_IP === '127.0.0.1') {
  console.error('FATAL: ANNOUNCED_IP must be set to the public IP in production. Refusing to start.');
  process.exit(1);
}

/** UDP port range for WebRTC media (must be open in firewall). */
const RTC_MIN_PORT = parseInt(process.env.RTC_MIN_PORT ?? '40000', 10);
const RTC_MAX_PORT = parseInt(process.env.RTC_MAX_PORT ?? '40100', 10);

export const WEBRTC_TRANSPORT_OPTIONS = {
  listenInfos: [
    { protocol: 'udp' as const, ip: '0.0.0.0', announcedAddress: ANNOUNCED_IP },
    { protocol: 'tcp' as const, ip: '0.0.0.0', announcedAddress: ANNOUNCED_IP },
  ],
  initialAvailableOutgoingBitrate: 1_000_000,
  minimumAvailableOutgoingBitrate: 600_000,
};

export async function createWorker(): Promise<Worker> {
  const worker = await mediasoup.createWorker({
    rtcMinPort: RTC_MIN_PORT,
    rtcMaxPort: RTC_MAX_PORT,
    logLevel: 'warn',
  });

  worker.on('died', (error) => {
    console.error('mediasoup worker died:', error);
    process.exit(1);
  });

  console.log(`mediasoup worker created [pid:${worker.pid}]`);
  return worker;
}
