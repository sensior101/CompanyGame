// Tiny in-memory fixed-window limiter. hit() is false once `max` hits happened inside `windowMs`.
// ponytail: single process, expired keys pruned lazily; use a shared store if the server is ever clustered.
export class WindowLimiter {
  private hits = new Map<string, { n: number; reset: number }>();
  private max: number;
  private windowMs: number;
  constructor(max: number, windowMs: number) { this.max = max; this.windowMs = windowMs; }
  hit(key: string, now = Date.now()): boolean {
    if (this.hits.size > 10_000) for (const [k, v] of this.hits) if (v.reset <= now) this.hits.delete(k);
    const cur = this.hits.get(key);
    if (!cur || cur.reset <= now) { this.hits.set(key, { n: 1, reset: now + this.windowMs }); return true; }
    return ++cur.n <= this.max;
  }
}

// Abuse limits, same numbers on the Node server and the Cloudflare worker.
export interface Limits {
  registerPerIpPerMin: number; // /auth/register per IP
  loginPerIpPerMin: number; // /auth/login per IP
  loginPerUserPerMin: number; // /auth/login per username
}
export const DEFAULT_LIMITS: Limits = {
  registerPerIpPerMin: 10, loginPerIpPerMin: 30, loginPerUserPerMin: 10,
};

export const PLACEHOLDER_SECRET = 'change-me-to-a-long-random-string';

export function checkSecret(secret: string | undefined): string {
  if (!secret || secret.length < 16) throw new Error('JWT_SECRET must be set (16+ chars). See .env.example');
  if (secret.startsWith('change-me')) {
    throw new Error('JWT_SECRET is still the .env.example placeholder. Set a real random value.');
  }
  return secret;
}
