using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Sessions;
using UnityEngine;

/// <summary>
/// The public server list and server codes on EOS Sessions (Docs/Multiplayer_Redesign.md 12.13). An open server keeps one
/// session: bucket <see cref="Bucket"/>, session id "companygame-" + server code, attributes NAME, MAXP, PLAYERS, HASPW, PUB
/// (1 public, 0 private), PUID (the server's) and HB (UTC minutes of the last update). <see cref="Tick"/> sends a changed
/// player count or public flag at most every 30 seconds and HB alone every 2 minutes (EOS allows 30 updates a minute).
/// A forced-closed server's session stays found for 2-5 minutes (12.4): searches hide sessions whose HB is over
/// <see cref="StaleMinutes"/> old. Main thread only.
/// </summary>
public sealed class EosSession
{
    public const string LocalName = "companygame", IdPrefix = "companygame-";
    public const float BatchSeconds = 30, HeartbeatSeconds = 120, RetrySeconds = 60;
    public const int StaleMinutes = 5, MaxResults = 200;
    public static string Bucket => "companygame:server:" + SyncProtocol.Version;

    /// <summary>The clock of HB, written and compared. QA moves it to test the stale filter.</summary>
    public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { UtcNow = () => DateTime.UtcNow; }

    /// <summary>A server as a search shows it.</summary>
    public sealed class Entry
    {
        public string code, name, puid;
        public int players, maxPlayers;
        public bool hasPassword, isPublic;
        public long hb;
    }

    public readonly string Code;
    readonly string name;
    readonly int maxPlayers;
    readonly bool hasPassword;
    bool isPublic, busy, sentPublic;
    int players, sentPlayers = -1;
    float lastSent = float.NegativeInfinity, retryAt;

    /// <summary>True once the session exists on EOS (the code works from then on).</summary>
    public bool Created { get; private set; }
    /// <summary>The last create or update result, for logs and QA.</summary>
    public Result LastResult { get; private set; } = Result.NotFound;
    /// <summary>Updates sent after the create (QA counts them against the limit).</summary>
    public int Updates { get; private set; }

    public EosSession(string code, string name, int maxPlayers, bool hasPassword, bool isPublic)
    {
        Code = code; this.name = name; this.maxPlayers = maxPlayers; this.hasPassword = hasPassword; this.isPublic = isPublic;
    }

    /// <summary>Changes whether the list shows it (sent with the next batch).</summary>
    public void SetPublic(bool value) => isPublic = value;
    public bool IsPublic => isPublic;

    public static long Minutes(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalMinutes;

    /// <summary>Once a frame with the server's player count: creates the session, then sends what changed.</summary>
    public void Tick(int playerCount)
    {
        players = playerCount;
        if (busy || EosAccount.State != EosAccount.Status.Ready) return;
        float now = Time.realtimeSinceStartup;
        if (!Created) { if (now >= retryAt) Send(true); return; }
        bool changed = players != sentPlayers || isPublic != sentPublic;
        if (changed && now - lastSent >= BatchSeconds || now - lastSent >= HeartbeatSeconds) Send(false);
    }

    async void Send(bool create)
    {
        busy = true;
        var sessions = EosAccount.Sessions;
        int sendingPlayers = players;
        bool sendingPublic = isPublic;
        Result result;
        try
        {
            SessionModification modification;
            if (create)
            {
                var options = new CreateSessionModificationOptions
                {
                    SessionName = LocalName, BucketId = Bucket, MaxPlayers = (uint)maxPlayers, LocalUserId = EosAccount.Puid,
                    PresenceEnabled = false, SessionId = IdPrefix + Code,
                };
                Check(sessions.CreateSessionModification(ref options, out modification), "CreateSessionModification");
                var permission = new SessionModificationSetPermissionLevelOptions { PermissionLevel = OnlineSessionPermissionLevel.PublicAdvertised };
                Check(modification.SetPermissionLevel(ref permission), "SetPermissionLevel"); // private ones too: found by id, PUB keeps them off the list
                Attribute(modification, "NAME", name);
                Attribute(modification, "MAXP", (long?)maxPlayers);
                Attribute(modification, "HASPW", (long?)(hasPassword ? 1 : 0));
                Attribute(modification, "PUID", EosAccount.LocalPuid);
            }
            else
            {
                var options = new UpdateSessionModificationOptions { SessionName = LocalName };
                Check(sessions.UpdateSessionModification(ref options, out modification), "UpdateSessionModification");
            }
            Attribute(modification, "PLAYERS", (long?)sendingPlayers);
            Attribute(modification, "PUB", (long?)(sendingPublic ? 1 : 0));
            Attribute(modification, "HB", (long?)Minutes(UtcNow()));
            var done = new TaskCompletionSource<Result>();
            var update = new UpdateSessionOptions { SessionModificationHandle = modification };
            sessions.UpdateSession(ref update, null, (ref UpdateSessionCallbackInfo info) => done.TrySetResult(info.ResultCode));
            try { result = await EosAccount.Within(done.Task, 30, "UpdateSession"); }
            finally { modification.Release(); }
        }
        catch (Exception e) { Debug.LogWarning("[EosSession] " + e.Message); result = Result.UnexpectedError; }
        busy = false;
        LastResult = result;
        lastSent = Time.realtimeSinceStartup;
        if (result != Result.Success)
        {
            // ponytail: a create that fails (EOS down, or a forced-closed run's session with this id not yet gone) is tried again every minute
            if (create) { retryAt = lastSent + RetrySeconds; Debug.LogWarning("[EosSession] session " + Code + " not made (" + result + "), trying again in a minute"); }
            return;
        }
        sentPlayers = sendingPlayers; sentPublic = sendingPublic;
        if (create) { Created = true; Debug.Log("[EosSession] session made, code " + Code + (sendingPublic ? " (public)" : " (private)")); }
        else Updates++;
    }

    /// <summary>Removes the session (the server is closing). Done when EOS answered or after 3 seconds.</summary>
    public async Task Destroy()
    {
        if (EosAccount.Sessions == null) return;
        Created = false;
        var done = new TaskCompletionSource<Result>();
        var options = new DestroySessionOptions { SessionName = LocalName };
        EosAccount.Sessions.DestroySession(ref options, null, (ref DestroySessionCallbackInfo info) => done.TrySetResult(info.ResultCode));
        await Task.WhenAny(done.Task, Task.Delay(3000));
        Debug.Log("[EosSession] session " + Code + " removed: " + (done.Task.IsCompleted ? done.Task.Result.ToString() : "no answer in 3s"));
    }

    // ---- search (clients) ----

    /// <summary>
    /// Public servers (<paramref name="code"/> null), or the server with that code (public or not). Stale ones (HB older
    /// than <see cref="StaleMinutes"/>) are left out. Error null on success (an empty list when nothing is found), else the EOS result.
    /// </summary>
    public static async Task<(string error, List<Entry> found)> Search(string code)
    {
        var found = new List<Entry>();
        if (EosAccount.State != EosAccount.Status.Ready) return ("not logged in", found);
        var sessions = EosAccount.Sessions;
        var create = new CreateSessionSearchOptions { MaxSearchResults = MaxResults };
        if (sessions.CreateSessionSearch(ref create, out SessionSearch search) != Result.Success) return ("CreateSessionSearch", found);
        try
        {
            // ponytail: one search of at most 200 (the EOS limit); add a name filter when there are more public servers than that
            if (code != null)
            {
                var byId = new SessionSearchSetSessionIdOptions { SessionId = IdPrefix + code };
                if (search.SetSessionId(ref byId) != Result.Success) return ("SetSessionId", found);
            }
            else
            {
                Parameter(search, SessionsInterface.SEARCH_BUCKET_ID, Bucket);
                Parameter(search, "PUB", (long?)1);
            }
            var done = new TaskCompletionSource<Result>();
            var find = new SessionSearchFindOptions { LocalUserId = EosAccount.Puid };
            search.Find(ref find, null, (ref SessionSearchFindCallbackInfo info) => done.TrySetResult(info.ResultCode));
            Result result = await EosAccount.Within(done.Task, 20, "SessionSearch.Find");
            if (result == Result.NotFound) return (null, found); // a session id search with no match
            if (result != Result.Success) return (result.ToString(), found);
            long now = Minutes(UtcNow());
            var countOptions = new SessionSearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            for (uint i = 0; i < count; i++)
            {
                var copy = new SessionSearchCopySearchResultByIndexOptions { SessionIndex = i };
                if (search.CopySearchResultByIndex(ref copy, out SessionDetails details) != Result.Success) continue;
                var infoOptions = new SessionDetailsCopyInfoOptions();
                details.CopyInfo(ref infoOptions, out SessionDetailsInfo? info);
                string id = info?.SessionId;
                var entry = new Entry
                {
                    code = id != null && id.StartsWith(IdPrefix) ? id.Substring(IdPrefix.Length) : "",
                    name = Text(details, "NAME"), puid = Text(details, "PUID"),
                    players = (int)Number(details, "PLAYERS"), maxPlayers = (int)Number(details, "MAXP"),
                    hasPassword = Number(details, "HASPW") == 1, isPublic = Number(details, "PUB") == 1, hb = Number(details, "HB"),
                };
                details.Release();
                if (entry.code.Length == 0 || entry.puid.Length == 0 || now - entry.hb > StaleMinutes) continue;
                entry.name = entry.name.Replace("<", "").Replace(">", ""); // from anyone's server: no rich text
                found.Add(entry);
            }
            return (null, found);
        }
        catch (Exception e) { return (e.Message, found); }
        finally { search.Release(); }
    }

    static void Attribute(SessionModification modification, string key, AttributeDataValue value)
    {
        var options = new SessionModificationAddAttributeOptions
        {
            SessionAttribute = new AttributeData { Key = key, Value = value }, AdvertisementType = SessionAttributeAdvertisementType.Advertise,
        };
        Check(modification.AddAttribute(ref options), "AddAttribute " + key);
    }

    static void Parameter(SessionSearch search, string key, AttributeDataValue value)
    {
        var options = new SessionSearchSetParameterOptions { Parameter = new AttributeData { Key = key, Value = value }, ComparisonOp = ComparisonOp.Equal };
        Check(search.SetParameter(ref options), "SetParameter " + key);
    }

    static AttributeDataValue? Value(SessionDetails details, string key)
    {
        var options = new SessionDetailsCopySessionAttributeByKeyOptions { AttrKey = key };
        if (details.CopySessionAttributeByKey(ref options, out SessionDetailsAttribute? attribute) != Result.Success || attribute?.Data == null) return null;
        return attribute.Value.Data.Value.Value;
    }

    static string Text(SessionDetails details, string key) => Value(details, key)?.AsUtf8 is Utf8String s ? (string)s ?? "" : "";
    static long Number(SessionDetails details, string key) => Value(details, key)?.AsInt64 ?? -1;

    static void Check(Result result, string what)
    {
        if (result != Result.Success) throw new Exception(what + " " + result);
    }
}
