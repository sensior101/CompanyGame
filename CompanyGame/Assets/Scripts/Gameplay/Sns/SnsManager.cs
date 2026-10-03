using System.Collections.Generic;
using UnityEngine;

public class SnsManager : MonoBehaviour
{
    public static SnsManager Instance { get; private set; }

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        EnsureInitialized();
    }

    private void Start()
    {
        if (!settings.autoPostNews) return;

        if (stockMarket == null) stockMarket = FindAnyObjectByType<StockMarketManager>();
        if (stockMarket != null) stockMarket.NewsPublished += OnNewsPublished;
    }

    private void OnDestroy()
    {
        if (stockMarket != null) stockMarket.NewsPublished -= OnNewsPublished;
        if (Instance == this) Instance = null;
    }

    public SnsPostResult Post(string text) =>
        Feed.TryPost(Now, settings.localUserId, settings.localUserName, text, out _);

    public bool ToggleLike(string postId) =>
        Feed.ToggleLike(postId, settings.localUserId, out bool liked) && liked;

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
