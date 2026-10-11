using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Minecraft-style billboard name above a character's head (world-space TMP + dark translucent backdrop).
/// Lives in Core so NPC scripts can call Ensure; player-only inputs (name source, camera hiding) are injected.
/// </summary>
[DisallowMultipleComponent]
public sealed class NameTag : MonoBehaviour
{
    public const float HeadGap = .35f, BoundsGap = .25f, FallbackHeight = 2.1f;
    const int MaxNameLength = 16;
    const float MaxDistance = 25f, ScalePerMeter = .12f, PadX = .25f, PadY = .1f, MaxWorldHeight = .3f; // height cap keeps the speech bubble (clearance .35) clear

    /// <summary>Set once by the UI layer (Korean font). Falls back to the TMP default.</summary>
    public static TMP_FontAsset Font;
    static Sprite pixel;

    public string Text { get => text; set { value = Clip(value); if (text == value) return; text = value; dirty = true; } }
    /// <summary>Optional polled name (e.g. profile nickname that arrives late).</summary>
    public Func<string> nameSource;
    /// <summary>Optional: tag is hidden while this returns true (first person, dialogue camera).</summary>
    public Func<bool> hideWhen;
    public Transform head;

    string text = "";
    bool dirty;
    float nextPoll, boundsHeight = -1f, tagHeight = .4f;
    GameObject visual;
    TextMeshPro label;
    Transform backdrop;

    /// <summary>Adds a tag to the object if missing (idempotent). An existing tag keeps its text (only a missing head is filled in).</summary>
    public static NameTag Ensure(GameObject target, string name, Transform head = null)
    {
        var tag = target.GetComponent<NameTag>();
        if (!tag) { tag = target.AddComponent<NameTag>(); tag.Text = name; }
        if (head) tag.head = head;
        return tag;
    }

    void OnEnable() { if (visual) visual.SetActive(true); }
    void OnDisable() { if (visual) visual.SetActive(false); }
    void OnDestroy() { if (visual) Destroy(visual); }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (nameSource != null && Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime + .5f; Text = nameSource() ?? ""; }
        float dist = cam ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
        bool show = cam && !string.IsNullOrEmpty(text) && dist <= MaxDistance && (hideWhen == null || !hideWhen());
        if (!show) { if (visual) visual.SetActive(false); return; }
        if (!visual) Build();
        visual.SetActive(true);
        if (dirty) Refresh();
        visual.transform.SetPositionAndRotation(AnchorPosition(), cam.transform.rotation);
        visual.transform.localScale = Vector3.one * Mathf.Min(ScalePerMeter * Mathf.Clamp(dist, 3f, MaxDistance), MaxWorldHeight / tagHeight);
    }

    static string Clip(string v) => string.IsNullOrEmpty(v) || v.Length <= MaxNameLength ? v : v.Substring(0, MaxNameLength);

    Vector3 AnchorPosition()
    {
        if (head) return head.position + Vector3.up * HeadGap;
        if (boundsHeight < 0f) boundsHeight = MeasureHeight(); // stays -1 until renderers exist (avatar may spawn late)
        return transform.position + Vector3.up * (boundsHeight < 0f ? FallbackHeight : boundsHeight);
    }

    float MeasureHeight()
    {
        bool any = false;
        float top = float.NegativeInfinity;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            top = Mathf.Max(top, r.bounds.max.y);
            any = true;
        }
        return any ? top - transform.position.y + BoundsGap : -1f;
    }

    void Build()
    {
        visual = new GameObject("NameTag");
        SceneManager.MoveGameObjectToScene(visual, gameObject.scene);
        var bg = new GameObject("Backdrop");
        backdrop = bg.transform;
        backdrop.SetParent(visual.transform, false);
        var sr = bg.AddComponent<SpriteRenderer>();
        sr.sprite = PixelSprite();
        sr.color = new Color(0, 0, 0, .5f);
        sr.sortingOrder = 0;
        var go = new GameObject("Text");
        go.transform.SetParent(visual.transform, false);
        label = go.AddComponent<TextMeshPro>();
        label.font = Font ? Font : TMP_Settings.defaultFontAsset;
        label.fontSize = 3f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = false;
        label.color = Color.white;
        label.GetComponent<MeshRenderer>().sortingOrder = 1;
        dirty = true;
    }

    void Refresh()
    {
        dirty = false;
        label.text = text;
        label.ForceMeshUpdate();
        Vector2 size = label.GetPreferredValues(text);
        label.rectTransform.sizeDelta = size;
        backdrop.localScale = new Vector3(size.x + PadX, size.y + PadY, 1f);
        // Bottom of the backdrop sits on the anchor; backdrop is a hair behind the text.
        tagHeight = size.y + PadY;
        float mid = tagHeight * .5f;
        backdrop.localPosition = new Vector3(0, mid, .01f);
        label.rectTransform.localPosition = new Vector3(0, mid, 0);
    }

    static Sprite PixelSprite()
    {
        if (pixel) return pixel;
        var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
    }
}
