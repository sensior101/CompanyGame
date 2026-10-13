using System;

/// <summary>
/// Single flat wire message (JsonUtility cannot do polymorphism). Unused fields keep their defaults.
/// See _temp/team NOTES 설계 3-2 for the protocol.
/// </summary>
[Serializable]
public sealed class NetMsg
{
    public string t, id, name, rid, room, host, hostName, reason, to, from, data, snap, ev, map, code;
    public string nm, veh; // pos: character name, ridden vehicle itemId ("" = on foot)
    public int max, count;
    public int g, act;     // pos: 1 = female look; attack counter (a change = one swing)
    public long seq;
    public float x, y, z, ry, sp;
    public RoomInfo[] rooms;
    public PeerInfo[] players;
    public OpInfo[] ops; // joined: recent ops after the stored snapshot

    public static NetMsg Of(string type) => new NetMsg { t = type };
}

[Serializable]
public sealed class RoomInfo
{
    public string rid, room, host, hostName;
    public int count, max;
}

[Serializable]
public sealed class PeerInfo
{
    public string id, name;
}

[Serializable]
public sealed class OpInfo
{
    public long seq;
    public string from, ev, data;
}
