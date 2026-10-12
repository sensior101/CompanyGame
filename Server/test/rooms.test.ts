import assert from 'node:assert/strict';
import { test } from 'node:test';
import { MAX_OPS, MAX_ROOMS, newRoomCode, ROOM_CODE_ALPHABET, Rooms, type Op, type Peer, type RoomRow, type RoomStore } from '../src/rooms.ts';

type P = Peer & { got: any[] };
const peer = (id: string): P => {
  const got: any[] = [];
  return { id, name: `n${id}`, got, send: (m) => got.push(m) };
};
const last = (p: P, t: string) => [...p.got].reverse().find((m) => m.t === t);

test('create, list, join, full, leave', () => {
  const r = new Rooms();
  const [a, b, c] = [peer('1'), peer('2'), peer('3')];
  assert.equal(r.create(a, 'room', 2), null);
  assert.equal(last(a, 'joined').host, '1');
  assert.equal(r.create(a, 'again', 2), 'already_in_room');
  assert.equal(r.create(b, '', 2), 'bad_request');
  assert.equal(r.create(b, 'x', 9), 'bad_request');
  assert.deepEqual(r.list().map((x) => [x.room, x.host, x.hostName, x.count, x.max]), [['room', '1', 'n1', 1, 2]]);

  const rid = last(a, 'joined').rid;
  assert.equal(r.join(b, 'nope'), 'room_not_found');
  assert.equal(r.join(b, rid), null);
  assert.deepEqual(last(b, 'joined').players.map((p: any) => p.id), ['1', '2']);
  assert.deepEqual(last(a, 'peer'), { t: 'peer', ev: 'join', id: '2', name: 'n2' });
  assert.equal(r.join(c, rid), 'room_full');

  assert.equal(r.leave(c), 'not_in_room');
  assert.equal(r.leave(b), null);
  assert.equal(last(a, 'peer').ev, 'leave');
  assert.equal(r.join(c, rid), null);
});

test('transfer is host only and needs a member target', () => {
  const r = new Rooms();
  const [a, b, out] = [peer('1'), peer('2'), peer('9')];
  r.create(a, 'room', 4);
  r.join(b, last(a, 'joined').rid);
  assert.equal(r.transfer(out, '1'), 'not_in_room');
  assert.equal(r.transfer(b, '1'), 'not_host');
  assert.equal(r.transfer(a, '9'), 'peer_not_found');
  assert.equal(r.transfer(a, '1'), 'peer_not_found');
  assert.equal(r.transfer(a, '2'), null);
  for (const p of [a, b]) assert.deepEqual([last(p, 'host').host, last(p, 'host').reason], ['2', 'transfer']);
  assert.equal(r.transfer(a, '1'), 'not_host');
});

test('host leaving hands over to earliest joiner with the last snap; empty room is deleted', () => {
  const r = new Rooms();
  const [a, b, c] = [peer('1'), peer('2'), peer('3')];
  r.create(a, 'room', 4);
  const rid = last(a, 'joined').rid;
  r.join(b, rid);
  r.join(c, rid);
  assert.equal(r.setSnap(b, '{"min":1}'), 'not_host');
  assert.equal(r.setSnap(a, '{"min":1}'), null);
  assert.equal(r.setSnap(a, '{"min":2,"opSeq":0}'), null);
  assert.equal(last(b, 'state').snap, '{"min":2,"opSeq":0}');
  assert.equal(last(a, 'state'), undefined);

  r.leave(a);
  const h = last(b, 'host');
  assert.deepEqual([h.host, h.reason, h.snap], ['2', 'disconnect', '{"min":2,"opSeq":0}']);
  assert.equal(last(c, 'host').host, '2');

  const d = peer('4');
  r.join(d, rid);
  assert.equal(last(d, 'joined').snap, '{"min":2,"opSeq":0}');

  r.leave(b); r.leave(c); r.leave(d);
  assert.equal(r.roomCount, 0);
  assert.equal(r.join(peer('5'), rid), 'room_not_found');
});

test('a stale connection with the same id cannot evict the new one', () => {
  const r = new Rooms();
  const old = peer('1');
  const fresh = peer('1');
  r.create(fresh, 'room', 4);
  assert.equal(r.leave(old), 'not_in_room');
  assert.equal(r.roomCount, 1);
});

test('room cap', () => {
  const r = new Rooms();
  for (let i = 0; i < MAX_ROOMS; i++) assert.equal(r.create(peer(`p${i}`), `r${i}`, 2), null);
  assert.equal(r.create(peer('extra'), 'one more', 2), 'too_many_rooms');
});

test('pos, world and msg relays', () => {
  const r = new Rooms();
  const [a, b, c] = [peer('1'), peer('2'), peer('3')];
  r.create(a, 'room', 4);
  r.join(b, last(a, 'joined').rid);
  r.join(c, last(a, 'joined').rid);
  const pos = { map: 'm', x: 1, y: 2, z: 3, ry: 4, sp: 5 };
  r.relayPos(b, pos);
  assert.deepEqual(last(a, 'pos'), { t: 'pos', from: '2', ...pos });
  assert.equal(last(b, 'pos'), undefined);
  r.relayPos(peer('8'), pos); // not in a room: ignored

  assert.equal(r.relayWorld(b, 'citizens', '{}'), 'not_host');
  assert.equal(r.relayWorld(a, 'citizens', '{"i":1}'), null);
  assert.deepEqual(last(c, 'world'), { t: 'world', ev: 'citizens', data: '{"i":1}' });
  assert.equal(last(a, 'world'), undefined);
  const d = peer('4');
  r.join(d, last(a, 'joined').rid);
  assert.equal(last(d, 'world'), undefined); // not stored

  assert.equal(r.relayMsg(b, '', 'x'), 'not_host');
  assert.equal(r.relayMsg(b, '3', 'x'), 'not_host'); // non-host to non-host
  assert.equal(r.relayMsg(peer('8'), '1', 'x'), 'not_in_room');
  assert.equal(r.relayMsg(b, '1', 'to host'), null); // any member -> current host
  assert.deepEqual(last(a, 'msg'), { t: 'msg', from: '2', data: 'to host' });
  assert.equal(last(c, 'msg'), undefined);
  r.transfer(a, '2');
  assert.equal(r.relayMsg(c, '1', 'old host'), 'not_host'); // follows the CURRENT host
  assert.equal(r.relayMsg(c, '2', 'new host'), null);
  r.transfer(b, '1');
  assert.equal(r.relayMsg(a, '3', 'hi'), null);
  assert.equal(last(c, 'msg').data, 'hi');
  assert.equal(last(b, 'msg').data, 'new host'); // unaffected by the direct msg to 3
  assert.equal(r.relayMsg(a, '77', 'hi'), 'peer_not_found');
});

test('op: server seq, echo to everyone incl. sender, ordering across senders', () => {
  const r = new Rooms();
  const [a, b, out] = [peer('1'), peer('2'), peer('9')];
  r.create(a, 'room', 4);
  r.join(b, last(a, 'joined').rid);
  assert.equal(r.relayOp(out, 'x', '{}'), 'not_in_room');
  assert.equal(r.relayOp(b, 'buy', '{"n":1}'), null);
  assert.equal(r.relayOp(a, 'sell', '{"n":2}'), null);
  assert.equal(r.relayOp(b, 'buy', '{"n":3}'), null);
  for (const p of [a, b]) {
    const ops = p.got.filter((m) => m.t === 'op');
    assert.deepEqual(ops.map((o) => [o.seq, o.from, o.ev]), [[1, '2', 'buy'], [2, '1', 'sell'], [3, '2', 'buy']]);
  }
});

test('op: joined carries recent ops, capped at MAX_OPS, seq keeps counting', () => {
  const r = new Rooms();
  const a = peer('1');
  r.create(a, 'room', 4);
  for (let i = 0; i < MAX_OPS + 10; i++) r.relayOp(a, 'e', String(i));
  const b = peer('2');
  r.join(b, last(a, 'joined').rid);
  const ops = last(b, 'joined').ops;
  assert.equal(ops.length, MAX_OPS);
  assert.deepEqual([ops[0].seq, ops[MAX_OPS - 1].seq], [11, MAX_OPS + 10]);
  r.relayOp(b, 'e', 'next');
  assert.equal(last(a, 'op').seq, MAX_OPS + 11);
  assert.deepEqual(last(a, 'joined').ops, []); // a's own joined copy was taken before any op
});

test('room codes: 6 chars, no 0/O/1/I, unique among live rooms', () => {
  assert.equal(ROOM_CODE_ALPHABET.length, 32);
  assert.equal(new Set(ROOM_CODE_ALPHABET).size, 32);
  for (const bad of '0O1I') assert.ok(!ROOM_CODE_ALPHABET.includes(bad));
  for (let i = 0; i < 500; i++) assert.match(newRoomCode(), /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$/);

  const r = new Rooms();
  const rids = new Set<string>();
  for (let i = 0; i < MAX_ROOMS; i++) {
    const p = peer(`p${i}`);
    r.create(p, `r${i}`, 2);
    rids.add(last(p, 'joined').rid);
  }
  assert.equal(rids.size, MAX_ROOMS);
});

test('join accepts any letter case and surrounding spaces, nothing else', () => {
  const r = new Rooms({ newCode: () => 'AB3K9Z' });
  const [a, b, c, d] = [peer('1'), peer('2'), peer('3'), peer('4')];
  r.create(a, 'room', 4);
  assert.equal(last(a, 'joined').rid, 'AB3K9Z');
  assert.equal(r.join(b, 'ab3k9z'), null);
  assert.equal(last(b, 'joined').rid, 'AB3K9Z');
  assert.equal(r.join(c, '  aB3k9Z '), null);
  assert.equal(r.join(d, 'AB3K9'), 'room_not_found'); // too short
  assert.equal(r.join(d, 'AB3K9ZZ'), 'room_not_found'); // too long
  assert.equal(r.join(d, 'ab3k9ſ'), 'room_not_found'); // U+017F would upper-case to S: only ASCII letters count
  assert.equal(r.join(d, ''), 'room_not_found');
});

test('a colliding code is re-rolled; a broken generator gives too_many_rooms instead of hanging', () => {
  const codes = ['AAAAAA', 'AAAAAA', 'BBBBBB'];
  const r = new Rooms({ newCode: () => codes.shift() ?? 'BBBBBB' });
  const [a, b, c] = [peer('1'), peer('2'), peer('3')];
  r.create(a, 'one', 2);
  r.create(b, 'two', 2);
  assert.equal(last(b, 'joined').rid, 'BBBBBB');
  assert.equal(r.create(c, 'three', 2), 'too_many_rooms'); // generator keeps returning BBBBBB
});

test('store hooks and restore: a room survives the transport losing its memory', () => {
  const saved = new Map<string, RoomRow>();
  const ops: Op[] = [];
  const store: RoomStore = {
    save: (row) => saved.set(row.rid, { ...row }),
    remove: (rid) => saved.delete(rid),
    addOp: (_rid, op) => ops.push(op),
  };
  const r = new Rooms({ store });
  const [a, b] = [peer('1'), peer('2')];
  r.create(a, 'persisted', 3);
  const rid = last(a, 'joined').rid;
  r.join(b, rid);
  r.setSnap(a, '{"min":7}');
  r.relayOp(b, 'buy', '{"n":1}');
  r.relayOp(a, 'sell', '{"n":2}');
  assert.deepEqual(saved.get(rid), { rid, name: 'persisted', host: '1', max: 3, snap: '{"min":7}' });
  assert.deepEqual(ops.map((o) => o.seq), [1, 2]);
  r.transfer(a, '2');
  assert.equal(saved.get(rid)!.host, '2');

  // "wake-up": new Rooms from the stored row + ops + the sockets that are still connected (join order)
  const again = new Rooms({ store });
  const [a2, b2] = [peer('1'), peer('2')];
  again.restore(saved.get(rid)!, ops, [a2, b2]);
  assert.equal(again.ridOf('2'), rid);
  assert.deepEqual(again.list().map((x) => [x.rid, x.host, x.count]), [[rid, '2', 2]]);
  assert.equal(again.relayOp(a2, 'e', 'x'), null);
  assert.equal(last(b2, 'op').seq, 3); // seq continues
  again.leave(b2); // host leaves: earliest remaining member (a2) takes over with the stored snapshot
  assert.deepEqual([last(a2, 'host').host, last(a2, 'host').snap], ['1', '{"min":7}']);
  const c = peer('3');
  assert.equal(again.join(c, rid.toLowerCase()), null);
  assert.deepEqual(last(c, 'joined').ops.map((o: Op) => o.seq), [1, 2, 3]);
  again.leave(a2); again.leave(c);
  assert.equal(saved.has(rid), false); // empty room removed from storage
});

test('restore falls back to the earliest member when the stored host is gone', () => {
  const saved: RoomRow[] = [];
  const r = new Rooms({ store: { save: (x) => saved.push({ ...x }), remove: () => {}, addOp: () => {} } });
  const [m1, m2] = [peer('5'), peer('6')];
  r.restore({ rid: 'ABCDEF', name: 'n', host: '9', max: 4, snap: 's' }, [], [m1, m2]);
  assert.equal(r.list()[0].host, '5');
  assert.equal(saved.at(-1)!.host, '5');
});
