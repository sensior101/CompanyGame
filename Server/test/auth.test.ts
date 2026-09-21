import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildApp } from '../src/app.ts';
import { openDb } from '../src/db.ts';

const app = buildApp(openDb(':memory:'), 'test-secret-test-secret');
const post = (url: string, payload: object, token?: string) =>
  app.inject({ method: 'POST', url, payload, headers: token ? { authorization: `Bearer ${token}` } : {} });

test('register -> login -> profile flow', async () => {
  const reg = await post('/auth/register', { username: 'tomas', password: 'password123' });
  assert.equal(reg.statusCode, 201);

  assert.equal((await post('/auth/register', { username: 'tomas', password: 'password123' })).statusCode, 409);
  assert.equal((await post('/auth/login', { username: 'tomas', password: 'wrong-password' })).statusCode, 401);
  assert.equal((await post('/auth/login', { username: 'nobody', password: 'password123' })).statusCode, 401);

  const { token } = (await post('/auth/login', { username: 'tomas', password: 'password123' })).json();
  const auth = { authorization: `Bearer ${token}` };

  assert.equal((await app.inject({ method: 'GET', url: '/me' })).statusCode, 401);
  assert.equal((await app.inject({ method: 'GET', url: '/me', headers: auth })).json().nickname, null);

  const put = (payload: object, headers = auth) => app.inject({ method: 'PUT', url: '/me/profile', payload, headers });
  assert.equal((await put({ nickname: '토마스', gender: 'male' })).statusCode, 200);
  assert.equal((await put({ nickname: 'x', gender: 'male' })).statusCode, 400);
  assert.equal((await put({ nickname: '토마스', gender: 'other' })).statusCode, 400);
  assert.equal((await app.inject({ method: 'GET', url: '/me', headers: auth })).json().nickname, '토마스');

  const other = (await post('/auth/register', { username: 'second', password: 'password123' })).json().token;
  const taken = await put({ nickname: '토마스', gender: 'female' }, { authorization: `Bearer ${other}` });
  assert.equal(taken.statusCode, 409);
});
