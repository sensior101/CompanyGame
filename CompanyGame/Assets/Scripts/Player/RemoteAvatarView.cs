using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// How another player looks on a Resources/Net/RemotePlayer avatar: the male or female look, the ridden vehicle's model
/// (in a car only the car is seen), bicycle pedalling, the punch swing, sitting, the held item and the NameTag.
/// AvatarState (sync-layer servers, Docs/Multiplayer_Redesign.md 12.7) sets the fields and moves the transform. DaldongneAvatarMotion animates the walk.
/// </summary>
[DefaultExecutionOrder(500)] // pose owner finishes before held-item attachment
public sealed class RemoteAvatarView : MonoBehaviour, DaldongneAvatarMotion.ICombatPoseSource
{
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
    float pedal, swingElapsed = -1f, swingDuration = .4f, swingContact = .36f;
    int swingStep;
    string hitState = "";
    float hitElapsed, hitDuration, hitStopRemaining;
    float combatFrameDelta;
    Vector3 hitDirection;
    bool stayDown;
    bool isCar;

    /// <summary>Names come from other machines: plain text (no TMP tags) and short.</summary>
    public static string CleanName(string name)
    {
        name = (name ?? "").Replace("<", "").Replace(">", "").Trim();
        return name.Length > NameMax ? name.Substring(0, NameMax) : name;
    }

    /// <summary>Compatibility entry point for older AvatarState payloads.</summary>
    public void Punch() => BeginCombatSwing(0, .4f, .36f);

    public void BeginCombatSwing(int step, float duration, float contact)
    {
        swingStep = Mathf.Clamp(step, 0, 2);
        swingDuration = Mathf.Clamp(duration, .15f, 2f);
        swingContact = Mathf.Clamp(contact, .15f, .7f);
        swingElapsed = 0f;
    }

    public void ConfirmCombatImpact(bool heavy)
    {
        hitStopRemaining = Mathf.Max(hitStopRemaining, heavy ? .065f : .045f);
        combatFrameDelta = 0f;
    }

    public void SynchronizeCombatHealth(float health)
    {
        if (health <= 0f && !stayDown) ReceiveCombatHit("Knockdown", Vector3.forward, .85f, 0f, true);
        else if (health > 0f && stayDown)
        {
            stayDown = false;
            hitState = "";
            hitElapsed = hitStopRemaining = 0f;
        }
    }

    public void ReceiveCombatHit(string poseState, Vector3 localHitDirection, float duration, float hitStop = 0f, bool remainDown = false)
    {
        if (poseState != "Hit_Face" && poseState != "Hit_Body" && poseState != "Hit_Heavy" &&
            poseState != "Knockdown" && poseState != "GetUp") return;
        hitState = poseState;
        stayDown = remainDown && poseState == "Knockdown";
        hitDirection = localHitDirection.sqrMagnitude > .0001f ? localHitDirection.normalized : Vector3.back;
        if (poseState == "Knockdown")
        {
            Vector3 fall = PlayerHitReaction.ClearFallDirection(transform, -transform.TransformDirection(hitDirection));
            hitDirection = transform.InverseTransformDirection(-fall);
        }
        hitDuration = Mathf.Clamp(duration, .1f, 6f);
        hitElapsed = 0f;
        hitStopRemaining = Mathf.Clamp(hitStop, 0f, .15f);
        combatFrameDelta = hitStopRemaining > 0f ? 0f : Time.deltaTime;
        swingElapsed = -1f;
    }

    void UpdateCombat(DaldongneAvatarMotion motion)
    {
        float dt = hitStopRemaining > 0f ? 0f : Time.deltaTime;
        combatFrameDelta = dt;
        hitStopRemaining = Mathf.Max(0f, hitStopRemaining - Time.deltaTime);
        if (swingElapsed >= 0f)
        {
            swingElapsed += dt;
            if (swingElapsed >= swingDuration) swingElapsed = -1f;
        }
        if (!string.IsNullOrEmpty(hitState))
        {
            hitElapsed += dt;
            if (hitElapsed >= hitDuration)
            {
                if (hitState == "Knockdown" && stayDown) hitElapsed = hitDuration;
                else if (hitState == "Knockdown") { hitState = "GetUp"; hitElapsed = 0f; hitDuration = .85f; }
                else hitState = "";
            }
        }
        if (motion) PresentCombat(motion);
    }

    public void PresentCombat(DaldongneAvatarMotion motion) => motion.SetCombatPresentation(swingStep,
        swingElapsed < 0f ? -1f : swingElapsed / swingDuration, swingContact, hitState,
        ReactionNormalizedTime, hitDirection, combatFrameDelta);

    float ReactionNormalizedTime
    {
        get
        {
            float start = hitState == "Knockdown" ? .04f : hitState == "GetUp" ? 0f : .13f;
            return Mathf.Lerp(start, 1f, hitDuration > 0f ? Mathf.Clamp01(hitElapsed / hitDuration) : 0f);
        }
    }

    void Awake()
    {
        male = transform.Find("MaleVisual");
        femaleVisual = transform.Find("FemaleVisual");
        nameTag = NameTag.Ensure(gameObject, playerName); // same billboard as the local player's tag
    }

    void Update()
    {
        UpdateVehicle();
        // Root capsule is the shared melee target; the vehicle supplies its own target while riding.
        var targetCapsule = GetComponent<CapsuleCollider>();
        if (targetCapsule) targetCapsule.enabled = !car;
        // In a car only the car is seen (same as the local driver); on foot or on a bicycle the body shows.
        if (male) male.gameObject.SetActive(!isCar && !female);
        if (femaleVisual) femaleVisual.gameObject.SetActive(!isCar && female);
        var motion = Motion();
        UpdateCombat(motion);
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
        if (motion.HasHumanoidRig)
        {
            motion.ApplyRightArmInteraction(holding ? 1f : 0f, 0f, 0f);
            if (holding) heldRoot.SetPositionAndRotation(motion.RightGripPosition + transform.up * .055f, transform.rotation);
            return;
        }
        // The local third-person hold (PlayerHeldItem): upper arm a little forward, forearm bent up.
        if (holding && !motion.IsClipPlaying && !motion.HasCombatPose)
        {
            arm.localRotation = motion.RightArmRest * Quaternion.Euler(forearm ? -12f : -25f, 0f, 0f);
            if (forearm) forearm.localRotation = Quaternion.Euler(-58f, 0f, 0f);
        }
        if (!holding) return;
        // ponytail: the hand is taken as one upper-arm length past the elbow, along the forearm; PlayerHeldItem measures
        // the skinned mesh instead. Measure the same way if a held item floats off a remote hand.
        Vector3 hand = forearm ? forearm.TransformPoint(forearm.localPosition) : arm.TransformPoint(Vector3.down * .5f);
        heldRoot.SetPositionAndRotation(hand + transform.up * .055f, transform.rotation);
    }
}
