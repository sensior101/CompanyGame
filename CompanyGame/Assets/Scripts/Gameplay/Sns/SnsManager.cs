using System.Collections.Generic;
using UnityEngine;

public class SnsManager : GameSystem<SnsManager>
{
    [SerializeField]
    private SnsSettings settings = new SnsSettings();

    [SerializeField]
    private StockMarketManager stockMarket;

    private SnsFeed feed;

    public SnsFeed Feed
    {
        get
        {
            EnsureInitialized();
            return feed;
        }
    }

    private GameTime Now => GameClock.Current != null ? GameClock.Current.Now : default;

    protected override void OnSystemAwake() => EnsureInitialized();

    private void Start()
    {
        if (!settings.autoPostNews) return;

        if (stockMarket == null) stockMarket = FindAnyObjectByType<StockMarketManager>();
        if (stockMarket != null) stockMarket.NewsPublished += OnNewsPublished;
    }

    protected override void OnDestroy()
    {
        if (stockMarket != null) stockMarket.NewsPublished -= OnNewsPublished;
        base.OnDestroy();
    }

    public SnsPostResult Post(string text)
    {
        if (SharedWriteGate.InRoom)
        {
            if (string.IsNullOrWhiteSpace(text)) return SnsPostResult.EmptyText;
            bool sent = SharedWriteGate.TrySend("sns.post", new SharedOpArgs { a = text, b = GameSession.LocalPlayerId, c = GameSession.LocalPlayerName });
            return sent ? SnsPostResult.Success : SnsPostResult.OnCooldown; // not sent: the gate already told the player; "try again later"
        }
        return PostAs(settings.localUserId, settings.localUserName, text);
    }

    public SnsPostResult PostAs(string userId, string userName, string text) =>
        Feed.TryPost(Now, userId, userName, text, out _);

    public bool ToggleLike(string postId)
    {
        if (SharedWriteGate.InRoom)
            return SharedWriteGate.TrySend("sns.like", new SharedOpArgs { a = postId, b = GameSession.LocalPlayerId });
        return LikeAs(postId, settings.localUserId);
    }

    public bool LikeAs(string postId, string userId) => Feed.ToggleLike(postId, userId, out bool liked) && liked;

    private void EnsureInitialized()
    {
        if (feed == null) feed = new SnsFeed(settings);
    }

    private void OnNewsPublished(IReadOnlyList<NewsItem> news)
    {
        int limit = Mathf.Min(settings.maxAutoNewsPostsPerDigest, news.Count);
        for (int i = 0; i < limit; i++)
        {
            Feed.Publish(SnsPostKind.News, settings.newsAuthorId, settings.newsAuthorName,
                news[i].headline, news[i].time, news[i].relatedId);
        }
    }
}
