import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { checkSecret } from '../src/limit.ts';
import { api, startServer, type Target } from './harness.ts';

let srv: Target;
const post = (url: string, payload: object) => api(srv, 'POST', url, payload);

before(async () => {
  srv = await startServer({ registerPerIpPerMin: 6, loginPerIpPerMin: 8, loginPerUserPerMin: 3 });
  for (let i = 0; i < 4; i++) await post('/auth/register', { username: `user${i}`, password: 'password123' });
});
after(() => srv.close());

test('M4: register is limited per IP (6 ok already used 4, so 2 more then 429)', async () => {
  assert.equal((await post('/auth/register', { username: 'extra1', password: 'password123' })).status, 201);
  assert.equal((await post('/auth/register', { username: 'extra2', password: 'password123' })).status, 201);
  assert.equal((await post('/auth/register', { username: 'extra3', password: 'password123' })).status, 429);
});

test('M4: login is limited per username and per IP', async () => {
  const bad = (u: string) => post('/auth/login', { username: u, password: 'wrong-password' });
  for (let i = 0; i < 3; i++) assert.equal((await bad('user0')).status, 401);
  assert.equal((await bad('user0')).status, 429); // 4th for the same username
  // 3 used by user0 (4th rejected by user limiter but already counted on the IP): IP budget 8
  for (let i = 0; i < 4; i++) await bad(`other${i}`);
  assert.equal((await bad('another')).status, 429); // IP budget spent
});

test('L12: placeholder JWT secret is refused', () => {
  assert.throws(() => checkSecret('change-me-to-a-long-random-string'), /placeholder/);
  assert.throws(() => checkSecret(undefined), /JWT_SECRET/);
  assert.throws(() => checkSecret('short'), /JWT_SECRET/);
  assert.equal(checkSecret('a-real-random-secret-1234'), 'a-real-random-secret-1234');
});
