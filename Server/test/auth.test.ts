import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { api, startServer, type Target } from './harness.ts';

let t: Target;
before(async () => { t = await startServer(); });
after(() => t.close());
const post = (url: string, payload: object | string) => api(t, 'POST', url, payload);

test('register -> login -> profile flow', async () => {
  const reg = await post('/auth/register', { username: 'tomas', password: 'password123' });
  assert.equal(reg.status, 201);

  assert.equal((await post('/auth/register', { username: 'tomas', password: 'password123' })).status, 409);
  assert.equal((await post('/auth/login', { username: 'tomas', password: 'wrong-password' })).status, 401);
  assert.equal((await post('/auth/login', { username: 'nobody', password: 'password123' })).status, 401);

  const { token } = (await post('/auth/login', { username: 'tomas', password: 'password123' })).json;
  const me = (tok?: string) => api(t, 'GET', '/me', undefined, tok);

  assert.equal((await me()).status, 401);
  assert.equal((await me(token)).json.nickname, null);

  const put = (payload: object, tok = token) => api(t, 'PUT', '/me/profile', payload, tok);
  assert.equal((await put({ nickname: '토마스', gender: 'male' })).status, 200);
  assert.equal((await put({ nickname: 'x', gender: 'male' })).status, 400);
  assert.equal((await put({ nickname: '토마스', gender: 'other' })).status, 400);
  assert.equal((await me(token)).json.nickname, '토마스');

  const other = (await post('/auth/register', { username: 'second', password: 'password123' })).json.token;
  const taken = await put({ nickname: '토마스', gender: 'female' }, other);
  assert.equal(taken.status, 409);
});

test('input rules: bad usernames/passwords/bodies are 400, garbage tokens are 401', async () => {
  for (const bad of [{ username: 'AB', password: 'password123' }, { username: 'UPPER_case', password: 'password123' },
    { username: 'okname', password: 'short' }, { username: 'okname' }, { username: 5, password: 'password123' }]) {
    assert.equal((await post('/auth/register', bad)).status, 400);
  }
  assert.equal((await post('/auth/register', 'not json')).status, 400);
  assert.equal((await api(t, 'GET', '/me', undefined, 'garbage')).status, 401);
});
