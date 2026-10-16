using System;
using System.IO;

/// <summary>
/// One section of the world entity Net/World (Docs/Multiplayer_Redesign.md 12.9): 0 clock, 1 names, 2..7 the shared
/// economy (stocks, companies, properties, plots, reports, sns). The server owns it and sends each section as a whole
/// blob; a copy hands what it gets to WorldStateSync. Events: 1 Op (client → server), 2 OpResult (server → requester).
/// </summary>
public sealed class WorldSection : NetBehaviour
{
    public const byte OpEvent = 1, OpResultEvent = 2;

    /// <summary>Section number (the order on the template, the same everywhere).</summary>
    public int index;

    byte[] blob = Array.Empty<byte>();

    public byte[] Blob => blob;

    public override void Write(BinaryWriter w) => w.Write(blob);

    public override void Read(BinaryReader r)
    {
        blob = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
        if (blob.Length > 0) WorldStateSync.OnSection(this);
    }

    public override void OnEvent(byte id, uint sender, BinaryReader r)
    {
        if (sender == 0 && id == OpResultEvent) WorldStateSync.OnOpResult(r); // only the server answers
    }
}
