// Transport-independent WebSocket protocol rules, shared by the Node server (ws.ts) and the Cloudflare
// worker (worker/src/lobby.ts): size limits in BYTES, per-connection budgets, message validation, dispatch.
import type { ErrCode, Peer, Pos, Rooms } from './rooms.ts';

export const MAX_MSG = 8192; // every message except state
export const MAX_STATE = 512 * 1024;
export const MAX_FRAME = MAX_STATE + 1024; // anything bigger drops the connection
const POS_PER_SEC = 30;
const MSG_PER_SEC = 60;
const OP_PER_SEC = 10;
const STATE_PER_SEC = 1; // `state` is a full snapshot, clients send one every ~10s
const STATE_BURST = 3;
const STATE_BYTES_PER_SEC = 128 * 1024; // sustained; the burst allows one max-size snapshot

type Msg = Record<string, unknown>;

// UTF-8 size without allocating (Korean text is 3 bytes/char). Lone surrogates count 3, like TextEncoder's U+FFFD.
export function utf8Len(s: string): number {
  let n = 0;
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c < 0x80) n += 1;
    else if (c < 0x800) n += 2;
    else if (c >= 0xd800 && c < 0xdc00 && i + 1 < s.length && (s.charCodeAt(i + 1) & 0xfc00) === 0xdc00) { n += 4; i++; }
    else n += 3;
  }
  return n;
}

const str = (v: unknown, max: number): v is string => typeof v === 'string' && utf8Len(v) <= max;
const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && Math.abs(v) <= 1e6;

// Refills `perSec` tokens each second; take() is false when empty.
export class Bucket {
  private tokens: number;
  private last = Date.now();
  private perSec: number;
  private cap: number;
  constructor(perSec: number, cap = perSec) { this.perSec = perSec; this.cap = cap; this.tokens = cap; }
  take(cost = 1): boolean {
    const now = Date.now();
    this.tokens = Math.min(this.cap, this.tokens + ((now - this.last) / 1000) * this.perSec);
    this.last = now;
    if (this.tokens < cost) return false;
    this.tokens -= cost;
    return true;
  }
}

// Everything the server remembers about one connection between its messages.
export class ConnState {
  lastPos: Pos | null = null; // saved to the player's state when the connection ends
  readonly pos = new Bucket(POS_PER_SEC);
  readonly msg = new Bucket(MSG_PER_SEC);
  readonly op = new Bucket(OP_PER_SEC);
  private stateCount = new Bucket(STATE_PER_SEC, STATE_BURST);
  private stateBytes = new Bucket(STATE_BYTES_PER_SEC, MAX_STATE);
  stateOk(n: number): boolean { return this.stateCount.take() && this.stateBytes.take(n); }
}

function parsePos(m: Msg): Pos | null {
  const { map, x, y, z, ry, sp } = m;
  const ok = str(map, 200) && num(x) && num(y) && num(z) && num(ry) && num(sp);
  return ok ? { map, x, y, z, ry, sp } : null;
}

function parse(text: string, size: number): Msg | null {
  if (size > MAX_STATE) return null;
  try {
    const m = JSON.parse(text);
    return m && typeof m === 'object' && !Array.isArray(m) && typeof m.t === 'string' ? m : null;
  } catch {
    return null;
  }
}

// One incoming frame (`text` is null for binary frames, `size` is its size in bytes).
// Returns true when the connection has to be closed with 1008 (more than 60 messages/s).
export function handleFrame(rooms: Rooms, peer: Peer, c: ConnState, text: string | null, size: number): boolean {
  if (!c.msg.take()) return true;
  const m = text === null ? null : parse(text, size);
  if (!m) { peer.send({ t: 'err', code: 'bad_request' }); return false; }
  if (m.t === 'pos') {
    const pos = c.pos.take() ? parsePos(m) : null; // over budget or malformed: dropped silently
    if (pos) { c.lastPos = pos; rooms.relayPos(peer, pos); }
    return false;
  }
  const err = dispatch(rooms, peer, m, size, c);
  if (err) peer.send({ t: 'err', code: err });
  return false;
}

function dispatch(rooms: Rooms, peer: Peer, m: Msg, size: number, c: ConnState): ErrCode | null {
  if (m.t !== 'state' && size > MAX_MSG) return 'bad_request';
  switch (m.t) {
    case 'create': return rooms.create(peer, String(m.room ?? '').trim(), m.max === undefined ? 4 : (m.max as number));
    case 'list': peer.send({ t: 'rooms', rooms: rooms.list() }); return null;
    case 'join': return str(m.rid, 32) ? rooms.join(peer, m.rid) : 'bad_request';
    case 'leave': return rooms.leave(peer);
    case 'transfer': return str(m.to, 32) ? rooms.transfer(peer, m.to) : 'bad_request';
    case 'state':
      if (!(str(m.snap, MAX_STATE) && m.snap !== '')) return 'bad_request';
      return c.stateOk(size) ? rooms.setSnap(peer, m.snap) : 'rate_limited';
    case 'world': return str(m.ev, 32) && m.ev !== '' && str(m.data, MAX_MSG) ? rooms.relayWorld(peer, m.ev, m.data) : 'bad_request';
    case 'op':
      if (!(str(m.ev, 32) && m.ev !== '' && str(m.data, MAX_MSG))) return 'bad_request';
      return c.op.take() ? rooms.relayOp(peer, m.ev, m.data) : 'rate_limited';
    case 'msg': return str(m.to, 32) && str(m.data, MAX_MSG) ? rooms.relayMsg(peer, m.to, m.data) : 'bad_request';
    default: return 'bad_request';
  }
}
