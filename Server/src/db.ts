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
    CREATE TABLE IF NOT EXISTS player_state (
      player_id      INTEGER PRIMARY KEY REFERENCES players(id),
      map            TEXT NOT NULL DEFAULT '',
      x REAL NOT NULL DEFAULT 0, y REAL NOT NULL DEFAULT 0, z REAL NOT NULL DEFAULT 0, ry REAL NOT NULL DEFAULT 0,
      inventory_json TEXT NOT NULL DEFAULT '',
      personal_json  TEXT NOT NULL DEFAULT '',
      updated_at     INTEGER NOT NULL DEFAULT 0
    );
  `);
  return db;
}

export interface PlayerPos { map: string; x: number; y: number; z: number; ry: number }
export interface PlayerState extends PlayerPos { inventory_json: string; personal_json: string; updated_at: number }

const EMPTY: PlayerState = { map: '', x: 0, y: 0, z: 0, ry: 0, inventory_json: '', personal_json: '', updated_at: 0 };

// No row yet -> all defaults with updated_at = 0 (clients treat 0 as "nothing saved").
export function loadState(db: DatabaseSync, id: number): PlayerState {
  const row = db.prepare(
    'SELECT map, x, y, z, ry, inventory_json, personal_json, updated_at FROM player_state WHERE player_id = ?',
  ).get(id);
  return row ? { ...(row as unknown as PlayerState) } : { ...EMPTY };
}

// Full save from the client. A null JSON field keeps the stored value.
export function saveState(
  db: DatabaseSync, id: number, p: PlayerPos, inventory: string | null, personal: string | null,
): void {
  db.prepare(`
    INSERT INTO player_state (player_id, map, x, y, z, ry, inventory_json, personal_json, updated_at)
    VALUES (?, ?, ?, ?, ?, ?, COALESCE(?, ''), COALESCE(?, ''), unixepoch())
    ON CONFLICT(player_id) DO UPDATE SET map = excluded.map, x = excluded.x, y = excluded.y, z = excluded.z,
      ry = excluded.ry, updated_at = excluded.updated_at,
      inventory_json = COALESCE(?, inventory_json), personal_json = COALESCE(?, personal_json)
  `).run(id, p.map, p.x, p.y, p.z, p.ry, inventory, personal, inventory, personal);
}
