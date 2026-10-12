// Worker only: the Lobby object must survive hibernation (no traffic for >10 s) without losing rooms, ops or snapshots.
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { api, register, startServer, type Target } from '../test/harness.ts';

const onWorker = process.env.TEST_TARGET === 'worker';
let srv: Target;
const tokens: string[] = [];

before(async () => {
  if (!onWorker) return;
  srv = await startServer();
  for (const [i, nick] of ['hosty', 'guesty'].entries()) {
    tokens.push(await register(srv, `hib${i}`));
    await api(srv, 'PUT', '/me/profile', { nickname: nick, gender: 'male' }, tokens[i]);
  }
});
after(() => srv?.close());

class Client {
  ws: WebSocket;
  inbox: any[] = [];
  constructor(token: string) {
    this.ws = new WebSocket(`ws://${srv.host}/ws?token=${token}`);
    this.ws.onmessage = (e) => this.inbox.push(JSON.parse(String(e.data)));
  }
  send(m: object) { this.ws.send(JSON.stringify(m)); }
  async next(t: string, ms = 3000): Promise<any> {
    for (const end = Date.now() + ms; Date.now() < end; await new Promise((r) => setTimeout(r, 10))) {
      const i = this.inbox.findIndex((m) => m.t === t);
      if (i >= 0) return this.inbox.splice(i, 1)[0];
    }
    throw new Error(`timeout waiting for ${t}`);
  }
}

test('rooms, ops, snapshot and host survive hibernation', { skip: !onWorker, timeout: 60_000 }, async () => {
  const host = new Client(tokens[0]);
  const hello = await host.next('hello');
  host.send({ t: 'create', room: 'sleepy', max: 3 });
  const { rid } = await host.next('joined');
  host.send({ t: 'state', snap: '{"min":5}' });
  host.send({ t: 'op', ev: 'a', data: '1' });
  host.send({ t: 'op', ev: 'b', data: '2' });
  await host.next('op'); await host.next('op');

  await new Promise((r) => setTimeout(r, 13_000)); // idle: the object is evicted from memory, sockets stay open

  const guest = new Client(tokens[1]);
  await guest.next('hello');
  guest.send({ t: 'join', rid });
  const joined = await guest.next('joined');
  assert.deepEqual([joined.host, joined.snap, joined.ops.map((o: any) => o.seq)], [hello.id, '{"min":5}', [1, 2]]);
  assert.equal((await host.next('peer')).ev, 'join');

  host.send({ t: 'op', ev: 'c', data: '3' }); // the host's socket still belongs to the room
  assert.equal((await guest.next('op')).seq, 3);
  host.send({ t: 'transfer', to: joined.players[1].id });
  assert.equal((await guest.next('host')).reason, 'transfer');

  await new Promise((r) => setTimeout(r, 13_000)); // sleep again, then the new host leaves
  guest.ws.close();
  await new Promise((r) => setTimeout(r, 300));
  const list = await api(srv, 'GET', '/rooms', undefined, tokens[0]);
  assert.deepEqual(list.json.rooms.map((x: any) => [x.rid, x.count, x.host]), [[rid, 1, hello.id]]);
  host.ws.close();
});
