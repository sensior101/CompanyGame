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
    NameTaken
}

/// <summary>저장·복원용 부지 상태. 상태가 Available인 부지는 저장하지 않는다.</summary>
[Serializable]
public class LandPlotSave
{
    public string id;
    public LandPlotState state;
    public string companyName;
    public string initials;
}

/// <summary>
/// 부지 구매와 시청 사업자등록(회사명·이니셜)을 처리한다. 씬이 바뀌어도 상태가 유지되도록
/// 부지 상태는 LandPlot 컴포넌트가 아니라 여기서 id로 들고 있다.
/// ponytail: 판정은 로컬에서 한다. 멀티플레이 연동 시 서버가 구매·회사명 중복을 판정하도록 옮긴다.
/// </summary>
public class LandPlotManager : MonoBehaviour
{
    private static readonly Regex NameRule = new Regex("^[가-힣A-Za-z0-9 ]{2,12}$");
    private static readonly Regex InitialsRule = new Regex("^[가-힣A-Za-z0-9]{1,3}$");

    public static LandPlotManager Instance { get; private set; }

    private readonly Dictionary<string, LandPlotSave> states = new Dictionary<string, LandPlotSave>();

    /// <summary>부지 상태가 바뀔 때 발생한다. UI 갱신과 건축 시스템이 받는다.</summary>
    public event Action<string> PlotChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

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

        var wallet = PropertyManager.Instance;
        if (wallet == null) return LandPlotResult.NoWallet;
        if (!wallet.TrySpend(plot.Price, MoneyChangeReason.Purchase)) return LandPlotResult.NotEnoughMoney;

        states[plot.Id] = new LandPlotSave { id = plot.Id, state = LandPlotState.Owned };
        PlotChanged?.Invoke(plot.Id);
        return LandPlotResult.Ok;
    }

    public LandPlotResult TryRegisterBusiness(LandPlot plot, string companyName, string initials)
    {
        if (plot == null || string.IsNullOrEmpty(plot.Id)) return LandPlotResult.NotFound;
        if (GetState(plot.Id) != LandPlotState.Owned) return LandPlotResult.WrongState;

        companyName = (companyName ?? "").Trim();
        initials = (initials ?? "").Trim().ToUpperInvariant();
        if (!NameRule.IsMatch(companyName)) return LandPlotResult.InvalidName;
        if (!InitialsRule.IsMatch(initials)) return LandPlotResult.InvalidInitials;
        if (IsNameTaken(companyName)) return LandPlotResult.NameTaken;

        var s = states[plot.Id];
        s.state = LandPlotState.Registered;
        s.companyName = companyName;
        s.initials = initials;
        PlotChanged?.Invoke(plot.Id);
        return LandPlotResult.Ok;
    }

    /// <summary>땅 세금 부과 대상 여부. 사업자등록을 하면 면제된다. (기획안 8-1)</summary>
    public bool IsTaxable(string plotId) => GetState(plotId) == LandPlotState.Owned;

    public List<LandPlotSave> CaptureState()
    {
        var list = new List<LandPlotSave>(states.Count);
        foreach (var s in states.Values)
            list.Add(new LandPlotSave { id = s.id, state = s.state, companyName = s.companyName, initials = s.initials });
        return list;
    }

    public void RestoreState(IEnumerable<LandPlotSave> saved)
    {
        states.Clear();
        foreach (var s in saved)
        {
            if (s == null || string.IsNullOrEmpty(s.id) || s.state == LandPlotState.Available) continue;
            states[s.id] = new LandPlotSave { id = s.id, state = s.state, companyName = s.companyName, initials = s.initials };
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
