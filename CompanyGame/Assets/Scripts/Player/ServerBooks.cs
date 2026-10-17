using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The book bodies of a sync-layer server's world (Docs/Multiplayer_Redesign.md 12.10.3): book instance id → the whole
/// book, saved in the world state of world.dat (WorldSaveData.books, never sent as a section). Characters, drops and
/// requests carry only a book's id, title, author, permission and library ids. Made and ended by ServerWorld.
///   older saves  a book whose pages are still in a character's content or a saved drop (before 4d) is moved here when
///                the server starts, and through the save filter (ServerLedger) when a client sends one
///   book.get     the body goes to the requester as BookText (event 4), in parts under 60KB, before its OpResult
///   book.page    one page (a long one in parts: append, more); only with the right to write it (BookInstanceData.
///                AllowsRevision on the server's copy) and within the length rule (MaxPageChars, or the page's old length)
///   book.meta    a new title (the right to write) and a new permission (the author only)
///   book.new     an id the server does not have yet; the author is the sender (its character name), unless the
///                client says it is a copy of someone else's book (a shop or library copy, decided on the server in 4d-3/4d-5).
///                Whoever sent the book.new fills its pages (a read-only or loan copy too), for as long as this server runs
/// Nothing here touches the inventory or the screen of this process (an in-app server's owner plays in it).
/// </summary>
public sealed class ServerBooks
{
    public static readonly HashSet<string> Kinds = new HashSet<string> { "book.get", "book.page", "book.meta", "book.new" };
    /// <summary>The prefab key of a dropped item (DropState.PrefabKey, in the UI assembly).</summary>
    const string DropPrefab = "Net/WorldDrop";
    /// <summary>New books (book.new) one account may make while this server runs.</summary>
    // ponytail: a count per server run; 4d-3 bounds books by what the inventory ledger holds instead.
    public const int MaxNewPerAccount = 200;
    const int MaxTitle = 80, MaxId = 64, TextChunk = 16000, PartBudget = SyncProtocol.MaxServerBlob - 1024;

    /// <summary>A book request (the json of the Op). Unused fields stay empty.</summary>
    [Serializable]
    public sealed class Request
    {
        public string id, title, text, authorId, authorName, loan, work;
        public int page, permission;
        public bool append, more;
    }

    readonly GameServer server;
    readonly ulong entity;
    readonly Dictionary<string, BookInstanceData> store = new Dictionary<string, BookInstanceData>();
    readonly Dictionary<string, string> partial = new Dictionary<string, string>(); // connection/id/page → a page in parts
    readonly Dictionary<string, string> makers = new Dictionary<string, string>(); // id → the account that sent its book.new
    readonly Dictionary<string, int> made = new Dictionary<string, int>(); // account → book.new taken while this server runs

    /// <summary>Books moved here from older saves, and BookText events sent, for QA.</summary>
    public int Adopted { get; private set; }
    public int TextsSent { get; private set; }
    public int Count => store.Count;
    /// <summary>Pages still coming in parts, for QA.</summary>
    public int PartialCount => partial.Count;

    public ServerBooks(GameServer server, ulong entity, List<BookSave> saved)
    {
        this.server = server;
        this.entity = entity;
        if (saved != null)
            foreach (var s in saved)
                if (s != null && ValidId(s.id) && s.book != null) { var book = s.book.Clone(); book.bodyLoaded = true; store[s.id] = book; }
        MoveOlderSaves(server.World);
    }

    /// <summary>The server's copy of a book (QA), or null.</summary>
    public BookInstanceData Find(string id) => id != null && store.TryGetValue(id, out var book) ? book : null;

    public List<BookSave> Capture()
    {
        var list = new List<BookSave>(store.Count);
        foreach (var pair in store) list.Add(new BookSave { id = pair.Key, book = pair.Value });
        return list;
    }

    static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= MaxId;

    // ---- bodies out of characters and drops ----

    /// <summary>An inventory snapshot without book pages; pages of a book the server does not have yet are kept here.</summary>
    public string Strip(string inventory) => InventorySnapshot.StripBodies(inventory, Adopt);

    void Adopt(string id, BookInstanceData book)
    {
        if (!ValidId(id) || store.ContainsKey(id)) return; // the server's copy wins
        book.bodyLoaded = true;
        store[id] = book;
        Adopted++;
        Debug.Log("[ServerBooks] book " + id + " (" + book.title + ") moved into the world's books");
    }

    // Characters, ground drops and the lost-and-found list saved before 4d-2. A saved entity's blob array is the very one
    // GameServer serves (it reads World.Entities into its entities once, sharing the arrays), so it is changed in place.
    void MoveOlderSaves(WorldStore world)
    {
        int before = Adopted;
        foreach (var c in world.Characters)
        {
            if (!CharacterSync.Read(c.content, out var saved)) continue;
            string inventory = Strip(saved.inventory);
            if (ReferenceEquals(inventory, saved.inventory)) continue;
            saved.inventory = inventory;
            c.content = CharacterSync.Encode(saved);
        }
        foreach (var e in world.Entities) if (e.prefab == DropPrefab) StripDrop(e.blobs);
        foreach (var l in world.Lost) if (l.prefab == DropPrefab) StripDrop(l.blobs);
        if (Adopted > before) Debug.Log("[ServerBooks] " + (Adopted - before) + " book(s) moved out of older saves of world " + world.Id);
    }

    // DropState's blob: u8 kind, f32 x y z yaw, blob gzip(inventory snapshot).
    void StripDrop(byte[][] blobs)
    {
        if (blobs == null || blobs.Length == 0 || blobs[0] == null || blobs[0].Length == 0) return;
        try
        {
            byte kind; float x, y, z, yaw; byte[] contents;
            using (var r = SyncProtocol.Reader(blobs[0]))
            {
                kind = r.ReadByte(); x = r.ReadSingle(); y = r.ReadSingle(); z = r.ReadSingle(); yaw = r.ReadSingle();
                contents = SyncProtocol.ReadBlob(r);
            }
            string json = ServerWorld.DecodeJson(contents), stripped = Strip(json);
            if (ReferenceEquals(json, stripped)) return;
            blobs[0] = ServerWorld.Bytes(w =>
            {
                w.Write(kind); w.Write(x); w.Write(y); w.Write(z); w.Write(yaw);
                SyncProtocol.WriteBlob(w, ServerWorld.Encode(stripped));
            });
        }
        catch (Exception e) { Debug.LogWarning("[ServerBooks] a saved drop is unreadable: " + e.Message); }
    }

    // ---- requests ----

    /// <summary>Decides a book.* request of <paramref name="account"/> on <paramref name="connection"/>. The error (Korean), or null.</summary>
    public string Apply(uint connection, string account, string kind, string json)
    {
        Request q;
        try { q = JsonUtility.FromJson<Request>(json); }
        catch (Exception) { q = null; }
        if (q == null || !ValidId(q.id)) return "책 정보를 확인해 주세요.";
        store.TryGetValue(q.id, out var book);
        if (kind == "book.new") return New(account, q, book);
        if (book == null) return "책을 찾을 수 없습니다.";
        switch (kind)
        {
            case "book.get":
                SendText(connection, q.id, book);
                return null;
            case "book.page":
                return Page(connection, account, q, book);
            case "book.meta":
                return Meta(account, q, book);
            default:
                return "처리할 수 없는 요청입니다.";
        }
    }

    string New(string account, Request q, BookInstanceData existing)
    {
        if (existing != null) return "이미 있는 책입니다.";
        if (string.IsNullOrWhiteSpace(q.title) || q.title.Length > MaxTitle) return "제목을 확인해 주세요.";
        if (!Enum.IsDefined(typeof(BookEditPermission), q.permission)) return "권한이 올바르지 않습니다.";
        made.TryGetValue(account, out int count);
        if (count >= MaxNewPerAccount) return "더 이상 새 책을 만들 수 없습니다.";
        // ponytail: a copy keeps the author the client gave it; 4d-3/4d-5 make shop and library copies on the server.
        bool copy = !string.IsNullOrEmpty(q.authorId) && q.authorId != account;
        var book = new BookInstanceData
        {
            title = q.title, isPublished = true, permission = (BookEditPermission)q.permission,
            authorPlayerId = copy ? q.authorId : account,
            authorName = copy ? q.authorName : server.World.Find(account)?.name ?? "",
            libraryLoanId = string.IsNullOrEmpty(q.loan) ? null : q.loan, libraryWorkId = string.IsNullOrEmpty(q.work) ? null : q.work
        }.Clone();
        store[q.id] = book;
        makers[q.id] = account;
        made[account] = count + 1;
        if (copy) Debug.Log("[ServerBooks] " + account + " added a copy " + q.id + " of " + q.authorId + "'s book");
        return null;
    }

    string Page(uint connection, string account, Request q, BookInstanceData book)
    {
        DropGoneParts();
        if (q.page < 0 || q.page >= BookInstanceData.PageCount) return "쪽 번호가 올바르지 않습니다.";
        string key = connection + "/" + q.id + "/" + q.page, text;
        if (q.append)
        {
            if (!partial.TryGetValue(key, out string sofar)) return "이어 쓸 쪽이 없습니다.";
            text = sofar + (q.text ?? "");
        }
        else text = q.text ?? "";
        partial.Remove(key);
        // ponytail: the maker of a copy may rewrite it while the server runs (anyone may make a book anyway); 4d-3/4d-5 make copies here.
        bool maker = makers.TryGetValue(q.id, out string by) && by == account;
        // Checked on every part too, so a sender without the right cannot pile parts up here.
        if (!maker && !book.CanEdit(account)) return "이 책을 고칠 권한이 없습니다.";
        if (text.Length > BookInstanceData.PageLimit(book.pages[q.page])) return "한 쪽에는 " + BookInstanceData.MaxPageChars.ToString("N0") + "자까지 쓸 수 있습니다.";
        if (q.more) { partial[key] = text; return null; }
        var proposed = book.Clone();
        proposed.pages[q.page] = text;
        if (!maker && !book.AllowsRevision(proposed, account)) return "이 책의 수정 권한으로는 해당 내용을 변경할 수 없습니다.";
        book.pages[q.page] = text;
        return null;
    }

    /// <summary>
    /// Parts of a connection that is gone never finish: they are dropped (connection numbers are never reused, so the
    /// key's first field tells). GameServer has no leave event: ServerWorld calls this every second, and every book.page does.
    /// </summary>
    public void DropGoneParts()
    {
        if (partial.Count == 0) return;
        List<string> gone = null;
        foreach (string key in partial.Keys)
        {
            int slash = key.IndexOf('/');
            if (!uint.TryParse(key.Substring(0, slash), out uint connection) || server.AccountOf(connection) == null) (gone ??= new List<string>()).Add(key);
        }
        if (gone != null) foreach (string key in gone) partial.Remove(key);
    }

    string Meta(string account, Request q, BookInstanceData book)
    {
        if (book.IsLibraryLoan) return "대여한 책은 고칠 수 없습니다.";
        bool rename = !string.IsNullOrEmpty(q.title) && q.title != book.title;
        bool permit = q.permission != (int)book.permission;
        if (rename && !book.CanRename(account)) return "제목을 바꿀 권한이 없습니다.";
        if (rename && (string.IsNullOrWhiteSpace(q.title) || q.title.Length > MaxTitle)) return "제목을 확인해 주세요.";
        if (permit && (!book.IsAuthor(account) || !Enum.IsDefined(typeof(BookEditPermission), q.permission))) return "저자만 다른 플레이어의 권한을 설정할 수 있습니다.";
        if (rename) book.title = q.title;
        if (permit) book.permission = (BookEditPermission)q.permission;
        return null;
    }

    // ---- BookText (event 4): str id, u8 flags (1 = the book's details follow: str title, str author id, str author name,
    //      u8 permission), u8 count, count x (u8 page, bool append, str text). The first part has the details. ----

    void SendText(uint connection, string id, BookInstanceData book)
    {
        var parts = new List<(int page, bool append, string text)>();
        for (int p = 0; p < BookInstanceData.PageCount; p++)
        {
            string text = book.pages[p] ?? "";
            for (int at = 0; at < text.Length;)
            {
                int take = Math.Min(TextChunk, text.Length - at);
                if (at + take < text.Length && char.IsHighSurrogate(text[at + take - 1])) take--;
                parts.Add((p, at > 0, text.Substring(at, take)));
                at += take;
            }
        }
        int next = 0;
        bool first = true;
        do
        {
            int from = next, budget = PartBudget - (first ? Encoding.UTF8.GetByteCount(book.title + book.authorPlayerId + book.authorName) + 16 : 0);
            while (next < parts.Count && next - from < byte.MaxValue)
            {
                int cost = Encoding.UTF8.GetByteCount(parts[next].text) + 4; // a part alone always fits (TextChunk x 3 bytes)
                if (next > from && cost > budget) break;
                budget -= cost;
                next++;
            }
            bool details = first;
            var blob = ServerWorld.Bytes(w =>
            {
                SyncProtocol.WriteStr(w, id);
                w.Write((byte)(details ? 1 : 0));
                if (details)
                {
                    SyncProtocol.WriteStr(w, book.title ?? ""); SyncProtocol.WriteStr(w, book.authorPlayerId ?? "");
                    SyncProtocol.WriteStr(w, book.authorName ?? ""); w.Write((byte)book.permission);
                }
                w.Write((byte)(next - from));
                for (int i = from; i < next; i++) { w.Write((byte)parts[i].page); w.Write(parts[i].append); SyncProtocol.WriteStr(w, parts[i].text); }
            });
            if (server.SendServerEvent(entity, 0, WorldSection.BookTextEvent, connection, blob)) TextsSent++;
            else Debug.LogWarning("[ServerBooks] a part of book " + id + " (" + blob.Length + " bytes) was not sent");
            first = false;
        } while (next < parts.Count);
    }

    /// <summary>Client side: one BookText into <paramref name="incoming"/> (a book is started by the part with its details). Its id.</summary>
    public static string ReadText(BinaryReader r, Dictionary<string, BookInstanceData> incoming)
    {
        string id = SyncProtocol.ReadStr(r);
        bool details = (r.ReadByte() & 1) != 0;
        if (details || !incoming.TryGetValue(id, out var book))
        {
            book = new BookInstanceData().Clone();
            book.isPublished = true;
            incoming[id] = book;
        }
        if (details)
        {
            book.title = SyncProtocol.ReadStr(r); book.authorPlayerId = SyncProtocol.ReadStr(r);
            book.authorName = SyncProtocol.ReadStr(r); book.permission = (BookEditPermission)r.ReadByte();
        }
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            int page = r.ReadByte();
            bool append = r.ReadBoolean();
            string text = SyncProtocol.ReadStr(r);
            if (page < BookInstanceData.PageCount) book.pages[page] = append ? book.pages[page] + text : text;
        }
        return id;
    }
}
