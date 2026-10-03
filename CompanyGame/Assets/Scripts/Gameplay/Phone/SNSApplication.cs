using System;
using System.Collections.Generic;
using UnityEngine;

public class SNSApplication : PhoneAppBase
{
    [SerializeField]
    private SnsManager sns;

    public event Action Refreshed;

    private void Awake()
    {
        if (sns == null) sns = FindAnyObjectByType<SnsManager>();
    }

    private void Start()
    {
        if (sns == null) return;

        sns.Feed.PostAdded += OnFeedChanged;
        sns.Feed.PostChanged += OnFeedChanged;
    }

    private void OnDestroy()
    {
        if (sns == null) return;

        sns.Feed.PostAdded -= OnFeedChanged;
        sns.Feed.PostChanged -= OnFeedChanged;
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public List<SnsPost> GetPosts(int count, SnsPostKind? kind = null) =>
        sns != null ? sns.Feed.GetRecent(count, kind) : new List<SnsPost>();

    public SnsPostResult Post(string text) => sns != null ? sns.Post(text) : SnsPostResult.EmptyText;

    public bool ToggleLike(string postId) => sns != null && sns.ToggleLike(postId);

    private void OnFeedChanged(SnsPost post)
    {
        if (IsOpen) Refreshed?.Invoke();
    }
}
