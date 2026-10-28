using UnityEngine;

/// <summary>Per-grade car performance. Shared by the player's car and NPC traffic; null on an item keeps the bicycle model.</summary>
[CreateAssetMenu(menuName = "Company Game/Vehicle Grade", fileName = "VehicleGrade")]
public sealed class VehicleGrade : ScriptableObject
{
    public string displayName;
    [Range(30f, 400f)] public float topSpeedKmh = 120f;
    [Range(.5f, 60f)] public float zeroTo100Sec = 10f;
    [Min(1f)] public float brakeMps2 = 9f;

    public float TopSpeedMps => topSpeedKmh / 3.6f;
    // Constant acceleration: 100 km/h takes exactly zeroTo100Sec, and the same rate continues to top speed.
    public float AccelMps2 => 100f / 3.6f / Mathf.Max(.01f, zeroTo100Sec);

    /// <summary>Next forward speed (m/s) heading for throttle01 * top speed; accelerates at AccelMps2, slows at brakeMps2.</summary>
    public float Step(float speedMps, float throttle01, float dt)
    {
        float target = Mathf.Clamp01(throttle01) * TopSpeedMps;
        return Mathf.MoveTowards(speedMps, target, dt * (target > speedMps ? AccelMps2 : brakeMps2));
    }
}
