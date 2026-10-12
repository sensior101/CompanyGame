using System;
using System.Collections.Generic;

/// <summary>저장·복원용 소유 기록. owner가 빈 문자열이면 포기된 상태다.</summary>
[Serializable]
public class PropertySave
{
    public string id;
    public string owner;
    public bool vehicleLocked;
    public bool vehicleDismantleUnlocked;
}

/// <summary>
/// 집·땅 id별 소유자 이름을 들고 있다. 씬이 바뀌어도 유지되도록 구역 컴포넌트가 아니라 여기서 보관한다.
/// ponytail: 판정은 로컬에서 한다. 멀티플레이 연동 시 서버가 중복 등록을 판정하도록 옮긴다.
/// </summary>
public class PropertyRegistry : GameSystem<PropertyRegistry>
{
    // 포기 = 키 유지 + 값 "" (포기한 id는 SeedOwner로 다시 시드되지 않는다)
    private readonly Dictionary<string, string> owners = new Dictionary<string, string>();
    readonly HashSet<string> lockedVehicles = new HashSet<string>();
    readonly HashSet<string> dismantleUnlockedVehicles = new HashSet<string>();
    static bool IsVehicle(string id) => id != null && id.StartsWith("vehicle:",StringComparison.Ordinal);

    // Identity: with a server account the owner is stored as the account id (what the server attaches as `from`).
    // The UI still passes/compares LocalPlayerName, so entry points translate name -> id and GetOwner id -> name.
    // Applying a received op passes ids already and must not be translated.
    static string ToKey(string player) =>
        !SharedWriteGate.Applying && GameSession.HasAccount && player == GameSession.LocalPlayerName ? GameSession.LocalPlayerId : player;
    string ToShown(string owner) =>
        owner == null || !GameSession.HasAccount ? owner : owner == GameSession.LocalPlayerId ? GameSession.LocalPlayerName : SharedWriteGate.NameOf(owner);
    string Raw(string id) =>
        id != null && owners.TryGetValue(id, out var o) && !string.IsNullOrEmpty(o) ? o : null;
    bool IsOwner(string id, string player) => Raw(id) != null && Raw(id) == ToKey(player);
    bool OwnerOrNone(string id, string player) => Raw(id) == null || Raw(id) == ToKey(player);

    public bool IsVehicleLocked(string id) => IsVehicle(id) && Raw(id)!=null && lockedVehicles.Contains(id);
    public bool CanRideVehicle(string id, string player) => IsVehicle(id) && !string.IsNullOrEmpty(player) &&
        (OwnerOrNone(id, player) || !IsVehicleLocked(id));
    public bool CanManageVehicle(string id, string player) => IsVehicle(id) && !string.IsNullOrEmpty(player) && OwnerOrNone(id, player);
    public bool IsVehicleDismantleUnlocked(string id) => IsVehicle(id) && Raw(id)!=null && dismantleUnlockedVehicles.Contains(id);
    public bool CanDismantleVehicle(string id, string player) => IsVehicle(id) && !string.IsNullOrEmpty(player) &&
        (OwnerOrNone(id, player) || IsVehicleDismantleUnlocked(id));
    public bool TrySetVehicleDismantleUnlocked(string id, string player, bool unlocked)
    {
        if (!IsVehicle(id) || string.IsNullOrEmpty(player) || !IsOwner(id, player)) return false;
        if (SharedWriteGate.TrySend("prop.dis", new SharedOpArgs { a = id, b = ToKey(player), f = unlocked })) return true;
        if (SharedWriteGate.SendFailed) return false;
        if (unlocked) dismantleUnlockedVehicles.Add(id); else dismantleUnlockedVehicles.Remove(id);
        return true;
    }
    public bool TrySetVehicleLocked(string id, string player, bool locked)
    {
        if (!IsVehicle(id) || string.IsNullOrEmpty(player) || !IsOwner(id, player)) return false;
        if (SharedWriteGate.TrySend("prop.lock", new SharedOpArgs { a = id, b = ToKey(player), f = locked })) return true;
        if (SharedWriteGate.SendFailed) return false;
        if (locked) lockedVehicles.Add(id); else lockedVehicles.Remove(id);
        return true;
    }

    /// <summary>Owner as the UI should see it: the local player's name for own records, a peer's name or id otherwise.</summary>
    public string GetOwner(string id) => ToShown(Raw(id));

    public bool TryRegister(string id, string owner)
    {
        owner = ToKey(owner);
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(owner) || Raw(id) != null) return false;
        if (SharedWriteGate.TrySend("prop.reg", new SharedOpArgs { a = id, b = owner })) return true;
        if (SharedWriteGate.SendFailed) return false;
        owners[id] = owner;
        if (IsVehicle(id)) lockedVehicles.Add(id);
        dismantleUnlockedVehicles.Remove(id);
        return true;
    }

    public bool TryAbandon(string id, string owner)
    {
        owner = ToKey(owner);
        if (string.IsNullOrEmpty(owner) || Raw(id) != owner) return false;
        if (SharedWriteGate.TrySend("prop.ab", new SharedOpArgs { a = id, b = owner })) return true;
        if (SharedWriteGate.SendFailed) return false;
        owners[id] = "";
        lockedVehicles.Remove(id);
        dismantleUnlockedVehicles.Remove(id);
        return true;
    }

    /// <summary>Replaces owner values (legacy GUID/name -> account id). Returns how many records changed.</summary>
    public int RemapOwners(Func<string, string> map)
    {
        int changed = 0;
        foreach (var key in new List<string>(owners.Keys))
        {
            string now = map(owners[key]);
            if (now != owners[key]) { owners[key] = now; changed++; }
        }
        return changed;
    }

    public void SeedOwner(string id, string owner)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(owner) || owners.ContainsKey(id)) return;
        owners[id] = owner;
    }

    public List<PropertySave> CaptureState()
    {
        var list = new List<PropertySave>();
        foreach (var kv in owners) list.Add(new PropertySave { id = kv.Key, owner = kv.Value,
            vehicleLocked = IsVehicleLocked(kv.Key), vehicleDismantleUnlocked = IsVehicleDismantleUnlocked(kv.Key) });
        return list;
    }

    public void RestoreState(IEnumerable<PropertySave> saved)
    {
        owners.Clear();
        lockedVehicles.Clear();
        dismantleUnlockedVehicles.Clear();
        if (saved == null) return;
        foreach (var s in saved)
            if (s != null && !string.IsNullOrEmpty(s.id))
            {
                owners[s.id] = s.owner ?? "";
                if(IsVehicle(s.id) && s.vehicleLocked && !string.IsNullOrEmpty(s.owner))lockedVehicles.Add(s.id);
                if(IsVehicle(s.id) && s.vehicleDismantleUnlocked && !string.IsNullOrEmpty(s.owner))dismantleUnlockedVehicles.Add(s.id);
            }
    }
}
