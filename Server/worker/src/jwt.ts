// HS256 JWT on WebCrypto. Same token shape as @fastify/jwt on the Node server: {id, iat, exp}.
const enc = new TextEncoder();
const TTL_SEC = 7 * 24 * 3600;

const b64u = (b: ArrayBuffer | Uint8Array) =>
  btoa(String.fromCharCode(...new Uint8Array(b))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const unb64u = (s: string) => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), (c) => c.charCodeAt(0));
const key = (secret: string, use: 'sign' | 'verify') =>
  crypto.subtle.importKey('raw', enc.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, [use]);

export async function signJwt(id: number, secret: string, now = Date.now()): Promise<string> {
  const iat = Math.floor(now / 1000);
  const head = b64u(enc.encode(JSON.stringify({ alg: 'HS256', typ: 'JWT' })));
  const body = b64u(enc.encode(JSON.stringify({ id, iat, exp: iat + TTL_SEC })));
  const sig = await crypto.subtle.sign('HMAC', await key(secret, 'sign'), enc.encode(`${head}.${body}`));
  return `${head}.${body}.${b64u(sig)}`;
}

// null for anything that is not a valid, unexpired HS256 token (bad shape, other alg, bad signature, expired).
export async function verifyJwt(token: string, secret: string, now = Date.now()): Promise<{ id: number } | null> {
  const parts = token.split('.');
  if (parts.length !== 3 || token.length > 2048) return null;
  try {
    if (JSON.parse(new TextDecoder().decode(unb64u(parts[0]))).alg !== 'HS256') return null;
    const ok = await crypto.subtle.verify('HMAC', await key(secret, 'verify'), unb64u(parts[2]), enc.encode(`${parts[0]}.${parts[1]}`));
    if (!ok) return null;
    const { id, exp } = JSON.parse(new TextDecoder().decode(unb64u(parts[1])));
    return Number.isInteger(id) && typeof exp === 'number' && exp * 1000 > now ? { id } : null;
  } catch {
    return null;
  }
}
