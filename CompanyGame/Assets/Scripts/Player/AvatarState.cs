using System.IO;
using UnityEngine;

/// <summary>
/// A player avatar's look and pose on a sync-layer server (Docs/Multiplayer_Redesign.md 12.7): gender, the ridden
/// vehicle's item id, ground speed, sitting and the held item's id, and the <see cref="PunchEvent"/>. The owner
/// (AvatarSync) calls <see cref="Set"/> every frame; a state goes out only on a change, and speed only once it is
/// <see cref="SpeedStep"/> away from the sent value (or stops), so standing or walking steadily sends no AvatarState.
/// Other machines hand it to the RemoteAvatarView with the owner's character name (AvatarSync.NameOf, from Peer).
/// </summary>
public sealed class AvatarState : NetBehaviour
{
    public const byte PunchEvent = 1;
    const float SpeedStep = 1f, Stopped = .05f;

    public bool female, seated;
    public string vehicle = "", held = "";
    public float speed;

    RemoteAvatarView view;

    public void Set(bool female, string vehicle, float speed, bool seated, string held)
    {
        vehicle ??= ""; held ??= "";
        bool changed = female != this.female || seated != this.seated || vehicle != this.vehicle || held != this.held
                       || Mathf.Abs(speed - this.speed) >= SpeedStep || (speed < Stopped) != (this.speed < Stopped);
        if (!changed) return;
        this.female = female; this.seated = seated; this.vehicle = vehicle; this.held = held; this.speed = speed;
        MarkDirty();
    }

    /// <summary>Everyone on the map (this machine too) swings this avatar's arm once.</summary>
    public void SendPunch() => SendEvent(PunchEvent, EventTarget.Map, null);

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

    public override void OnEvent(byte id, uint sender, BinaryReader r)
    {
        if (id == PunchEvent && view) view.Punch();
    }

    void Awake() => view = GetComponent<RemoteAvatarView>(); // none on my own hidden copy (AvatarSync)

    void Update()
    {
        if (!view || IsOwner) return;
        view.female = female; view.seated = seated; view.vehicle = vehicle; view.held = held; view.speed = speed;
        view.playerName = AvatarSync.NameOf(Entity.Owner);
    }
}
