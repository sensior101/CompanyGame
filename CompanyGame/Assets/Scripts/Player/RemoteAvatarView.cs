using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// How another player looks on a Resources/Net/RemotePlayer avatar: the male or female look, the ridden vehicle's model
/// (in a car only the car is seen), bicycle pedalling, the punch swing, sitting, the held item and the NameTag.
/// AvatarState (sync-layer servers, Docs/Multiplayer_Redesign.md 12.7) sets the fields and moves the transform. DaldongneAvatarMotion animates the walk.
/// </summary>
[DefaultExecutionOrder(500)] // after DaldongneAvatarMotion's LateUpdate, so the punch and the held-item arm override the walk arm
public sealed class RemoteAvatarView : MonoBehaviour
{
    const float PunchTime = .3f;
    public const int NameMax = 12;

    public bool female, seated;
    /// <summary>Item ids ("" = none).</summary>
    public string vehicle = "", held = "";
    /// <summary>Ground speed in m/s (bicycle pedals).</summary>
    public float speed;
    /// <summary>Shown on the NameTag as it is: pass it through <see cref="CleanName"/>.</summary>
    public string playerName = "";

    Transform male, femaleVisual, heldRoot;
    NameTag nameTag;
    GameObject car;
    PlayerHeldItem heldBuilder;
    DaldongneAvatarMotion seatedMotion;
    string carItem = "", heldItem = "";
    float pedal, punchAt = -10f;
    bool isCar;

    /// <summary>Names come from other machines: plain text (no TMP tags) and short.</summary>
    public static string CleanName(string name)
    {
        name = (name ?? "").Replace("<", "").Replace(">", "").Trim();
        return name.Length > NameMax ? name.Substring(0, NameMax) : name;
    }

    /// <summary>One swing of the right arm, starting now.</summary>
    public void Punch() => punchAt = Time.time;

    void Awake()
    {
        male = transform.Find("MaleVisual");
        femaleVisual = transform.Find("FemaleVisual");
        nameTag = NameTag.Ensure(gameObject, playerName); // same billboard as the local player's tag
    }

    void Update()
    {
        UpdateVehicle();
        // In a car only the car is seen (same as the local driver); on foot or on a bicycle the body shows.
        if (male) male.gameObject.SetActive(!isCar && !female);
        if (femaleVisual) femaleVisual.gameObject.SetActive(!isCar && female);
        var motion = Motion();
        if (motion && car && !isCar)
        {
            var item = InventorySnapshot.Find(carItem);
            pedal += speed * Time.deltaTime / Mathf.Max(.1f, item ? item.wheelRadius : .35f) * .45f;
            motion.SetCycling(true, pedal);
        }
        UpdateSeated(seated && !car ? motion : null);
        UpdateHeld();
        nameTag.Text = playerName;
    }

    void UpdateVehicle()
    {
        if (vehicle == carItem) return;
        if (car) Destroy(car);
        car = null; isCar = false; carItem = vehicle;
        foreach (var motion in GetComponentsInChildren<DaldongneAvatarMotion>(true)) motion.SetCycling(false, 0f);
        var item = InventorySnapshot.Find(vehicle);
        if (!item || !item.vehiclePrefab) return;
        // The local driver's pivot is the car's pivot (driver's seat), so the model sits at the avatar's origin.
        car = Instantiate(item.vehiclePrefab, transform, false);
        foreach (var collider in car.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        foreach (var child in car.GetComponentsInChildren<Transform>(true))
            if (child.name == "Kickstand") child.gameObject.SetActive(false);
        isCar = item.IsCar;
    }

    // The same pose as the local player on a seat (PlayerSeating → DaldongneAvatarMotion.SetSeated); the position is the seat's already.
    void UpdateSeated(DaldongneAvatarMotion motion)
    {
        if (motion == seatedMotion) return;
        if (seatedMotion) seatedMotion.SetSeated(false);
        seatedMotion = motion;
        if (motion) motion.SetSeated(true);
    }

    // The same model the local hand shows (PlayerHeldItem), built by a disabled PlayerHeldItem of this avatar.
    void UpdateHeld()
    {
        if (held == heldItem) return;
        heldItem = held;
        if (heldRoot) Destroy(heldRoot.gameObject);
        heldRoot = null;
        var item = InventorySnapshot.Find(held);
        if (!item) return;
        if (!heldBuilder) { heldBuilder = gameObject.AddComponent<PlayerHeldItem>(); heldBuilder.enabled = false; }
        heldRoot = new GameObject("RightHandHeldItem").transform;
        heldRoot.SetParent(transform, false);
        heldBuilder.BuildWorldModel(item, heldRoot);
    }

    DaldongneAvatarMotion Motion()
    {
        var visual = female ? femaleVisual : male;
        return visual ? visual.GetComponentInChildren<DaldongneAvatarMotion>() : null;
    }

    void LateUpdate()
    {
        var motion = Motion();
        bool holding = heldRoot && !car;
        if (heldRoot) heldRoot.gameObject.SetActive(holding && motion);
        if (!motion || !motion.rightArm || isCar) return;
        var arm = motion.rightArm;
        var forearm = motion.rightForearm;
        // The local third-person hold (PlayerHeldItem): upper arm a little forward, forearm bent up.
        if (holding)
        {
            arm.localRotation = motion.RightArmRest * Quaternion.Euler(forearm ? -12f : -25f, 0f, 0f);
            if (forearm) forearm.localRotation = Quaternion.Euler(-58f, 0f, 0f);
        }
        // The same swing as the local third-person punch (PlayerHeldItem): right arm forward, then back.
        float elapsed = Time.time - punchAt;
        if (elapsed < PunchTime)
        {
            float amount = Mathf.Sin(elapsed / PunchTime * Mathf.PI);
            arm.localRotation = Quaternion.Slerp(arm.localRotation, motion.RightArmRest * Quaternion.Euler(-76f, 0f, -3f), amount);
        }
        if (!holding) return;
        // ponytail: the hand is taken as one upper-arm length past the elbow, along the forearm; PlayerHeldItem measures
        // the skinned mesh instead. Measure the same way if a held item floats off a remote hand.
        Vector3 hand = forearm ? forearm.TransformPoint(forearm.localPosition) : arm.TransformPoint(Vector3.down * .5f);
        heldRoot.SetPositionAndRotation(hand + transform.up * .055f, transform.rotation);
    }
}
