using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batchmode Play Mode probe: opens BusinessDistrict, plays, logs lamp colours of a few heads over time, exits.</summary>
[InitializeOnLoad]
public static class TrafficPlayProbe
{
    const string Flag = "TrafficPlayProbe";
    static double startedAt;
    static int logged;

    static TrafficPlayProbe() => EditorApplication.playModeStateChanged += OnState;

    public static void Run()
    {
        EditorSceneManager.OpenScene(TrafficMapBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Flag, false) || change != PlayModeStateChange.EnteredPlayMode) return;
        startedAt = EditorApplication.timeSinceStartup;
        logged = 0;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        double t = EditorApplication.timeSinceStartup - startedAt;
        if (t > logged * 6 + 2 && logged < 4) { Dump(t); logged++; }
        if (t < 26) return;
        EditorApplication.update -= Tick;
        SessionState.SetBool(Flag, false);
        EditorApplication.Exit(0);
    }

    static void Dump(double t)
    {
        var sb = new StringBuilder($"PROBE t={t:F1} Time.time={Time.timeAsDouble:F1} timeScale={Time.timeScale}\n");
        TrafficSignal[] all = Object.FindObjectsByType<TrafficSignal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"signals={all.Length}");
        int n = 0;
        foreach (TrafficSignal s in all)
        {
            if (n++ >= 3) break;
            sb.AppendLine($" {s.name} active={s.gameObject.activeInHierarchy} enabled={s.enabled} phase={s.Current} left={s.TimeLeft:F1} heads={s.heads.Length}");
            foreach (TrafficSignal.Head h in s.heads)
            {
                if (h == null || !h.red) continue;
                sb.AppendLine($"   red={Lamp(h.red)} yellow={Lamp(h.yellow)} green={Lamp(h.green)}");
                break;
            }
        }
        Debug.Log(sb.ToString());
    }

    static string Lamp(Renderer r)
    {
        var b = new MaterialPropertyBlock();
        r.GetPropertyBlock(b);
        Material m = r.sharedMaterial;
        return $"[base={b.GetColor("_BaseColor")} emis={b.GetColor("_EmissionColor")} blockEmpty={b.isEmpty} kw={(m ? m.IsKeywordEnabled("_EMISSION") : false)} shader={(m ? m.shader.name : "-")}]";
    }
}
