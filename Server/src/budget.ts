// Self-imposed daily budget (UTC day) so a shared free Cloudflare account never hits its own quota.
// The count lives in memory and is written to storage only every `flushEvery` events (rows written are scarce);
// ponytail: a restart loses at most flushEvery-1 events of the count. Fine for a guard, not for billing.
export const DAY_MS = 86_400_000;

export function secondsToUtcMidnight(now = Date.now()): number {
  return Math.max(1, Math.ceil((DAY_MS - (now % DAY_MS)) / 1000));
}

export interface BudgetStore {
  load(): { day: number; used: number } | null;
  save(day: number, used: number): void;
}

export interface BudgetOpts { hardLimit?: number; flushEvery?: number; now?: () => number }

export class DayBudget {
  private limit: number;
  private hardLimit: number;
  private store: BudgetStore;
  private flushEvery: number;
  private now: () => number;
  private day: number;
  private used = 0;
  private events = 0;

  constructor(limit: number, store: BudgetStore, opts: BudgetOpts = {}) {
    this.limit = limit;
    this.hardLimit = opts.hardLimit ?? Math.ceil(limit * 1.25);
    this.store = store;
    this.flushEvery = opts.flushEvery ?? 10;
    this.now = opts.now ?? Date.now;
    this.day = Math.floor(this.now() / DAY_MS);
    const saved = store.load();
    if (saved && saved.day === this.day) this.used = saved.used;
  }

  add(cost = 1): void {
    this.roll();
    this.used += cost;
    if (++this.events >= this.flushEvery) this.flush();
  }

  over(): boolean { this.roll(); return this.used >= this.limit; } // refuse new work
  hard(): boolean { this.roll(); return this.used >= this.hardLimit; } // cut running work
  flush(): void { this.events = 0; this.store.save(this.day, this.used); }

  private roll(): void {
    const day = Math.floor(this.now() / DAY_MS);
    if (day !== this.day) { this.day = day; this.used = 0; this.events = 0; }
  }
}
