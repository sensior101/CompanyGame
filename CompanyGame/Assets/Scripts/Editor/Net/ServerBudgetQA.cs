using System;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Cloudflare server's daily budget: HTTP 503 {"error":"daily_budget"} + Retry-After, and WebSocket close 4503.
/// A local HttpListener plays the server and counts requests. After the first 503 the game must send nothing more
/// (login, register, state GET/PUT, new sockets) until the next UTC day. Also checks the autosave interval (120s).
/// Run WITHOUT -quit:  Unity.exe -batchmode -nographics -projectPath ... -executeMethod ServerBudgetQA.Run
/// </summary>
[InitializeOnLoad]
public static class ServerBudgetQA
{
    const string Flag = "ServerBudgetQA.run";
    static int checks, hits;

    static ServerBudgetQA() => EditorApplication.playModeStateChanged += OnState;

    public static void Run()
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Flag, false) || change != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool(Flag, false);
        Drive();
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what);
        checks++;
        Debug.Log("[ServerBudgetQA] ok: " + what);
    }

    static async void Drive()
    {
        int code = 0;
        HttpListener listener = null;
        ServerBudget.Clear();
        try
        {
            listener = StartServer(out string url);
            Clock();
            await Http(url);
            await Socket(url);
            Interval();
            Debug.Log("[ServerBudgetQA] RESULT passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError("[ServerBudgetQA] RESULT FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        ServerBudget.Now = () => DateTime.UtcNow;
        ServerBudget.Clear();
        try { listener?.Stop(); } catch (Exception) { }
        EditorApplication.Exit(code);
    }

    // A server that has used up its budget: every request gets 503 + Retry-After.
    static HttpListener StartServer(out string url)
    {
        int port = 3200 + new System.Random().Next(500);
        url = "http://localhost:" + port;
        var listener = new HttpListener();
        listener.Prefixes.Add(url + "/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    hits++;
                    byte[] body = Encoding.UTF8.GetBytes("{\"error\":\"daily_budget\"}");
                    ctx.Response.StatusCode = 503;
                    ctx.Response.AddHeader("Retry-After", "3600");
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                    ctx.Response.Close();
                }
                catch (Exception) { }
            }
        });
        return listener;
    }

    static void Clock()
    {
        ServerBudget.Now = () => new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
        Check(!ServerBudget.Blocked, "not blocked at the start");
        ServerBudget.Trip(0);
        Check(ServerBudget.Blocked && ServerBudget.BlockedUntilUtc == new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), "no Retry-After: blocked until the next UTC midnight (09:00 KST)");
        ServerBudget.Now = () => new DateTime(2026, 10, 14, 0, 0, 1, DateTimeKind.Utc);
        Check(!ServerBudget.Blocked, "unblocked after the UTC day changed");
        ServerBudget.Now = () => DateTime.UtcNow;
        ServerBudget.Clear();
        Check(ServerBudget.IsBudgetResponse(503, "{\"error\":\"daily_budget\"}") && !ServerBudget.IsBudgetResponse(503, "{}") && !ServerBudget.IsBudgetResponse(500, "daily_budget"), "only 503 with daily_budget counts");
        Check(TitleFlow.Message("daily_budget").Contains("오전 9시"), "Korean message mentions tomorrow 09:00");
    }

    static async Task Http(string url)
    {
        var data = new GameObject("QA PlayerData").AddComponent<PlayerData>();
        data.BaseUrl = url;
        hits = 0;
        var first = await data.Login("u123456789012", "password123");
        Check(first.Error == "daily_budget" && hits == 1, "first login: server 503 -> daily_budget (1 request)");
        Check(ServerBudget.Blocked, "Retry-After honoured: blocked now");
        double seconds = (ServerBudget.BlockedUntilUtc - DateTime.UtcNow).TotalSeconds;
        Check(seconds > 3500 && seconds <= 3601, "blocked for about Retry-After (3600s), was " + (int)seconds);
        var again = await data.Login("u123456789012", "password123");
        var reg = await data.Register("u123456789012", "password123");
        var get = await data.GetState();
        var put = await data.PutState(new PlayerState { map = "m", inventory_json = "", personal_json = "" });
        Check(again.Error == "daily_budget" && reg.Error == "daily_budget" && get.result.Error == "daily_budget" && put.Error == "daily_budget", "login, register, state GET and PUT all refuse with daily_budget");
        Check(hits == 1, "no further HTTP request reached the server (hits=" + hits + ")");
        Check(await data.Ping(url), "connection test does not send a request while blocked");
        Check(hits == 1, "ping sent nothing either");
        UnityEngine.Object.Destroy(data.gameObject);
    }

    // WebSocket close 4503 comes in as a synthetic {t:"closed",code:"4503"}; afterwards NetSession.Connect opens no socket.
    static async Task Socket(string url)
    {
        ServerBudget.Clear();
        var net = NetSession.Instance;
        net.Disconnect();
        var handle = typeof(NetSession).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance);
        handle.Invoke(net, new object[] { new NetMsg { t = "closed", code = "4503" } });
        Check(net.LastError == "daily_budget" && ServerBudget.Blocked, "close code 4503: LastError daily_budget and blocked until the next UTC day");
        hits = 0;
        net.Connect(url, "token");
        await Awaitable.NextFrameAsync();
        Check(!net.Connecting && !net.Connected && net.LastError == "daily_budget" && hits == 0, "no socket attempt while blocked");
        Check(TitleFlow.Message(net.LastError) == ServerBudget.Message, "title screen shows the friendly message");
        ServerBudget.Clear();
    }

    static void Interval()
    {
        var field = typeof(NetPlayerSave).GetField("SaveInterval", BindingFlags.NonPublic | BindingFlags.Static);
        Check(field != null && (float)field.GetRawConstantValue() == 120f, "autosave interval is 120 seconds");
    }
}
