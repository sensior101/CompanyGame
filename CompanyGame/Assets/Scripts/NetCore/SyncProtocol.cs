using System;
using System.IO;
using System.Text;

/// <summary>Client → server message kinds (first byte of a frame).</summary>
public enum ToServer : byte { Hello = 1, Spawn = 2, State = 3, Despawn = 4, Event = 5, Map = 6, Claim = 7, CharacterCreate = 8, CharacterSave = 9, Release = 10, Ping = 11 }

/// <summary>Server → client message kinds (first byte of a frame).</summary>
public enum ToClient : byte { Welcome = 101, Reject = 102, Spawn = 103, State = 104, Despawn = 105, Event = 106, Owner = 107, Peer = 108, Character = 109, CharacterResult = 110, Pong = 111 }

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
    /// <summary>Cleared to the lost-and-found list at the first 06:00 of the world clock after it was made, while server-owned (a dropped item, 12.8).</summary>
    Expiring = 8,
}

/// <summary>Who receives an entity event: its owner, everyone on its map (sender included), or the server's ServerEvent.</summary>
public enum EventTarget : byte { Owner = 0, Map = 1, Server = 2 }

/// <summary>Why a connection is closed. Kicked and Banned (12.15) come from an admin or the server's ban list.</summary>
public enum RejectReason : byte { Version = 1, Full = 2, Malformed = 3, Rate = 4, Password = 5, AlreadyConnected = 6, Identity = 7, Kicked = 8, Banned = 9 }

/// <summary>Answer to CharacterCreate (12.5).</summary>
public enum CharacterResult : byte { Ok = 0, NameTaken = 1, Invalid = 2, AlreadyExists = 3 }

/// <summary>
/// Binary sync protocol (Docs/Multiplayer_Redesign.md 12.1; version 2 adds the Hello password and the Welcome server name, 12.3;
/// version 3 the character messages, 12.5; version 4 Release and the Expiring flag, 12.8; version 5 server-made entities on map
/// <see cref="AllMaps"/> with blobs up to <see cref="MaxServerBlob"/>, 12.9; version 6 the EOS ID token at the end of Hello and
/// Ping/Pong, 12.13; version 7 the exact minutes and seed in the world's clock section, 12.14; version 8 the car count in it and Reject Kicked/Banned, 12.15). Little-endian; str = u16 byte length + UTF-8,
/// blob = u16 length + bytes, blob32 = u32 length + bytes (only the character content, up to <see cref="MaxCharacter"/>).
/// A client's blobs stay at <see cref="MaxBlob"/>; what the server sends may be up to <see cref="MaxServerBlob"/> (ReadServerBlob).
/// Entity id = (connection number u32 &lt;&lt; 32) | serial u32, so a client makes ids without a round trip.
/// The builders below write the client → server messages (SyncClient and the QA fake clients share them).
/// </summary>
public static class SyncProtocol
{
    public const ushort Version = 9; // 9: player chat (Op "chat", world event 6)
    // The frame limit is 64KB for the 48KB character content; entity blobs stay at 4KB.
    public const int MaxFrame = 64 * 1024, MaxBlob = 4 * 1024, MaxCharacter = 48 * 1024, MaxFramesPerSecond = 300, MaxEntitiesPerConnection = 256;
    /// <summary>A blob the server made (a world section, 12.9). Still one u16 length, and one State of it stays under <see cref="MaxFrame"/>.</summary>
    public const int MaxServerBlob = 60 * 1024;
    /// <summary>The map of entities every connection gets, whatever its map (the world entity, 12.9). Only the server makes them.</summary>
    public const string AllMaps = "*";

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

    /// <summary>Server side: a blob of any entity, up to <see cref="MaxServerBlob"/>.</summary>
    public static void WriteServerBlob(BinaryWriter w, byte[] blob)
    {
        blob ??= Array.Empty<byte>();
        if (blob.Length > MaxServerBlob) throw new InvalidDataException("blob over " + MaxServerBlob + " bytes");
        w.Write((ushort)blob.Length);
        w.Write(blob);
    }

    /// <summary>Client side: a blob from the server (up to <see cref="MaxServerBlob"/>).</summary>
    public static byte[] ReadServerBlob(BinaryReader r)
    {
        int length = r.ReadUInt16();
        if (length > MaxServerBlob) throw new InvalidDataException("blob over " + MaxServerBlob + " bytes");
        return ReadExactly(r, length);
    }

    public static void WriteBlob32(BinaryWriter w, byte[] blob)
    {
        blob ??= Array.Empty<byte>();
        if (blob.Length > MaxCharacter) throw new InvalidDataException("character over " + MaxCharacter + " bytes");
        w.Write((uint)blob.Length);
        w.Write(blob);
    }

    public static byte[] ReadBlob32(BinaryReader r)
    {
        uint length = r.ReadUInt32();
        if (length > MaxCharacter) throw new InvalidDataException("character over " + MaxCharacter + " bytes");
        return ReadExactly(r, (int)length);
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

    /// <summary><paramref name="token"/>: this player's EOS ID token ("" without EOS); the server checks it against the account (12.13).</summary>
    public static byte[] Hello(string account, string name, string map, string password = "", ushort version = Version, string token = "") =>
        Build((byte)ToServer.Hello, w => { w.Write(version); WriteStr(w, account); WriteStr(w, name); WriteStr(w, map); WriteStr(w, password); WriteStr(w, token); });

    /// <summary>Round-trip probe (12.13): the server answers Pong with the same stamp.</summary>
    public static byte[] Ping(uint stamp) => Build((byte)ToServer.Ping, w => w.Write(stamp));

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

    /// <summary>Gives a PersistOnLeave + Claimable entity I own back to the server (the opposite of Claim).</summary>
    public static byte[] Release(ulong id) => Build((byte)ToServer.Release, w => w.Write(id));

    public static byte[] CharacterCreate(string name, string gender) =>
        Build((byte)ToServer.CharacterCreate, w => { WriteStr(w, name); WriteStr(w, gender); });

    public static byte[] CharacterSave(byte[] content) => Build((byte)ToServer.CharacterSave, w => WriteBlob32(w, content));
}
