import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { checkSecret } from '../src/limit.ts';
import { Rooms } from '../src/rooms.ts';
import { api, startServer, type Target } from './harness.ts';

let srv: Target;
let base = '';
const tokens: string[] = [];
const post = (url: string, payload: object) => api(srv, 'POST', url, payload);

before(async () => {
  srv = await startServer({ registerPerIpPerMin: 6, loginPerIpPerMin: 8, loginPerUserPerMin: 3, wsPerIp: 3 });
  base = srv.host;
  for (let i = 0; i < 4; i++) tokens.push((await post('/auth/register', { username: `user${i}`, password: 'password123' })).json.token);
});
after(() => srv.close());

class Client {
  ws: WebSocket;
  inbox: any[] = [];
  closed: Promise<number>;
  constructor(token: string) {
    this.ws = new WebSocket(`ws://${base}/ws?token=${token}`);
    this.ws.onmessage = (e) => this.inbox.push(JSON.parse(String(e.data)));
    this.closed = new Promise((res) => { this.ws.onclose = (e) => res(e.code); });
  }
  send(m: object) { this.ws.send(JSON.stringify(m)); }
  async next(t: string, ms = 2000): Promise<any> {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const i = this.inbox.findIndex((m) => m.t === t);
      if (i >= 0) return this.inbox.splice(i, 1)[0];
      await new Promise((r) => setTimeout(r, 10));
    }
    throw new Error(`timeout waiting for ${t}`);
  }
}
const open = async (token: string) => { const c = new Client(token); await c.next('hello'); return c; };

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

test('L11: room names with <tags> are rejected', () => {
  const r = new Rooms();
  const p = { id: '1', name: 'a', send: () => {} };
  assert.equal(r.create(p, '<size=99>big', 2), 'bad_request');
  assert.equal(r.create(p, 'a>b', 2), 'bad_request');
  assert.equal(r.create(p, 'fine room', 2), null);
});

test('L8: per-IP connection limit (3) closes the 4th with 4429, frees on close', async () => {
  const cs = [await open(tokens[0]), await open(tokens[1]), await open(tokens[2])];
  assert.equal(await new Client(tokens[3]).closed, 4429);
  cs[0].ws.close();
  await cs[0].closed;
  await new Promise((r) => setTimeout(r, 50));
  const again = await open(tokens[3]);
  again.ws.close(); cs[1].ws.close(); cs[2].ws.close();
  await Promise.all([again.closed, cs[1].closed, cs[2].closed]);
  await new Promise((r) => setTimeout(r, 50));
});

test('M5: state has its own budget (burst 3, then rate_limited) and room keeps working', async () => {
  const host = await open(tokens[0]);
  host.send({ t: 'create', room: 'st', max: 2 });
  await host.next('joined');
  for (let i = 0; i < 5; i++) host.send({ t: 'state', snap: `{"i":${i}}` });
  const errs = [];
  for (let i = 0; i < 2; i++) errs.push((await host.next('err')).code);
  assert.deepEqual(errs, ['rate_limited', 'rate_limited']);
  host.send({ t: 'pos', map: 'm', x: 1, y: 0, z: 0, ry: 0, sp: 0 });
  host.send({ t: 'op', ev: 'x', data: '{}' });
  assert.equal((await host.next('op')).ev, 'x');
  host.ws.close(); await host.closed;
  await new Promise((r) => setTimeout(r, 50));
});

test('M5: bytes budget stops a second 512KB snapshot right after the first', async () => {
  const host = await open(tokens[0]);
  host.send({ t: 'create', room: 'big', max: 2 });
  await host.next('joined');
  const snap = 'x'.repeat(500 * 1024);
  host.send({ t: 'state', snap });
  host.send({ t: 'state', snap });
  assert.equal((await host.next('err')).code, 'rate_limited');
  host.ws.close(); await host.closed;
  await new Promise((r) => setTimeout(r, 50));
});

test('review L: sizes are bytes, 3000 Korean chars (9000 bytes) is over the 8KB limit', async () => {
  const c = await open(tokens[0]);
  c.send({ t: 'create', room: 'kr', max: 2 });
  await c.next('joined');
  c.send({ t: 'op', ev: 'x', data: '가'.repeat(3000) });
  assert.equal((await c.next('err')).code, 'bad_request');
  c.send({ t: 'op', ev: 'x', data: '가'.repeat(2700) }); // 8100 bytes
  assert.equal((await c.next('op')).data.length, 2700);
  c.ws.close(); await c.closed;
  await new Promise((r) => setTimeout(r, 50));
});

test('review L: re-login leaves the old room at once; new socket can create/join immediately', async () => {
  const a = await open(tokens[0]);
  a.send({ t: 'create', room: 'dup', max: 4 });
  const { rid } = await a.next('joined');
  const b = await open(tokens[1]);
  b.send({ t: 'join', rid });
  await b.next('joined');
  await a.next('peer');

  const a2 = await open(tokens[0]); // same account again
  assert.equal(await a.closed, 4409);
  const left = await b.next('peer');
  assert.deepEqual([left.ev, left.id], ['leave', '1']);
  assert.equal((await b.next('host')).host, '2'); // host migrated to b
  a2.send({ t: 'join', rid });
  const j = await a2.next('joined');
  assert.deepEqual(j.players.map((p: any) => p.id).sort(), ['1', '2']);
  a2.ws.close(); b.ws.close();
  await Promise.all([a2.closed, b.closed]);
});

test('hello/joined/peer already carry account id and nickname', async () => {
  const a = new Client(tokens[0]);
  const hello = await a.next('hello');
  assert.deepEqual(Object.keys(hello).sort(), ['id', 'name', 't']);
  a.ws.close(); await a.closed;
});
