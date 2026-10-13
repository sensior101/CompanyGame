import { randomBytes, scrypt, timingSafeEqual } from 'node:crypto';
import { promisify } from 'node:util';
import type { DatabaseSync } from 'node:sqlite';
import Fastify from 'fastify';
import jwt from '@fastify/jwt';
import websocket from '@fastify/websocket';
import { loadCharacter, loadState, saveCharacter, saveState } from './db.ts';
import { DEFAULT_LIMITS, WindowLimiter, type Limits } from './limit.ts';
import { MAX_FRAME } from './protocol.ts';
import { Rooms } from './rooms.ts';
import { registerWs } from './ws.ts';

const scryptAsync = promisify(scrypt) as (pw: string, salt: Buffer, len: number) => Promise<Buffer>;

async function hashPassword(pw: string): Promise<string> {
  const salt = randomBytes(16);
  return `${salt.toString('hex')}:${(await scryptAsync(pw, salt, 64)).toString('hex')}`;
}

async function verifyPassword(pw: string, stored: string): Promise<boolean> {
  const [salt, hash] = stored.split(':');
  const expected = Buffer.from(hash, 'hex');
  return timingSafeEqual(await scryptAsync(pw, Buffer.from(salt, 'hex'), 64), expected);
}

const credentials = {
  type: 'object',
  required: ['username', 'password'],
  additionalProperties: false,
  properties: {
    username: { type: 'string', pattern: '^[a-z0-9_]{3,20}$' },
    password: { type: 'string', minLength: 8, maxLength: 64 },
  },
} as const;

const profile = {
  type: 'object',
  required: ['nickname', 'gender'],
  additionalProperties: false,
  properties: {
    nickname: { type: 'string', pattern: '^[가-힣A-Za-z0-9_]{2,12}$' },
    gender: { enum: ['male', 'female'] },
  },
} as const;

const coord = { type: 'number', minimum: -1e6, maximum: 1e6 } as const;
const STATE_BODY_LIMIT = 64 * 1024;
const playerState = {
  type: 'object',
  required: ['map', 'x', 'y', 'z', 'ry'],
  additionalProperties: false,
  properties: {
    map: { type: 'string', maxLength: 200 },
    x: coord, y: coord, z: coord, ry: coord,
    inventory_json: { type: 'string', maxLength: STATE_BODY_LIMIT },
    personal_json: { type: 'string', maxLength: STATE_BODY_LIMIT },
    // '' = keep the stored value (Unity's JsonUtility always writes every field)
    name: { type: 'string', pattern: '^([가-힣A-Za-z0-9_]{2,12})?$' },
    gender: { enum: ['', 'male', 'female'] },
  },
} as const;

// ?world=<id>: the character of that world (WorldSave.WorldId, a 32-hex GUID). Without it: the account-wide state.
const worldQuery = {
  type: 'object',
  additionalProperties: false,
  properties: { world: { type: 'string', pattern: '^[A-Za-z0-9_-]{1,64}$' } },
} as const;

// "" means "nothing"; anything else must at least be valid JSON.
const isJsonOrEmpty = (s: string | undefined) => {
  if (!s) return true;
  try { JSON.parse(s); return true; } catch { return false; }
};

type Row = { id: number; username: string; pw_hash: string; nickname: string | null; gender: string | null };

export function buildApp(db: DatabaseSync, jwtSecret: string, over: Partial<Limits> = {}) {
  const lim = { ...DEFAULT_LIMITS, ...over };
  const regIp = new WindowLimiter(lim.registerPerIpPerMin, 60_000);
  const loginIp = new WindowLimiter(lim.loginPerIpPerMin, 60_000);
  const loginUser = new WindowLimiter(lim.loginPerUserPerMin, 60_000);
  // behind a tunnel/proxy every client shares its IP: TRUST_PROXY=1 makes req.ip use X-Forwarded-For
  const app = Fastify({ trustProxy: process.env.TRUST_PROXY === '1' });
  app.register(jwt, { secret: jwtSecret, sign: { expiresIn: '7d' } });
  // room messages stay <= 8KB, only `state` may use the full 512KB (checked per message in ws.ts)
  app.register(websocket, { options: { maxPayload: MAX_FRAME } });
  const rooms = new Rooms();

  const isUnique = (e: unknown) => String((e as Error).message).includes('UNIQUE');
  const auth = async (req: any, reply: any) => {
    try { await req.jwtVerify(); } catch { reply.code(401).send({ error: 'unauthorized' }); }
  };

  app.post('/auth/register', { schema: { body: credentials } }, async (req, reply) => {
    const { username, password } = req.body as { username: string; password: string };
    if (!regIp.hit(req.ip)) return reply.code(429).send({ error: 'rate_limited' });
    try {
      const { lastInsertRowid } = db
        .prepare('INSERT INTO players (username, pw_hash) VALUES (?, ?)')
        .run(username, await hashPassword(password));
      return reply.code(201).send({ token: app.jwt.sign({ id: Number(lastInsertRowid) }) });
    } catch (e) {
      if (isUnique(e)) return reply.code(409).send({ error: 'username_taken' });
      throw e;
    }
  });

  app.post('/auth/login', { schema: { body: credentials } }, async (req, reply) => {
    const { username, password } = req.body as { username: string; password: string };
    if (!loginIp.hit(req.ip) || !loginUser.hit(username)) return reply.code(429).send({ error: 'rate_limited' });
    const row = db.prepare('SELECT id, pw_hash FROM players WHERE username = ?').get(username) as Row | undefined;
    // same error for unknown user and wrong password, so usernames can't be probed
    if (!row || !(await verifyPassword(password, row.pw_hash))) {
      return reply.code(401).send({ error: 'invalid_credentials' });
    }
    return { token: app.jwt.sign({ id: row.id }) };
  });

  app.get('/me', { preHandler: auth }, async (req, reply) => {
    const { id } = req.user as { id: number };
    const row = db.prepare('SELECT id, username, nickname, gender FROM players WHERE id = ?').get(id);
    return row ?? reply.code(404).send({ error: 'not_found' });
  });

  app.put('/me/profile', { preHandler: auth, schema: { body: profile } }, async (req, reply) => {
    const { id } = req.user as { id: number };
    const { nickname, gender } = req.body as { nickname: string; gender: string };
    try {
      db.prepare('UPDATE players SET nickname = ?, gender = ? WHERE id = ?').run(nickname, gender, id);
      return { nickname, gender };
    } catch (e) {
      if (isUnique(e)) return reply.code(409).send({ error: 'nickname_taken' });
      throw e;
    }
  });

  app.get('/me/state', { preHandler: auth, schema: { querystring: worldQuery } }, async (req) => {
    const { id } = req.user as { id: number };
    const { world } = req.query as { world?: string };
    return world ? loadCharacter(db, id, world) : loadState(db, id);
  });

  app.put('/me/state', { preHandler: auth, bodyLimit: STATE_BODY_LIMIT, schema: { body: playerState, querystring: worldQuery } }, async (req, reply) => {
    const { id } = req.user as { id: number };
    const { world } = req.query as { world?: string };
    const b = req.body as { map: string; x: number; y: number; z: number; ry: number; inventory_json?: string; personal_json?: string; name?: string; gender?: string };
    if (!isJsonOrEmpty(b.inventory_json) || !isJsonOrEmpty(b.personal_json)) {
      return reply.code(400).send({ error: 'invalid_json' });
    }
    if (!db.prepare('SELECT 1 FROM players WHERE id = ?').get(id)) return reply.code(404).send({ error: 'not_found' });
    if (world) { saveCharacter(db, id, world, b, b.inventory_json ?? null, b.personal_json ?? null); return loadCharacter(db, id, world); }
    saveState(db, id, b, b.inventory_json ?? null, b.personal_json ?? null);
    return loadState(db, id);
  });

  app.get('/rooms', { preHandler: auth }, async () => ({ rooms: rooms.list() }));

  registerWs(app, rooms, db, lim);
  return app;
}
