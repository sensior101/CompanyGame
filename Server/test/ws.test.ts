import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { api, register, startServer, type Target } from './harness.ts';

let srv: Target;
let base = '';
const tokens: Record<string, string> = {};

before(async () => {
  srv = await startServer();
  base = srv.host;
  for (const [i, nick] of ['알파', 'beta', 'gamma', 'delta'].entries()) {
    const token = await register(srv, `user${i}`);
    await api(srv, 'PUT', '/me/profile', { nickname: nick, gender: 'male' }, token);
    tokens[nick] = token;
  }
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
  send(m: object | string) { this.ws.send(typeof m === 'string' ? m : JSON.stringify(m)); }
  async next(t: string, ms = 2000): Promise<any> {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const i = this.inbox.findIndex((m) => m.t === t);
      if (i >= 0) return this.inbox.splice(i, 1)[0];
      await new Promise((r) => setTimeout(r, 10));
    }
    throw new Error(`timeout waiting for ${t}; inbox=${JSON.stringify(this.inbox).slice(0, 300)}`);
  }
  async quiet(t: string, ms = 150) {
    await new Promise((r) => setTimeout(r, ms));
    assert.equal(this.inbox.find((m) => m.t === t), undefined, `unexpected ${t}`);
  }
  ready() { return this.next('hello'); }
}

test('bad or missing token is closed with 4401', async () => {
  const codes = await Promise.all(['', 'garbage', tokens['알파'] + 'x'].map((t) => new Client(t).closed));
  assert.deepEqual(codes, [4401, 4401, 4401]);
});

test('hello carries players.id and nickname', async () => {
  const a = new Client(tokens['알파']);
  const hello = await a.ready();
  assert.equal(hello.name, '알파');
  assert.match(hello.id, /^\d+$/);
  a.ws.close();
});

test('room flow: create, join, pos, world, transfer, host migration with snap', async () => {
  const [a, b, c] = ['알파', 'beta', 'gamma'].map((n) => new Client(tokens[n]));
  const [ha, hb, hc] = [await a.ready(), await b.ready(), await c.ready()];

  a.send({ t: 'create', room: 'test room', max: 3 });
  const joinedA = await a.next('joined');
  assert.equal(joinedA.host, ha.id);

  b.send({ t: 'list' });
  const rooms = (await b.next('rooms')).rooms;
  assert.deepEqual([rooms[0].room, rooms[0].hostName, rooms[0].count, rooms[0].max], ['test room', '알파', 1, 3]);

  b.send({ t: 'join', rid: joinedA.rid });
  assert.deepEqual((await b.next('joined')).players.map((p: any) => p.name), ['알파', 'beta']);
  assert.equal((await a.next('peer')).ev, 'join');
  c.send({ t: 'join', rid: joinedA.rid });
  await c.next('joined');
  await a.next('peer'); await b.next('peer');

  // pos relay: sender excluded, bad pos dropped
  b.send({ t: 'pos', map: 'm', x: 1, y: 2, z: 3, ry: 4, sp: 5 });
  b.send({ t: 'pos', map: 'm', x: 'bad', y: 2, z: 3, ry: 4, sp: 5 });
  const pos = await a.next('pos');
  assert.deepEqual([pos.from, pos.map, pos.x, pos.sp], [hb.id, 'm', 1, 5]);
  await b.quiet('pos');
  await a.quiet('pos');

  // world: host only, not echoed to host
  b.send({ t: 'world', ev: 'citizens', data: '{}' });
  assert.equal((await b.next('err')).code, 'not_host');
  a.send({ t: 'world', ev: 'traffic', data: '{"s":1}' });
  assert.deepEqual(await c.next('world'), { t: 'world', ev: 'traffic', data: '{"s":1}' });
  await a.quiet('world');

  // state: host only; snap kept for migration
  b.send({ t: 'state', snap: '{"min":1}' });
  assert.equal((await b.next('err')).code, 'not_host');
  a.send({ t: 'state', snap: '{"min":42}' });
  assert.equal((await b.next('state')).snap, '{"min":42}');

  // transfer: host only
  b.send({ t: 'transfer', to: hc.id });
  assert.equal((await b.next('err')).code, 'not_host');
  a.send({ t: 'transfer', to: hb.id });
  for (const x of [a, b, c]) assert.equal((await x.next('host')).host, hb.id);

  // new host b disconnects -> earliest remaining (a) becomes host with the snap
  b.ws.close();
  const host = await a.next('host');
  assert.deepEqual([host.host, host.reason, host.snap], [ha.id, 'disconnect', '{"min":42}']);
  assert.equal((await c.next('host')).host, ha.id);

  a.ws.close(); c.ws.close();
  await a.closed; await c.closed;
});

test('room code: 6 chars from the safe alphabet, join is case-insensitive, unknown codes are room_not_found', async () => {
  const [a, b, c] = ['알파', 'beta', 'gamma'].map((n) => new Client(tokens[n]));
  await a.ready(); await b.ready(); await c.ready();
  a.send({ t: 'create', room: 'code room', max: 4 });
  const { rid } = await a.next('joined');
  assert.match(rid, /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$/);

  b.send({ t: 'join', rid: rid.toLowerCase() });
  assert.equal((await b.next('joined')).rid, rid); // the room's own code comes back, upper case
  assert.equal((await a.next('peer')).ev, 'join');

  c.send({ t: 'join', rid: 'ZZZZZZ' === rid ? 'YYYYYY' : 'ZZZZZZ' });
  assert.equal((await c.next('err')).code, 'room_not_found');
  c.send({ t: 'join', rid: ' ' + rid.slice(0, 3).toLowerCase() + rid.slice(3) + ' ' }); // mixed case, stray spaces
  assert.equal((await c.next('joined')).rid, rid);
  for (const x of [a, b, c]) x.ws.close();
});

test('msg: any member to the current host, host to anyone', async () => {
  const [a, b, c] = ['알파', 'beta', 'gamma'].map((n) => new Client(tokens[n]));
  const [ha, hb, hc] = [await a.ready(), await b.ready(), await c.ready()];
  a.send({ t: 'create', room: 'msgs', max: 3 });
  const { rid } = await a.next('joined');
  for (const x of [b, c]) { x.send({ t: 'join', rid }); await x.next('joined'); }
  b.send({ t: 'msg', to: ha.id, data: 'hello host' });
  assert.deepEqual(await a.next('msg'), { t: 'msg', from: hb.id, data: 'hello host' });
  b.send({ t: 'msg', to: hc.id, data: 'x' });
  assert.equal((await b.next('err')).code, 'not_host');
  a.send({ t: 'msg', to: hc.id, data: 'reply' });
  assert.equal((await c.next('msg')).data, 'reply');
  for (const x of [a, b, c]) x.ws.close();
});

test('op: ordered across two senders, echoed to sender, included in joined, limited', async () => {
  const [a, b, c] = ['알파', 'beta', 'gamma'].map((n) => new Client(tokens[n]));
  await a.ready(); await b.ready(); await c.ready();
  a.send({ t: 'create', room: 'ops', max: 4 });
  const { rid } = await a.next('joined');
  b.send({ t: 'join', rid });
  await b.next('joined');

  a.send({ t: 'op', ev: 'prop', data: '{"n":1}' });
  b.send({ t: 'op', ev: 'sns', data: '{"n":2}' });
  const seen = async (x: Client) => [await x.next('op'), await x.next('op')];
  const [oa, ob] = [await seen(a), await seen(b)];
  assert.deepEqual(oa, ob); // both see the same order, sender included
  assert.deepEqual(oa.map((o) => o.seq), [1, 2]);
  assert.deepEqual(oa.map((o) => o.ev).sort(), ['prop', 'sns']);

  // late joiner gets the history in joined
  c.send({ t: 'join', rid });
  const joined = await c.next('joined');
  assert.deepEqual(joined.ops, oa.map(({ t, ...op }) => op));

  // limits: 8KB per op, bad shapes, 10 ops/s
  a.send({ t: 'op', ev: 'big', data: 'x'.repeat(9000) });
  assert.equal((await a.next('err')).code, 'bad_request');
  a.send({ t: 'op', ev: '', data: '{}' });
  assert.equal((await a.next('err')).code, 'bad_request');
  await new Promise((r) => setTimeout(r, 1100)); // refill the bucket
  for (let i = 0; i < 25; i++) b.send({ t: 'op', ev: 'spam', data: String(i) });
  await new Promise((r) => setTimeout(r, 400));
  const accepted = b.inbox.filter((m) => m.t === 'op').length;
  const limited = b.inbox.filter((m) => m.t === 'err' && m.code === 'rate_limited').length;
  assert.ok(accepted >= 10 && accepted <= 14, `accepted ${accepted}`);
  assert.equal(accepted + limited, 25);

  a.ws.close(); b.ws.close(); c.ws.close();
});

test('input validation and size limits', async () => {
  const a = new Client(tokens['알파']);
  await a.ready();
  for (const bad of ['not json', '[1]', '{"t":1}', '{"t":"nope"}', { t: 'create', room: '', max: 2 },
    { t: 'create', room: 'x', max: 99 }, { t: 'join' }, { t: 'leave' }]) {
    a.send(bad);
    const code = (await a.next('err')).code;
    assert.ok(['bad_request', 'not_in_room'].includes(code), code);
  }
  a.send({ t: 'create', room: 'limits', max: 2 });
  await a.next('joined');
  // non-state messages are capped at 8KB, state at 512KB
  a.send({ t: 'world', ev: 'big', data: 'x'.repeat(9000) });
  assert.equal((await a.next('err')).code, 'bad_request');
  a.send({ t: 'state', snap: 'x'.repeat(100_000) });
  await a.quiet('err');
  a.send({ t: 'state', snap: 'x'.repeat(600_000) });
  assert.ok([1006, 1009].includes(await a.closed)); // above the frame limit the socket is dropped
});

test('pos is rate limited to ~30/s', async () => {
  const [a, b] = [new Client(tokens['알파']), new Client(tokens['beta'])];
  await a.ready(); await b.ready();
  a.send({ t: 'create', room: 'rate', max: 2 });
  const { rid } = await a.next('joined');
  b.send({ t: 'join', rid });
  await b.next('joined');
  for (let i = 0; i < 50; i++) a.send({ t: 'pos', map: 'm', x: i, y: 0, z: 0, ry: 0, sp: 0 });
  await new Promise((r) => setTimeout(r, 300));
  const got = b.inbox.filter((m) => m.t === 'pos').length;
  assert.ok(got >= 25 && got <= 40, `relayed ${got}`);
  a.ws.close(); b.ws.close();
});

test('same account connecting twice replaces the old connection', async () => {
  const first = new Client(tokens['delta']);
  await first.ready();
  const second = new Client(tokens['delta']);
  await second.ready();
  assert.equal(await first.closed, 4409);
  second.ws.close();
});

test('/me/state round trip, validation, and last pos saved on disconnect', async () => {
  const tok = tokens['gamma'];
  const get = () => api(srv, 'GET', '/me/state', undefined, tok);
  const put = (payload: object) => api(srv, 'PUT', '/me/state', payload, tok);

  assert.equal((await api(srv, 'GET', '/me/state')).status, 401);
  assert.equal((await get()).json.updated_at, 0);

  const inv = JSON.stringify({ items: [{ id: 'cash:10000', n: 3 }] });
  const personal = JSON.stringify({ bank: 12345, portfolio: [] });
  const saved = await put({ map: 'Maps/BD', x: 1.5, y: 2, z: -3, ry: 90, inventory_json: inv, personal_json: personal });
  assert.equal(saved.status, 200);
  const s = (await get()).json;
  assert.deepEqual([s.map, s.x, s.y, s.z, s.ry, s.inventory_json, s.personal_json], ['Maps/BD', 1.5, 2, -3, 90, inv, personal]);
  assert.ok(s.updated_at > 0);

  // omitting the json fields keeps what is stored
  await put({ map: 'Maps/BD', x: 9, y: 2, z: -3, ry: 90 });
  const kept = (await get()).json;
  assert.deepEqual([kept.x, kept.inventory_json, kept.personal_json], [9, inv, personal]);

  assert.equal((await put({ map: 'm', x: 'a', y: 0, z: 0, ry: 0 })).status, 400);
  assert.equal((await put({ x: 0, y: 0, z: 0, ry: 0 })).status, 400);
  assert.equal((await put({ map: 'm', x: 0, y: 0, z: 0, ry: 0, inventory_json: '{not json' })).status, 400);
  assert.equal((await put({ map: 'm', x: 1e9, y: 0, z: 0, ry: 0 })).status, 400);
  const huge = await put({ map: 'm', x: 0, y: 0, z: 0, ry: 0, inventory_json: JSON.stringify('x'.repeat(70_000)) });
  assert.equal(huge.status, 413);
  assert.equal((await get()).json.x, 9); // rejected writes changed nothing

  // disconnect saves the last pos, keeps the json blobs
  const c = new Client(tokens['gamma']);
  await c.ready();
  c.send({ t: 'pos', map: 'Maps/Coast', x: 7, y: 8, z: 9, ry: 10, sp: 0 });
  await new Promise((r) => setTimeout(r, 100));
  c.ws.close();
  await c.closed;
  await new Promise((r) => setTimeout(r, 100));
  const after = (await get()).json;
  assert.deepEqual([after.map, after.x, after.y, after.z, after.ry, after.inventory_json], ['Maps/Coast', 7, 8, 9, 10, inv]);
});

test('GET /rooms needs a token and lists rooms', async () => {
  assert.equal((await api(srv, 'GET', '/rooms')).status, 401);
  const res = await api(srv, 'GET', '/rooms', undefined, tokens['beta']);
  assert.equal(res.status, 200);
  assert.ok(Array.isArray(res.json.rooms));
});
