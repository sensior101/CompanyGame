import { randomBytes, scrypt, timingSafeEqual } from 'node:crypto';
import { promisify } from 'node:util';
import type { DatabaseSync } from 'node:sqlite';
import Fastify from 'fastify';
import jwt from '@fastify/jwt';

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

type Row = { id: number; username: string; pw_hash: string; nickname: string | null; gender: string | null };

export function buildApp(db: DatabaseSync, jwtSecret: string) {
  const app = Fastify();
  app.register(jwt, { secret: jwtSecret, sign: { expiresIn: '7d' } });

  const isUnique = (e: unknown) => String((e as Error).message).includes('UNIQUE');
  const auth = async (req: any, reply: any) => {
    try { await req.jwtVerify(); } catch { reply.code(401).send({ error: 'unauthorized' }); }
  };

  app.post('/auth/register', { schema: { body: credentials } }, async (req, reply) => {
    const { username, password } = req.body as { username: string; password: string };
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

  return app;
}
