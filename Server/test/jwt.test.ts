import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { signJwt, verifyJwt } from '../worker/src/jwt.ts';
import { register, SECRET, startServer, type Target } from './harness.ts';

const b64u = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');
let srv: Target;
before(async () => { srv = await startServer(); });
after(() => srv.close());

test('a token issued by the running server verifies with the worker JWT code (same HS256 format)', async () => {
  const token = await register(srv, 'jwtuser');
  assert.deepEqual(await verifyJwt(token, SECRET), { id: 1 });
  assert.equal(await verifyJwt(token, 'another-secret-another-secret'), null);
});

test('sign/verify: round trip, tampering, expiry, wrong algorithm', async () => {
  const now = Date.now();
  const token = await signJwt(42, SECRET, now);
  assert.deepEqual(await verifyJwt(token, SECRET, now + 1000), { id: 42 });
  assert.equal(await verifyJwt(token, SECRET, now + 7 * 24 * 3600 * 1000 + 1000), null); // 7 days later
  const [h, p, s] = token.split('.');
  assert.equal(await verifyJwt(`${h}.${b64u({ id: 43, exp: 9e9 })}.${s}`, SECRET, now), null); // payload swapped
  assert.equal(await verifyJwt(`${b64u({ alg: 'none' })}.${p}.`, SECRET, now), null);
  assert.equal(await verifyJwt(`${b64u({ alg: 'none' })}.${p}.${s}`, SECRET, now), null);
  for (const junk of ['', 'a.b', 'a.b.c', '....', 'x'.repeat(3000)]) assert.equal(await verifyJwt(junk, SECRET, now), null);
});
