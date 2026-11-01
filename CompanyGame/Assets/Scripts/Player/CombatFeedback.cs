using UnityEngine;

/// <summary>Character-local audio and feedback. Camera offsets are applied by the existing camera controller.</summary>
[DisallowMultipleComponent]
public sealed class CombatFeedback : MonoBehaviour
{
    [Header("Optional recordings (empty arrays use synthesized basic sounds)")]
    public AudioClip[] swingClips;
    public AudioClip[] hitClips;
    public AudioClip[] heavyHitClips;
    [Range(0f, 1f)] public float volume = .55f;
    [Range(0f, .2f)] public float pitchVariation = .07f;
    public bool synthesizeWhenEmpty = true;
    public bool cameraShake = true;
    AudioSource source;
    AudioClip fallbackSwing, fallbackHit, fallbackHeavy;
    int lastSwing = -1, lastHit = -1, lastHeavy = -1;

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = 1.5f; source.maxDistance = 18f;
        source.rolloffMode = AudioRolloffMode.Linear;
    }

    public void PlaySwing() => Play(swingClips, ref lastSwing, ref fallbackSwing, 0);

    public void PlayImpact(bool heavy, bool victim)
    {
        // The victim emits the world-space impact; the attacker only gets its own camera response.
        if (victim)
        {
            if (heavy) Play(heavyHitClips, ref lastHeavy, ref fallbackHeavy, 2);
            else Play(hitClips, ref lastHit, ref fallbackHit, 1);
        }
        var movement = GetComponent<PlayerMovement>();
        if (!cameraShake || !movement || !movement.viewCamera) return;
        if (PlayerSpawner.Player && PlayerSpawner.Player != movement) return;
        var camera = movement.viewCamera.GetComponent<PlayerCameraController>();
        if (camera && camera.target == transform) camera.AddCombatShake(heavy ? .11f : .065f, heavy ? .65f : .35f);
    }

    void Play(AudioClip[] clips, ref int last, ref AudioClip fallback, int kind)
    {
        if (!source) return;
        AudioClip clip = null;
        if (clips != null && clips.Length > 0)
        {
            int index = Random.Range(0, clips.Length);
            if (clips.Length > 1 && index == last) index = (index + 1) % clips.Length;
            last = index; clip = clips[index];
        }
        if (!clip && synthesizeWhenEmpty)
        {
            if (!fallback) fallback = Synthesize(kind);
            clip = fallback;
        }
        if (!clip) return;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(clip, volume);
    }

    static AudioClip Synthesize(int kind)
    {
        const int rate = 22050;
        float duration = kind == 0 ? .14f : kind == 1 ? .11f : .18f;
        var data = new float[Mathf.CeilToInt(rate * duration)];
        var random = new System.Random(4723 + kind);
        float filtered = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate, x = t / duration;
            float noise = (float)random.NextDouble() * 2f - 1f;
            filtered = Mathf.Lerp(filtered, noise, kind == 0 ? .12f : .32f);
            float envelope = kind == 0 ? Mathf.Sin(x * Mathf.PI) * (1f - x) : Mathf.Exp(-x * 7f) * Mathf.Min(1f, x * 70f);
            float body = Mathf.Sin(2f * Mathf.PI * (kind == 2 ? 65f : 110f) * t);
            data[i] = envelope * (kind == 0 ? filtered * .8f : filtered * .42f + body * .4f);
        }
        var clip = AudioClip.Create(kind == 0 ? "Basic melee swish" : kind == 1 ? "Basic melee impact" : "Basic heavy impact", data.Length, 1, rate, false);
        clip.hideFlags = HideFlags.DontSave;
        clip.SetData(data, 0);
        return clip;
    }

    void OnDestroy()
    {
        if (source) Destroy(source);
        if (fallbackSwing) Destroy(fallbackSwing);
        if (fallbackHit) Destroy(fallbackHit);
        if (fallbackHeavy) Destroy(fallbackHeavy);
    }
}
