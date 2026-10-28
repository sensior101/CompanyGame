using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Base of a component with shared values. Write/Read always carry the component's whole state (so a lost or late
/// update is fixed by the next one). The owner calls MarkDirty after a change; SyncClient sends it within 0.1s.
/// Events are one-off actions delivered to the same component on other machines.
/// </summary>
[RequireComponent(typeof(NetEntity))]
public abstract class NetBehaviour : MonoBehaviour
{
    NetEntity entity;

    public NetEntity Entity => entity ? entity : entity = GetComponent<NetEntity>();
    public bool IsOwner => Entity && Entity.IsMine;
    internal bool Dirty { get; set; }

    public abstract void Write(BinaryWriter w);
    public abstract void Read(BinaryReader r);

    /// <summary>Queue this component's state for sending. Does nothing unless this machine owns the entity.</summary>
    public void MarkDirty()
    {
        if (IsOwner) Dirty = true;
    }

    public void SendEvent(byte id, EventTarget target, Action<BinaryWriter> write)
    {
        if (SyncClient.Instance) SyncClient.Instance.SendEvent(this, id, target, write);
    }

    /// <summary>sender = connection number of the machine that sent it.</summary>
    public virtual void OnEvent(byte id, uint sender, BinaryReader r) { }
}
