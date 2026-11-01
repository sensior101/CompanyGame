using System.IO;
using UnityEngine;

/// <summary>
/// A player avatar's look and pose on a sync-layer server (Docs/Multiplayer_Redesign.md 12.7): gender, the ridden
/// vehicle's item id, ground speed, sitting and the held item's id, and the <see cref="PunchEvent"/>. The owner
/// (AvatarSync) calls <see cref="Set"/> every frame; a state goes out only on a change, and speed only once it is
/// <see cref="SpeedStep"/> away from the sent value (or stops), so standing or walking steadily sends no AvatarState.
/// Other machines hand it to the RemoteAvatarView with the owner's character name (AvatarSync.NameOf, from Peer).
/// Attacks use server intents and authenticated swing/results. The legacy IDamageable surface remains for callers,
/// but a remote copy never accepts client-authored damage. ServerCombat owns contact timing and health.
/// </summary>
public sealed class AvatarState : NetBehaviour, IDamageable
{
    public const byte PunchEvent = 1, HitEvent = 2;
    public const byte AttackIntentEvent = 3, SwingEvent = 4, ResultEvent = 5, ReactionEvent = 6, HealthEvent = 7;
    const float SpeedStep = 1f, Stopped = .05f;

    public bool female, seated;
    public string vehicle = "", held = "";
    public float speed;

    RemoteAvatarView view;
    int lastSwingStep;

    public void Set(bool female, string vehicle, float speed, bool seated, string held)
    {
        vehicle ??= ""; held ??= "";
        bool changed = female != this.female || seated != this.seated || vehicle != this.vehicle || held != this.held
                       || Mathf.Abs(speed - this.speed) >= SpeedStep || (speed < Stopped) != (this.speed < Stopped);
        if (!changed) return;
        this.female = female; this.seated = seated; this.vehicle = vehicle; this.held = held; this.speed = speed;
        MarkDirty();
    }

    /// <summary>Legacy call kept source-compatible; only a current owner combat swing may become an intent.</summary>
    public void SendPunch()
    {
        var player = PlayerSpawner.Player;
        var combat = player ? player.GetComponent<PlayerCombat>() : null;
        if (combat) SendAttack(combat);
    }

    public void SendAttack(PlayerCombat combat)
    {
        if (!IsOwner || !combat || combat.SwingSequence == 0) return;
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (inventory == null) return;
        // Flush current pose/position before the intent; NetTransform's normal 10 Hz cadence remains otherwise.
        GetComponent<NetTransform>()?.MarkDirty();
        MarkDirty();
        SendEvent(AttackIntentEvent, EventTarget.Server, w =>
        {
            w.Write(combat.SwingSequence); w.Write((byte)(combat.LastAttackWasShot ? 0 : combat.ComboStep)); w.Write(combat.LastAttackWasShot);
            w.Write((byte)inventory.SelectedHotbarIndex);
            Vector3 direction = combat.AttackDirection;
            w.Write(direction.x); w.Write(direction.y); w.Write(direction.z);
        });
    }

    // u8 bits (1 female, 2 seated), u8 speed in 0.1 m/s, str vehicle, str held
    public override void Write(BinaryWriter w)
    {
        w.Write((byte)((female ? 1 : 0) | (seated ? 2 : 0)));
        w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(speed * 10f), 0, 255));
        w.Write(vehicle);
        w.Write(held);
    }

    public override void Read(BinaryReader r)
    {
        byte bits = r.ReadByte();
        female = (bits & 1) != 0; seated = (bits & 2) != 0;
        speed = r.ReadByte() / 10f;
        vehicle = r.ReadString();
        held = r.ReadString(); // unknown ids show nothing (InventorySnapshot.Find)
    }

    /// <summary>Kept for existing raycast callers. Only ServerCombat may damage a network player.</summary>
    public bool TakeDamage(float amount, GameObject attacker, Vector3 hitPoint) => false;

    public override void OnEvent(byte id, uint sender, BinaryReader r)
    {
        if (sender != 0) return; // authenticated server outcomes only, including old event identifiers.
        var player = PlayerSpawner.Player;
        var combat = IsOwner && player ? player.GetComponent<PlayerCombat>() : null;
        if (id == SwingEvent)
        {
            uint sequence = r.ReadUInt32(); int step = r.ReadByte(); bool shot = r.ReadBoolean();
            lastSwingStep = step;
            float duration = r.ReadSingle(), contact = r.ReadSingle();
            if (combat) combat.ReceiveServerSwing(sequence, step, duration, contact);
            else if (view && !shot)
            {
                view.BeginCombatSwing(step, duration, contact);
                RemoteFeedback().PlaySwing();
            }
        }
        else if (id == ResultEvent)
        {
            uint sequence = r.ReadUInt32(); bool hit = r.ReadBoolean();
            var point = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float damage = r.ReadSingle();
            if (combat) combat.ReceiveServerResult(sequence, hit, point, damage);
            else if (view && hit) view.ConfirmCombatImpact(lastSwingStep == 2);
        }
        else if (id == HealthEvent)
        {
            float health = r.ReadSingle();
            if (combat) combat.ReceiveServerHealth(health);
            else if (IsOwner && player && player.TryGetComponent<PlayerStats>(out var stats)) stats.ApplyAuthoritativeHealth(health);
            else if (view) view.SynchronizeCombatHealth(health);
        }
        else if (id == ReactionEvent) OnHit(combat, r);
    }

    void OnHit(PlayerCombat combat, BinaryReader r)
    {
        uint attacker = r.ReadUInt32(); r.ReadUInt32(); int step = r.ReadByte();
        float health = r.ReadSingle(), damage = r.ReadSingle();
        Vector3 direction = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Vector3 point = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); bool face = r.ReadBoolean();
        bool heavy = r.ReadBoolean(), down = r.ReadBoolean(); float stun = r.ReadSingle();
        if (combat) combat.ReceiveServerHit(health, direction, damage, heavy, step, down, stun, point);
        if (view)
        {
            view.ReceiveCombatHit(down ? "Knockdown" : heavy ? "Hit_Heavy" : face ? "Hit_Face" : "Hit_Body",
                transform.InverseTransformDirection(-direction), down ? .85f : stun, heavy ? .065f : .045f, health <= 0f);
            RemoteFeedback().PlayImpact(heavy, true);
        }
        if (IsOwner)
        {
            string name = AvatarSync.NameOf(attacker);
            WorldStateSync.Say((name.Length > 0 ? name : "누군가") + "님에 의해 폭행 당했습니다.");
        }
    }

    CombatFeedback RemoteFeedback()
    {
        var feedback = GetComponent<CombatFeedback>();
        return feedback ? feedback : gameObject.AddComponent<CombatFeedback>();
    }

    void Awake() => view = GetComponent<RemoteAvatarView>(); // none on my own hidden copy (AvatarSync)

    void Update()
    {
        if (!view || IsOwner) return;
        view.female = female; view.seated = seated; view.vehicle = vehicle; view.held = held; view.speed = speed;
        view.playerName = AvatarSync.NameOf(Entity.Owner);
    }
}
