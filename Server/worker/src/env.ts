import { DEFAULT_LIMITS, type Limits } from '../../src/limit.ts';
import type { Accounts } from './accounts.ts';
import type { Lobby } from './lobby.ts';

export interface Env {
  ACCOUNTS: DurableObjectNamespace<Accounts>;
  LOBBY: DurableObjectNamespace<Lobby>;
  JWT_SECRET?: string; // `wrangler secret put JWT_SECRET`; never in wrangler.toml
  // Optional abuse-limit overrides (plain vars). Production uses the defaults; the tests lower them.
  LIMIT_REGISTER_PER_IP?: string;
  LIMIT_LOGIN_PER_IP?: string;
  LIMIT_LOGIN_PER_USER?: string;
  LIMIT_WS_PER_IP?: string;
  WORKER_DAILY_BUDGET?: string | number; // wrangler.toml [vars], see budget.ts
  DO_DAILY_BUDGET?: string | number;
}

const pick = (v: string | undefined, d: number) => (v && Number(v) > 0 ? Number(v) : d);

export function limitsOf(env: Env): Limits {
  return {
    registerPerIpPerMin: pick(env.LIMIT_REGISTER_PER_IP, DEFAULT_LIMITS.registerPerIpPerMin),
    loginPerIpPerMin: pick(env.LIMIT_LOGIN_PER_IP, DEFAULT_LIMITS.loginPerIpPerMin),
    loginPerUserPerMin: pick(env.LIMIT_LOGIN_PER_USER, DEFAULT_LIMITS.loginPerUserPerMin),
    wsPerIp: pick(env.LIMIT_WS_PER_IP, DEFAULT_LIMITS.wsPerIp),
  };
}

// The two Durable Objects are singletons: one for accounts, one for all rooms.
export const accountsOf = (env: Env) => env.ACCOUNTS.get(env.ACCOUNTS.idFromName('main'));
export const lobbyOf = (env: Env) => env.LOBBY.get(env.LOBBY.idFromName('main'));
