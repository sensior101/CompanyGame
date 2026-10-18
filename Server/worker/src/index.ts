// Stateless entry point (Workers Free: 10 ms CPU per request, so only cheap work happens here: routing, body
// checks, JWT sign/verify). Everything stateful or CPU-heavy is delegated to the Accounts Durable Object.
import { secondsToUtcMidnight } from '../../src/budget.ts';
import { checkSecret } from '../../src/limit.ts';
import { accountsOf, type Env } from './env.ts';
import { signJwt, verifyJwt } from './jwt.ts';
import { BODY_LIMIT, credentials, profile } from './validate.ts';

export { Accounts } from './accounts.ts';

class HttpError extends Error {
  status: number;
  code: string;
  constructor(status: number, code: string) { super(code); this.status = status; this.code = code; }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });

// Reads at most `limit` bytes of JSON; anything else is a 400 (bad JSON) or 413 (too big).
async function readJson(req: Request, limit: number): Promise<Record<string, unknown>> {
  if (Number(req.headers.get('content-length') ?? 0) > limit) throw new HttpError(413, 'payload_too_large');
  const text = req.body ? await readText(req.body, limit) : '';
  try {
    const b = JSON.parse(text);
    if (b && typeof b === 'object' && !Array.isArray(b)) return b;
  } catch { /* falls through */ }
  throw new HttpError(400, 'bad_request');
}

async function readText(body: ReadableStream<Uint8Array>, limit: number): Promise<string> {
  const reader = body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > limit) { await reader.cancel(); throw new HttpError(413, 'payload_too_large'); }
    chunks.push(value);
  }
  return new TextDecoder().decode(await new Blob(chunks).arrayBuffer());
}

async function authedId(req: Request, env: Env): Promise<number> {
  const token = /^Bearer (.+)$/i.exec(req.headers.get('authorization') ?? '')?.[1];
  const claims = token ? await verifyJwt(token, env.JWT_SECRET!) : null;
  if (!claims) throw new HttpError(401, 'unauthorized');
  return claims.id;
}

const must = <T>(v: T | null): T => { if (v === null) throw new HttpError(400, 'bad_request'); return v; };
const failed = (r: { error: string }) => new HttpError(r.error === 'daily_budget' ? 503 : r.error === 'rate_limited' ? 429 : r.error === 'invalid_credentials' ? 401 : r.error === 'not_found' ? 404 : 409, r.error);

async function register(req: Request, env: Env, ip: string): Promise<Response> {
  const c = must(credentials(await readJson(req, BODY_LIMIT)));
  const r = await accountsOf(env).register(ip, c.username, c.password);
  if ('error' in r) throw failed(r);
  return json({ token: await signJwt(r.id, env.JWT_SECRET!) }, 201);
}

async function login(req: Request, env: Env, ip: string): Promise<Response> {
  const c = must(credentials(await readJson(req, BODY_LIMIT)));
  const r = await accountsOf(env).login(ip, c.username, c.password);
  if ('error' in r) throw failed(r);
  return json({ token: await signJwt(r.id, env.JWT_SECRET!) });
}

async function me(req: Request, env: Env): Promise<Response> {
  const row = await accountsOf(env).me(await authedId(req, env));
  if (row && 'error' in row) throw failed(row);
  return row ? json(row) : json({ error: 'not_found' }, 404);
}

async function putProfile(req: Request, env: Env): Promise<Response> {
  const id = await authedId(req, env);
  const p = must(profile(await readJson(req, BODY_LIMIT)));
  const r = await accountsOf(env).setProfile(id, p.nickname, p.gender);
  if ('error' in r) throw failed(r);
  return json(r);
}

// Budget refusals tell the client when to come back: 00:00 UTC.
function withRetry(res: Response): Response {
  if (res.status === 503) res.headers.set('Retry-After', String(secondsToUtcMidnight()));
  return res;
}

async function route(req: Request, env: Env): Promise<Response> {
  const url = new URL(req.url);
  const ip = req.headers.get('CF-Connecting-IP') ?? 'unknown';
  switch (`${req.method} ${url.pathname}`) {
    case 'GET /': // no Durable Object is touched by these two, but they still count as Worker requests: keep polling rare
    case 'GET /health': return json({ ok: true, service: 'companygame-server' });
    case 'POST /auth/register': return register(req, env, ip);
    case 'POST /auth/login': return login(req, env, ip);
    case 'GET /me': return me(req, env);
    case 'PUT /me/profile': return putProfile(req, env);
    default: throw new HttpError(404, 'not_found');
  }
}

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    try {
      checkSecret(env.JWT_SECRET); // refuse to serve with a missing / placeholder secret
    } catch (e) {
      console.error((e as Error).message);
      return json({ error: 'server_misconfigured' }, 500);
    }
    try {
      return await route(req, env);
    } catch (e) {
      if (e instanceof HttpError) return withRetry(json({ error: e.code }, e.status));
      console.error(e);
      return json({ error: 'internal' }, 500);
    }
  },
} satisfies ExportedHandler<Env>;
