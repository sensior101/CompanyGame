using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// The state of a dropped item or a placed vehicle entity (Net/WorldDrop, Docs/Multiplayer_Redesign.md 12.8): u8 kind,
/// f32 x y z, f32 yaw, blob contents = gzip(InventorySnapshot.Export of the drop's inventory, no book pages). Here so that
/// the server (Player: ServerDrops) and the client (UI: DropState) read and write the same bytes (12.10.5).
/// </summary>
public static class DropBlob
{
    public const string PrefabKey = "Net/WorldDrop";
    public const byte ItemKind = 0, VehicleKind = 1;
    /// <summary>Bytes before the contents: kind and the place.</summary>
    public const int PlaceBytes = 17;
    /// <summary>An entity blob's limit (SyncProtocol.MaxBlob).</summary>
    public const int MaxBlob = 4 * 1024;

    public static byte[] Pack(InventoryState inventory)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            byte[] json = Encoding.UTF8.GetBytes(InventorySnapshot.Export(inventory, false)); // a book without its pages (12.10.3)
            gzip.Write(json, 0, json.Length);
        }
        return stream.ToArray();
    }

    /// <summary>The snapshot JSON of packed contents, or null when unreadable.</summary>
    public static string UnpackJson(byte[] contents)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(contents ?? Array.Empty<byte>(), false), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception e) { Debug.LogWarning("[DropBlob] unreadable contents: " + e.Message); return null; }
    }

    /// <summary>The contents into <paramref name="inventory"/> (replaced); false when unreadable.</summary>
    public static bool Unpack(byte[] contents, InventoryState inventory)
    {
        string json = UnpackJson(contents);
        return json != null && InventorySnapshot.Import(inventory, json);
    }

    public static void Write(BinaryWriter w, byte kind, Vector3 position, float yaw, byte[] contents)
    {
        w.Write(kind);
        w.Write(position.x); w.Write(position.y); w.Write(position.z);
        w.Write(yaw);
        contents ??= Array.Empty<byte>();
        if (contents.Length > MaxBlob) throw new InvalidDataException("drop contents over " + MaxBlob + " bytes");
        w.Write((ushort)contents.Length); // SyncProtocol's blob (NetCore is not referenced here)
        w.Write(contents);
    }

    public static byte[] Encode(byte kind, Vector3 position, float yaw, byte[] contents)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        Write(w, kind, position, yaw, contents);
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>Reads a whole state; false when it is not one.</summary>
    public static bool TryDecode(byte[] blob, out byte kind, out Vector3 position, out float yaw, out byte[] contents)
    {
        kind = 0; position = default; yaw = 0; contents = null;
        try
        {
            using var r = new BinaryReader(new MemoryStream(blob ?? Array.Empty<byte>(), false), Encoding.UTF8);
            kind = r.ReadByte();
            position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            yaw = r.ReadSingle();
            int length = r.ReadUInt16();
            contents = r.ReadBytes(length);
            return length <= MaxBlob && contents.Length == length && r.BaseStream.Position == r.BaseStream.Length;
        }
        catch (Exception) { return false; }
    }

    /// <summary>A place in a request (p.drop, p.vehicle.place): "x,y,z,yaw".</summary>
    public static string FormatPlace(Vector3 position, float yaw) =>
        string.Join(",", Array.ConvertAll(new[] { position.x, position.y, position.z, yaw }, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The place of <see cref="FormatPlace"/>; false unless four finite numbers within a million.</summary>
    public static bool TryParsePlace(string text, out Vector3 position, out float yaw)
    {
        position = default; yaw = 0;
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 4) return false;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])
                || float.IsNaN(v[i]) || float.IsInfinity(v[i]) || Math.Abs(v[i]) > 1e6f) return false;
        position = new Vector3(v[0], v[1], v[2]); yaw = v[3];
        return true;
    }

    /// <summary>
    /// <paramref name="kept"/> with only the place (x y z yaw) of <paramref name="incoming"/>: what a rider may change of a
    /// car (12.10.5). Null when <paramref name="incoming"/> has no readable, finite place.
    /// </summary>
    public static byte[] WithPlaceOf(byte[] kept, byte[] incoming)
    {
        if (kept == null || kept.Length < PlaceBytes || incoming == null || incoming.Length < PlaceBytes) return null;
        for (int at = 1; at < PlaceBytes; at += 4)
        {
            float v = BitConverter.ToSingle(incoming, at); // little-endian, as BinaryWriter writes it on every platform we build
            if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 1e6f) return null;
        }
        var blob = (byte[])kept.Clone();
        Buffer.BlockCopy(incoming, 1, blob, 1, PlaceBytes - 1);
        return blob;
    }
}
