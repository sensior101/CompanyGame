import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { buildApp } from './app.ts';
import { openDb } from './db.ts';

const secret = process.env.JWT_SECRET;
if (!secret || secret.length < 16) throw new Error('JWT_SECRET must be set (16+ chars). See .env.example');

const dbPath = process.env.DB_PATH ?? './data/game.db';
mkdirSync(dirname(dbPath), { recursive: true });

const app = buildApp(openDb(dbPath), secret);
await app.listen({ port: Number(process.env.PORT ?? 3000), host: '0.0.0.0' });
