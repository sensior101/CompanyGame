using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Top-right date/time readout, built at runtime. Shown in every scene.</summary>
public class ClockHud : MonoBehaviour
{
    TextMeshProUGUI label;
    long lastMinute = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<ClockHud>()) return;
        var go = new GameObject("ClockHud");
        DontDestroyOnLoad(go);
        go.AddComponent<ClockHud>();
    }

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        bg.SetParent(transform, false);
        bg.anchorMin = bg.anchorMax = bg.pivot = Vector2.one;
        bg.anchoredPosition = new Vector2(-16f, -16f);
        bg.sizeDelta = new Vector2(360f, 44f);
        var img = bg.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, .5f);
        img.raycastTarget = false;

        var t = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        t.SetParent(bg, false);
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.offsetMin = t.offsetMax = Vector2.zero;
        label = t.gameObject.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24f;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 24f;
    }

    void Update()
    {
        var clock = GameClock.Current;
        if (clock == null) return;
        // The Korean font lives on the player prefab, which spawns later; keep retrying until it exists.
        var font = PlayerInteraction.Local ? PlayerInteraction.Local.uiFont : null;
        if (font && label.font != font) { label.font = font; lastMinute = -1; }
        if (clock.Now.TotalMinutes == lastMinute) return;

        lastMinute = clock.Now.TotalMinutes;
        label.text = clock.Now.ToString();
    }
}
