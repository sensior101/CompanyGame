using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Body of GET/PUT /me/state. Field names match the server's JSON.</summary>
[Serializable]
public class PlayerState
{
    public string map;
    public float x, y, z, ry;
    public string inventory_json;
    public string personal_json;
    public long updated_at;

    public bool IsSaved => updated_at > 0;
}

[Serializable]
public class PlayerProfile
{
    public int id;
    public string username;
    public string nickname;
    public string gender; // 서버 값 그대로: "male" | "female"

    /// <summary>닉네임과 성별을 아직 고르지 않았으면 false (튜토리얼 대상).</summary>
    public bool IsComplete => !string.IsNullOrEmpty(nickname) && !string.IsNullOrEmpty(gender);
}

public readonly struct ApiResult
{
    /// <summary>실패 시 서버 오류 코드: username_taken, nickname_taken, invalid_credentials, unauthorized, invalid_input, network</summary>
    public readonly string Error;
    public bool Ok => Error == null;

    public ApiResult(string error) { Error = error; }
}

/// <summary>
/// 로그인 상태와 내 프로필(닉네임, 성별)을 서버 API(Docs/Server_API.md)와 주고받는다.
/// 씬이 바뀌어도 유지되도록 루트 오브젝트에 하나만 둔다.
/// </summary>
public class PlayerData : MonoBehaviour
{
    private static string TokenKey => ProfileSlot.Key("auth_token");

    public static PlayerData Instance { get; private set; }

    [SerializeField] private string baseUrl = "http://localhost:3000";
    [SerializeField] private int timeoutSeconds = 10;

    public PlayerProfile Profile { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(token);
    public string Token => token;

    /// <summary>HTTP base address of the server. Set before Login/Register (the title screen does).</summary>
    public string BaseUrl { get => baseUrl; set => baseUrl = (value ?? "").Trim().TrimEnd('/'); }

    private string token;

#pragma warning disable 0649 // JsonUtility가 리플렉션으로 채우는 필드
    [Serializable] private class Credentials { public string username; public string password; }
    [Serializable] private class ProfileBody { public string nickname; public string gender; }
    [Serializable] private class TokenResponse { public string token; }
    [Serializable] private class ErrorResponse { public string error; }
#pragma warning restore 0649

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ProfileSlot.Changed += ReloadToken;
        DontDestroyOnLoad(gameObject);
        // ponytail: 토큰을 PlayerPrefs에 평문 저장. 7일 만료 토큰이라 허용, 필요하면 암호화한다.
        token = PlayerPrefs.GetString(TokenKey, "");
    }

    private void OnDestroy()
    {
        if (Instance == this) ProfileSlot.Changed -= ReloadToken;
    }

    /// <summary>The profile slot changed: the token of the new slot (if any) replaces the old one.</summary>
    public void ReloadToken()
    {
        token = PlayerPrefs.GetString(TokenKey, "");
        Profile = null;
    }

    /// <summary>Is a server answering at <paramref name="url"/>? Any HTTP answer counts (a 401 is fine); no answer is false.</summary>
    public async Awaitable<bool> Ping(string url)
    {
        if (ServerBudget.Blocked) return true; // the server answered recently (with its budget message): no new request today
        using var request = UnityWebRequest.Get((url ?? "").Trim().TrimEnd('/') + "/me");
        request.timeout = 5;
        var operation = request.SendWebRequest();
        while (!operation.isDone)
            await Awaitable.NextFrameAsync();
        return request.responseCode > 0;
    }

    public Awaitable<ApiResult> Register(string username, string password) => Authenticate("/auth/register", username, password);

    public Awaitable<ApiResult> Login(string username, string password) => Authenticate("/auth/login", username, password);

    public void Logout()
    {
        token = "";
        Profile = null;
        PlayerPrefs.DeleteKey(TokenKey);
    }

    /// <summary>저장된 토큰으로 내 프로필을 불러온다. 앱 시작 시 자동 로그인에 쓴다.</summary>
    public async Awaitable<ApiResult> LoadProfile()
    {
        var (code, body) = await Send("GET", "/me");
        if (code != 200) return Fail(code, body);

        Profile = JsonUtility.FromJson<PlayerProfile>(body);
        return default;
    }

    /// <summary>튜토리얼에서 고른 닉네임과 성별을 저장한다. gender는 "male" 또는 "female".</summary>
    public async Awaitable<ApiResult> SaveProfile(string nickname, string gender)
    {
        var json = JsonUtility.ToJson(new ProfileBody { nickname = nickname, gender = gender });
        var (code, body) = await Send("PUT", "/me/profile", json);
        if (code != 200) return Fail(code, body);

        var saved = JsonUtility.FromJson<ProfileBody>(body);
        Profile ??= new PlayerProfile();
        Profile.nickname = saved.nickname;
        Profile.gender = saved.gender;
        return default;
    }

    /// <summary>Loads the saved position, inventory and personal economy. Nothing saved yet: UpdatedAt == 0.</summary>
    public async Awaitable<(ApiResult result, PlayerState state)> GetState()
    {
        var (code, body) = await Send("GET", "/me/state");
        if (code != 200) return (Fail(code, body), null);
        try { return (default, JsonUtility.FromJson<PlayerState>(body)); }
        catch (Exception) { return (new ApiResult("network"), null); }
    }

    public async Awaitable<ApiResult> PutState(PlayerState state)
    {
        var (code, body) = await Send("PUT", "/me/state", JsonUtility.ToJson(state));
        return code == 200 ? default : Fail(code, body);
    }

    private async Awaitable<ApiResult> Authenticate(string path, string username, string password)
    {
        var json = JsonUtility.ToJson(new Credentials { username = username, password = password });
        var (code, body) = await Send("POST", path, json);
        if (code != 200 && code != 201) return Fail(code, body);

        token = JsonUtility.FromJson<TokenResponse>(body).token;
        PlayerPrefs.SetString(TokenKey, token);
        return await LoadProfile();
    }

    private async Awaitable<(long code, string body)> Send(string method, string path, string json = null)
    {
        if (ServerBudget.Blocked) return (503, "{\"error\":\"" + ServerBudget.Code + "\"}"); // no request at all until the next UTC day
        using var request = new UnityWebRequest(baseUrl + path, method);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = timeoutSeconds;

        if (json != null)
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        if (IsLoggedIn)
            request.SetRequestHeader("Authorization", "Bearer " + token);

        var operation = request.SendWebRequest();
        while (!operation.isDone)
            await Awaitable.NextFrameAsync();

        string text = request.downloadHandler.text;
        if (ServerBudget.IsBudgetResponse(request.responseCode, text))
        {
            int.TryParse(request.GetResponseHeader("Retry-After"), out int retryAfter);
            ServerBudget.Trip(retryAfter);
        }
        // 연결 실패는 responseCode가 0이므로 아래 Fail에서 network로 처리된다.
        return (request.responseCode, text);
    }

    private ApiResult Fail(long code, string body)
    {
        if (code == 0) return new ApiResult("network");
        if (ServerBudget.IsBudgetResponse(code, body)) return new ApiResult(ServerBudget.Code);
        if (code == 401 && IsLoggedIn && !body.Contains("invalid_credentials")) Logout(); // 만료된 토큰
        if (code == 400) return new ApiResult("invalid_input");
        if (code == 429) return new ApiResult("rate_limited");

        // 프록시 등이 JSON이 아닌 본문(HTML)을 돌려주면 파싱하지 않고 network로 본다.
        if (!body.TrimStart().StartsWith("{")) return new ApiResult("network");

        var error = JsonUtility.FromJson<ErrorResponse>(body).error;
        return new ApiResult(string.IsNullOrEmpty(error) ? "network" : error);
    }
}
