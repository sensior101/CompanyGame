using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Put on any NPC to trade with it: the player walks up and presses Space, and the shared trade window opens.
/// The range follows the NPC, so walking NPCs can trade too. Its offers are shop <see cref="shopId"/> of the TradeCatalog
/// (12.10.4), the same data a sync-layer server decides trades with.
/// </summary>
[DisallowMultipleComponent]
public sealed class NpcTrader : MonoBehaviour
{
    public string prompt = "거래하기";
    [Tooltip("Shown above the head. Empty falls back to DialogueData.displayName, then \"NPC\".")]
    public string displayName;
    [Min(.1f)] public float radius = 2f;
    [Min(.1f)] public float heightTolerance = 1f;
    [Tooltip("The shop in Resources/Trade/TradeCatalog (e.g. convenience).")]
    public string shopId;
    [Tooltip("Open the common trade window after this NPC's greeting dialogue.")]
    public bool openAfterDialogue;

    static readonly HashSet<NpcTrader> active = new HashSet<NpcTrader>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => active.Clear();

    void OnEnable() { active.Add(this); NameTag.Ensure(gameObject, DialogueData.ResolveName(gameObject)); }
    void OnDisable() => active.Remove(this);

    /// <summary>The shop's offers, or null (logged) when the shop is not in the catalog: then no trade window opens.</summary>
    public TradeOffer[] Offers
    {
        get
        {
            var offers = TradeCatalog.Offers(shopId);
            if (offers == null) Debug.LogWarning("[NpcTrader] " + name + ": shop '" + shopId + "' is not in the TradeCatalog; no trade", this);
            return offers;
        }
    }

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
