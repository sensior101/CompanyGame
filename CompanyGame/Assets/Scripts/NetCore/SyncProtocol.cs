using System;
using System.IO;
using System.Text;

/// <summary>Client → server message kinds (first byte of a frame).</summary>
public enum ToServer : byte { Hello = 1, Spawn = 2, State = 3, Despawn = 4, Event = 5, Map = 6, Claim = 7 }

/// <summary>Server → client message kinds (first byte of a frame).</summary>
public enum ToClient : byte { Welcome = 101, Reject = 102, Spawn = 103, State = 104, Despawn = 105, Event = 106, Owner = 107, Peer = 108 }

[Flags]
public enum EntityFlags : byte
{
    None = 0,
    /// <summary>When the owner leaves the entity stays, owned by the server (a placed car).</summary>
    PersistOnLeave = 1,
    /// <summary>While server-owned, the first Claim takes it (getting into an empty car).</summary>
    Claimable = 2,
    /// <summary>Moves to the owner's map whenever the owner changes map (the player avatar).</summary>
    FollowsOwnerMap = 4,
}

/// <summary>Who receives an entity event: its owner, everyone on its map (sender included), or the server's ServerEvent.</summary>
public enum EventTarget : byte { Owner = 0, Map = 1, Server = 2 }

public enum RejectReason : byte { Version = 1, Full = 2, Malformed = 3, Rate = 4, Password = 5 }

/// <summary>
/// Binary sync protocol (Docs/Multiplayer_Redesign.md 12.1; version 2 adds the Hello password and the Welcome server name, 12.3). Little-endian; str = u16 byte length + UTF-8, blob = u16 length + bytes.
/// Entity id = (connection number u32 &lt;&lt; 32) | serial u32, so a client makes ids without a round trip.
/// The builders below write the client → server messages (SyncClient and the QA fake clients share them).
/// </summary>
public static class SyncProtocol
{
    public const ushort Version = 2;
    public const int MaxFrame = 16 * 1024, MaxBlob = 4 * 1024, MaxFramesPerSecond = 300, MaxEntitiesPerConnection = 256;

    public static ulong MakeId(uint connection, uint serial) => (ulong)connection << 32 | serial;
    public static uint ConnectionOf(ulong id) => (uint)(id >> 32);

    public static void WriteStr(BinaryWriter w, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
        if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("string too long");
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    public static string ReadStr(BinaryReader r) => Encoding.UTF8.GetString(ReadExactly(r, r.ReadUInt16()));

    public static void WriteBlob(BinaryWriter w, byte[] blob)
    {
        blob ??= Array.Empty<byte>();
        if (blob.Length > MaxBlob) throw new InvalidDataException("blob over " + MaxBlob + " bytes");
        w.Write((ushort)blob.Length);
        w.Write(blob);
    }

    public static byte[] ReadBlob(BinaryReader r)
    {
        int length = r.ReadUInt16();
        if (length > MaxBlob) throw new InvalidDataException("blob over " + MaxBlob + " bytes");
        return ReadExactly(r, length);
    }

    static byte[] ReadExactly(BinaryReader r, int count)
    {
        byte[] bytes = r.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return bytes;
    }

    /// <summary>Throws when the frame has bytes after the message (a frame is exactly one message).</summary>
    public static void End(BinaryReader r)
    {
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("trailing bytes");
    }

    public static BinaryReader Reader(byte[] frame) => new BinaryReader(new MemoryStream(frame, false), Encoding.UTF8);

    public static byte[] Build(byte kind, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        w.Write(kind);
        body?.Invoke(w);
        w.Flush();
        return stream.ToArray();
    }

    public static byte[] Hello(string account, string name, string map, string password = "", ushort version = Version) =>
        Build((byte)ToServer.Hello, w => { w.Write(version); WriteStr(w, account); WriteStr(w, name); WriteStr(w, map); WriteStr(w, password); });

    public static byte[] Spawn(ulong id, string prefabKey, string map, EntityFlags flags, byte[][] blobs) =>
        Build((byte)ToServer.Spawn, w =>
        {
            if (blobs.Length > byte.MaxValue) throw new InvalidDataException("too many components");
            w.Write(id); WriteStr(w, prefabKey); WriteStr(w, map); w.Write((byte)flags);
            w.Write((byte)blobs.Length);
            foreach (byte[] blob in blobs) WriteBlob(w, blob);
        });

    public static byte[] State(ulong id, byte component, byte[] blob) =>
        Build((byte)ToServer.State, w => { w.Write(id); w.Write(component); WriteBlob(w, blob); });

    public static byte[] Despawn(ulong id) => Build((byte)ToServer.Despawn, w => w.Write(id));

    public static byte[] Event(ulong id, byte component, byte eventId, EventTarget target, byte[] blob) =>
        Build((byte)ToServer.Event, w => { w.Write(id); w.Write(component); w.Write(eventId); w.Write((byte)target); WriteBlob(w, blob); });

    public static byte[] Map(string map) => Build((byte)ToServer.Map, w => WriteStr(w, map));

    public static byte[] Claim(ulong id) => Build((byte)ToServer.Claim, w => w.Write(id));
}
