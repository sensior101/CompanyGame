using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Cloudflare account server's daily budget: HTTP 503 {"error":"daily_budget"} + Retry-After.
/// A local HttpListener plays the server and counts requests. After the first 503 the game must send no more account
/// requests (login, register, ping) until the next UTC day.
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
        var profile = await data.LoadProfile();
        Check(again.Error == "daily_budget" && reg.Error == "daily_budget" && profile.Error == "daily_budget", "login, register and the profile all refuse with daily_budget");
        Check(hits == 1, "no further HTTP request reached the server (hits=" + hits + ")");
        Check(await data.Ping(url), "connection test does not send a request while blocked");
        Check(hits == 1, "ping sent nothing either");
        Check(TitleFlow.Message("daily_budget") == ServerBudget.Message, "the title text for it is the friendly message");
        UnityEngine.Object.Destroy(data.gameObject);
    }
}
