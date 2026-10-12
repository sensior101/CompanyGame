using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Run with -executeMethod NetSessionQA.Run. Offline checks (message JSON, inventory snapshot) always run.
/// The server check needs `npm start` on localhost:3000 and prints SKIP when it is not reachable.
/// </summary>
public static class NetSessionQA
{
    static string Server => Environment.GetEnvironmentVariable("QA_SERVER") ?? "http://localhost:3000"; // env QA_SERVER: use another port when 3000 is taken

    [MenuItem("CompanyGame/Net/Run NetSession QA")]
    public static void Run()
    {
        MessageRoundTrip();
        InventoryRoundTrip();
        ServerFlow();
        Debug.Log("[NetSessionQA] done");
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[NetSessionQA] FAIL: " + what);
        Debug.Log("[NetSessionQA] ok: " + what);
    }

    static void MessageRoundTrip()
    {
        var m = NetMsg.Of("joined");
        m.rid = "r1"; m.seq = 5000000000L; m.snap = "{\"a\":\"b\\\"c\"}";
        m.players = new[] { new PeerInfo { id = "1", name = "가나" } };
        m.ops = new[] { new OpInfo { seq = 7, from = "1", ev = "k", data = "{}" } };
        var back = JsonUtility.FromJson<NetMsg>(JsonUtility.ToJson(m));
        Check(back.rid == "r1" && back.seq == 5000000000L && back.snap == m.snap, "NetMsg scalar round trip");
        Check(back.players[0].name == "가나" && back.ops[0].seq == 7 && back.ops[0].ev == "k", "NetMsg arrays round trip");
    }

    static void InventoryRoundTrip()
    {
        var source = new InventoryState();
        var items = Resources.LoadAll<ItemData>("Inventory");
        int added = 0;
        foreach (var item in items)
            if (added < 5 && !item.IsBook && source.TryAdd(item, 1, out _)) added++;
        string json = InventorySnapshot.Export(source);
        var target = new InventoryState();
        Check(InventorySnapshot.Import(target, json), "inventory import accepts export");
        for (int i = 0; i < source.Capacity; i++)
        {
            var a = source.GetSlot(i); var b = target.GetSlot(i);
            Check(a.Item == b.Item && a.Count == b.Count && a.InstanceId == b.InstanceId, "slot " + i + " identical");
        }
        Check(InventorySnapshot.Import(target, json), "second import is idempotent");
        int vehicles = 0;
        for (int i = 0; i < target.Capacity; i++)
            if (!target.GetSlot(i).IsEmpty && target.GetSlot(i).Item.IsVehicle) vehicles++;
        int expected = 0;
        for (int i = 0; i < source.Capacity; i++)
            if (!source.GetSlot(i).IsEmpty && source.GetSlot(i).Item.IsVehicle) expected++;
        Check(vehicles == expected, "no duplicated vehicle/book instances after re-import");
        Check(!InventorySnapshot.Import(target, "not json"), "garbage json rejected");
    }

    static void ServerFlow()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string suffix = UnityEngine.Random.Range(1000, 9999).ToString();
        string tokenA, tokenB;
        try { tokenA = Register(http, "qa_a" + suffix); tokenB = Register(http, "qa_b" + suffix); }
        catch (Exception e) { Debug.Log("[NetSessionQA] SKIP server flow (" + e.Message + ")"); return; }

        var a = Open(tokenA); var b = Open(tokenB);
        var helloA = Wait(a, "hello"); var helloB = Wait(b, "hello");
        a.Send(Msg("create", m => { m.room = "qa" + suffix; m.max = 4; }));
        var joinedA = Wait(a, "joined");
        Check(joinedA.host == helloA.id, "creator is host");
        Check(long.TryParse(helloA.id, out _) && !string.IsNullOrEmpty(helloA.name) && helloA.id != helloB.id, "hello carries the account id and nickname (LocalPlayerId/Name source)");
        b.Send(Msg("join", m => m.rid = joinedA.rid));
        var joinedB = Wait(b, "joined");
        Check(Array.Exists(joinedB.players, p => p.id == helloA.id && !string.IsNullOrEmpty(p.name)), "joined.players lists peers by account id");
        a.Send(Msg("pos", m => { m.map = "m"; m.x = 1; m.y = 2; m.z = 3; }));
        var pos = Wait(b, "pos");
        Check(pos.from == helloA.id && pos.x == 1f && pos.z == 3f, "pos relayed with from");
        a.Send(Msg("state", m => m.snap = "{\"min\":1}"));
        a.Send(Msg("transfer", m => m.to = helloB.id));
        var host = Wait(b, "host");
        Check(host.host == helloB.id && host.snap == "{\"min\":1}", "transfer delivers host + snapshot");
        b.Send(Msg("op", m => { m.ev = "k"; m.data = "{}"; }));
        var op = Wait(a, "op");
        Check(op.seq > 0 && op.from == helloB.id, "op echoed with seq");
        Check(op.from == helloB.id && op.from != helloA.id, "op `from` is the sender's account id (what actors are checked against)");
        a.Close(); b.Close();
    }

    static string Register(HttpClient http, string user)
    {
        var body = new StringContent("{\"username\":\"" + user + "\",\"password\":\"qa-pass-123\"}", Encoding.UTF8, "application/json");
        string text = http.PostAsync(Server + "/auth/register", body).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonUtility.FromJson<TokenBody>(text).token;
    }

    static NetClient Open(string token)
    {
        var client = new NetClient();
        client.Connect(new Uri(Server.Replace("http://", "ws://") + "/ws?token=" + Uri.EscapeDataString(token))).GetAwaiter().GetResult();
        return client;
    }

    static NetMsg Msg(string type, Action<NetMsg> fill) { var m = NetMsg.Of(type); fill(m); return m; }

    static NetMsg Wait(NetClient client, string type)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            while (client.TryDequeue(out var m))
            {
                if (m.t == type) return m;
                if (m.t == "err") throw new Exception("[NetSessionQA] server error " + m.code + " while waiting for " + type);
            }
            Task.Delay(20).Wait();
        }
        throw new Exception("[NetSessionQA] FAIL: timeout waiting for " + type);
    }

    [Serializable] class TokenBody { public string token; }
}
