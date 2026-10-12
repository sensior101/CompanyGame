using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// A minimal second "player" for the title QA: plain HTTP + WebSocket against the real test server, so a room can have
/// someone else in it without a second Unity window. Not part of the game.
/// </summary>
public sealed class QaRawClient : IDisposable
{
    static readonly HttpClient Http = new HttpClient();

    public readonly string Server;
    public string Token, Id = "", Name = "";
    ClientWebSocket socket;

    public QaRawClient(string server) { Server = server.TrimEnd('/'); }

    [Serializable] class Auth { public string username, password; }
    [Serializable] class Tok { public string token; }
    [Serializable] class Nick { public string nickname, gender; }

    public async Task Register(string nickname)
    {
        string user = "q" + Guid.NewGuid().ToString("N").Substring(0, 12);
        Token = await PostForToken("/auth/register", user, "pw-" + Guid.NewGuid().ToString("N"));
        var resp = await Send(HttpMethod.Put, "/me/profile", JsonUtility.ToJson(new Nick { nickname = nickname, gender = "male" }));
        if (!resp.IsSuccessStatusCode) throw new Exception("raw profile failed " + (int)resp.StatusCode);
        Name = nickname;
    }

    public async Task Login(string user, string password) { Token = await PostForToken("/auth/login", user, password); }

    async Task<string> PostForToken(string path, string user, string pw)
    {
        var resp = await Http.PostAsync(Server + path, new StringContent(JsonUtility.ToJson(new Auth { username = user, password = pw }), Encoding.UTF8, "application/json"));
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception("raw " + path + " " + (int)resp.StatusCode + " " + body);
        return JsonUtility.FromJson<Tok>(body).token;
    }

    async Task<HttpResponseMessage> Send(HttpMethod method, string path, string json)
    {
        var req = new HttpRequestMessage(method, Server + path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Add("Authorization", "Bearer " + Token);
        return await Http.SendAsync(req);
    }

    public async Task Connect()
    {
        socket = new ClientWebSocket();
        string ws = Server.Replace("http://", "ws://").Replace("https://", "wss://");
        await socket.ConnectAsync(new Uri(ws + "/ws?token=" + Uri.EscapeDataString(Token)), CancellationToken.None);
        var hello = await Next(m => m.t == "hello");
        Id = hello.id;
    }

    public bool Open => socket != null && socket.State == WebSocketState.Open;

    public async Task Say(NetMsg message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>Reads messages until one matches (others are dropped). Throws after 10 s.</summary>
    public async Task<NetMsg> Next(Func<NetMsg, bool> match)
    {
        var cts = new CancellationTokenSource(10000);
        var buffer = new byte[1 << 16];
        while (true)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult r;
            do
            {
                r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) throw new Exception("raw socket closed " + r.CloseStatus);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            } while (!r.EndOfMessage);
            var m = JsonUtility.FromJson<NetMsg>(sb.ToString());
            if (match(m)) return m;
        }
    }

    public async Task<NetMsg> CreateRoom(string room, int max = 4)
    {
        var m = NetMsg.Of("create"); m.room = room; m.max = max;
        await Say(m);
        return await Next(x => x.t == "joined" || x.t == "err");
    }

    public async Task<RoomInfo[]> ListRooms()
    {
        await Say(NetMsg.Of("list"));
        return (await Next(x => x.t == "rooms")).rooms ?? new RoomInfo[0];
    }

    public async Task<NetMsg> JoinRoom(string rid)
    {
        var m = NetMsg.Of("join"); m.rid = rid;
        await Say(m);
        return await Next(x => x.t == "joined" || x.t == "err");
    }

    public void Dispose() { try { socket?.Abort(); socket?.Dispose(); } catch (Exception) { } }
}
