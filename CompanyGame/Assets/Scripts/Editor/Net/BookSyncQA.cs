using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the book bodies kept apart on the server (Docs/Multiplayer_Redesign.md 12.10.3), on the EconomySyncQA helpers.
///   -executeMethod BookSyncQA.Run         in memory (with -quit): InventorySnapshot without pages, ServerBooks (older
///                                         saves moved in, book.* rules, BookText in parts under 60KB)
///   -executeMethod BookSyncQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + a fake TCP player
///   -executeMethod BookSyncQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client
/// Env QA_SHOTS = folder for B_*.png (needs graphics).
/// </summary>
public static class BookSyncQA
{
    public static void Run() => EconomySyncQA.BookMemory();
    public static void RunPlay() => EconomySyncQA.StartPlay("book-play");
    public static void RunProcess() => EconomySyncQA.BuildAndPlay("BookSyncQA", "book-process");
}

public static partial class EconomySyncQA
{
    const string BookFake = "qa-book-fake", DropKey = "Net/WorldDrop";
    const EntityFlags DroppedFlags = EntityFlags.PersistOnLeave | EntityFlags.Claimable | EntityFlags.Expiring;

    static ItemData Blank => Resources.Load<ItemData>("Inventory/Books/BlankBook");

    // Korean text with spaces, the same for the same seed.
    static string Text(int length, int seed)
    {
        var rnd = new System.Random(seed);
        var text = new StringBuilder(length);
        for (int i = 0; i < length; i++) text.Append(i % 9 == 8 ? ' ' : (char)(0xAC00 + rnd.Next(11172)));
        return text.ToString();
    }

    static BookInstanceData NewBook(string title, string author, string authorName, int pages, int perPage, int seed)
    {
        var book = new BookInstanceData { title = title, authorPlayerId = author, authorName = authorName, isPublished = true }.Clone();
        for (int p = 0; p < pages; p++) book.pages[p] = Text(perPage, seed * 100 + p);
        return book;
    }

    static bool SamePages(BookInstanceData a, BookInstanceData b)
    {
        if (a == null || b == null) return false;
        for (int p = 0; p < BookInstanceData.PageCount; p++) if ((a.pages[p] ?? "") != (b.pages[p] ?? "")) return false;
        return true;
    }

    static int Chars(BookInstanceData book) { int n = 0; foreach (var p in book.pages) n += p?.Length ?? 0; return n; }

    // One book in an inventory snapshot, with its pages (the format before 4d-2).
    static string OldSnapshot(string id, BookInstanceData book)
    {
        var inventory = new InventoryState();
        if (!inventory.TryAddBookInstance(Blank, id, book, out string error)) throw new Exception("[BookSyncQA] FAIL: " + error);
        return InventorySnapshot.Export(inventory);
    }

    // A dropped-item state (DropState's layout) around a snapshot as it is given (with pages: the format before 4d-2).
    static byte[] DropOf(string snapshot, Vector3 at)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        w.Write(DropState.ItemKind); w.Write(at.x); w.Write(at.y); w.Write(at.z); w.Write(0f);
        using (var packed = new MemoryStream())
        {
            using (var gzip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Optimal, true))
            { byte[] json = Encoding.UTF8.GetBytes(snapshot); gzip.Write(json, 0, json.Length); }
            SyncProtocol.WriteBlob(w, packed.ToArray());
        }
        w.Flush();
        return stream.ToArray();
    }

    // The snapshot inside a drop state.
    static string SnapshotOf(byte[] drop)
    {
        using var r = SyncProtocol.Reader(drop);
        r.ReadByte(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
        return SectionJson(SyncProtocol.ReadBlob(r));
    }

    static string InventoryOf(byte[] content) => CharacterSync.Read(content, out var saved) ? saved.inventory ?? "" : "";

    static BookInstanceData BookIn(string snapshot, string id)
    {
        var inventory = new InventoryState();
        return InventorySnapshot.Import(inventory, snapshot) ? inventory.FindBook(id)?.BookData : null;
    }

    // ================= in memory (BookSyncQA.Run) =================

    internal static void BookMemory()
    {
        tag = "BookSyncQA";
        Begin();
        root = Path.Combine(Path.GetTempPath(), "BookSyncQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            SnapshotWithoutPages();
            ServerBookRules();
            Report("RESULT passed");
        }
        catch (Exception e) { Report("RESULT FAILED: " + e.Message + "\n" + e.StackTrace, true); throw; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void SnapshotWithoutPages()
    {
        Section("inventory snapshot without pages");
        var book = NewBook("목록 책", "acc-a", "QA가", 3, 900, 1);
        string full = OldSnapshot("qa-snap", book);
        var inventory = new InventoryState();
        Check(InventorySnapshot.Import(inventory, full), "a snapshot with pages imports");
        var loaded = inventory.FindBook("qa-snap").BookData;
        Check(loaded.bodyLoaded && SamePages(loaded, book), "a book from a snapshot with pages (the format before 4d-2) is loaded");
        string bare = InventorySnapshot.Export(inventory, false);
        Check(!bare.Contains(book.pages[0].Substring(0, 30)) && bare.Contains("\"bodyless\":true") && bare.Contains("목록 책"),
            "Export without pages: no page text, the title stays, marked bodyless (" + Encoding.UTF8.GetByteCount(bare) + " bytes, with pages " + Encoding.UTF8.GetByteCount(full) + ")");
        var other = new InventoryState();
        Check(InventorySnapshot.Import(other, bare), "it imports");
        var bodyless = other.FindBook("qa-snap")?.BookData;
        Check(bodyless != null && !bodyless.bodyLoaded && !bodyless.HasContent && bodyless.title == "목록 책" && bodyless.IsAuthor("acc-a") && other.GetSlot(0).IsUniqueBook,
            "as a book without its pages (bodyLoaded false), still the same unique book with its title and author");
        Check(!InventorySnapshot.Export(other).Contains("\"bodyless\":false") && BookIn(InventorySnapshot.Export(other), "qa-snap")?.bodyLoaded == false,
            "a book without pages stays without them even in an export with pages (nothing empty passes for a body)");
        var moved = new List<string>();
        string stripped = InventorySnapshot.StripBodies(full, (id, b) => { moved.Add(id); Check(SamePages(b, book), "the stripped body is handed over whole"); });
        Check(moved.Count == 1 && moved[0] == "qa-snap" && !stripped.Contains(book.pages[1].Substring(0, 30)) && BookIn(stripped, "qa-snap")?.bodyLoaded == false,
            "StripBodies hands the pages over and leaves a bodyless book");
        Check(ReferenceEquals(InventorySnapshot.StripBodies(stripped, (id, b) => moved.Add(id)), stripped) && moved.Count == 1, "a stripped snapshot is left as it is");
    }

    static void ServerBookRules()
    {
        Section("older saves move into the world's books");
        string world = WorldStore.NewId();
        StartServer(world);
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        var charBook = NewBook("캐릭터 책", "acc-a", "QA가", 2, 2500, 2); // a page longer than 2,000 written before
        var dropBook = NewBook("떨어진 책", "acc-b", "QA나", 1, 600, 3);
        var lostBook = NewBook("분실물 책", "acc-b", "QA나", 1, 500, 4);
        server.World.Find("acc-a").content = CharacterSync.Encode(new CharacterSync.Saved { map = "m1", inventory = OldSnapshot("qa-char", charBook) });
        server.Stop();
        var store = WorldStore.Open(root, world);
        ulong dropId = SyncProtocol.MakeId(900, 1);
        store.Entities.Add(new WorldStore.SavedEntity { id = dropId, prefab = DropKey, map = "m1", flags = DroppedFlags, created = 400, blobs = new[] { DropOf(OldSnapshot("qa-drop", dropBook), Vector3.zero) } });
        store.Lost.Add(new WorldStore.LostItem { prefab = DropKey, movedAt = 400, blobs = new[] { DropOf(OldSnapshot("qa-lost", lostBook), Vector3.zero) } });
        store.WorldMinutes = 400;
        store.Save();
        StartServer(world);
        ulong entity = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new byte[ServerWorld.SectionCount][]);
        var books = new ServerBooks(server, entity, null);
        Check(books.Adopted == 3 && SamePages(books.Find("qa-char"), charBook) && SamePages(books.Find("qa-drop"), dropBook) && SamePages(books.Find("qa-lost"), lostBook),
            "a character's, a ground drop's and a lost item's book (with pages, before 4d-2) are in the world's books");
        string content = InventoryOf(server.World.Find("acc-a").content);
        Check(content.Contains("캐릭터 책") && !content.Contains(charBook.pages[0].Substring(0, 30)) && BookIn(content, "qa-char")?.bodyLoaded == false,
            "the character's content keeps the book without its pages");
        a = Join("acc-a", "QA가", "m1"); b = Join("acc-b", "QA나", "m1");
        var c = Join("acc-c", "QA다", "m1");
        var spawn = c.Take(ToClient.Spawn, dropId);
        Check(spawn != null && !SnapshotOf(spawn.blobs[0]).Contains(dropBook.pages[0].Substring(0, 30)) && BookIn(SnapshotOf(spawn.blobs[0]), "qa-drop")?.bodyLoaded == false,
            "the saved drop is served without the pages (" + spawn.blobs[0].Length + " bytes)");
        Check(!SnapshotOf(server.World.Lost[0].blobs[0]).Contains(lostBook.pages[0].Substring(0, 30)), "the lost item too");
        Check(books.Adopted == 3 && ReferenceEquals(books.Strip(content), content), "nothing is moved twice");

        Section("book.new");
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-new", title = "새 책", permission = (int)BookEditPermission.FullEdit }) == null,
            "book.new of an id the server lacks");
        var made = books.Find("qa-new");
        Check(made.IsAuthor("acc-a") && made.authorName == "QA가" && made.isPublished && !made.HasContent, "its author is the sender and its character name");
        Check(Ask(books, b, "book.new", new ServerBooks.Request { id = "qa-new", title = "덮기" }) == "이미 있는 책입니다." && books.Find("qa-new").IsAuthor("acc-a"), "book.new of an id that exists is refused");
        Check(Ask(books, b, "book.new", new ServerBooks.Request { id = "qa-copy", title = "사본", authorId = "acc-z", authorName = "원작자", permission = (int)BookEditPermission.ReadOnly }) == null
              && books.Find("qa-copy").IsAuthor("acc-z") && books.Find("qa-copy").permission == BookEditPermission.ReadOnly, "a copy keeps the author the client gave it (4d-2)");
        Check(Ask(books, b, "book.new", new ServerBooks.Request { id = "qa-loan", title = "대여본", authorId = "acc-a", loan = "loan-1" }) == null && books.Find("qa-loan").IsLibraryLoan, "a loan copy");
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-bad", title = " " }) != null && Ask(books, a, "book.new", new ServerBooks.Request { id = "", title = "x" }) != null,
            "no title or no id is refused");

        Section("book.page");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "첫 쪽" }) == null && books.Find("qa-new").pages[0] == "첫 쪽", "a page by the author");
        string long1 = Text(900, 5), long2 = Text(900, 6);
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 1, text = long1, more = true }) == null && books.Find("qa-new").pages[1] == "", "a first part waits");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 1, text = long2, append = true, more = true }) == null, "a middle part");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 1, text = "끝", append = true }) == null && books.Find("qa-new").pages[1] == long1 + long2 + "끝",
            "the last part puts the whole page (1,801 characters) in");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 2, text = "x", append = true }) == "이어 쓸 쪽이 없습니다.", "an appended part without a first one is refused");
        Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 2, text = Text(1000, 7), more = true });
        Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 2, text = Text(1000, 8), append = true, more = true });
        string over = Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 2, text = "x", append = true });
        Check(over != null && books.Find("qa-new").pages[2] == "", "2,001 characters in parts are refused (" + over + ")");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 2, text = Text(2000, 9) }) == null, "2,000 characters are taken");
        string old = books.Find("qa-char").pages[0];
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-char", page = 0, text = old.Substring(0, 2400) }) == null
              && Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-char", page = 0, text = old.Substring(0, 2400) + Text(300, 10) }) != null
              && books.Find("qa-char").pages[0].Length == 2400, "a 2,500-character page written before can be shortened (2,400) but not grow past its length");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 20, text = "x" }) != null
              && Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-none", page = 0, text = "x" }) == "책을 찾을 수 없습니다.", "page 20 and an unknown book are refused");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-loan", page = 0, text = "대여본 본문" }) == null && books.Find("qa-loan").pages[0] == "대여본 본문",
            "the maker of a loan copy fills its pages (it was sent as made)");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-loan", page = 0, text = "x" }) != null, "nobody else can write a loan copy, not even its author");

        Section("permissions");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "남이 고침" }) == null, "FullEdit: another player may rewrite a page");
        Check(Ask(books, b, "book.meta", new ServerBooks.Request { id = "qa-new", title = "남이 바꾼 제목", permission = (int)BookEditPermission.FullEdit }) == null
              && books.Find("qa-new").title == "남이 바꾼 제목", "FullEdit: and rename it");
        Check(Ask(books, b, "book.meta", new ServerBooks.Request { id = "qa-new", title = "남이 바꾼 제목", permission = (int)BookEditPermission.ReadOnly }) != null
              && books.Find("qa-new").permission == BookEditPermission.FullEdit, "the permission is the author's only");
        Check(Ask(books, a, "book.meta", new ServerBooks.Request { id = "qa-new", title = "새 책", permission = (int)BookEditPermission.ReadOnly }) == null
              && books.Find("qa-new").permission == BookEditPermission.ReadOnly && books.Find("qa-new").title == "새 책", "the author renames and sets 읽기만 가능");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "남이 고침 또" }) != null && books.Find("qa-new").pages[0] == "남이 고침",
            "읽기만 가능: another player's page is refused");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "x", more = true }) != null, "even a first part of it");
        Check(Ask(books, b, "book.meta", new ServerBooks.Request { id = "qa-new", title = "또 바꿈", permission = (int)BookEditPermission.ReadOnly }) != null, "and a new title");
        Ask(books, a, "book.meta", new ServerBooks.Request { id = "qa-new", title = "새 책", permission = (int)BookEditPermission.AddOnly });
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "남이 고침, 덧붙임" }) == null && books.Find("qa-new").pages[0] == "남이 고침, 덧붙임",
            "추가만 가능: adding is taken");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = "남이" }) != null && books.Find("qa-new").pages[0] == "남이 고침, 덧붙임", "removing is refused");
        string added = "남이 고침, 덧붙임" + Text(1500, 11);
        Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = added.Substring(0, 1000), more = true });
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-new", page = 0, text = added.Substring(1000), append = true }) == null && books.Find("qa-new").pages[0] == added,
            "an addition in two parts is judged as a whole page (the first part alone would cut the old text)");
        Check(Ask(books, b, "book.meta", new ServerBooks.Request { id = "qa-new", title = "또 바꿈", permission = (int)BookEditPermission.AddOnly }) != null, "추가만 가능: no new title");

        Section("limits of the server's memory");
        var d = Join("acc-d", "QA라", "m1");
        int newBooks = 0;
        string refused = null;
        for (int i = 0; i < ServerBooks.MaxNewPerAccount + 1 && refused == null; i++)
        {
            refused = Ask(books, d, "book.new", new ServerBooks.Request { id = "qa-many-" + i, title = "많은 책 " + i });
            if (refused == null) newBooks++;
        }
        Check(newBooks == ServerBooks.MaxNewPerAccount && refused == "더 이상 새 책을 만들 수 없습니다." && books.Find("qa-many-" + newBooks) == null,
            "one account makes at most " + ServerBooks.MaxNewPerAccount + " new books; the next is refused: " + refused);
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-after-many", title = "다른 계정의 책" }) == null,
            "another account still can (acc-a has made only a few; the 3 books moved from older saves count for nobody)");
        Check(Ask(books, d, "book.page", new ServerBooks.Request { id = "qa-many-0", page = 0, text = Text(500, 14), more = true }) == null
              && Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 3, text = Text(500, 15), more = true }) == null
              && books.PartialCount == 2, "two pages wait in parts (acc-d's and acc-a's)");
        d.client.Close();
        Step(a);
        books.DropGoneParts();
        Check(books.PartialCount == 1, "acc-d left: its unfinished part is dropped, acc-a's stays (" + books.PartialCount + " left)");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-new", page = 3, text = "끝", append = true }) == null && books.PartialCount == 0,
            "acc-a finishes its page");

        Section("BookText in parts");
        var huge = NewBook("긴 책", "acc-a", "QA가", 20, 2000, 12);
        huge.pages[19] = Text(70000, 13); // a very long page saved before
        books.Strip(OldSnapshot("qa-huge", huge));
        Clear(a, b);
        Check(Ask(books, a, "book.get", new ServerBooks.Request { id = "qa-huge" }) == null, "book.get");
        Step(a, b);
        var parts = a.inbox.FindAll(m => m.kind == ToClient.Event && m.id == entity && m.eventId == WorldSection.BookTextEvent && m.sender == 0);
        var got = new Dictionary<string, BookInstanceData>();
        int biggest = 0;
        foreach (var m in parts) { biggest = Math.Max(biggest, m.blob.Length); using var r = SyncProtocol.Reader(m.blob); ServerBooks.ReadText(r, got); }
        Check(parts.Count > 1 && biggest <= SyncProtocol.MaxServerBlob && SamePages(got["qa-huge"], huge) && got["qa-huge"].title == "긴 책" && got["qa-huge"].IsAuthor("acc-a"),
            "a " + Chars(huge).ToString("N0") + "-character book comes in " + parts.Count + " parts, the biggest " + biggest + " bytes (under " + SyncProtocol.MaxServerBlob + ")");
        Check(!b.Has(m => m.kind == ToClient.Event && m.eventId == WorldSection.BookTextEvent), "only the requester gets it");
        Note("BookText of a " + Chars(huge) + "-character book: " + parts.Count + " parts, biggest " + biggest + " bytes");

        Section("saved with the world state");
        var state = JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(new WorldSaveData { books = books.Capture() }));
        var again = new ServerBooks(server, entity, state.books);
        Check(again.Count == books.Count && SamePages(again.Find("qa-huge"), huge) && again.Find("qa-new").permission == BookEditPermission.AddOnly,
            "the books go through the world state JSON whole (" + again.Count + " books)");
        server.Stop();
    }

    static string Ask(ServerBooks books, QaPeer who, string kind, ServerBooks.Request q) =>
        books.Apply(who.number, server.AccountOf(who.number), kind, JsonUtility.ToJson(q));

    // ================= in-app server (BookSyncQA.RunPlay) =================

    static async Task BookPlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;

        Section("saves from before 4d-2");
        var settings = new ServerSettings { name = "BookQA", maxPlayers = 4, port = 0, world = WorldStore.NewId() };
        var store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        playWorld = store.Folder;
        var oldChar = NewBook("옛 캐릭터 책", BookFake, "QA책가짜", 3, 1200, 21);
        var oldDrop = NewBook("옛 드롭 책", BookFake, "QA책가짜", 1, 700, 22);
        Check(store.Create(BookFake, "QA책가짜", "female", out var made) == CharacterResult.Ok, "the fake's character in a world file");
        made.content = CharacterSync.Encode(new CharacterSync.Saved { map = "qa-map", personal = JsonUtility.ToJson(new PersonalSaveData { bankBalance = 1000 }), inventory = OldSnapshot("qa-old-char", oldChar) });
        ulong oldDropId = SyncProtocol.MakeId(900, 1);
        store.WorldMinutes = 400;
        store.Entities.Add(new WorldStore.SavedEntity { id = oldDropId, prefab = DropKey, map = "qa-map", flags = DroppedFlags, created = 400, blobs = new[] { DropOf(OldSnapshot("qa-old-drop", oldDrop), Vector3.zero) } });
        store.Save();
        Check(Encoding.UTF8.GetByteCount(InventoryOf(made.content)) > 9000, "the fake's saved content has the book's pages (" + made.content.Length + " bytes gzipped), and a saved drop has one too");

        var host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA책주인", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var books = ServerWorld.Instance.Books;
        Check(SamePages(books.Find("qa-old-char"), oldChar) && SamePages(books.Find("qa-old-drop"), oldDrop), "both older books are in the world's books");
        string fakeInventory = InventoryOf(host.Server.World.Find(BookFake).content);
        Check(fakeInventory.Contains("옛 캐릭터 책") && !fakeInventory.Contains(oldChar.pages[0].Substring(0, 30)), "the fake's content keeps the book without its pages");

        var fake = await JoinFake(host.Port, "qa-map", BookFake, "QA책가짜");
        var oldSpawn = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.id == oldDropId, "the older drop at the fake");
        Check(!SnapshotOf(oldSpawn.blobs[0]).Contains(oldDrop.pages[0].Substring(0, 30)) && BookIn(SnapshotOf(oldSpawn.blobs[0]), "qa-old-drop")?.bodyLoaded == false,
            "the older drop comes without its pages (" + oldSpawn.blobs[0].Length + " bytes)");
        var got = await FakeGet(fake, "qa-old-char");
        Check(SamePages(got.book, oldChar), "joining again the fake reads its older book with book.get (" + got.parts + " part)");
        got = await FakeGet(fake, "qa-old-drop");
        Check(SamePages(got.book, oldDrop), "and the older drop's book");
        fake.Send(SyncProtocol.Map(map));
        await Awaitable.WaitForSecondsAsync(.3f);

        Section("a 30,000-character book");
        var bag = InventoryManager.Instance.State;
        Give(ServerWorld.Instance.Ledger, me, Blank, 1); // the server keeps the bag (12.10.4); nothing comes in on trust since 12.10.6
        Check(bag.TryAdd(Blank, 1, out _), "a blank book in the bag (and the ledger)");
        int blank = FindBlank(bag);
        var draft = NewBook("삼만 자 책", null, null, 20, 1500, 23);
        draft.isPublished = false;
        int requests = WorldStateSync.BookRequests;
        Check(bag.TryPublishBook(blank, draft, me, GameSession.LocalPlayerName, out int slot, out string error), "the owner writes 20 pages x 1,500 characters " + error);
        string longId = bag.GetSlot(slot).InstanceId;
        Check(Chars(bag.GetSlot(slot).BookData) == 30000, "30,000 characters");
        await WaitFor(() => SamePages(books.Find(longId), draft), 10, "the book on the server");
        Check(books.Find(longId).IsAuthor(me) && books.Find(longId).title == "삼만 자 책" && WorldStateSync.LastBookError == null,
            "the server's books have it whole, by the owner (" + (WorldStateSync.BookRequests - requests) + " requests)");
        Note("uploading 30,000 characters: " + (WorldStateSync.BookRequests - requests) + " requests");
        var mine = bag.GetSlot(slot).BookData;
        Check(mine.bodyLoaded && SamePages(mine, draft), "the owner's own book keeps its pages here");
        CharacterSync.SaveNow();
        await WaitFor(() => InventoryOf(host.Server.World.Find(me).content).Contains(longId), 5, "the owner's save");
        string myInventory = InventoryOf(host.Server.World.Find(me).content);
        Check(!myInventory.Contains(draft.pages[0].Substring(0, 30)) && !myInventory.Contains(draft.pages[19].Substring(0, 30)) && BookIn(myInventory, longId)?.bodyLoaded == false,
            "the owner's character content has the book without pages (" + host.Server.World.Find(me).content.Length + " bytes gzipped)");
        Note("owner's character content with the 30,000-character book: " + host.Server.World.Find(me).content.Length + " bytes");

        Section("dropped and picked up by the fake");
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        Drain(fake);
        Check(inv.OpenInventory(), "inventory open");
        bool dropped = WorldDroppedItem.TryDropStorage(inv, slot, out string dropError);
        inv.CloseInventory();
        Check(dropped, "the 30,000-character book can be dropped (the 4KB limit is gone) " + dropError);
        var dropMsg = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.key == DropKey && m.flags == DroppedFlags, "the drop at the fake");
        Check(dropMsg.blobs[0].Length < 1024 && BookIn(SnapshotOf(dropMsg.blobs[0]), longId)?.title == "삼만 자 책", "the drop's state is " + dropMsg.blobs[0].Length + " bytes: the book without pages");
        Check(dropMsg.owner == 0, "the server made it (12.10.5)");
        var pickAnswer = await FakeOp(fake, "p.pickup", new SharedOpArgs { n = (long)dropMsg.id });
        Check(pickAnswer.error == "", "the fake picks it up (p.pickup) " + pickAnswer.error);
        got = await FakeGet(fake, longId);
        Check(SamePages(got.book, draft) && got.book.title == "삼만 자 책", "and reads the same 30,000 characters with book.get (" + got.parts + " BookText parts, " + got.bytes + " bytes)");
        Note("book.get of 30,000 characters: " + got.parts + " parts, " + got.bytes + " bytes");

        Section("refused book requests");
        Give(ServerWorld.Instance.Ledger, me, Blank, 1);
        Check(bag.TryAdd(Blank, 1, out _), "another blank");
        var small = NewBook("권한 책", null, null, 2, 100, 24);
        small.isPublished = false;
        Check(bag.TryPublishBook(FindBlank(bag), small, me, GameSession.LocalPlayerName, out int smallSlot, out _), "the owner writes a short book");
        string smallId = bag.GetSlot(smallSlot).InstanceId;
        Check(bag.TrySetBookPermission(smallId, BookEditPermission.ReadOnly, me, out _), "and sets 읽기만 가능");
        await WaitFor(() => books.Find(smallId)?.permission == BookEditPermission.ReadOnly, 5, "the permission on the server");
        string page0 = small.pages[0];
        var answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = smallId, page = 0, text = "덮어쓰기" });
        Check(answer.error != "" && books.Find(smallId).pages[0] == page0, "the fake's book.page on it is refused: " + answer.error);
        answer = await FakeBookOp(fake, "book.meta", new ServerBooks.Request { id = smallId, title = "남의 제목", permission = (int)BookEditPermission.ReadOnly });
        Check(answer.error != "" && books.Find(smallId).title == "권한 책", "its book.meta (a title) too: " + answer.error);
        answer = await FakeBookOp(fake, "book.meta", new ServerBooks.Request { id = smallId, title = "권한 책", permission = (int)BookEditPermission.FullEdit });
        Check(answer.error != "" && books.Find(smallId).permission == BookEditPermission.ReadOnly, "and a new permission: " + answer.error);
        Check(bag.TrySetBookPermission(smallId, BookEditPermission.AddOnly, me, out _), "the owner sets 추가만 가능");
        await WaitFor(() => books.Find(smallId)?.permission == BookEditPermission.AddOnly, 5, "the permission on the server");
        answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = smallId, page = 0, text = page0 + " 가짜가 덧붙임" });
        Check(answer.error == "가지고 있지 않은 책입니다." && books.Find(smallId).pages[0] == page0,
            "추가만 가능, but the book is not in the fake's bag: its addition is refused (12.10.4; the permission rules are in BookSyncQA.Run)");
        Check(bag.FindBook(smallId).BookData.pages[0] == page0, "and the owner's screen book is not touched by the server's work");
        answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = smallId, page = 0, text = page0.Substring(0, 10) });
        Check(answer.error != "", "a removal is refused: " + answer.error);
        answer = await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = smallId, title = "가로채기" });
        Check(answer.error == "이미 있는 책입니다." && books.Find(smallId).IsAuthor(me), "book.new of an id that exists is refused");
        Give(ServerWorld.Instance.Ledger, BookFake, Blank, 1);
        Check(Blanks(ServerWorld.Instance.Ledger.Bag(BookFake)) == 1, "the fake's ledger gets a blank");
        answer = await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = "qa-fake-book", title = "가짜의 책" });
        Check(answer.error == "" && books.Find("qa-fake-book").IsAuthor(BookFake) && books.Find("qa-fake-book").authorName == "QA책가짜", "the fake's own new book (author: its account and character name)");
        string fakeText = Text(2000, 25);
        await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = "qa-fake-book", page = 0, text = fakeText.Substring(0, 1000), more = true });
        await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = "qa-fake-book", page = 0, text = fakeText.Substring(1000), append = true, more = true });
        answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = "qa-fake-book", page = 0, text = "넘침", append = true });
        Check(answer.error != "" && books.Find("qa-fake-book").pages[0] == "", "2,002 characters on a page are refused: " + answer.error);
        await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = "qa-fake-book", page = 0, text = fakeText.Substring(0, 1000), more = true });
        answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = "qa-fake-book", page = 0, text = fakeText.Substring(1000), append = true });
        Check(answer.error == "" && books.Find("qa-fake-book").pages[0] == fakeText, "2,000 characters in two parts are taken");

        Section("the owner picks up the fake's book and opens it");
        var look = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
        Vector3 spot = Ground(player.transform.position + look * 2.5f);
        answer = await FakeOp(fake, "p.drop", new SharedOpArgs { a = Blank.itemId, b = "qa-fake-book", n = 1, c = "-1", d = DropBlob.FormatPlace(spot, 0f) });
        Check(answer.error == "", "the fake drops its book (p.drop) " + answer.error);
        WorldDroppedItem view = null;
        await WaitFor(() => view = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v.NetId != 0 && v.Item == Blank), 5, "the fake's drop on the Unity screen");
        await StandBeside(player.GetComponent<PlayerMovement>(), view.transform.position);
        view.TryPickUp(inv, out _);
        await WaitFor(() => bag.FindBook("qa-fake-book") != null, 5, "the book in the owner's bag");
        var picked = bag.FindBook("qa-fake-book");
        Check(!picked.BookData.bodyLoaded && picked.BookData.title == "가짜의 책", "picked up without its pages");
        int fetches = WorldStateSync.BookFetches;
        var reader = player.GetComponent<BookReader>();
        OpenReader(reader, picked, bag);
        Check(WorldStateSync.BookFetches == fetches + 1 && Field<bool>(reader, "loading"), "opening it asks the server once (book.get)");
        Check(Field<TMP_Text>(reader, "hint").text == "불러오는 중…" && Field<TMP_InputField>(reader, "leftInput").readOnly, "while it comes the book says 불러오는 중… (read-only)");
        await Shot("B_loading.png", 0f); // the answer is a frame or two away here: the picture may already show the pages
        await WaitFor(() => !Field<bool>(reader, "loading"), 5, "the pages");
        var shown = Field<BookInstanceData>(reader, "draft");
        Check(SamePages(shown, books.Find("qa-fake-book")) && picked.BookData.bodyLoaded, "the reader shows the fake's 2,000 characters");
        await Shot("B_picked_up_open.png");
        Invoke(reader, "ForceClose");
        OpenReader(reader, picked, bag);
        Check(WorldStateSync.BookFetches == fetches + 1 && !Field<bool>(reader, "loading"), "opening it again asks nothing (the pages are here)");
        Invoke(reader, "ForceClose");

        Section("a library or shop copy");
        // What LibraryCatalog's CreateItem hands over for a loan: someone else's book, read-only, with a loan id.
        var copy = NewBook("도서관 사본", "acc-other", "다른작가", 2, 300, 26);
        copy.permission = BookEditPermission.ReadOnly; copy.libraryLoanId = "qa-loan"; copy.libraryWorkId = "qa-work";
        string copyId = Guid.NewGuid().ToString("N");
        // A copy the ledger has (12.10.6: the library's copies are made by the server; this one is put in the ledger here)
        // whose pages only this client has: book.new takes the ledger's details, its maker sends the pages.
        Check(ServerWorld.Instance.Ledger.Bag(me).TryAddBookInstance(Blank, copyId, LibraryCatalog.Bodyless(copy), out _), "a borrowed copy in the owner's ledger");
        ServerWorld.Instance.Ledger.Store(me);
        Check(bag.TryAddBookInstance(Blank, copyId, copy, out _), "and in its bag, with its pages");
        await WaitFor(() => SamePages(books.Find(copyId), copy), 5, "the copy on the server");
        var copyOnServer = books.Find(copyId);
        Check(copyOnServer.IsAuthor("acc-other") && copyOnServer.authorName == "다른작가" && copyOnServer.IsLibraryLoan && copyOnServer.permission == BookEditPermission.ReadOnly,
            "it is sent (book.new + book.page) with the ledger's details: its author, read-only, a loan");
        answer = await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = copyId, page = 0, text = "대여본 고침" });
        Check(answer.error != "" && books.Find(copyId).pages[0] == copy.pages[0], "a loan copy cannot be written on the server");
        await Awaitable.WaitForSecondsAsync(1.5f);
        Check(WorldStateSync.LastBookError == null, "the owner's client sent nothing the server refused");

        Section("the server restarts");
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        string ownerSaved = InventoryOf(store.Find(me).content), fakeSaved = InventoryOf(store.Find(BookFake).content);
        Check(ownerSaved.Contains(smallId) && !ownerSaved.Contains(small.pages[0].Substring(0, 30)) && !fakeSaved.Contains(oldChar.pages[0].Substring(0, 30)),
            "world.dat: the characters' contents have no pages");
        var saved = JsonUtility.FromJson<WorldSaveData>(SectionJson(store.WorldState));
        var savedLong = saved.books.Find(x => x.id == longId)?.book;
        Check(SamePages(savedLong, draft) && saved.books.Exists(x => x.id == "qa-old-char"), "world.dat: the world state has the books (" + saved.books.Count + ")");
        host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && CharacterSync.Active && WorldStateSync.World, 30, "back on the restarted server");
        books = ServerWorld.Instance.Books;
        Check(SamePages(books.Find(longId), draft) && books.Find(smallId).pages[0] == page0, "after the restart the server's books are the same");
        bag = InventoryManager.Instance.State;
        var again = bag.FindBook(smallId);
        Check(again != null && !again.BookData.bodyLoaded, "the owner's book came back without its pages");
        Check(!LibraryCatalog.CanPublish(again, me), "without its pages it cannot go to the library (LibraryDesk fetches them first)");
        fetches = WorldStateSync.BookFetches;
        reader = PlayerSpawner.Player.GetComponent<BookReader>();
        OpenReader(reader, again, bag);
        await WaitFor(() => !Field<bool>(reader, "loading"), 5, "the pages");
        Check(WorldStateSync.BookFetches == fetches + 1 && Field<BookInstanceData>(reader, "draft").pages[0] == page0,
            "opened, it shows the server's pages (the fake's refused addition is not there)");
        Check(LibraryCatalog.CanPublish(again, me), "with its pages it can be published in the library again");
        Invoke(reader, "ForceClose");
    }

    static int FindBlank(InventoryState bag)
    {
        for (int i = 0; i < bag.Capacity; i++) { var s = bag.GetSlot(i); if (!s.IsEmpty && s.Item.IsBook && !s.IsUniqueBook) return i; }
        throw new Exception("[BookSyncQA] FAIL: no blank book");
    }

    struct FakeBook { public BookInstanceData book; public int parts, bytes; }

    // book.get by the fake: the BookText parts before the OpResult.
    static async Task<FakeBook> FakeGet(QaPeer fake, string id)
    {
        Drain(fake);
        fake.Send(SyncProtocol.Event(worldId, 0, WorldSection.OpEvent, EventTarget.Server, Str2("book.get", JsonUtility.ToJson(new ServerBooks.Request { id = id }))));
        float end = Time.realtimeSinceStartup + 5f;
        var result = new FakeBook();
        var incoming = new Dictionary<string, BookInstanceData>();
        while (true)
        {
            fake.Pump();
            while (fake.inbox.Count > 0)
            {
                var m = fake.inbox[0];
                fake.inbox.RemoveAt(0);
                if (m.kind != ToClient.Event || m.id != worldId || m.sender != 0) continue;
                if (m.eventId == WorldSection.BookTextEvent) { result.parts++; result.bytes += m.blob.Length; using var r = SyncProtocol.Reader(m.blob); ServerBooks.ReadText(r, incoming); }
                if (m.eventId == WorldSection.OpResultEvent)
                {
                    using var r = SyncProtocol.Reader(m.blob);
                    Check(SyncProtocol.ReadStr(r) == "book.get" && SyncProtocol.ReadStr(r) == "", "book.get " + id + " answered");
                    incoming.TryGetValue(id, out result.book);
                    return result;
                }
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for book.get " + id);
            await Awaitable.NextFrameAsync();
        }
    }

    static async Task<OpAnswer> FakeBookOp(QaPeer fake, string kind, ServerBooks.Request q)
    {
        Drain(fake);
        fake.Send(SyncProtocol.Event(worldId, 0, WorldSection.OpEvent, EventTarget.Server, Str2(kind, JsonUtility.ToJson(q))));
        var answer = await WaitOp(fake);
        Check(answer.kind == kind, "OpResult for " + kind);
        return answer;
    }

    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static void OpenReader(BookReader reader, ItemStack stack, InventoryState bag)
    {
        int slot = -1;
        for (int i = 0; i < bag.Capacity; i++) if (bag.GetSlot(i) == stack) slot = i;
        typeof(BookReader).GetMethod("Open", Private).Invoke(reader, new object[] { stack, slot });
    }

    static void Invoke(BookReader reader, string method) => typeof(BookReader).GetMethod(method, Private).Invoke(reader, null);
    static T Field<T>(BookReader reader, string name) => (T)typeof(BookReader).GetField(name, Private).GetValue(reader);
    static void Field<T>(BookReader reader, string name, T value) => typeof(BookReader).GetField(name, Private).SetValue(reader, value);

    // ================= server process (BookSyncQA.RunProcess) =================

    static async Task BookProcessBody(string exe)
    {
        exePath = exe;
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        serverFolder = Path.Combine(Path.GetDirectoryName(exe), "qa-server");
        if (Directory.Exists(serverFolder)) Directory.Delete(serverFolder, true);
        Directory.CreateDirectory(serverFolder);
        serverPort = FreePort();
        settingsFile = Path.Combine(serverFolder, "settings.json");
        var settings = new ServerSettings { name = "BookQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        Section("a long book written on the remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60); // day 1 (Rejoin waits for a day past 0)
        await StartProcessServer("A");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins the server process (Client)");
        Check(await TitleFlow.CreateServerCharacter("QA책원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string me = GameSession.LocalPlayerId;
        var bag = InventoryManager.Instance.State;
        await WaitFor(() => WorldStateSync.Ledgers > 0, 10, "the first Ledger (a new character's start bag)");
        await Awaitable.WaitForSecondsAsync(2.5f);
        // A blank bought at the library (p.lib.buy, 12.10.6) with a note taken from the bank (p.cash.withdraw).
        Check(CashService.TryWithdraw(bag, LibraryCatalog.BlankPrice, 1, out string bought) && LibraryCatalog.Purchase(me, bag, null, out bought) && FindBlank(bag) >= 0,
            "a blank book bought at the library " + bought);
        var draft = NewBook("원격 긴 책", null, null, 20, 1500, 31);
        draft.isPublished = false;
        int results = WorldStateSync.Results, requests = WorldStateSync.BookRequests;
        Check(bag.TryPublishBook(FindBlank(bag), draft, me, GameSession.LocalPlayerName, out int slot, out string error), "20 pages x 1,500 characters written " + error);
        string id = bag.GetSlot(slot).InstanceId;
        await WaitFor(() => WorldStateSync.BookRequests > requests, 5, "the book going out");
        await WaitFor(() => WorldStateSync.Results >= results + (WorldStateSync.BookRequests - requests), 10, "the server's answers");
        Check(WorldStateSync.LastBookError == null, "the server took all " + (WorldStateSync.BookRequests - requests) + " book requests");
        await LeaveRemote(client);
        await StopProcessServer();
        var store = WorldStore.Open(worldsRoot, settings.world);
        string savedInventory = InventoryOf(store.Find(me).content);
        var saved = JsonUtility.FromJson<WorldSaveData>(SectionJson(store.WorldState));
        Check(savedInventory.Contains(id) && !savedInventory.Contains(draft.pages[0].Substring(0, 30)) && SamePages(saved.books?.Find(x => x.id == id)?.book, draft),
            "the server's world.dat: the character has the book without pages, the world's books have its 30,000 characters");

        Section("joining again and opening it");
        await StartProcessServer("B");
        await Rejoin(client);
        bag = InventoryManager.Instance.State;
        var book = bag.FindBook(id);
        Check(book != null && !book.BookData.bodyLoaded && book.BookData.title == "원격 긴 책", "the book came back without its pages");
        var reader = PlayerSpawner.Player.GetComponent<BookReader>();
        int fetches = WorldStateSync.BookFetches;
        OpenReader(reader, book, bag);
        Check(WorldStateSync.BookFetches == fetches + 1, "opening it asks the server (book.get)");
        await WaitFor(() => !Field<bool>(reader, "loading"), 10, "the pages");
        Check(SamePages(Field<BookInstanceData>(reader, "draft"), draft) && book.BookData.bodyLoaded, "the same 30,000 characters are shown");
        Invoke(reader, "Turn", 2);
        await Shot("B_remote_reopened.png");
        Invoke(reader, "ForceClose");
        OpenReader(reader, book, bag);
        Check(WorldStateSync.BookFetches == fetches + 1 && !Field<bool>(reader, "loading"), "opened again: nothing is asked (only the first opening fetches)");
        Invoke(reader, "ForceClose");
        await LeaveRemote(client);
        await StopProcessServer();
    }

    static void Invoke(BookReader reader, string method, int delta) => typeof(BookReader).GetMethod(method, Private).Invoke(reader, new object[] { delta });
}
