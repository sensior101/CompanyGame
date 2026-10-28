// The same test files run against two servers:
//   npm test                        -> the Node/Fastify server, in this process
//   (cd worker && npm test)         -> the Cloudflare worker, in `wrangler dev --local` (TEST_TARGET=worker)
// Every test talks to the server over real HTTP and WebSocket, so both targets must behave the same.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { buildApp } from '../src/app.ts';
import { openDb } from '../src/db.ts';
import type { Limits } from '../src/limit.ts';

export const SECRET = 'test-secret-test-secret';
export interface Target { host: string; close(): Promise<void> }

export async function startServer(limits: Partial<Limits> = {}, extraVars: Record<string, string> = {}): Promise<Target> {
  return process.env.TEST_TARGET === 'worker' ? startWorker(limits, extraVars) : startNode(limits);
}

async function startNode(limits: Partial<Limits>): Promise<Target> {
  const app = buildApp(openDb(':memory:'), SECRET, limits);
  await app.listen({ port: 0, host: '127.0.0.1' });
  return { host: `127.0.0.1:${(app.server.address() as { port: number }).port}`, close: () => app.close() };
}

const freePort = () => new Promise<number>((resolve) => {
  const s = createServer().listen(0, '127.0.0.1', () => { const p = (s.address() as { port: number }).port; s.close(() => resolve(p)); });
});

const VARS: Record<keyof Limits, string> = {
  registerPerIpPerMin: 'LIMIT_REGISTER_PER_IP', loginPerIpPerMin: 'LIMIT_LOGIN_PER_IP',
  loginPerUserPerMin: 'LIMIT_LOGIN_PER_USER', wsPerIp: 'LIMIT_WS_PER_IP',
};

async function startWorker(limits: Partial<Limits>, extraVars: Record<string, string>): Promise<Target> {
  const dir = join(import.meta.dirname, '..', 'worker');
  const state = mkdtempSync(join(tmpdir(), 'cg-worker-')); // fresh Durable Object storage per server
  const port = await freePort();
  const vars = [['JWT_SECRET', SECRET], ...Object.entries(limits).map(([k, v]) => [VARS[k as keyof Limits], String(v)]), ...Object.entries(extraVars)];
  const args = ['node_modules/wrangler/bin/wrangler.js', 'dev', '--local', '--ip', '127.0.0.1', '--port', String(port),
    '--persist-to', state, '--log-level', 'warn', ...vars.flatMap(([k, v]) => ['--var', `${k}:${v}`])];
  const child = spawn(process.execPath, args, { cwd: dir, stdio: ['ignore', 'inherit', 'inherit'], env: { ...process.env, WRANGLER_SEND_METRICS: 'false', NO_UPDATE_NOTIFIER: '1' } });
  const host = `127.0.0.1:${port}`;
  for (let i = 0; ; i++) {
    if (i > 240) throw new Error('wrangler dev did not start');
    if (child.exitCode !== null) throw new Error(`wrangler dev exited with ${child.exitCode}`);
    if (await fetch(`http://${host}/`).then((r) => r.ok, () => false)) break;
    await new Promise((r) => setTimeout(r, 250));
  }
  const close = async () => {
    // workerd is a child of wrangler: kill the whole tree (plain kill() would leave it running on Windows)
    if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F']);
    else child.kill('SIGTERM');
    await new Promise((r) => setTimeout(r, 300));
    rmSync(state, { recursive: true, force: true });
  };
  return { host, close };
}

export interface Reply { status: number; json: any; headers: Headers }

export async function api(t: Target, method: string, path: string, payload?: object | string, token?: string): Promise<Reply> {
  const res = await fetch(`http://${t.host}${path}`, {
    method,
    headers: { ...(payload !== undefined ? { 'content-type': 'application/json' } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: payload === undefined ? undefined : typeof payload === 'string' ? payload : JSON.stringify(payload),
  });
  return { status: res.status, json: await res.json().catch(() => null), headers: res.headers };
}

export const register = async (t: Target, username: string, password = 'password123') =>
  (await api(t, 'POST', '/auth/register', { username, password })).json?.token as string;
