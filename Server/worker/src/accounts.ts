// Accounts, in the SQLite of one Durable Object. Tables mirror Server/src/db.ts.
// Password hashing lives here (not in the Worker) on purpose: scrypt needs ~50-100 ms of CPU, which is over the
// Workers Free 10 ms/request limit but fine for a Durable Object (30 s CPU per request, also on Free).
import { DurableObject } from 'cloudflare:workers';
import { randomBytes, scrypt, timingSafeEqual } from 'node:crypto';
import { WindowLimiter } from '../../src/limit.ts';
import { BUDGET_SCHEMA, budgetsOf, type Budgets } from './budget.ts';
import { limitsOf, type Env } from './env.ts';

export interface Me { id: number; username: string; nickname: string | null; gender: string | null }
type Fail<E extends string> = { error: E };

const SCHEMA = [
  BUDGET_SCHEMA,
  `CREATE TABLE IF NOT EXISTS players (
    id         INTEGER PRIMARY KEY,
    username   TEXT NOT NULL UNIQUE,
    pw_hash    TEXT NOT NULL,
    nickname   TEXT UNIQUE,
    gender     TEXT CHECK (gender IN ('male', 'female')),
    created_at INTEGER NOT NULL DEFAULT (unixepoch())
  )`,
];

const isUnique = (e: unknown) => String((e as Error).message).includes('UNIQUE');

// Same parameters and storage format as the Node server (scrypt N=16384 r=8 p=1, "salt:hash" hex), so hashes are portable.
const scryptAsync = (pw: string, salt: Uint8Array) =>
  new Promise<Uint8Array>((res, rej) => scrypt(pw, salt, 64, (e, key) => (e ? rej(e) : res(key))));
const hex = (b: Uint8Array) => Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
const unhex = (s: string) => Uint8Array.from(s.match(/../g) ?? [], (h) => parseInt(h, 16));

async function hashPassword(pw: string): Promise<string> {
  const salt = randomBytes(16);
  return `${hex(salt)}:${hex(await scryptAsync(pw, salt))}`;
}

async function verifyPassword(pw: string, stored: string): Promise<boolean> {
  const [salt, hash] = stored.split(':');
  const expected = unhex(hash);
  const actual = await scryptAsync(pw, unhex(salt));
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export class Accounts extends DurableObject<Env> {
  private sql: SqlStorage;
  private regIp: WindowLimiter;
  private loginIp: WindowLimiter;
  private loginUser: WindowLimiter;
  private budget: Budgets;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    for (const q of SCHEMA) this.sql.exec(q);
    // ponytail: counters live in memory, so a restart/eviction resets them; an attacker slow enough to benefit
    // (idle > 10 s between requests) stays below every limit anyway. Persist them if limits ever need to be hard.
    this.budget = budgetsOf(this.sql, env);
    const lim = limitsOf(env);
    this.regIp = new WindowLimiter(lim.registerPerIpPerMin, 60_000);
    this.loginIp = new WindowLimiter(lim.loginPerIpPerMin, 60_000);
    this.loginUser = new WindowLimiter(lim.loginPerUserPerMin, 60_000);
  }

  // Every call is one Durable Object request and one HTTP request of our Worker.
  // true = today's budget is spent (the Worker answers 503 daily_budget).
  private spent(): boolean {
    const over = this.budget.dos.over() || this.budget.http.over();
    this.budget.dos.add();
    this.budget.http.add();
    return over;
  }

  async register(ip: string, username: string, password: string): Promise<{ id: number } | Fail<'rate_limited' | 'username_taken' | 'daily_budget'>> {
    if (this.spent()) return { error: 'daily_budget' };
    if (!this.regIp.hit(ip)) return { error: 'rate_limited' };
    const hash = await hashPassword(password);
    try {
      return { id: this.rows<{ id: number }>('INSERT INTO players (username, pw_hash) VALUES (?, ?) RETURNING id', username, hash)[0].id };
    } catch (e) {
      if (isUnique(e)) return { error: 'username_taken' };
      throw e;
    }
  }

  async login(ip: string, username: string, password: string): Promise<{ id: number } | Fail<'rate_limited' | 'invalid_credentials' | 'daily_budget'>> {
    if (this.spent()) return { error: 'daily_budget' };
    if (!this.loginIp.hit(ip) || !this.loginUser.hit(username)) return { error: 'rate_limited' };
    const row = this.rows<{ id: number; pw_hash: string }>('SELECT id, pw_hash FROM players WHERE username = ?', username)[0];
    // same error for unknown user and wrong password, so usernames can't be probed
    if (!row || !(await verifyPassword(password, row.pw_hash))) return { error: 'invalid_credentials' };
    return { id: row.id };
  }

  me(id: number): Me | null | Fail<'daily_budget'> {
    if (this.spent()) return { error: 'daily_budget' };
    return this.meRow(id);
  }

  private meRow(id: number): Me | null {
    return this.rows<Me>('SELECT id, username, nickname, gender FROM players WHERE id = ?', id)[0] ?? null;
  }

  setProfile(id: number, nickname: string, gender: string): { nickname: string; gender: string } | Fail<'nickname_taken' | 'daily_budget'> {
    if (this.spent()) return { error: 'daily_budget' };
    try {
      this.sql.exec('UPDATE players SET nickname = ?, gender = ? WHERE id = ?', nickname, gender, id);
      return { nickname, gender };
    } catch (e) {
      if (isUnique(e)) return { error: 'nickname_taken' };
      throw e;
    }
  }

  private rows<T>(query: string, ...bindings: unknown[]): T[] {
    return this.sql.exec(query, ...bindings).toArray() as unknown as T[];
  }

}
