using System.IO;

/// <summary>
/// SyncLayerQA's test component: no state, remembers the last event it received. Its own assembly is a runtime one
/// (components from Editor assemblies cannot be attached) limited to the editor by the UNITY_EDITOR constraint, so builds never get it.
/// </summary>
public sealed class SyncQaProbe : NetBehaviour
{
    public int lastEvent = -1;
    public uint lastSender;
    public byte lastValue;

    public override void Write(BinaryWriter w) { }
    public override void Read(BinaryReader r) { }

    public override void OnEvent(byte id, uint sender, BinaryReader r)
    {
        lastEvent = id; lastSender = sender; lastValue = r.ReadByte();
    }
}
