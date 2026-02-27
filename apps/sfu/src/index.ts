import { timingSafeEqual } from 'crypto';
import express from 'express';
import { rateLimit } from 'express-rate-limit';
import { createWorker } from './worker.js';
import { createRoomRouter } from './rooms.js';

const PORT = parseInt(process.env.SFU_PORT ?? '3001', 10);
const SFU_INTERNAL_KEY = process.env.SFU_INTERNAL_KEY ?? '';

if (!SFU_INTERNAL_KEY && process.env.NODE_ENV === 'production') {
  console.error('FATAL: SFU_INTERNAL_KEY must be set in production. Refusing to start.');
  process.exit(1);
}

async function main() {
  const worker = await createWorker();
  const app = express();
  // Limit request bodies to 32 KB — RTP parameters never approach this.
  app.use(express.json({ limit: '32kb' }));
  app.get('/health', (_req, res) => res.json({ status: 'ok' }));

  // Rate-limit /rooms/* to 120 requests per minute per IP.
  const roomsRateLimit = rateLimit({
    windowMs: 60_000,
    max: 120,
    standardHeaders: true,
    legacyHeaders: false,
    message: { error: 'Too many requests' },
  });
  app.use('/rooms', roomsRateLimit);

  // Protect all /rooms/* routes with a shared internal key.
  // The key is required in production (when SFU_INTERNAL_KEY is set) and
  // skipped in development (when the variable is empty).
  if (SFU_INTERNAL_KEY) {
    const keyBuf = Buffer.from(SFU_INTERNAL_KEY);
    app.use('/rooms', (req, res, next) => {
      const incoming = req.headers['x-internal-key'];
      const incomingBuf = Buffer.from(typeof incoming === 'string' ? incoming : '');
      const valid =
        incomingBuf.length === keyBuf.length &&
        timingSafeEqual(incomingBuf, keyBuf);
      if (!valid) {
        res.status(401).json({ error: 'Unauthorized' });
        return;
      }
      next();
    });
  } else {
    console.warn('SFU_INTERNAL_KEY not set — /rooms routes are unprotected');
  }

  app.use(createRoomRouter(worker));
  app.listen(PORT, () => {
    console.log(`SFU listening on http://0.0.0.0:${PORT}`);
  });
}

main().catch((err) => {
  console.error('SFU failed to start:', err);
  process.exit(1);
});
