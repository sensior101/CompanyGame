using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// A staff NPC that sits in one chair all day (office desks, counters). Placed where a seated player would be
/// (PlayerSeating: the seat point minus <see cref="SeatDrop"/>), it claims the seat under it so the player cannot sit
/// there, and poses its avatar seated. The seat is found at runtime because furniture seat objects can be regenerated
/// (FurnitureCollectionImporter.RefreshHouseholdSeats), which would break a saved reference.
/// </summary>
[DisallowMultipleComponent]
public sealed class SeatedStaff : MonoBehaviour
{
    public const float SeatDrop = .44f;
    const float SearchRadius = .3f;

    public Seat Seat { get; private set; }

    /// <summary>The seat whose sitting position is closest to <paramref name="root"/> (within 0.3 m), or null.</summary>
    public static Seat FindSeat(Transform root)
    {
        Seat best = null;
        float bestDistance = SearchRadius * SearchRadius;
        Vector3 point = root.position + Vector3.up * SeatDrop;
        foreach (var seat in root.gameObject.scene.IsValid() ? FindObjectsByType<Seat>(FindObjectsInactive.Include, FindObjectsSortMode.None) : new Seat[0])
        {
            if (seat.gameObject.scene != root.gameObject.scene) continue;
            float d = (seat.transform.position - point).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = seat; }
        }
        return best;
    }

    void OnEnable()
    {
        Seat = FindSeat(transform);
        if (!Seat) return;
        Seat.assignedOccupant = transform;
        transform.SetPositionAndRotation(Seat.transform.position - Vector3.up * SeatDrop, Seat.transform.rotation);
    }

    void OnDisable()
    {
        if (Seat && Seat.assignedOccupant == transform) Seat.assignedOccupant = null;
    }

    // After the avatar's own setup, so its neutral pose does not replace the seated one.
    void Start()
    {
        var motion = GetComponentInChildren<DaldongneAvatarMotion>(true);
        if (motion) motion.SetSeated(true);
    }
}
