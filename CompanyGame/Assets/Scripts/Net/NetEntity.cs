using System;
using UnityEngine;

/// <summary>
/// A shared object. The owner makes it with <see cref="SyncClient.Spawn"/>; everyone on the same map gets a copy made from
/// the same prefab key. Destroying the owner's object despawns it everywhere. The <see cref="NetBehaviour"/>s on this
/// GameObject are numbered in GetComponents order, which is the same on every machine because the prefab is the same.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetEntity : MonoBehaviour
{
    public ulong Id { get; private set; }
    /// <summary>Connection number of the owner (0 = the server).</summary>
    public uint Owner { get; private set; }
    public string PrefabKey { get; private set; } = "";
    public EntityFlags Flags { get; private set; }
    public string Map { get; internal set; } = "";
    public bool IsSpawned => Id != 0;
    public bool IsMine => Id != 0 && SyncClient.Instance && SyncClient.Instance.Connected && Owner == SyncClient.Instance.MyConnection;

    /// <summary>
    /// Raised on every Owner message from the server: a Claim, a Release, the owner leaving a PersistOnLeave entity, and
    /// also a refused Claim's answer, which repeats the unchanged owner (so a claimer always hears back).
    /// </summary>
    public event Action OwnerChanged;

    internal NetBehaviour[] Behaviours { get; private set; } = new NetBehaviour[0];
    /// <summary>Set when the server despawned it (or the connection ended), so OnDestroy does not send a Despawn back.</summary>
    internal bool RemovedByServer { get; set; }

    internal void Init(ulong id, uint owner, string prefabKey, EntityFlags flags, string map)
    {
        Id = id; Owner = owner; PrefabKey = prefabKey ?? ""; Flags = flags; Map = map ?? "";
        Behaviours = GetComponents<NetBehaviour>();
    }

    internal void SetOwner(uint owner)
    {
        Owner = owner;
        OwnerChanged?.Invoke();
    }

    void OnDestroy()
    {
        if (SyncClient.Instance) SyncClient.Instance.Forget(this);
    }
}
