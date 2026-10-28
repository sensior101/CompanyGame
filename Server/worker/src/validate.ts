// Request body rules, equal to the Fastify JSON schemas in Server/src/app.ts (unknown fields are ignored).
type Obj = Record<string, unknown>;
export interface Credentials { username: string; password: string }
export interface Profile { nickname: string; gender: 'male' | 'female' }

export const BODY_LIMIT = 4 * 1024;
const len = (s: string) => [...s].length; // characters, not UTF-16 units (JSON schema maxLength)

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
