using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

// Play mode parts of EconomySyncQA (12.9): RunPlay (in-app server) and RunProcess (a built server process).
public static partial class EconomySyncQA
{
    const string ModeKey = "EconomySyncQA.mode", ExeKey = "EconomySyncQA.exe";
    const int Slot = 4;
    const string PcMark = "qa:pc-only-econ";
    const int Properties = ServerWorld.FirstShared + 2, Companies = ServerWorld.FirstShared + 1, Plots = ServerWorld.FirstShared + 3,
        Reports = ServerWorld.FirstShared + 4, Sns = ServerWorld.FirstShared + 5, Stocks = ServerWorld.FirstShared;

    static string playWorld, worldFile;
    static byte[] pcBackup;
    static Process serverProcess;
    static ulong worldId;

    public static void RunPlay() => StartPlay("play");

    public static void RunProcess() => BuildAndPlay("EconomySyncQA", "process");

    // Builds the server player into Builds/<name>/ and enters play mode in <mode> with it.
    internal static void BuildAndPlay(string name, string mode)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/" + name));
        string exe = Path.Combine(dir, name + ".exe");
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // one scene is enough for a server
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[" + name + "] RESULT " + mode + " FAILED: build " + report.summary.result);
            EditorApplication.Exit(1);
            return;
        }
        SessionState.SetString(ExeKey, exe);
        StartPlay(mode);
    }

    internal static void StartPlay(string mode)
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetString(ModeKey, mode);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        string mode = SessionState.GetString(ModeKey, "");
        if (mode == "" || change != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetString(ModeKey, "");
        Drive(mode);
    }

    static async void Drive(string mode)
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1), code = 0;
        tag = mode.StartsWith("ledger", StringComparison.Ordinal) ? "LedgerQA" : mode.StartsWith("book", StringComparison.Ordinal) ? "BookSyncQA"
            : mode.StartsWith("inv", StringComparison.Ordinal) ? "InventoryLedgerQA" : mode.StartsWith("item", StringComparison.Ordinal) ? "WorldItemQA" : "EconomySyncQA";
        Begin();
        try
        {
            string exe = SessionState.GetString(ExeKey, "");
            if (mode == "play") await PlayBody();
            else if (mode == "ledger-play") await LedgerPlayBody();
            else if (mode == "ledger-process") await LedgerProcessBody(exe);
            else if (mode == "book-play") await BookPlayBody();
            else if (mode == "book-process") await BookProcessBody(exe);
            else if (mode == "inv-play") await InventoryPlayBody();
            else if (mode == "inv-process") await InventoryProcessBody(exe);
            else if (mode == "item-play") await ItemPlayBody();
            else if (mode == "item-process") await ItemProcessBody(exe);
            else await ProcessBody(exe);
            Report("RESULT " + mode + " passed");
        }
        catch (Exception e) { Report("RESULT " + mode + " FAILED: " + e.Message + "\n" + e.StackTrace, true); code = 1; }
        try
        {
            NightActivity.Select(NightActivityKind.Sleep);
            WorldStateSync.HoldSections = false;
            if (SyncHost.Instance) { playWorld ??= SyncHost.Instance.Server.World.Folder; SyncHost.Instance.Stop(); }
            if (SyncClient.Instance) SyncClient.Instance.Disconnect();
            if (playWorld != null && Directory.Exists(playWorld)) Directory.Delete(playWorld, true); // the QA's world, not a player's
            if (serverProcess != null && !serverProcess.HasExited) serverProcess.Kill(); // only the process this QA started
            if (worldFile != null) { if (pcBackup != null) File.WriteAllBytes(worldFile, pcBackup); else if (File.Exists(worldFile)) File.Delete(worldFile); }
        }
        catch (Exception e) { Debug.LogWarning("[" + tag + "] cleanup: " + e.Message); }
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    // ================= in-app server (RunPlay) =================

    static async Task PlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        string pcHash = MarkPcWorld();
        var client = SyncClient.Instance;
        var registry = PropertyRegistry.Instance;

        Section("in-app server");
        var settings = new ServerSettings { name = "EconQA", maxPlayers = 4, port = 0 };
        var host = SyncHost.StartInApp(settings, true);
        playWorld = host.Server.World.Folder;
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA경제", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        Check(GameSession.Mode == SessionMode.Host && !GameSession.RemoteServer && ServerWorld.Instance, "the in-app server's owner stays Host; ServerWorld runs here");
        Check(RawOwner(PcMark) == null, "the server's shared economy is not this PC's world (the PC-only record is not there)");
        await Awaitable.WaitForSecondsAsync(1.2f);
        Note("new world, section sizes (gzip bytes): " + Sizes(ServerWorld.Instance.SectionBytes));
        string map = SceneLoadManager.CurrentMap.path;
        string me = GameSession.LocalPlayerId;

        var fake = await JoinFake(host.Port, map, "qa-econ-fake", "QA가짜");
        Check(fake.number != 0, "the fake player is in with its character");

        Section("the fake's requests");
        var r = await FakeOp(fake, "prop.reg", new SharedOpArgs { a = "qa:house-1", b = Acc(fake) });
        Check(r.error == "" && r.sections.Contains(Properties), "prop.reg: OpResult ok, and the properties section came before it");
        Check(registry.GetOwner("qa:house-1") == "QA가짜", "the Unity managers have it; the owner shows as the fake's character name (" + registry.GetOwner("qa:house-1") + ")");
        r = await FakeOp(fake, "p.bank", new SharedOpArgs { n = LandPlot.DefaultPrice, a = "book.royalty" }); // the plot is paid from the fake's ledger (12.10.1); deposit is gone (12.10.4)
        Check(r.error == "", "the fake deposits the plot's price");
        r = await FakeOp(fake, "land.buy", new SharedOpArgs { a = "qa-plot-1" });
        Check(r.error == "" && r.sections.Contains(Plots) && LandPlotManager.Instance.GetOwner("qa-plot-1") == Acc(fake), "land.buy: the plot is the fake's, the plots section first");
        var company = new CompanyData { companyId = "qa_company_fake", displayName = "가짜회사", sector = "qa" };
        r = await FakeOp(fake, "company.add", new SharedOpArgs { a = JsonUtility.ToJson(company), b = Acc(fake) });
        Check(r.error == "" && r.sections.Contains(Companies) && CompanyManager.Instance.Market.Get("qa_company_fake") != null, "company.add: the company exists, the companies section first");
        r = await FakeOp(fake, "sns.post", new SharedOpArgs { a = "가짜가 쓴 글", b = Acc(fake), c = "사칭이름" });
        var post = SnsManager.Instance.Feed.Posts[0];
        Check(r.error == "" && r.sections.Contains(Sns) && post.text == "가짜가 쓴 글" && post.authorName == "QA가짜",
            "sns.post: the post is there under the server's character name, not the one the client wrote (" + post.authorName + ")");
        r = await FakeOp(fake, "report.sub", new SharedOpArgs { a = me, b = "qa-evidence", c = Acc(fake), n = 1000, d = ((int)CrimeType.Theft).ToString() });
        Check(r.error == "" && r.sections.Contains(Reports) && ReportManager.Instance.Service.Reports.Count > 0 && Last(ReportManager.Instance.Service.Reports).reporterId == Acc(fake),
            "report.sub: the report is filed by the fake, the reports section first");
        r = await FakeOp(fake, "prop.reg", new SharedOpArgs { a = "vehicle:qa-fake-car", b = Acc(fake) });
        Check(r.error == "" && registry.IsVehicleLocked("vehicle:qa-fake-car"), "a car registered by the fake is locked");
        r = await FakeOp(fake, "prop.lock", new SharedOpArgs { a = "vehicle:qa-fake-car", b = Acc(fake), f = false });
        Check(r.error == "" && r.sections.Contains(Properties) && !registry.IsVehicleLocked("vehicle:qa-fake-car"), "prop.lock false unlocks it");
        r = await FakeOp(fake, "prop.lock", new SharedOpArgs { a = "vehicle:qa-fake-car", b = Acc(fake), f = true });
        Check(r.error == "" && registry.IsVehicleLocked("vehicle:qa-fake-car"), "prop.lock true locks it again");

        Section("one house, two owners");
        int results = WorldStateSync.Results;
        fake.Send(OpFrame("prop.reg", new SharedOpArgs { a = "qa:house-2", b = Acc(fake) }));
        Check(registry.TryRegister("qa:house-2", GameSession.LocalPlayerName), "the Unity owner asks for the same house in the same frame");
        var race = await WaitOp(fake);
        await WaitFor(() => WorldStateSync.Results > results, 5, "the Unity owner's answer");
        bool fakeWon = race.error == "", unityWon = WorldStateSync.LastMessage == "소유권을 등록했습니다.";
        Check(fakeWon != unityWon, "exactly one got it (" + (fakeWon ? "the fake" : "the Unity owner") + "); the other: " + (fakeWon ? WorldStateSync.LastMessage : race.error));
        Check(RawOwner("qa:house-2") == (fakeWon ? Acc(fake) : me), "and the house is the winner's");

        Section("refused requests");
        r = await FakeOp(fake, "prop.reg", new SharedOpArgs { a = "qa:house-3", b = me });
        Check(r.error == "요청한 사람이 올바르지 않아 거부했습니다." && RawOwner("qa:house-3") == null, "an op naming someone else as the actor is refused");
        r = await FakeOp(fake, "sns.like", new SharedOpArgs { a = post.id, b = me });
        Check(r.error != "", "a like in someone else's name is refused");
        r = await FakeOp(fake, "veh.put", new SharedOpArgs { a = "x" });
        Check(r.error == "처리할 수 없는 요청입니다." && ServerWorld.Instance.Refused.Contains("veh.put"), "veh.* is refused");
        r = await FakeOp(fake, "qa.unknown", new SharedOpArgs());
        Check(r.error == "처리할 수 없는 요청입니다." && ServerWorld.Instance.Refused.Contains("qa.unknown"), "an unknown kind is refused");

        Section("the Unity owner's requests");
        results = WorldStateSync.Results;
        Drain(fake);
        Check(SnsManager.Instance.Post("Unity가 쓴 글") == SnsPostResult.Success, "the Unity owner posts (sent as a request)");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the post");
        Check(WorldStateSync.LastMessage == "게시했습니다." && SnsManager.Instance.Feed.Posts[0].text == "Unity가 쓴 글", "it went through the server event path and the done message shows (" + WorldStateSync.LastMessage + ")");
        var snsAtFake = await WaitMsg(fake, m => m.kind == ToClient.State && m.id == worldId && m.component == Sns && SectionJson(m.blob).Contains("Unity가 쓴 글"), "the post at the fake");
        Check(snsAtFake != null, "the fake sees the Unity owner's post in the sns section");
        int refusedBefore = ServerWorld.Instance.Refused.Count;
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        var bag = InventoryManager.Instance.State;
        int carSlot = -1;
        for (int i = 0; i < InventoryState.HotbarSize; i++) if (bag.GetSlot(i).Item && bag.GetSlot(i).Item.IsCar) { carSlot = i; break; }
        Check(carSlot >= 0, "a car on the hotbar");
        bag.SelectHotbar(carSlot);
        Check(WorldDroppedItem.TryPlaceVehicle(inv, out string placeError), "the Unity owner places a car " + placeError);
        WorldDroppedItem myCar = null;
        await WaitFor(() => (myCar = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId != 0 && v.IsPlacedVehicle)) != null, 5, "the placed car");
        string carKey = myCar.VehicleOwnershipKey;
        results = WorldStateSync.Results;
        Check(myCar.TryRegisterVehicle(GameSession.LocalPlayerName), "registers it");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the register answer");
        Check(RawOwner(carKey) == me && registry.IsVehicleLocked(carKey), "the car is the owner's and locked (" + carKey + ")");
        results = WorldStateSync.Results;
        Check(myCar.TrySetVehicleLocked(GameSession.LocalPlayerName, false), "unlocks it");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the lock answer");
        Check(!registry.IsVehicleLocked(carKey), "the lock state changed through the server");
        Check(ServerWorld.Instance.Refused.Count == refusedBefore,
            "placing and managing a car sent no veh.* to the server (the entity path came first)");

        Section("night activity");
        var stats = player.GetComponent<PlayerStats>();
        var bank = BankManager.Instance;
        int applied = 0;
        var happened = NightActivityKind.Contract;
        Action<NightActivityKind> count = k => { applied++; happened = k; };
        NightActivity.Applied += count;
        try
        {
            Check(!NightActivity.IsAvailable(NightActivityKind.Contract) && !NightActivity.Select(NightActivityKind.Contract) && NightActivity.Selected == NightActivityKind.Sleep,
                "살인 청부 cannot be picked before it is unlocked; the pick stays 잠자기");
            stats.stamina = 100f;
            long money = bank.Money;
            await PassNight(host, () => applied == 1);
            Check(happened == NightActivityKind.Sleep && bank.Money == money && Mathf.Approximately(stats.stamina, 100f), "a night of 잠자기: applied once, money and energy unchanged");
            Check(NightActivity.Select(NightActivityKind.NightJob), "the QA picks 야간 알바");
            money = bank.Money;
            await PassNight(host, () => applied == 2);
            await Awaitable.WaitForSecondsAsync(1f);
            Check(applied == 2 && happened == NightActivityKind.NightJob && bank.Money == money + NightActivity.NightJobPay && Mathf.Approximately(stats.stamina, 100f + NightActivity.NightJobEnergy),
                "a night of 야간 알바: once, money +" + NightActivity.NightJobPay + ", energy " + NightActivity.NightJobEnergy + " (" + bank.Money + ", " + stats.stamina + ")");
            NightActivity.Select(NightActivityKind.Sleep);
        }
        finally { NightActivity.Applied -= count; }

        Section("leaving and this PC's world");
        WorldSaveRunner.SaveNow();
        Check(Hash(worldFile) == pcHash, "while on the server this PC's world.json is untouched (even after SaveNow)");
        string worldName = host.Server.World.Id;
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        await Awaitable.NextFrameAsync();
        Check(RawOwner(PcMark) == "PC의주인" && RawOwner("qa:house-1") == null && CompanyManager.Instance.Market.Get("qa_company_fake") == null,
            "this PC's own world is back (its record is there, the server's are not)");
        Check(GameSession.Mode == SessionMode.Host && !(GameClock.Current is SimpleGameClock c && c.Paused) && GameTime.NightSkipStart == 0 && GameTime.NightSkipEnd == 6, "Host, the clock runs itself, the night is 0-6");
        Check(Hash(worldFile) == pcHash, "world.json has the same hash as before the server");

        Section("the same world again");
        pcHash = Hash(worldFile); // off the server the PC's own clock ran: compare this stay on its own
        settings.world = worldName;
        host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && CharacterSync.Active && WorldStateSync.World, 15, "back on the reopened world");
        await WaitFor(() => registry.GetOwner("qa:house-1") == "QA가짜", 5, "the names section");
        Check(true, "ownership is back, shown with the character name of a player who is not connected (" + registry.GetOwner("qa:house-1") + ")");
        Check(CompanyManager.Instance.Market.Get("qa_company_fake") != null && SnsManager.Instance.Feed.Posts.Count >= 2 && LandPlotManager.Instance.GetOwner("qa-plot-1") == Acc(fake),
            "the company, the posts and the plot are back");

        Section("sizes and traffic");
        var watcher = await JoinFake(host.Port, map, "qa-econ-fake", "QA가짜");
        Drain(watcher);
        await Awaitable.WaitForSecondsAsync(10f);
        float idle = Received(watcher) / 10f;
        Note("one client idle for 10 s receives " + idle.ToString("F1") + " bytes/s (clock every 5 s, sections only on change)");
        int startDay = GameClock.Current.Now.absoluteDay;
        host.Server.AdvanceClock((long)(30 * Day * GameServer.RealSecondsPerGameHour * 1000 / 60));
        await WaitFor(() => GameClock.Current.Now.absoluteDay >= startDay + 30, 60, "30 game days");
        Drain(watcher);
        await Awaitable.WaitForSecondsAsync(1.5f);
        Note("after 30 game days, section sizes (gzip bytes): " + Sizes(ServerWorld.Instance.SectionBytes) + "; SNS posts " + SnsManager.Instance.Feed.Posts.Count
             + ", reports " + ReportManager.Instance.Service.Reports.Count);
        int max = 0;
        foreach (int s in ServerWorld.Instance.SectionBytes) max = Math.Max(max, s);
        Check(max <= SyncProtocol.MaxServerBlob, "every section stays under 60KB after 30 game days (largest " + max + ")");
        double now = host.Server.World.WorldMinutes, tenOClock = Math.Floor(now / Day) * Day + 10 * 60 - .5;
        if (tenOClock <= now) tenOClock += Day;
        host.Server.AdvanceClock((long)((tenOClock - now) * GameServer.RealSecondsPerGameHour * 1000 / 60)); // just before 10:00 (market hours)
        await Awaitable.WaitForSecondsAsync(1.5f);
        Drain(watcher);
        host.Server.AdvanceClock((long)(60 * GameServer.RealSecondsPerGameHour * 1000 / 60)); // one market hour at once: the 10:00 price tick
        await Awaitable.WaitForSecondsAsync(1.5f);
        long hour = Received(watcher);
        Note("one market hour (the price tick) sends " + hour + " bytes to one client; a game hour is " + GameServer.RealSecondsPerGameHour
             + " s, so about " + (hour / GameServer.RealSecondsPerGameHour + idle).ToString("F0") + " bytes/s in market hours with the clock");
        host.Stop();
        await WaitFor(() => !client.Connected, 5, "leaving at the end");
        Check(Hash(worldFile) == pcHash, "world.json is still unchanged at the end");
    }

    // ================= server process (RunProcess) =================

    static string serverFolder, settingsFile, serverLog, exePath;
    static int serverPort;

    static async Task ProcessBody(string exe)
    {
        exePath = exe;
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        string pcHash = MarkPcWorld();
        var client = SyncClient.Instance;
        var registry = PropertyRegistry.Instance;
        serverFolder = Path.Combine(Path.GetDirectoryName(exe), "qa-server");
        if (Directory.Exists(serverFolder)) Directory.Delete(serverFolder, true);
        Directory.CreateDirectory(serverFolder);
        serverPort = FreePort();
        settingsFile = Path.Combine(serverFolder, "settings.json");
        var settings = new ServerSettings { name = "EconQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        // ---- run A: day 1 10:30, -nightskip 2-5 ----
        Section("remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60 + 30);
        await StartProcessServer("A");
        pcHash = Hash(worldFile);
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null, "the Unity client joins the server process");
        Check(GameSession.Mode == SessionMode.Client && GameSession.RemoteServer, "on another process's server the client turns Client (RemoteServer)");
        Check(await TitleFlow.CreateServerCharacter("QA원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path;
        var fake = await JoinFake(serverPort, map, "qa-econ-remote-fake", "QA가짜원격");

        Section("the world clock");
        Check(GameTime.NightSkipStart == 2 && GameTime.NightSkipEnd == 5, "the Unity clock takes the server's night 2-5 (from -nightskip)");
        await WaitFor(() => fake.Has(m => m.kind == ToClient.State && m.id == worldId && m.component == ServerWorld.ClockSection), 8, "a clock section at the fake");
        await WaitFor(() => GameClock.Current.Now.TotalMinutes == ClockOf(LastClock(fake)), 3, "the Unity clock on the server's minute");
        Check(true, "the screen clock shows the server's world minute (" + GameClock.Current.Now + ")");
        for (int i = 0; i < 3; i++)
        {
            await Awaitable.WaitForSecondsAsync(2.5f);
            Check(GameClock.Current.Now.TotalMinutes == ClockOf(LastClock(fake)), "it does not run by itself: " + (i + 1) * 2.5f + " s later it still equals the server's latest minute (" + GameClock.Current.Now + ")");
        }
        await Shot("E_remote_clock.png");

        Section("someone else's house");
        var deeds = new List<ItemData>(Array.FindAll(Resources.LoadAll<ItemData>("Inventory/Deeds"), d => d && d.IsDeed));
        deeds.Sort((x, y) => string.CompareOrdinal(x.itemId, y.itemId));
        var deed = deeds.Count > 1 ? deeds[deeds.Count - 1] : null; // the last: the starting deed goes to the first free one
        Check(deed, "a deed to look at (" + (deed ? deed.propertyId : "none") + ")");
        var r = await FakeOp(fake, "prop.reg", new SharedOpArgs { a = deed.propertyId, b = Acc(fake) });
        Check(r.error == "", "the fake registered " + deed.propertyName);
        await WaitFor(() => registry.GetOwner(deed.propertyId) == "QA가짜원격", 5, "the owner on the Unity client");
        Check(true, "the Unity client shows the owner as the fake's character name");
        var press = typeof(PropertyUseController).GetMethod("Press", BindingFlags.NonPublic | BindingFlags.Instance);
        press.Invoke(PlayerSpawner.Player.GetComponent<PropertyUseController>(), new object[] { registry, deed, deed.propertyId });
        await Shot("E_owner_name.png");

        Section("someone else's post");
        r = await FakeOp(fake, "sns.post", new SharedOpArgs { a = "원격에서 보이는 남의 글", b = Acc(fake), c = "x" });
        await WaitFor(() => SnsManager.Instance.Feed.Posts.Count > 0 && SnsManager.Instance.Feed.Posts[0].text == "원격에서 보이는 남의 글", 5, "the post on the Unity client");
        Check(SnsManager.Instance.Feed.Posts[0].authorName == "QA가짜원격", "the fake's post shows on the Unity client under its character name");
        PhoneManager.Instance.OpenPhone();
        PhoneManager.Instance.OpenApp(nameof(SNSApplication));
        await Shot("E_sns_other_post.png", 1f);
        PhoneManager.Instance.ClosePhone();

        Section("land race lost");
        var plot = new GameObject("QA plot").AddComponent<LandPlot>();
        typeof(LandPlot).GetField("plotId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(plot, "qa-plot-remote");
        long price = plot.Price, money = BankManager.Instance.Money;
        if (money < price) await Deposit(price - money + 1000); // the server's ledger must have it too (12.10.1)
        money = BankManager.Instance.Money;
        r = await FakeOp(fake, "p.bank", new SharedOpArgs { n = price, a = "book.royalty" });
        Check(r.error == "", "the fake gets the plot's price (a trusted royalty; deposit is gone in 12.10.4)");
        WorldStateSync.HoldSections = true; // the client has not heard of the fake's purchase yet: a real race
        r = await FakeOp(fake, "land.buy", new SharedOpArgs { a = "qa-plot-remote" });
        Check(r.error == "" && LandPlotManager.Instance.GetState("qa-plot-remote") == LandPlotState.Available, "the fake bought the plot first; this client does not know yet");
        int results = WorldStateSync.Results;
        Check(LandPlotManager.Instance.TryPurchase(plot) == LandPlotResult.Ok && BankManager.Instance.Money == money - price, "the Unity client pays and asks for it");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer");
        WorldStateSync.HoldSections = false;
        Check(WorldStateSync.LastMessage == "이미 다른 플레이어가 구매한 땅입니다. 결제 금액은 돌려드렸습니다." && BankManager.Instance.Money == money,
            "it lost: the message says so and the bank money is back (" + BankManager.Instance.Money + ")");
        await WaitFor(() => LandPlotManager.Instance.GetOwner("qa-plot-remote") == Acc(fake), 3, "the plot section");
        Object.Destroy(plot.gameObject);
        await Shot("E_land_lost.png");

        Section("a car the fake locked");
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        var driver = player.GetComponent<PlayerVehicle>();
        var cam = Camera.main.transform;
        var ahead = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 carSpot = Ground(player.transform.position + ahead * 4f);
        // 12.10.5: the server places a car of the fake's ledger (an unreadable layout brings the ledger's bag back).
        var fakeBag = new InventoryState();
        Check(InventorySnapshot.Import(fakeBag, (await InvOp(fake, "p.inv.layout", new SharedOpArgs { b = "" })).inventory), "the fake's ledger bag");
        string carId = null;
        foreach (var s in InventoryLedgerRules.Stacks(fakeBag)) if (s.Item.IsCar) { carId = s.InstanceId; break; }
        Drain(fake);
        Check(carId != null && (await FakeOp(fake, "p.vehicle.place", new SharedOpArgs { a = carId, d = DropBlob.FormatPlace(carSpot, Quaternion.LookRotation(ahead).eulerAngles.y) })).error == "",
            "the fake places a car of its ledger");
        ulong car = 0;
        await WaitFor(() => (car = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId != 0 && v.IsPlacedVehicle && v.VehicleOwnershipKey == "vehicle:" + carId)?.NetId ?? 0) != 0, 5, "the fake's car");
        WorldDroppedItem view = null;
        await WaitFor(() => (view = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId == car)) != null && !view.IsOccupied, 5, "the fake's car on the Unity screen");
        r = await FakeOp(fake, "prop.reg", new SharedOpArgs { a = view.VehicleOwnershipKey, b = Acc(fake) });
        await WaitFor(() => registry.IsVehicleLocked(view.VehicleOwnershipKey), 5, "the lock on the Unity client");
        Check(r.error == "", "the fake registered (and so locked) its car " + view.VehicleOwnershipKey);
        await StandBeside(player, view.transform.position);
        Drain(fake);
        Check(!view.TryMount(inv) && inv.StatusMessage.Contains("잠긴 탈것") && !driver.IsRiding, "the Unity client cannot get in: the normal path stops at the lock (" + inv.StatusMessage + ")");
        await Awaitable.WaitForSecondsAsync(.5f);
        Check(!fake.Has(m => m.kind == ToClient.Owner && m.id == car), "and no Claim went to the server");

        Section("leaving");
        await LeaveRemote(client);
        Check(GameSession.Mode == SessionMode.Host && !GameSession.RemoteServer && GameTime.NightSkipStart == 0 && GameTime.NightSkipEnd == 6, "off the server: Host again, the night back to 0-6");
        Check(RawOwner(PcMark) == "PC의주인" && RawOwner(deed.propertyId) == null && Hash(worldFile) == pcHash, "this PC's world is back and world.json is unchanged");
        await StopProcessServer();

        // ---- run B: day 2 09:59, prices tick at 10:00 ----
        Section("prices");
        SetWorldClock(worldsRoot, settings.world, 2 * Day + 9 * 60 + 59.2);
        await StartProcessServer("B");
        pcHash = Hash(worldFile); // off a server the PC's own clock runs and its world may be saved: compare each stay
        await Rejoin(client);
        fake = await JoinFake(serverPort, map, "qa-econ-remote-fake", "QA가짜원격");
        var first = await WaitMsg(fake, m => m.kind == ToClient.State && m.id == worldId && m.component == Stocks, "the stocks section", 8f);
        string before = SectionJson(first.blob);
        var moved = await WaitMsg(fake, m => m.kind == ToClient.State && m.id == worldId && m.component == Stocks && SectionJson(m.blob) != before, "the stocks after 10:00", 20f);
        var serverStocks = JsonUtility.FromJson<WorldSaveData>(SectionJson(moved.blob)).stocks.states;
        await Awaitable.WaitForSecondsAsync(1f);
        int same = 0;
        foreach (var s in serverStocks) if (StockMarketManager.Instance.Exchange.GetPrice(s.stockId) == s.price) same++;
        Check(serverStocks.Count > 0 && same == serverStocks.Count && GameClock.Current.Now.hour >= 10, "after the 10:00 tick on the server, every price on the Unity client equals the server's (" + same + "/" + serverStocks.Count + ")");
        Check(JsonUtility.FromJson<WorldSaveData>(before).stocks.states.Exists(s => s.price != StockMarketManager.Instance.Exchange.GetPrice(s.stockId)), "and the prices did move");
        await LeaveRemote(client);
        Check(Hash(worldFile) == pcHash, "world.json unchanged through run B");
        await StopProcessServer();

        // ---- run C: day 3 01:57 with the night 2-5 ----
        Section("night on a remote server");
        SetWorldClock(worldsRoot, settings.world, 3 * Day + 1 * 60 + 57);
        await StartProcessServer("C");
        pcHash = Hash(worldFile);
        await Rejoin(client);
        var stats = PlayerSpawner.Player.GetComponent<PlayerStats>();
        var bank = BankManager.Instance;
        Check(GameClock.Current.Now.hour == 1, "the client is at " + GameClock.Current.Now + " before the night");
        Check(NightActivity.Select(NightActivityKind.NightJob), "the QA picks 야간 알바");
        stats.stamina = 100f;
        long cash = bank.Money;
        int nights = WorldStateSync.NightsApplied;
        await WaitFor(() => GameClock.Current.Now.hour == 5, 40, "the server's clock over the night 2-5");
        Check(GameClock.Current.Now.minute <= 1 && GameClock.Current.Now.absoluteDay == 3, "the Unity clock went 01:59 → 05:00 (" + GameClock.Current.Now + ")");
        await Awaitable.WaitForSecondsAsync(6f); // another clock section or two
        Check(WorldStateSync.NightsApplied == nights + 1 && bank.Money == cash + NightActivity.NightJobPay && Mathf.Approximately(stats.stamina, 100f + NightActivity.NightJobEnergy),
            "the night activity was applied once (money " + (bank.Money - cash) + ", energy " + stats.stamina + ")");
        NightActivity.Select(NightActivityKind.Sleep);
        await LeaveRemote(client);
        Check(GameTime.NightSkipStart == 0 && GameTime.NightSkipEnd == 6 && GameSession.Mode == SessionMode.Host, "leaving puts the night back to 0-6");
        Check(Hash(worldFile) == pcHash, "world.json unchanged through run C");
        await StopProcessServer();
    }

    // Money on this client that the server is told of (a royalty, the trusted p.bank left after 12.10.4), and its answer.
    static async Task Deposit(long amount)
    {
        int results = WorldStateSync.Results;
        Check(BankManager.Instance.AddMoney(amount, MoneyChangeReason.BookRoyalty), "money of " + amount);
        SharedWriteGate.SendPersonal("p.bank", new SharedOpArgs { n = amount, a = "book.royalty" });
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the deposit");
    }

    static void SetWorldClock(string worldsRoot, string id, double minutes)
    {
        var store = WorldStore.Open(worldsRoot, id);
        store.WorldMinutes = minutes;
        store.Save();
    }

    static async Task StartProcessServer(string run)
    {
        serverLog = Path.Combine(serverFolder, "server-" + run + ".log");
        serverProcess = Process.Start(new ProcessStartInfo(exePath,
            $"-batchmode -nographics -gameserver {serverPort} -maxplayers 4 -bind 127.0.0.1 -serversettings \"{settingsFile}\" -nightskip 2-5 -logFile \"{serverLog}\"")
        { UseShellExecute = false, CreateNoWindow = true });
        string running = Path.Combine(serverFolder, "running.json");
        await WaitFor(() => File.Exists(running) || serverProcess.HasExited, 60, "the server process " + run);
        Check(!serverProcess.HasExited, "server process " + run + " runs (pid " + serverProcess.Id + ")");
    }

    // Saves and quits through stop.request, as 끄기 does.
    static async Task StopProcessServer()
    {
        File.WriteAllText(Path.Combine(serverFolder, LocalServerProcess.StopRequestName), "");
        await WaitFor(() => serverProcess.HasExited, 15, "the server process to save and quit");
        serverProcess = null;
    }

    static async Task Rejoin(SyncClient client)
    {
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins again (Client)");
        await WaitFor(() => CharacterSync.Active && WorldStateSync.World && GameClock.Current.Now.absoluteDay > 0, 15, "the character and the world again");
        await Awaitable.WaitForSecondsAsync(.5f);
    }

    static async Task LeaveRemote(SyncClient client)
    {
        CharacterSync.SaveNow();
        client.Disconnect();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        await Awaitable.NextFrameAsync();
    }

    // ================= helpers =================

    // This PC's own world with a record only it has; returns the hash of its world.json.
    static string MarkPcWorld()
    {
        Check(WorldSave.Baseline != null, "the fresh world was captured at startup (WorldSaveRunner)");
        worldFile = WorldSave.Path;
        pcBackup = File.Exists(worldFile) ? File.ReadAllBytes(worldFile) : null;
        WorldSave.RestoreBaseline();
        if (pcBackup != null) WorldSave.LoadFromDisk();
        PropertyRegistry.Instance.SeedOwner(PcMark, "PC의주인");
        Check(WorldSave.SaveToDisk() && RawOwner(PcMark) == "PC의주인", "this PC's own world.json has a record only it has");
        return Hash(worldFile);
    }

    static string RawOwner(string id)
    {
        var found = PropertyRegistry.Instance.CaptureState().Find(p => p.id == id);
        return found == null || string.IsNullOrEmpty(found.owner) ? null : found.owner;
    }

    static readonly Dictionary<QaPeer, string> fakeAccounts = new Dictionary<QaPeer, string>();
    static string Acc(QaPeer fake) => fakeAccounts[fake];

    static async Task<QaPeer> JoinFake(int port, string map, string account, string name)
    {
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", port);
        var fake = new QaPeer(socket);
        fakeAccounts[fake] = account;
        fake.Send(SyncProtocol.Hello(account, "fake", map));
        fake.number = (await WaitMsg(fake, m => m.kind == ToClient.Welcome, "fake welcome")).number;
        if (!(await WaitMsg(fake, m => m.kind == ToClient.Character, "fake character")).has)
        {
            fake.Send(SyncProtocol.CharacterCreate(name, "female"));
            Check((await WaitMsg(fake, m => m.kind == ToClient.CharacterResult, "fake character result")).result == CharacterResult.Ok, "the fake made its character " + name);
        }
        var spawn = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.key == WorldKey, "the world entity at the fake");
        Check(spawn.map == AllMaps && spawn.owner == 0, "the fake got the world entity (map *, owner 0)");
        worldId = spawn.id;
        await WaitMsg(fake, m => m.kind == ToClient.State && m.id == worldId && m.component == ServerWorld.NamesSection, "the names section at the fake");
        return fake;
    }

    static byte[] OpFrame(string kind, SharedOpArgs args) =>
        SyncProtocol.Event(worldId, 0, WorldSection.OpEvent, EventTarget.Server, Str2(kind, JsonUtility.ToJson(args)));

    static byte[] Str2(string a, string b)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        SyncProtocol.WriteStr(w, a); SyncProtocol.WriteStr(w, b);
        w.Flush();
        return stream.ToArray();
    }

    struct OpAnswer { public string kind, error; public List<int> sections; }

    static async Task<OpAnswer> FakeOp(QaPeer fake, string kind, SharedOpArgs args)
    {
        Drain(fake);
        fake.Send(OpFrame(kind, args));
        var answer = await WaitOp(fake);
        Check(answer.kind == kind, "OpResult for " + kind);
        return answer;
    }

    // Waits for an OpResult; the sections are the world States that arrived before it, in order.
    static async Task<OpAnswer> WaitOp(QaPeer fake)
    {
        float end = Time.realtimeSinceStartup + 5f;
        while (true)
        {
            fake.Pump();
            int at = fake.inbox.FindIndex(m => m.kind == ToClient.Event && m.id == worldId && m.eventId == WorldSection.OpResultEvent && m.sender == 0);
            if (at >= 0)
            {
                var answer = new OpAnswer { sections = new List<int>() };
                for (int i = 0; i < at; i++) if (fake.inbox[i].kind == ToClient.State && fake.inbox[i].id == worldId) answer.sections.Add(fake.inbox[i].component);
                using (var r = SyncProtocol.Reader(fake.inbox[at].blob)) { answer.kind = SyncProtocol.ReadStr(r); answer.error = SyncProtocol.ReadStr(r); }
                fake.inbox.RemoveRange(0, at + 1);
                return answer;
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for an OpResult");
            await Awaitable.NextFrameAsync();
        }
    }

    // The server's clock over the coming midnight: in-app, so AdvanceClock on it.
    static async Task PassNight(SyncHost host, Func<bool> done)
    {
        double now = host.Server.World.WorldMinutes;
        double toMidnight = (Math.Floor(now / Day) + 1) * Day - now + .5;
        host.Server.AdvanceClock((long)(toMidnight * GameServer.RealSecondsPerGameHour * 1000 / 60));
        await WaitFor(done, 10, "the night activity");
    }

    static string SectionJson(byte[] blob)
    {
        using var gzip = new GZipStream(new MemoryStream(blob, false), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static uint ClockOf(byte[] blob) => BitConverter.ToUInt32(blob, 0);
    static byte[] LastClock(QaPeer p) { p.Pump(); return p.inbox.FindLast(m => m.kind == ToClient.State && m.id == worldId && m.component == ServerWorld.ClockSection)?.blob ?? new byte[4]; }

    static long Received(QaPeer p)
    {
        p.Pump();
        long sum = 0;
        foreach (var m in p.inbox) sum += m.size + 4; // + the TCP length
        p.inbox.Clear();
        return sum;
    }

    static string Sizes(int[] bytes)
    {
        string[] names = { "clock", "names", "stocks", "companies", "properties", "plots", "reports", "sns" };
        var text = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++) text.Append(i > 0 ? ", " : "").Append(names[i]).Append(' ').Append(bytes[i]);
        return text.ToString();
    }

    static T Last<T>(IReadOnlyList<T> list) => list[list.Count - 1];

    static string Hash(string path)
    {
        if (!File.Exists(path)) return "none";
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static Vector3 Ground(Vector3 at)
    {
        Vector3 best = at;
        float nearest = 1.5f;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.normal.y > .7f && !hit.transform.IsChildOf(PlayerSpawner.Player.transform) && Mathf.Abs(hit.point.y - at.y) < nearest)
            { nearest = Mathf.Abs(hit.point.y - at.y); best = hit.point + Vector3.up * .03f; }
        return best;
    }

    static async Task StandBeside(PlayerMovement player, Vector3 spot)
    {
        var motor = player.GetComponent<CharacterController>();
        var to = spot - Vector3.ProjectOnPlane(spot - player.transform.position, Vector3.up).normalized * 1.6f;
        motor.enabled = false;
        player.transform.position = to;
        player.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(spot - to, Vector3.up).normalized);
        motor.enabled = true;
        Physics.SyncTransforms();
        await Awaitable.WaitForSecondsAsync(.3f);
    }

    static void Drain(QaPeer p) { p.Pump(); p.inbox.Clear(); }

    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    static async Task<QaMsg> WaitMsg(QaPeer p, Func<QaMsg, bool> match, string what, float seconds = 5f)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (true)
        {
            var m = p.Take(match);
            if (m != null) return m;
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
    }

    // The game camera with the overlay canvases (clock, chat, phone) drawn into it, as the player sees the screen.
    static async Task Shot(string file, float settle = .5f)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        if (settle > 0f) await Awaitable.WaitForSecondsAsync(settle);
        var camera = Camera.main;
        var moved = new List<Canvas>();
        foreach (var canvas in Object.FindObjectsByType<Canvas>())
            if (canvas && canvas.isRootCanvas && canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .05f; moved.Add(canvas); }
        try { await TitleShots.Snap(camera, Path.Combine(dir, file)); }
        finally { foreach (var canvas in moved) if (canvas) canvas.renderMode = RenderMode.ScreenSpaceOverlay; }
    }
}
