// Request body rules, equal to the Fastify JSON schemas in Server/src/app.ts (unknown fields are ignored).
type Obj = Record<string, unknown>;
export interface Credentials { username: string; password: string }
export interface Profile { nickname: string; gender: 'male' | 'female' }
export interface StateBody { map: string; x: number; y: number; z: number; ry: number; inventory_json?: string; personal_json?: string }

export const STATE_BODY_LIMIT = 64 * 1024;
const len = (s: string) => [...s].length; // characters, not UTF-16 units (JSON schema maxLength)
const coord = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && Math.abs(v) <= 1e6;
const jsonField = (v: unknown) => v === undefined || (typeof v === 'string' && len(v) <= STATE_BODY_LIMIT);

export function credentials(b: Obj): Credentials | null {
  const { username, password } = b;
  const ok = typeof username === 'string' && /^[a-z0-9_]{3,20}$/.test(username)
    && typeof password === 'string' && len(password) >= 8 && len(password) <= 64;
  return ok ? { username, password } : null;
}

export function profile(b: Obj): Profile | null {
  const { nickname, gender } = b;
  const ok = typeof nickname === 'string' && /^[가-힣A-Za-z0-9_]{2,12}$/.test(nickname) && (gender === 'male' || gender === 'female');
  return ok ? { nickname, gender } : null;
}

export function stateBody(b: Obj): StateBody | null {
  const { map, x, y, z, ry, inventory_json, personal_json } = b;
  const ok = typeof map === 'string' && len(map) <= 200 && coord(x) && coord(y) && coord(z) && coord(ry)
    && jsonField(inventory_json) && jsonField(personal_json);
  return ok ? { map, x, y, z, ry, inventory_json: inventory_json as string | undefined, personal_json: personal_json as string | undefined } : null;
}

// "" means "nothing"; anything else must at least be valid JSON.
export function isJsonOrEmpty(s: string | undefined): boolean {
  if (!s) return true;
  try { JSON.parse(s); return true; } catch { return false; }
}
