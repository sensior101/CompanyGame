using UnityEngine;

/// <summary>
/// Walk / don't-walk box at the end of a signalled crosswalk. Green figure in the junction's walk phase (steady while
/// pedestrians may start, blinking when the rest is too short to start), red standing figure otherwise; cars never have
/// green then (TrafficSignal, 12.14). Pure function of the signal, so it needs no extra network sync.
/// </summary>
public sealed class PedestrianSignal : MonoBehaviour
{
    public Crosswalk walk;
    public Renderer stand, go;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Color RedOn = new Color(1f, .1f, .08f), GreenOn = new Color(.2f, 1f, .4f), Off = new Color(.03f, .03f, .03f);
    static MaterialPropertyBlock block;
    int shown = -1;

    /// <summary>0 = don't walk, 1 = steady walk (<see cref="Crosswalk.CanCross"/>: citizens may start), 2/3 = blinking dark/lit (don't start).</summary>
    public static int StateOf(Crosswalk walk)
    {
        var signal = walk ? walk.signal : null;
        if (!signal) return 1;
        if (walk.CanCross) return 1;
        if (!signal.WalkAt(TrafficSignal.Clock, out _, out _)) return 0;
        return ((int)(TrafficSignal.Clock * 2.0) & 1) == 0 ? 2 : 3;
    }

    void Update()
    {
        int state = StateOf(walk);
        if (state == shown) return;
        shown = state;
        Tint(stand, state == 0 ? RedOn : Off);
        Tint(go, state == 1 || state == 3 ? GreenOn : Off);
    }

    static void Tint(Renderer r, Color c)
    {
        if (!r) return;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        r.SetPropertyBlock(block);
    }
}
