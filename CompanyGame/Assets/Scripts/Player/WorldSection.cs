using System;
using System.IO;

/// <summary>
/// One section of the world entity Net/World (Docs/Multiplayer_Redesign.md 12.9): 0 clock, 1 names, 2..7 the shared
/// economy (stocks, companies, properties, plots, reports, sns), 8 the library (12.10.6). The server owns it and sends each section as a whole
/// blob; a copy hands what it gets to WorldStateSync. Events: 1 Op (client → server), 2 OpResult (server → requester),
/// 3 Ledger (server → that character's connection only: its own bank money and stocks, 12.10.1), 4 BookText (server →
/// the requester of book.get only: a book's body in parts, 12.10.3), 5 Notice (server → one connection or everyone: a chat
/// line, an admin's answer or announcement, 12.15).
/// </summary>
public sealed class WorldSection : NetBehaviour
{
    public const byte OpEvent = 1, OpResultEvent = 2, LedgerEvent = 3, BookTextEvent = 4, NoticeEvent = 5;

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
        if (sender != 0) return; // only the server answers
        if (id == OpResultEvent) WorldStateSync.OnOpResult(r);
        else if (id == LedgerEvent) WorldStateSync.OnLedger(r);
        else if (id == BookTextEvent) WorldStateSync.OnBookText(r);
        else if (id == NoticeEvent) WorldStateSync.OnNotice(r);
    }
}
