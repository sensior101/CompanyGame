using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>부지 진행 단계. 건축(한대건설)과 완공은 이후 단계로 추가한다.</summary>
public enum LandPlotState
{
    Available = 0,  // 아직 안 산 부지
    Owned = 1,      // 구매함, 사업자등록 전 (땅 세금 부과 대상)
    Registered = 2  // 시청에서 사업자등록 완료 (땅 세금 면제)
}

public enum LandPlotResult
{
    Ok,
    NotFound,
    WrongState,
    NoWallet,
    NotEnoughMoney,
    InvalidName,
    InvalidInitials,
    NameTaken,
    SendFailed, // in a room but the op could not be sent; nothing was charged
    NotOwner    // the plot belongs to someone else
}

/// <summary>저장·복원용 부지 상태. 상태가 Available인 부지는 저장하지 않는다.</summary>
[Serializable]
public class LandPlotSave
{
    public string id;
    public LandPlotState state;
    public string companyName;
    public string initials;
    public string owner; // account id of the buyer (empty in worlds from before owners existed)
}

/// <summary>
/// 부지 구매와 시청 사업자등록(회사명·이니셜)을 처리한다. 씬이 바뀌어도 상태가 유지되도록
/// 부지 상태는 LandPlot 컴포넌트가 아니라 여기서 id로 들고 있다.
/// ponytail: 판정은 로컬에서 한다. 멀티플레이 연동 시 서버가 구매·회사명 중복을 판정하도록 옮긴다.
/// </summary>
public class LandPlotManager : GameSystem<LandPlotManager>
{
    private static readonly Regex NameRule = new Regex("^[가-힣A-Za-z0-9 ]{2,12}$");
    private static readonly Regex InitialsRule = new Regex("^[가-힣A-Za-z0-9]{1,3}$");

    private readonly Dictionary<string, LandPlotSave> states = new Dictionary<string, LandPlotSave>();

    /// <summary>부지 상태가 바뀔 때 발생한다. UI 갱신과 건축 시스템이 받는다.</summary>
    public event Action<string> PlotChanged;

    // Price this client paid for a plot whose purchase op is in flight. A lost race refunds exactly this, never an op field.
    readonly Dictionary<string, long> pendingPayments = new Dictionary<string, long>();

    /// <summary>A new room starts: payments of the old room's unanswered ops are gone with it.</summary>
    public void ClearPending() => pendingPayments.Clear();

    public LandPlotState GetState(string plotId) =>
        states.TryGetValue(plotId, out var s) ? s.state : LandPlotState.Available;

    public string GetCompanyName(string plotId) =>
        states.TryGetValue(plotId, out var s) ? s.companyName : null;

    public string GetInitials(string plotId) =>
        states.TryGetValue(plotId, out var s) ? s.initials : null;

    public LandPlotResult TryPurchase(LandPlot plot)
    {
        if (plot == null || string.IsNullOrEmpty(plot.Id)) return LandPlotResult.NotFound;
        if (GetState(plot.Id) != LandPlotState.Available) return LandPlotResult.WrongState;

        var wallet = BankManager.Instance;
        if (wallet == null) return LandPlotResult.NoWallet;
        if (!wallet.TrySpend(plot.Price, MoneyChangeReason.Purchase)) return LandPlotResult.NotEnoughMoney;

        // In a room the payment is personal and made now; WorldOps refunds it if the op loses the race.
        if (SharedWriteGate.TrySend("land.buy", new SharedOpArgs { a = plot.Id })) { pendingPayments[plot.Id] = plot.Price; return LandPlotResult.Ok; }
        if (SharedWriteGate.SendFailed) { wallet.AddMoney(plot.Price, MoneyChangeReason.Purchase); return LandPlotResult.SendFailed; }
        ApplyPurchase(plot.Id, GameSession.LocalPlayerId);
        return LandPlotResult.Ok;
    }

    /// <summary>Gives back what this client paid for a lost purchase op. False when no payment is pending (e.g. a replayed old op).</summary>
    public bool RefundPending(string plotId)
    {
        if (!pendingPayments.Remove(plotId, out long price) || !BankManager.Instance) return false;
        BankManager.Instance.AddMoney(price, MoneyChangeReason.Purchase);
        return true;
    }

    /// <summary>A server accepted my purchase (12.9): the payment stays spent and is no longer pending.</summary>
    public void ConfirmPending(string plotId) { if (plotId != null) pendingPayments.Remove(plotId); }

    /// <summary>Shared part of a purchase (no payment). False when the plot is not available. The owner is the op's `from`.</summary>
    public bool ApplyPurchase(string plotId, string owner)
    {
        if (string.IsNullOrEmpty(plotId) || string.IsNullOrEmpty(owner) || GetState(plotId) != LandPlotState.Available) return false;
        states[plotId] = new LandPlotSave { id = plotId, state = LandPlotState.Owned, owner = owner };
        if (owner == GameSession.LocalPlayerId) pendingPayments.Remove(plotId);
        PlotChanged?.Invoke(plotId);
        return true;
    }

    public string GetOwner(string plotId) => states.TryGetValue(plotId, out var s) ? s.owner : null;

    /// <summary>Own-world migration: plots without an owner, or owned by the old per-PC id, belong to the local player.</summary>
    public void ClaimUnowned(string owner)
    {
        foreach (var s in states.Values) if (string.IsNullOrEmpty(s.owner) || s.owner == GameSession.OfflinePlayerId) s.owner = owner;
    }

    public LandPlotResult TryRegisterBusiness(LandPlot plot, string companyName, string initials)
    {
        if (plot == null || string.IsNullOrEmpty(plot.Id)) return LandPlotResult.NotFound;
        string actor = SharedWriteGate.InRoom ? GameSession.LocalPlayerId : null;
        var check = ApplyRegisterBusiness(plot.Id, companyName, initials, false, actor);
        if (check != LandPlotResult.Ok) return check;
        if (SharedWriteGate.TrySend("land.reg", new SharedOpArgs { a = plot.Id, b = companyName, c = initials })) return LandPlotResult.Ok;
        if (SharedWriteGate.SendFailed) return LandPlotResult.SendFailed;
        return ApplyRegisterBusiness(plot.Id, companyName, initials, true);
    }

    /// <summary>
    /// Validates (commit=false) or applies a business registration by plot id. With an actor (the op's `from`)
    /// only the plot's owner may register; null skips the check (single player).
    /// </summary>
    public LandPlotResult ApplyRegisterBusiness(string plotId, string companyName, string initials, bool commit = true, string actor = null)
    {
        if (string.IsNullOrEmpty(plotId)) return LandPlotResult.NotFound;
        if (GetState(plotId) != LandPlotState.Owned) return LandPlotResult.WrongState;
        if (actor != null && GetOwner(plotId) != actor) return LandPlotResult.NotOwner;

        companyName = (companyName ?? "").Trim();
        initials = (initials ?? "").Trim().ToUpperInvariant();
        if (!NameRule.IsMatch(companyName)) return LandPlotResult.InvalidName;
        if (!InitialsRule.IsMatch(initials)) return LandPlotResult.InvalidInitials;
        if (IsNameTaken(companyName)) return LandPlotResult.NameTaken;

        if (!commit) return LandPlotResult.Ok;
        var s = states[plotId];
        s.state = LandPlotState.Registered;
        s.companyName = companyName;
        s.initials = initials;
        PlotChanged?.Invoke(plotId);
        return LandPlotResult.Ok;
    }

    /// <summary>땅 세금 부과 대상 여부. 사업자등록을 하면 면제된다. (기획안 8-1)</summary>
    public bool IsTaxable(string plotId) => GetState(plotId) == LandPlotState.Owned;

    public List<LandPlotSave> CaptureState()
    {
        var list = new List<LandPlotSave>(states.Count);
        foreach (var s in states.Values)
            list.Add(new LandPlotSave { id = s.id, state = s.state, companyName = s.companyName, initials = s.initials, owner = s.owner });
        return list;
    }

    public void RestoreState(IEnumerable<LandPlotSave> saved)
    {
        states.Clear();
        foreach (var s in saved)
        {
            if (s == null || string.IsNullOrEmpty(s.id) || s.state == LandPlotState.Available) continue;
            states[s.id] = new LandPlotSave { id = s.id, state = s.state, companyName = s.companyName, initials = s.initials, owner = s.owner };
            PlotChanged?.Invoke(s.id);
        }
    }

    private bool IsNameTaken(string companyName)
    {
        foreach (var s in states.Values)
            if (s.state == LandPlotState.Registered &&
                string.Equals(s.companyName, companyName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
