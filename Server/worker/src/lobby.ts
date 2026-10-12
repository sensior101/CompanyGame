// All rooms and all realtime WebSockets live in this one Durable Object, using the WebSocket Hibernation API:
// the object is evicted from memory while nobody talks and no duration is billed for idle connections.
//
// What survives hibernation (memory is wiped, sockets are not):
//  - per socket: its attachment {id, name, ip, rid, order} (= who it is and which room it is in)
//  - per room:   one SQLite row (name, host, max, latest snapshot) + a ring of the last 256 ops
// Per-connection rate-limit buckets and the last `pos` live in memory only and start fresh after a wake-up.
import { DurableObject } from 'cloudflare:workers';
import { ConnState, handleFrame, MAX_FRAME, utf8Len } from '../../src/protocol.ts';
import { MAX_OPS, Rooms, type Op, type Peer, type RoomRow, type RoomStore } from '../../src/rooms.ts';
import { BUDGET_SCHEMA, budgetsOf, WS_MESSAGE_COST, type Budgets } from './budget.ts';
import { accountsOf, limitsOf, type Env } from './env.ts';
import { verifyJwt } from './jwt.ts';

interface Att { id: string; name: string; ip: string; rid: string; order: number }
interface Conn { ws: WebSocket; att: Att; peer: Peer; state: ConnState }

// Rows written are the scarce free-plan resource (100k/day): WITHOUT ROWID = one row per write, and the op ring
// overwrites its slot (seq % 256) instead of insert + delete.
const SCHEMA = [
  BUDGET_SCHEMA,
  `CREATE TABLE IF NOT EXISTS rooms (
    rid TEXT PRIMARY KEY, name TEXT NOT NULL, host TEXT NOT NULL, max_players INTEGER NOT NULL, snap TEXT NOT NULL DEFAULT ''
  ) WITHOUT ROWID`,
  `CREATE TABLE IF NOT EXISTS ops (
    rid TEXT NOT NULL, slot INTEGER NOT NULL, seq INTEGER NOT NULL, from_id TEXT NOT NULL, ev TEXT NOT NULL, data TEXT NOT NULL,
    PRIMARY KEY (rid, slot)
  ) WITHOUT ROWID`,
];

class SqlStore implements RoomStore {
  private sql: SqlStorage;
  constructor(sql: SqlStorage) { this.sql = sql; }
  save(r: RoomRow): void {
    this.sql.exec(
      `INSERT INTO rooms (rid, name, host, max_players, snap) VALUES (?, ?, ?, ?, ?)
       ON CONFLICT(rid) DO UPDATE SET host = excluded.host, snap = excluded.snap`,
      r.rid, r.name, r.host, r.max, r.snap,
    );
  }
  remove(rid: string): void {
    this.sql.exec('DELETE FROM ops WHERE rid = ?', rid);
    this.sql.exec('DELETE FROM rooms WHERE rid = ?', rid);
  }
  addOp(rid: string, op: Op): void {
    this.sql.exec(
      `INSERT INTO ops (rid, slot, seq, from_id, ev, data) VALUES (?, ?, ?, ?, ?, ?)
       ON CONFLICT(rid, slot) DO UPDATE SET seq = excluded.seq, from_id = excluded.from_id, ev = excluded.ev, data = excluded.data`,
      rid, op.seq % MAX_OPS, op.seq, op.from, op.ev, op.data,
    );
  }
}

export class Lobby extends DurableObject<Env> {
  private sql: SqlStorage;
  private store: SqlStore;
  private rooms: Rooms;
  private conns = new Map<WebSocket, Conn>();
  private nextOrder = 1; // join order inside a room; host migration picks the earliest joiner
  private wsPerIp: number;
  private budget: Budgets;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    for (const q of SCHEMA) this.sql.exec(q);
    this.store = new SqlStore(this.sql);
    this.rooms = new Rooms({ store: this.store });
    this.wsPerIp = limitsOf(env).wsPerIp;
    this.budget = budgetsOf(this.sql, env, 'lobby');
    // clients may send the text "ping": answered with "pong" without waking the object (free of charge)
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping', 'pong'));
    this.restore();
  }

  // Called by the Worker for GET /rooms (the token was already checked there).
  listRooms() {
    const spent = this.spend();
    return spent ? { error: 'daily_budget' as const } : { rooms: this.rooms.list() };
  }

  // One HTTP request + one Durable Object request arrive here (an upgrade or GET /rooms). true = budget spent.
  private spend(): boolean {
    const over = this.budget.dos.over() || this.budget.http.over();
    this.budget.dos.add();
    this.budget.http.add();
    return over;
  }

  async fetch(req: Request): Promise<Response> {
    if (req.headers.get('Upgrade') !== 'websocket') return new Response('expected a websocket', { status: 426 });
    const [client, server] = Object.values(new WebSocketPair());
    if (this.spend()) return this.refuse(client, server, 4503, 'daily_budget'); // client: no reconnects until the next UTC day
    const ip = req.headers.get('CF-Connecting-IP') ?? 'unknown';
    const who = await this.authenticate(new URL(req.url).searchParams.get('token'));
    if (!who) return this.refuse(client, server, 4401, 'unauthorized');
    if (this.count((c) => c.att.ip === ip) >= this.wsPerIp) return this.refuse(client, server, 4429, 'too_many_connections');

    // the same account connecting again replaces the old connection (its room is left right now, 4409)
    const old = [...this.conns.values()].find((c) => c.att.id === String(who.id));
    if (old) { this.conns.delete(old.ws); this.rooms.leave(old.peer); old.ws.close(4409, 'replaced'); }

    this.ctx.acceptWebSocket(server);
    const att: Att = { id: String(who.id), name: who.name, ip, rid: '', order: 0 };
    server.serializeAttachment(att);
    this.track(server, att).peer.send({ t: 'hello', id: att.id, name: att.name });
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    const c = this.conns.get(ws);
    if (!c) return; // replaced or refused: ignore whatever the old socket still sends
    this.budget.dos.add(WS_MESSAGE_COST);
    if (this.budget.dos.hard()) return ws.close(4503, 'daily_budget'); // hard stop: running sessions end too
    const text = typeof message === 'string' ? message : null;
    const size = text === null ? (message as ArrayBuffer).byteLength : utf8Len(text);
    if (size > MAX_FRAME) return ws.close(1009, 'message_too_big');
    if (handleFrame(this.rooms, c.peer, c.state, text, size)) ws.close(1008, 'rate_limit');
    this.syncRoom(c);
  }

  async webSocketClose(ws: WebSocket, code: number): Promise<void> {
    // answer the peer's close frame ourselves: without this the local runtime keeps the socket open for ~10 s
    try { ws.close(code === 1005 || code === 1006 ? 1000 : code, 'closing'); } catch { /* we closed it first */ }
    await this.drop(ws);
  }
  async webSocketError(ws: WebSocket): Promise<void> { await this.drop(ws); }

  private async drop(ws: WebSocket): Promise<void> {
    const c = this.conns.get(ws);
    if (!c) return; // replaced connections were removed already; the new one owns the state
    this.conns.delete(ws);
    this.rooms.leave(c.peer);
    this.budget.dos.flush();
    this.budget.http.flush(); // a connection ended: persist the counts
    if (c.state.lastPos) await accountsOf(this.env).savePos(Number(c.att.id), c.state.lastPos);
  }

  private async authenticate(token: string | null): Promise<{ id: number; name: string } | null> {
    const claims = token && this.env.JWT_SECRET ? await verifyJwt(token, this.env.JWT_SECRET) : null;
    const name = claims ? await accountsOf(this.env).playerName(claims.id) : null;
    return claims && name !== null ? { id: claims.id, name } : null;
  }

  private count(pred: (c: Conn) => boolean): number {
    let n = 0;
    for (const c of this.conns.values()) if (pred(c)) n++;
    return n;
  }

  private track(ws: WebSocket, att: Att): Conn {
    const send = (m: object) => { try { ws.send(JSON.stringify(m)); } catch { /* socket is closing */ } };
    const c: Conn = { ws, att, peer: { id: att.id, name: att.name, send }, state: new ConnState() };
    this.conns.set(ws, c);
    return c;
  }

  // Room membership changed by this connection's own message -> keep it in the attachment (survives hibernation).
  private syncRoom(c: Conn): void {
    const rid = this.rooms.ridOf(c.att.id) ?? '';
    if (rid === c.att.rid) return;
    c.att.rid = rid;
    c.att.order = rid ? this.nextOrder++ : 0;
    c.ws.serializeAttachment(c.att);
  }

  // Clients only see a clean close code if the upgrade succeeds first, so refuse by accepting and closing.
  private refuse(client: WebSocket, server: WebSocket, code: number, reason: string): Response {
    this.ctx.acceptWebSocket(server);
    server.close(code, reason);
    return new Response(null, { status: 101, webSocket: client });
  }

  // Constructor = (re)start of the object: rebuild connections from attachments and rooms from SQLite.
  private restore(): void {
    const byRid = new Map<string, Conn[]>();
    for (const ws of this.ctx.getWebSockets()) {
      const att = ws.deserializeAttachment() as Att | null;
      if (!att) continue; // never finished the handshake logic
      const c = this.track(ws, att);
      this.nextOrder = Math.max(this.nextOrder, att.order + 1);
      if (att.rid) byRid.set(att.rid, [...(byRid.get(att.rid) ?? []), c]);
    }
    const rows = this.sql.exec<{ rid: string; name: string; host: string; max_players: number; snap: string }>(
      'SELECT rid, name, host, max_players, snap FROM rooms',
    ).toArray();
    for (const { max_players, ...row } of rows) {
      const members = (byRid.get(row.rid) ?? []).sort((a, b) => a.att.order - b.att.order);
      byRid.delete(row.rid);
      // no connected member left (e.g. the object restarted and every socket dropped): the room is gone
      if (!members.length) { this.store.remove(row.rid); continue; }
      const ops = this.sql.exec('SELECT seq, from_id AS "from", ev, data FROM ops WHERE rid = ? ORDER BY seq', row.rid).toArray() as unknown as Op[];
      this.rooms.restore({ ...row, max: max_players }, ops, members.map((m) => m.peer));
    }
    for (const lost of byRid.values()) for (const c of lost) { c.att.rid = ''; c.ws.serializeAttachment(c.att); }
  }
}
