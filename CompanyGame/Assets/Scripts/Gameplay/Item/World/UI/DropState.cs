using System;
using System.IO;
using UnityEngine;

/// <summary>
/// A dropped item or a placed vehicle on a sync-layer server (Docs/Multiplayer_Redesign.md 12.8). The entity is a hidden
/// empty object (<see cref="PrefabKey"/>, a runtime template like AvatarSync's); WorldDroppedItem keeps the record and the
/// visual, keyed by the entity id. State: u8 kind, f32 x y z, f32 yaw, blob contents = gzip(InventorySnapshot.Export of
/// the drop's one-stack inventory). A book goes without its pages (the server keeps them, 12.10.3), so a drop stays far
/// under the 4KB of an entity blob. The bytes are DropBlob's (the server makes these entities from its ledger, 12.10.5).
/// </summary>
public sealed class DropState : NetBehaviour
{
    public const string PrefabKey = DropBlob.PrefabKey;
    public const byte ItemKind = DropBlob.ItemKind, VehicleKind = DropBlob.VehicleKind;

    static GameObject template;

    public byte kind;
    public Vector3 position;
    public float yaw;
    public byte[] contents = Array.Empty<byte>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Register()
    {
        // SyncClient resets PrefabFactory at SubsystemRegistration, before this.
        var fallback = SyncClient.PrefabFactory;
        SyncClient.PrefabFactory = key => key == PrefabKey ? Template() : fallback?.Invoke(key);
    }

    /// <summary>Inactive; SyncClient activates each copy after giving it its state.</summary>
    public static GameObject Template()
    {
        if (template) return template;
        template = new GameObject("WorldDrop");
        template.SetActive(false);
        DontDestroyOnLoad(template);
        template.AddComponent<NetEntity>();
        template.AddComponent<DropState>();
        return template;
    }

    public static byte[] Pack(InventoryState inventory) => DropBlob.Pack(inventory);

    /// <summary>The contents into <paramref name="inventory"/> (replaced); false when unreadable.</summary>
    public static bool Unpack(byte[] contents, InventoryState inventory) => DropBlob.Unpack(contents, inventory);

    public override void Write(BinaryWriter w)
    {
        DropBlob.Write(w, kind, position, yaw, contents);
    }

    public override void Read(BinaryReader r)
    {
        kind = r.ReadByte();
        position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        yaw = r.ReadSingle();
        contents = SyncProtocol.ReadBlob(r);
        WorldDroppedItem.SyncState(this);
    }

    void Awake() => Entity.OwnerChanged += OnOwner;
    void OnOwner() => WorldDroppedItem.SyncOwner(this);

    void OnDestroy()
    {
        if (Entity) Entity.OwnerChanged -= OnOwner;
        WorldDroppedItem.SyncGone(this);
    }
}
