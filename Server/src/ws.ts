import type { DatabaseSync } from 'node:sqlite';
import type { FastifyInstance } from 'fastify';
import { saveState } from './db.ts';
import { ConnState, handleFrame } from './protocol.ts';
import type { Peer, Rooms } from './rooms.ts';

const PING_MS = 15_000;
export interface WsLimits { wsPerIp: number }

export function registerWs(app: FastifyInstance, rooms: Rooms, db: DatabaseSync, lim: WsLimits): void {
  const live = new Map<string, { peer: Peer; close(code: number): void }>();
  const perIp = new Map<string, number>();

  app.register(async (f) => {
    f.get('/ws', { websocket: true }, (socket, req) => {
      const token = (req.query as { token?: unknown }).token;
      const who = authenticate(app, db, token);
      if (!who) return socket.close(4401, 'unauthorized');

      const ip = req.ip;
      if ((perIp.get(ip) ?? 0) >= lim.wsPerIp) return socket.close(4429, 'too_many_connections');
      perIp.set(ip, (perIp.get(ip) ?? 0) + 1);

      const peer: Peer = {
        id: String(who.id), name: who.name,
        send: (m) => { if (socket.readyState === 1) socket.send(JSON.stringify(m)); },
      };
      // same account connecting again replaces the old connection: the old one leaves its room right
      // now (peers see `leave`, host migrates) so the new socket starts clean and may create/join at once
      const old = live.get(peer.id);
      if (old) { rooms.leave(old.peer); old.close(4409); }
      const conn = { peer, close: (c: number) => socket.close(c, 'replaced') };
      live.set(peer.id, conn);

      const state = new ConnState();
      let alive = true;
      const timer = setInterval(() => {
        if (!alive) return socket.terminate();
        alive = false;
        socket.ping();
      }, PING_MS);
      timer.unref();
      socket.on('pong', () => { alive = true; });

      socket.on('message', (data, isBinary) => {
        if (live.get(peer.id) !== conn) return; // replaced: ignore whatever the old socket still sends
        const size = rawBytes(data);
        if (handleFrame(rooms, peer, state, isBinary ? null : data.toString(), size)) socket.close(1008, 'rate_limit');
      });

      socket.on('close', () => {
        clearInterval(timer);
        perIp.set(ip, (perIp.get(ip) ?? 1) - 1);
        if (perIp.get(ip) === 0) perIp.delete(ip);
        rooms.leave(peer);
        if (live.get(peer.id) !== conn) return; // replaced, the new connection owns the state
        live.delete(peer.id);
        if (state.lastPos) saveState(db, who.id, state.lastPos, null, null);
      });

      peer.send({ t: 'hello', id: peer.id, name: peer.name });
    });
  });
}

function authenticate(app: FastifyInstance, db: DatabaseSync, token: unknown): { id: number; name: string } | null {
  if (typeof token !== 'string' || token.length > 2048) return null;
  try {
    const { id } = app.jwt.verify<{ id: number }>(token);
    const row = db.prepare('SELECT nickname FROM players WHERE id = ?').get(id) as { nickname: string | null } | undefined;
    return row ? { id, name: row.nickname ?? `player${id}` } : null;
  } catch {
    return null;
  }
}

const rawBytes = (d: unknown): number =>
  Buffer.isBuffer(d) ? d.length : Array.isArray(d) ? Buffer.concat(d).length : (d as ArrayBuffer).byteLength;
