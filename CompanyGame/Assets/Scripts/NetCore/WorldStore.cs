using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// One server world file (Docs/Multiplayer_Redesign.md 12.5): <c>&lt;root&gt;/&lt;world id&gt;/world.dat</c> with the world clock and
/// every account's character (name, gender, character clock, the world clock at its last visit, and the content blob the
/// client made: bank, stocks, inventory, position). Binary, little-endian (no Unity JSON here). A save writes a temporary
/// file and swaps it in, keeping the previous file as world.prev.dat; a broken world.dat is read from that one instead.
/// A store made with a null folder lives in memory only (QA, servers without a world).
/// </summary>
public sealed class WorldStore
{
    public const ushort FileVersion = 1;
    public const string FileName = "world.dat", PrevName = "world.prev.dat";

    // Same rule as TitleFlow.IsValidName (12.5).
    static readonly Regex NamePattern = new Regex("^[가-힣A-Za-z0-9_]{2,12}$");

    public sealed class Character
    {
        public string account = "", name = "", gender = "male";
        /// <summary>Game minutes this character has been connected (the character clock).</summary>
        public double minutes;
        /// <summary>The world clock when it last left; the away settlement (capped at 7 game days, 9장-2) will use it.</summary>
        public double lastSeenWorldMinutes;
        public byte[] content = Array.Empty<byte>();
        /// <summary>Its account is connected right now (not saved; the server allows one connection per account).</summary>
        public bool online;
    }

    /// <summary>What the world list shows without opening the server.</summary>
    public struct Summary
    {
        public string id;
        public DateTime createdAt, savedAt; // UTC
        public int characters;
    }

    readonly Dictionary<string, Character> byAccount = new Dictionary<string, Character>();

    public string Id { get; private set; }
    /// <summary>The folder of this world (null = memory only).</summary>
    public string Folder { get; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    /// <summary>Game minutes the world has run (the world clock).</summary>
    public double WorldMinutes;
    /// <summary>True when world.dat was unreadable and world.prev.dat was used.</summary>
    public bool LoadedFromPrev { get; private set; }

    public int CharacterCount => byAccount.Count;
    public IEnumerable<Character> Characters => byAccount.Values;

    WorldStore(string id, string folder) { Id = id; Folder = folder; }

    /// <summary>A world that is never written (QA, a server started without one).</summary>
    public static WorldStore InMemory() => new WorldStore(NewId(), null);

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 32) return false;
        foreach (char c in id) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'z')) return false;
        return true;
    }

    public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);

    /// <summary>
    /// The world <paramref name="id"/> under <paramref name="root"/>: read from world.dat (world.prev.dat when that is broken),
    /// or a new world when neither exists. Throws when a file exists but neither can be read, so nothing overwrites it.
    /// </summary>
    public static WorldStore Open(string root, string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("bad world id: " + id);
        var store = new WorldStore(id, Path.Combine(root, id));
        string main = Path.Combine(store.Folder, FileName), prev = Path.Combine(store.Folder, PrevName);
        if (!File.Exists(main) && !File.Exists(prev)) return store;
        Exception first = null;
        try { store.Read(main); return store; }
        catch (Exception e) { first = e; }
        try { store.Read(prev); store.LoadedFromPrev = true; return store; }
        catch (Exception e) { throw new InvalidDataException("world " + id + " unreadable: " + first.Message + " / prev: " + e.Message); }
    }

    /// <summary>Every world under <paramref name="root"/> that can be read, newest save first.</summary>
    public static List<Summary> List(string root)
    {
        var list = new List<Summary>();
        if (!Directory.Exists(root)) return list;
        foreach (string dir in Directory.GetDirectories(root))
        {
            string id = Path.GetFileName(dir);
            if (!IsValidId(id)) continue;
            try
            {
                var store = Open(root, id);
                string file = Path.Combine(dir, store.LoadedFromPrev ? PrevName : FileName);
                if (!File.Exists(file)) continue; // an empty folder: never saved
                list.Add(new Summary { id = id, createdAt = store.CreatedAt, savedAt = File.GetLastWriteTimeUtc(file), characters = store.CharacterCount });
            }
            catch (Exception) { /* unreadable: not offered */ }
        }
        list.Sort((a, b) => b.savedAt.CompareTo(a.savedAt));
        return list;
    }

    public Character Find(string account) => account != null && byAccount.TryGetValue(account, out var c) ? c : null;

    /// <summary>Another account already has this name in this world (case does not matter).</summary>
    public bool NameTaken(string name)
    {
        foreach (var c in byAccount.Values) if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Makes <paramref name="account"/>'s character, or says why not.</summary>
    public CharacterResult Create(string account, string name, string gender, out Character made)
    {
        made = null;
        if (Find(account) != null) return CharacterResult.AlreadyExists;
        if (!IsValidName(name) || gender != "male" && gender != "female") return CharacterResult.Invalid;
        if (NameTaken(name)) return CharacterResult.NameTaken;
        made = new Character { account = account, name = name, gender = gender, lastSeenWorldMinutes = WorldMinutes };
        byAccount[account] = made;
        return CharacterResult.Ok;
    }

    // ---- file ----

    /// <summary>Writes world.tmp, then swaps it in as world.dat keeping the old one as world.prev.dat. Throws on IO errors.</summary>
    public void Save()
    {
        if (Folder == null) return;
        Directory.CreateDirectory(Folder);
        string main = Path.Combine(Folder, FileName), tmp = Path.Combine(Folder, "world.tmp");
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(stream, Encoding.UTF8))
        {
            Write(w);
            w.Flush();
            stream.Flush(true);
        }
        if (File.Exists(main)) File.Replace(tmp, main, Path.Combine(Folder, PrevName));
        else File.Move(tmp, main);
    }

    void Write(BinaryWriter w)
    {
        w.Write(FileVersion);
        SyncProtocol.WriteStr(w, Id);
        w.Write(CreatedAt.Ticks);
        w.Write(WorldMinutes);
        w.Write(byAccount.Count);
        foreach (var c in byAccount.Values)
        {
            SyncProtocol.WriteStr(w, c.account); SyncProtocol.WriteStr(w, c.name); SyncProtocol.WriteStr(w, c.gender);
            w.Write(c.minutes); w.Write(c.lastSeenWorldMinutes);
            SyncProtocol.WriteBlob32(w, c.content);
        }
    }

    // Reads into a fresh list and only then replaces this store's, so a half-read file changes nothing.
    void Read(string path)
    {
        using var r = SyncProtocol.Reader(File.ReadAllBytes(path));
        if (r.ReadUInt16() != FileVersion) throw new InvalidDataException("version");
        string id = SyncProtocol.ReadStr(r);
        if (id != Id) throw new InvalidDataException("world id " + id);
        long created = r.ReadInt64();
        double world = r.ReadDouble();
        int count = r.ReadInt32();
        if (count < 0 || created < 0 || created > DateTime.MaxValue.Ticks || double.IsNaN(world)) throw new InvalidDataException("header");
        var read = new Dictionary<string, Character>();
        for (int i = 0; i < count; i++)
        {
            var c = new Character
            {
                account = SyncProtocol.ReadStr(r), name = SyncProtocol.ReadStr(r), gender = SyncProtocol.ReadStr(r),
                minutes = r.ReadDouble(), lastSeenWorldMinutes = r.ReadDouble(), content = SyncProtocol.ReadBlob32(r),
            };
            read[c.account] = c;
        }
        SyncProtocol.End(r);
        CreatedAt = new DateTime(created, DateTimeKind.Utc);
        WorldMinutes = world;
        byAccount.Clear();
        foreach (var pair in read) byAccount[pair.Key] = pair.Value;
    }
}
