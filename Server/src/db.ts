import { DatabaseSync } from 'node:sqlite';

// ponytail: single SQLite file, fine for hundreds of concurrent players; move to Postgres beyond that.
export function openDb(path: string): DatabaseSync {
  const db = new DatabaseSync(path);
  db.exec(`
    PRAGMA journal_mode = WAL;
    CREATE TABLE IF NOT EXISTS players (
      id         INTEGER PRIMARY KEY,
      username   TEXT NOT NULL UNIQUE,
      pw_hash    TEXT NOT NULL,
      nickname   TEXT UNIQUE,
      gender     TEXT CHECK (gender IN ('male', 'female')),
      created_at INTEGER NOT NULL DEFAULT (unixepoch())
    );
  `);
  return db;
}
