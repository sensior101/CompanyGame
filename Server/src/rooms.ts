// Pure room logic, no transport. Members are kept in join order so host migration is "earliest joiner".
export interface Peer {
  id: string;
  name: string;
  send(m: object): void;
}

export type ErrCode =
  | 'bad_request' | 'not_in_room' | 'already_in_room' | 'room_not_found'
  | 'room_full' | 'not_host' | 'too_many_rooms' | 'peer_not_found' | 'rate_limited';

export interface Pos { map: string; x: number; y: number; z: number; ry: number; sp: number }

export const MAX_ROOMS = 50;
export const MAX_PLAYERS = 8;
export const MAX_OPS = 256;

export interface Op { seq: number; from: string; ev: string; data: string }

// Room codes: 6 chars, no 0/O/1/I. 32 symbols, so `byte & 31` is unbiased.
export const ROOM_CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
export const ROOM_CODE_LEN = 6;
export function newRoomCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(ROOM_CODE_LEN));
  return Array.from(bytes, (b) => ROOM_CODE_ALPHABET[b & 31]).join('');
}
const CODE_SHAPE = /^[A-Za-z0-9]{6}$/;

// The part of a room that must survive the transport losing its memory (Cloudflare DO wake-up). Members are
// NOT stored: the transport knows which sockets are still connected and passes them to restore().
export interface RoomRow { rid: string; name: string; host: string; max: number; snap: string }
export interface RoomStore {
  save(room: RoomRow): void; // create, host change, new snapshot
  remove(rid: string): void; // room became empty (also drops its ops)
  addOp(rid: string, op: Op): void;
}
export interface RoomsOpts { store?: RoomStore; newCode?: () => string }

interface Room {
  rid: string;
  name: string;
  host: string;
  max: number;
  members: Map<string, Peer>;
  snap: string; // latest host snapshot, memory only
  seq: number;
  ops: Op[]; // last MAX_OPS ops
}

export class Rooms {
  private rooms = new Map<string, Room>();
  private byPeer = new Map<string, Room>();
  private store?: RoomStore;
  private newCode: () => string;

  constructor(opts: RoomsOpts = {}) {
    this.store = opts.store;
    this.newCode = opts.newCode ?? newRoomCode;
  }

  get roomCount(): number { return this.rooms.size; }

  create(peer: Peer, name: string, max: number): ErrCode | null {
    if (this.byPeer.has(peer.id)) return 'already_in_room';
    // `<...>` would be rendered as rich text by the TMP-based client UI
    if (!name || name.length > 30 || /[<>]/.test(name) || !Number.isInteger(max) || max < 1 || max > MAX_PLAYERS) return 'bad_request';
    if (this.rooms.size >= MAX_ROOMS) return 'too_many_rooms';
    const rid = this.freeCode();
    if (!rid) return 'too_many_rooms';
    const room: Room = { rid, name, host: peer.id, max, members: new Map(), snap: '', seq: 0, ops: [] };
    this.rooms.set(rid, room);
    this.store?.save(rowOf(room));
    this.enter(room, peer);
    return null;
  }

  // Codes are case-insensitive: "ab3k9z" joins room "AB3K9Z".
  join(peer: Peer, rid: string): ErrCode | null {
    if (this.byPeer.has(peer.id)) return 'already_in_room';
    const code = rid.trim();
    const room = CODE_SHAPE.test(code) ? this.rooms.get(code.toUpperCase()) : undefined;
    if (!room) return 'room_not_found';
    if (room.members.size >= room.max) return 'room_full';
    this.enter(room, peer);
    return null;
  }

  // Identity check: a replaced (stale) connection must not evict the new one with the same id.
  leave(peer: Peer): ErrCode | null {
    const room = this.byPeer.get(peer.id);
    if (!room || room.members.get(peer.id) !== peer) return 'not_in_room';
    room.members.delete(peer.id);
    this.byPeer.delete(peer.id);
    if (room.members.size === 0) { this.rooms.delete(room.rid); this.store?.remove(room.rid); return null; }
    this.bcast(room, { t: 'peer', ev: 'leave', id: peer.id, name: peer.name });
    if (room.host === peer.id) this.setHost(room, room.members.keys().next().value!, 'disconnect');
    return null;
  }

  transfer(peer: Peer, to: string): ErrCode | null {
    const room = this.byPeer.get(peer.id);
    if (!room) return 'not_in_room';
    if (room.host !== peer.id) return 'not_host';
    if (to === peer.id || !room.members.has(to)) return 'peer_not_found';
    this.setHost(room, to, 'transfer');
    return null;
  }

  setSnap(peer: Peer, snap: string): ErrCode | null {
    const room = this.hostRoom(peer);
    if (typeof room === 'string') return room;
    room.snap = snap;
    this.store?.save(rowOf(room));
    this.bcast(room, { t: 'state', snap }, peer.id);
    return null;
  }

  relayPos(peer: Peer, pos: Pos): void {
    const room = this.byPeer.get(peer.id);
    if (room) this.bcast(room, { t: 'pos', from: peer.id, ...pos }, peer.id);
  }

  // Host-only relay: world (not stored).
  relayWorld(peer: Peer, ev: string, data: string): ErrCode | null {
    const room = this.hostRoom(peer);
    if (typeof room === 'string') return room;
    this.bcast(room, { t: 'world', ev, data }, peer.id);
    return null;
  }

  // Host may send to anyone or everyone; other members may only send to the current host.
  relayMsg(peer: Peer, to: string, data: string): ErrCode | null {
    const room = this.byPeer.get(peer.id);
    if (!room) return 'not_in_room';
    if (room.host !== peer.id && to !== room.host) return 'not_host';
    const m = { t: 'msg', from: peer.id, data };
    if (to === '') this.bcast(room, m, peer.id);
    else if (to !== peer.id && room.members.has(to)) room.members.get(to)!.send(m);
    else return 'peer_not_found';
    return null;
  }

  // Any member may send; the server orders ops and echoes them to everyone including the sender.
  relayOp(peer: Peer, ev: string, data: string): ErrCode | null {
    const room = this.byPeer.get(peer.id);
    if (!room) return 'not_in_room';
    const op: Op = { seq: ++room.seq, from: peer.id, ev, data };
    room.ops.push(op);
    if (room.ops.length > MAX_OPS) room.ops.shift();
    this.store?.addOp(room.rid, op);
    this.bcast(room, { t: 'op', ...op });
    return null;
  }

  ridOf(peerId: string): string | undefined { return this.byPeer.get(peerId)?.rid; }

  // Rebuild a room from storage; `members` are the still-connected peers in join order (never empty).
  restore(row: RoomRow, ops: Op[], members: Peer[]): void {
    const host = members.some((m) => m.id === row.host) ? row.host : members[0].id;
    const room: Room = {
      ...row, host, members: new Map(members.map((m) => [m.id, m])), seq: ops.length ? ops[ops.length - 1].seq : 0, ops: ops.slice(-MAX_OPS),
    };
    this.rooms.set(room.rid, room);
    for (const m of members) this.byPeer.set(m.id, room);
    if (host !== row.host) this.store?.save(rowOf(room));
  }

  list() {
    return [...this.rooms.values()].map((r) => ({
      rid: r.rid, room: r.name, host: r.host, hostName: r.members.get(r.host)!.name, count: r.members.size, max: r.max,
    }));
  }

  private freeCode(): string | null {
    for (let i = 0; i < 20; i++) {
      const code = this.newCode();
      if (!this.rooms.has(code)) return code;
    }
    return null; // 32^6 codes, 50 rooms: only a broken generator gets here
  }

  private hostRoom(peer: Peer): Room | ErrCode {
    const room = this.byPeer.get(peer.id);
    if (!room) return 'not_in_room';
    return room.host === peer.id ? room : 'not_host';
  }

  private enter(room: Room, peer: Peer): void {
    room.members.set(peer.id, peer);
    this.byPeer.set(peer.id, room);
    const players = [...room.members.values()].map((p) => ({ id: p.id, name: p.name }));
    peer.send({ t: 'joined', rid: room.rid, host: room.host, players, snap: room.snap, ops: [...room.ops] });
    this.bcast(room, { t: 'peer', ev: 'join', id: peer.id, name: peer.name }, peer.id);
  }

  private setHost(room: Room, id: string, reason: 'transfer' | 'disconnect'): void {
    room.host = id;
    this.store?.save(rowOf(room));
    this.bcast(room, { t: 'host', host: id, reason, snap: room.snap });
  }

  private bcast(room: Room, m: object, except?: string): void {
    for (const p of room.members.values()) if (p.id !== except) p.send(m);
  }
}

const rowOf = (r: Room): RoomRow => ({ rid: r.rid, name: r.name, host: r.host, max: r.max, snap: r.snap });
