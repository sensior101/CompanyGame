// Daily self-limits per Durable Object (the counters live where the requests already land, no extra calls).
// WORKER_DAILY_BUDGET = HTTP requests of our Worker/day, DO_DAILY_BUDGET = Durable Object requests/day (a received
// WebSocket message counts 1/20). Each DO gets a fixed share so the sum never exceeds the configured total.
import { DayBudget, type BudgetStore } from '../../src/budget.ts';
import type { Env } from './env.ts';

export const DEFAULT_WORKER_DAILY_BUDGET = 20_000; // 20% of the 100k/day Workers Free quota shared with Pages Functions
export const DEFAULT_DO_DAILY_BUDGET = 60_000; // 60% of the 100k/day Durable Objects Free quota
export const WS_MESSAGE_COST = 1 / 20;
const ACCOUNTS_HTTP_SHARE = 0.75; // REST traffic; the Lobby gets the rest (WebSocket upgrades, GET /rooms)
const ACCOUNTS_DO_SHARE = 0.25; // the Lobby carries the WebSocket messages

export const BUDGET_SCHEMA = 'CREATE TABLE IF NOT EXISTS budget (name TEXT PRIMARY KEY, day INTEGER NOT NULL, used REAL NOT NULL) WITHOUT ROWID';

class SqlBudgetStore implements BudgetStore {
  private sql: SqlStorage;
  private name: string;
  constructor(sql: SqlStorage, name: string) { this.sql = sql; this.name = name; }
  load() {
    const r = this.sql.exec('SELECT day, used FROM budget WHERE name = ?', this.name).toArray()[0];
    return r ? { day: Number(r.day), used: Number(r.used) } : null;
  }
  save(day: number, used: number) {
    this.sql.exec('INSERT INTO budget (name, day, used) VALUES (?, ?, ?) ON CONFLICT(name) DO UPDATE SET day = excluded.day, used = excluded.used', this.name, day, used);
  }
}

const num = (v: string | number | undefined, d: number) => (Number(v) > 0 ? Number(v) : d);

export interface Budgets { http: DayBudget; dos: DayBudget }

export function budgetsOf(sql: SqlStorage, env: Env, who: 'accounts' | 'lobby'): Budgets {
  const w = num(env.WORKER_DAILY_BUDGET, DEFAULT_WORKER_DAILY_BUDGET);
  const d = num(env.DO_DAILY_BUDGET, DEFAULT_DO_DAILY_BUDGET);
  const accHttp = Math.floor(w * ACCOUNTS_HTTP_SHARE);
  const accDo = Math.floor(d * ACCOUNTS_DO_SHARE);
  const mine = who === 'accounts' ? [accHttp, accDo] : [w - accHttp, d - accDo];
  return {
    http: new DayBudget(mine[0], new SqlBudgetStore(sql, `${who}.http`), { flushEvery: 10 }),
    dos: new DayBudget(mine[1], new SqlBudgetStore(sql, `${who}.do`), { flushEvery: who === 'lobby' ? 500 : 10 }),
  };
}
