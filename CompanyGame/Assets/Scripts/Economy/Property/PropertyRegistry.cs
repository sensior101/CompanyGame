using System;
using System.Collections.Generic;

/// <summary>저장·복원용 소유 기록. owner가 빈 문자열이면 포기된 상태다.</summary>
[Serializable]
public class PropertySave
{
    public string id;
    public string owner;
    public bool vehicleLocked;
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
    static bool IsVehicle(string id) => id != null && id.StartsWith("vehicle:",StringComparison.Ordinal);
    public bool IsVehicleLocked(string id) => IsVehicle(id) && GetOwner(id)!=null && lockedVehicles.Contains(id);
    public bool CanRideVehicle(string id, string player) => IsVehicle(id) && !string.IsNullOrEmpty(player) &&
        (GetOwner(id)==null || GetOwner(id)==player || !IsVehicleLocked(id));
    public bool CanManageVehicle(string id, string player) => IsVehicle(id) && !string.IsNullOrEmpty(player) &&
        (GetOwner(id)==null || GetOwner(id)==player);
    public bool TrySetVehicleLocked(string id, string player, bool locked)
    {
        if (!IsVehicle(id) || string.IsNullOrEmpty(player) || GetOwner(id)!=player) return false;
        if (locked) lockedVehicles.Add(id); else lockedVehicles.Remove(id);
        return true;
    }

    public string GetOwner(string id) =>
        id != null && owners.TryGetValue(id, out var o) && !string.IsNullOrEmpty(o) ? o : null;

    public bool TryRegister(string id, string owner)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(owner) || GetOwner(id) != null) return false;
        owners[id] = owner;
        if (IsVehicle(id)) lockedVehicles.Add(id);
        return true;
    }

    public bool TryAbandon(string id, string owner)
    {
        if (string.IsNullOrEmpty(owner) || GetOwner(id) != owner) return false;
        owners[id] = "";
        lockedVehicles.Remove(id);
        return true;
    }

    public void SeedOwner(string id, string owner)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(owner) || owners.ContainsKey(id)) return;
        owners[id] = owner;
    }

    public List<PropertySave> CaptureState()
    {
        var list = new List<PropertySave>();
        foreach (var kv in owners) list.Add(new PropertySave { id = kv.Key, owner = kv.Value, vehicleLocked = IsVehicleLocked(kv.Key) });
        return list;
    }

    public void RestoreState(IEnumerable<PropertySave> saved)
    {
        owners.Clear();
        lockedVehicles.Clear();
        if (saved == null) return;
        foreach (var s in saved)
            if (s != null && !string.IsNullOrEmpty(s.id))
            {
                owners[s.id] = s.owner ?? "";
                if(IsVehicle(s.id) && s.vehicleLocked && !string.IsNullOrEmpty(s.owner))lockedVehicles.Add(s.id);
            }
    }
}
