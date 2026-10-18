import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { DAY_MS, DayBudget, secondsToUtcMidnight, type BudgetStore } from '../src/budget.ts';
import { api, register, startServer, type Target } from './harness.ts';

const memStore = (): BudgetStore & { saves: number; row: { day: number; used: number } | null } => {
  const s = { saves: 0, row: null as { day: number; used: number } | null, load: () => s.row, save: (day: number, used: number) => { s.saves++; s.row = { day, used }; } };
  return s;
};

test('DayBudget: soft limit, hard stop, flush only every N events, reset on a new UTC day', () => {
  let now = 5 * DAY_MS + 1000; // injectable clock
  const store = memStore();
  const b = new DayBudget(10, store, { hardLimit: 12, flushEvery: 4, now: () => now });
  for (let i = 0; i < 9; i++) b.add();
  assert.equal(b.over(), false);
  assert.equal(store.saves, 2); // 9 events, flushed at 4 and 8: rows written stay low
  b.add();
  assert.equal(b.over(), true);
  assert.equal(b.hard(), false);
  b.add(); b.add();
  assert.equal(b.hard(), true);

  b.flush(); // a restart in the same day keeps the count
  assert.equal(new DayBudget(10, store, { now: () => now }).over(), true);

  now += DAY_MS; // next UTC day: fresh budget, and a stored count from yesterday is ignored
  assert.equal(b.over(), false);
  assert.equal(new DayBudget(10, store, { now: () => now }).over(), false);
});

test('fractional costs (WebSocket messages at 1/20) add up', () => {
  const b = new DayBudget(1, memStore(), { flushEvery: 1000 });
  for (let i = 0; i < 19; i++) b.add(1 / 20);
  assert.equal(b.over(), false);
  b.add(1 / 20);
  assert.equal(b.over(), true);
});

test('Retry-After counts down to 00:00 UTC', () => {
  assert.equal(secondsToUtcMidnight(3 * DAY_MS), 86400);
  assert.equal(secondsToUtcMidnight(3 * DAY_MS + DAY_MS - 1500), 2);
  assert.equal(secondsToUtcMidnight(3 * DAY_MS + DAY_MS - 1), 1);
});

// ---- Cloudflare worker only: tiny budget through [vars]. WORKER_DAILY_BUDGET=6 -> all REST.
const onWorker = process.env.TEST_TARGET === 'worker';
let srv: Target;
before(async () => { if (onWorker) srv = await startServer({}, { WORKER_DAILY_BUDGET: '6' }); });
after(() => srv?.close());

test('worker: REST over budget -> 503 daily_budget + Retry-After; /health is free', { skip: !onWorker, timeout: 60_000 }, async () => {
  for (let i = 0; i < 20; i++) assert.equal((await api(srv, 'GET', '/health')).status, 200); // never counted
  const token = await register(srv, 'budgetuser'); // REST request 1
  for (let i = 0; i < 5; i++) assert.equal((await api(srv, 'GET', '/me', undefined, token)).status, 200); // 2..6
  const over = await api(srv, 'GET', '/me', undefined, token);
  assert.equal(over.status, 503);
  assert.deepEqual(over.json, { error: 'daily_budget' });
  const retry = Number(over.headers.get('retry-after'));
  assert.ok(retry >= 1 && retry <= 86400, `retry-after ${retry}`);
  assert.equal((await api(srv, 'PUT', '/me/profile', { nickname: 'abc', gender: 'male' }, token)).status, 503);
  assert.equal((await api(srv, 'GET', '/health')).status, 200); // still answers without any Durable Object
});
