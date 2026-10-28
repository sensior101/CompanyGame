// Only what accounts.ts uses from node:crypto (available through the nodejs_compat flag).
declare module 'node:crypto' {
  export function randomBytes(size: number): Uint8Array;
  export function scrypt(password: string, salt: Uint8Array, keylen: number, cb: (err: Error | null, key: Uint8Array) => void): void;
  export function timingSafeEqual(a: Uint8Array, b: Uint8Array): boolean;
}
