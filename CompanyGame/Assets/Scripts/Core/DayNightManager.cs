using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Outdoor day/night: dusk starts at 18:00, full dark at 20:00, bright again at 06:00.
/// Scales whatever the scene authored (sun, ambient, skybox, reflections); Interiors scenes are left alone.
/// </summary>
public class DayNightManager : MonoBehaviour
{
    public const float DuskStart = 18f;
    public const float DuskEnd = 20f;

    static readonly Color NightSun = new Color(.35f, .45f, .75f);
    static readonly Color DuskSun = new Color(1f, .55f, .25f);

    Light sun;
    Color sunColor;
    float sunIntensity, ambient, reflection, exposure;
    Material skyInstance;
    int sceneHandle = -1;
    long lastMinute = -1;
    bool outdoor;

    /// <summary>0 = night, 1 = day. Continuous over [6, 24).</summary>
    public static float Daylight(float hour)
    {
        if (hour < GameTime.DayStartHour) return 0f;
        if (hour <= DuskStart) return 1f;
        if (hour >= DuskEnd) return 0f;
        return 1f - Mathf.SmoothStep(0f, 1f, (hour - DuskStart) / (DuskEnd - DuskStart));
    }

    /// <summary>0..1 orange tint weight, peaking at 19:00.</summary>
    public static float SunsetTint(float hour)
    {
        float mid = (DuskStart + DuskEnd) * .5f;
        return Mathf.Clamp01(1f - Mathf.Abs(hour - mid) / (mid - DuskStart));
    }

    void Awake()
    {
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (skyInstance) Destroy(skyInstance);
    }

    void Update()
    {
        var clock = GameClock.Current;
        if (clock == null) return;

        var scene = SceneManager.GetActiveScene();
        if (scene.handle != sceneHandle)
        {
            Capture(scene);
            lastMinute = -1;
        }
        if (!outdoor || clock.Now.TotalMinutes == lastMinute) return;

        lastMinute = clock.Now.TotalMinutes;
        Apply(clock.Now.hour + clock.Now.minute / 60f);
    }

    // Reads the scene's authored values once; a loaded scene starts fresh, so nothing needs restoring.
    void Capture(Scene scene)
    {
        sceneHandle = scene.handle;
        outdoor = !scene.path.Contains("/Interiors/");
        if (skyInstance) Destroy(skyInstance);
        skyInstance = null;
        sun = null;
        if (!outdoor) return;

        sun = FindSun();
        if (sun) { sunColor = sun.color; sunIntensity = sun.intensity; }
        ambient = RenderSettings.ambientIntensity;
        reflection = RenderSettings.reflectionIntensity;
        if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            skyInstance = new Material(RenderSettings.skybox);
            exposure = skyInstance.GetFloat("_Exposure");
            RenderSettings.skybox = skyInstance;
        }
    }

    static Light FindSun()
    {
        if (RenderSettings.sun) return RenderSettings.sun;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    void Apply(float hour)
    {
        float d = Daylight(hour);
        float tint = SunsetTint(hour);

        if (sun)
        {
            sun.intensity = sunIntensity * Mathf.Lerp(.05f, 1f, d);
            var c = Color.Lerp(NightSun, sunColor, d);
            sun.color = Color.Lerp(c, DuskSun, tint * .6f);
        }
        if (RenderSettings.ambientMode == AmbientMode.Skybox)
            RenderSettings.ambientIntensity = ambient * Mathf.Lerp(.15f, 1f, d);
        RenderSettings.reflectionIntensity = reflection * Mathf.Lerp(.15f, 1f, d);
        if (skyInstance) skyInstance.SetFloat("_Exposure", exposure * Mathf.Lerp(.1f, 1f, d));
        // If ambient/reflection lag behind, DynamicGI.UpdateEnvironment() may be needed here.
    }
}
