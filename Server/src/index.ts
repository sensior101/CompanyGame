import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { buildApp } from './app.ts';
import { openDb } from './db.ts';
import { checkSecret } from './limit.ts';

const secret = checkSecret(process.env.JWT_SECRET);

const dbPath = process.env.DB_PATH ?? './data/game.db';
mkdirSync(dirname(dbPath), { recursive: true });

const app = buildApp(openDb(dbPath), secret);
await app.listen({ port: Number(process.env.PORT ?? 3000), host: '0.0.0.0' });
