using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Put on any NPC to trade with it: the player walks up and presses Space, and the shared trade window opens.
/// The range follows the NPC, so walking NPCs can trade too.
/// </summary>
[DisallowMultipleComponent]
public sealed class NpcTrader : MonoBehaviour
{
    public string prompt = "거래하기";
    [Min(.1f)] public float radius = 2f;
    [Min(.1f)] public float heightTolerance = 1f;
    public TradeOffer[] offers = System.Array.Empty<TradeOffer>();
    [Tooltip("Open the common trade window after this NPC's greeting dialogue.")]
    public bool openAfterDialogue;

    static readonly HashSet<NpcTrader> active = new HashSet<NpcTrader>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => active.Clear();

    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);

    public bool IsInRange(Transform player)
    {
        if (!player || !isActiveAndEnabled || gameObject.scene != SceneManager.GetActiveScene()) return false;
        Vector3 d = player.position - transform.position;
        return Mathf.Abs(d.y) <= heightTolerance && d.x * d.x + d.z * d.z <= radius * radius;
    }

    public static NpcTrader FindNearest(Transform player)
    {
        NpcTrader nearest = null;
        float best = float.PositiveInfinity;
        foreach (var trader in active)
        {
            if (!trader || !trader.IsInRange(player)) continue;
            float d = (trader.transform.position - player.position).sqrMagnitude;
            if (d < best) { best = d; nearest = trader; }
        }
        return nearest;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
