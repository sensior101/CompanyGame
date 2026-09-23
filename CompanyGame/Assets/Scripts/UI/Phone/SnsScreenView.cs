using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SnsScreenView : MonoBehaviour
{
    [SerializeField] private SNSApplication app;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_InputField postInput;
    [SerializeField] private Button postButton;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private GameObject rowTemplate;
    [SerializeField, Min(1)] private int visiblePosts = 8;

    private class Row
    {
        public GameObject root;
        public TMP_Text author;
        public TMP_Text body;
        public Button like;
        public TMP_Text likeLabel;
    }

    private readonly List<Row> rows = new List<Row>();

    private void Awake()
    {
        if (app == null) app = GetComponent<SNSApplication>();
        app.Refreshed += Refresh;
        postButton.onClick.AddListener(OnPost);
        rowTemplate.SetActive(false);
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    private void Refresh()
    {
        var posts = app.GetPosts(visiblePosts);
        while (rows.Count < posts.Count)
        {
            var go = Instantiate(rowTemplate, rowContainer);
            go.SetActive(true);
            rows.Add(new Row
            {
                root = go,
                author = go.transform.Find("Info/Author").GetComponent<TMP_Text>(),
                body = go.transform.Find("Info/Body").GetComponent<TMP_Text>(),
                like = go.transform.Find("LikeButton").GetComponent<Button>(),
                likeLabel = go.transform.Find("LikeButton/Label").GetComponent<TMP_Text>()
            });
        }

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].root.SetActive(i < posts.Count);
            if (i >= posts.Count) continue;

            var post = posts[i];
            rows[i].author.text = $"{post.authorName}   {post.time.hour:00}:{post.time.minute:00}";
            rows[i].body.text = post.text;
            rows[i].likeLabel.text = "좋아요 " + post.Likes;
            rows[i].like.onClick.RemoveAllListeners();
            rows[i].like.onClick.AddListener(() =>
            {
                app.ToggleLike(post.id);
                Refresh();
            });
        }
    }

    private void OnPost()
    {
        var result = app.Post(postInput.text);
        switch (result)
        {
            case SnsPostResult.Success:
                messageText.text = "게시했습니다.";
                postInput.text = string.Empty;
                break;
            case SnsPostResult.EmptyText:
                messageText.text = "내용을 입력하세요.";
                break;
            case SnsPostResult.TooLong:
                messageText.text = "글자 수를 넘었습니다.";
                break;
            default:
                messageText.text = "잠시 후 다시 게시할 수 있습니다.";
                break;
        }
        Refresh();
    }
}
