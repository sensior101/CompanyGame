using System.Collections.Generic;
using UnityEngine;

/// <summary>One usable seat position. A sofa may expose several positions on the same fixed fixture.</summary>
public sealed class Seat : MonoBehaviour
{
    public WorldObject owner;
    [Min(.5f)] public float radius = 2f;
    [Tooltip("Fixed seated actor, if this position is assigned to an NPC.")]
    public Transform assignedOccupant;
    Transform occupant;
    public Transform Occupant => assignedOccupant && assignedOccupant.gameObject.activeInHierarchy ? assignedOccupant :
        occupant && occupant.gameObject.activeInHierarchy ? occupant : null;
    static readonly HashSet<Seat> active = new HashSet<Seat>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => active.Clear();
    void OnEnable() => active.Add(this);
    void OnDisable() { active.Remove(this); occupant = null; }
    public bool InRange(Transform player)
    {
        if (!player || !owner || !owner.HasFunction(FurnitureFunction.Seating) || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
        Vector3 delta = player.position - transform.position;
        return Mathf.Abs(delta.y) < 1.5f && new Vector2(delta.x, delta.z).sqrMagnitude <= radius * radius;
    }
    public bool Reserve(Transform player)
    {
        if (Occupant || !InRange(player)) return false;
        occupant = player; return true;
    }
    public void Release(Transform player) { if (occupant == player) occupant = null; }
    public static Seat FindNearest(Transform player)
    {
        Seat nearest = null; float best = float.PositiveInfinity;
        foreach (var seat in active)
        {
            if (!seat || !seat.InRange(player)) continue;
            float distance = (seat.transform.position - player.position).sqrMagnitude;
            if (distance >= best) continue;
            Vector3 start = player.position + Vector3.up * 1.2f;
            Vector3 ray = seat.transform.position + Vector3.up * .5f - start;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(start, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(player) && !hit.transform.IsChildOf(seat.owner.transform) &&
                    !(seat.Occupant && hit.transform.IsChildOf(seat.Occupant))) { blocked = true; break; }
            if (blocked) continue;
            nearest = seat; best = distance;
        }
        return nearest && !nearest.Occupant ? nearest : null;
    }
}
