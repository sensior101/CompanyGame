using System;
using System.Collections.Generic;
using UnityEngine;

public enum SnsPostKind
{
    Player,
    Npc,
    News,
    System
}

public enum SnsPostResult
{
    Success,
    EmptyText,
    TooLong,
    OnCooldown
}

[Serializable]
public class SnsPost
{
    public string id;
    public SnsPostKind kind;
    public string authorId;
    public string authorName;
    public string text;
    public GameTime time;
    public string relatedId;
    public List<string> likedBy = new List<string>();

    public int Likes => likedBy.Count;
}

[Serializable]
public class SnsSettings
{
    [Min(1)] public int maxPosts = 200;
    [Min(1)] public int maxPostLength = 140;
    [Min(0)] public int postCooldownGameMinutes = 10;

    public string localUserId = "player_local";
    public string localUserName = "나";

    [Header("Stock news auto posts")]
    public bool autoPostNews = true;
    [Min(0)] public int maxAutoNewsPostsPerDigest = 3;
    public string newsAuthorId = "system_news";
    public string newsAuthorName = "증시 뉴스";
}

[Serializable]
public class SnsFeedState
{
    public List<SnsPost> posts = new List<SnsPost>();
    public int nextId = 1;
}

/// <summary>Newest-first post feed with per-user cooldown and likes. No scene dependencies.</summary>
public class SnsFeed
{
    private readonly SnsSettings settings;
    private readonly List<SnsPost> posts = new List<SnsPost>();
    private readonly Dictionary<string, long> lastPostMinutes = new Dictionary<string, long>();
    private int nextId = 1;

    public event Action<SnsPost> PostAdded;
    public event Action<SnsPost> PostChanged;

    public SnsFeed(SnsSettings settings)
    {
        this.settings = settings;
    }

    public IReadOnlyList<SnsPost> Posts => posts;

    public SnsPostResult TryPost(GameTime now, string userId, string userName, string text, out SnsPost post)
    {
        post = null;
        text = text == null ? string.Empty : text.Trim();
        if (text.Length == 0) return SnsPostResult.EmptyText;
        if (text.Length > settings.maxPostLength) return SnsPostResult.TooLong;

        if (lastPostMinutes.TryGetValue(userId, out long last)
            && now.TotalMinutes - last < settings.postCooldownGameMinutes)
        {
            return SnsPostResult.OnCooldown;
        }

        post = Publish(SnsPostKind.Player, userId, userName, text, now);
        lastPostMinutes[userId] = now.TotalMinutes;
        return SnsPostResult.Success;
    }

    public SnsPost Publish(SnsPostKind kind, string authorId, string authorName, string text,
        GameTime time, string relatedId = null)
    {
        var post = new SnsPost
        {
            id = "post_" + nextId++,
            kind = kind,
            authorId = authorId,
            authorName = authorName,
            text = text,
            time = time,
            relatedId = relatedId
        };

        posts.Insert(0, post);
        int excess = posts.Count - settings.maxPosts;
        if (excess > 0) posts.RemoveRange(posts.Count - excess, excess);

        PostAdded?.Invoke(post);
        return post;
    }

    public bool ToggleLike(string postId, string userId, out bool liked)
    {
        liked = false;
        var post = posts.Find(p => p.id == postId);
        if (post == null) return false;

        liked = !post.likedBy.Remove(userId);
        if (liked) post.likedBy.Add(userId);

        PostChanged?.Invoke(post);
        return true;
    }

    public List<SnsPost> GetRecent(int count, SnsPostKind? kind = null)
    {
        var result = new List<SnsPost>();
        foreach (var post in posts)
        {
            if (result.Count >= count) break;
            if (kind == null || post.kind == kind.Value) result.Add(post);
        }
        return result;
    }

    public SnsFeedState CaptureState() => new SnsFeedState { posts = new List<SnsPost>(posts), nextId = nextId };

    public void RestoreState(SnsFeedState state)
    {
        if (state == null) return;

        posts.Clear();
        posts.AddRange(state.posts);
        nextId = Math.Max(1, state.nextId);
    }
}
