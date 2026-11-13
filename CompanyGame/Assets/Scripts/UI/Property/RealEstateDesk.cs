using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// The real-estate office's staff (phase 1: buying and selling). Each consultant deals in one kind of listing
/// (<see cref="RealEstateCatalog"/>): it pages through the free listings, sells one from the bank account, and buys back
/// one of mine at <see cref="RealEstateCatalog.BuyBackPercent"/>%. The reception tells what I own and who handles what.
/// Ownership is <see cref="LandPlotManager"/>'s (land.buy / land.sell on a server). The NPCs are placed by
/// RealEstateInteriorBuilder with npcId "realestate_land" / "_commercial" / "_house" / "_reception".
/// </summary>
public static class RealEstateDesk
{
    public const string Prefix = "realestate_";
    public const string LandDesk = Prefix + "land", CommercialDesk = Prefix + "commercial", HouseDesk = Prefix + "house",
        Reception = Prefix + "reception";
    public const int PageSize = 5;

    // Option ids. A listing option carries its parcel id after the colon.
    public const string Browse = "re_browse", Mine = "re_mine", Bye = "re_bye", Next = "re_next",
        Pick = "re_pick:", Buy = "re_buy:", SellList = "re_sell", SellPick = "re_sellpick:", Sell = "re_sellok:",
        Holdings = "re_holdings", Guide = "re_guide";

    static int page;
    static DialogueData waitingNpc; // my land.buy / land.sell on a server: told when it is answered
    static string waitingKind, waitingText;

    public static DialogueOption[] ConsultantOptions(RealEstateKind kind) => new[]
    {
        new DialogueOption(Browse, KindName(kind) + " 매물 보기"),
        new DialogueOption(SellList, "내 " + KindName(kind) + " 팔기"),
        new DialogueOption(Bye, "괜찮아요")
    };

    public static DialogueOption[] ReceptionOptions() => new[]
    {
        new DialogueOption(Holdings, "내 부동산 현황"),
        new DialogueOption(Guide, "상담 안내"),
        new DialogueOption(Bye, "괜찮아요")
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        DialogueManager.EnsureInstance();
        DialogueManager.Instance.OptionSelected -= OnOption;
        DialogueManager.Instance.OptionSelected += OnOption;
        DialogueManager.Instance.Began -= OnBegan;
        DialogueManager.Instance.Began += OnBegan;
        WorldStateSync.Answered -= OnAnswered;
        WorldStateSync.Answered += OnAnswered;
    }

    static string KindName(RealEstateKind kind) => RealEstateCatalog.KindName(kind);
    static string Won(long amount) => RealEstateCatalog.Won(amount);
    static string Me => GameSession.LocalPlayerId;
    static LandPlotManager Plots => LandPlotManager.Instance;

    public static bool TryKindOf(DialogueData npc, out RealEstateKind kind)
    {
        kind = RealEstateKind.Land;
        switch (npc ? npc.npcId : null)
        {
            case LandDesk: kind = RealEstateKind.Land; return true;
            case CommercialDesk: kind = RealEstateKind.Commercial; return true;
            case HouseDesk: kind = RealEstateKind.House; return true;
            default: return false;
        }
    }

    /// <summary>Free listings of a kind, cheapest first.</summary>
    public static List<RealEstateListing> ForSale(RealEstateKind kind) => RealEstateCatalog.All
        .Where(l => l.kind == kind && (!Plots || Plots.GetState(l.id) == LandPlotState.Available))
        .OrderBy(l => l.price).ThenBy(l => l.number).ToList();

    /// <summary>My listings of a kind (any state).</summary>
    public static List<RealEstateListing> Owned(RealEstateKind? kind = null) => !Plots ? new List<RealEstateListing>() : RealEstateCatalog.All
        .Where(l => (kind == null || l.kind == kind) && Plots.GetState(l.id) != LandPlotState.Available && Plots.GetOwner(l.id) == Me)
        .OrderBy(l => l.kind).ThenBy(l => l.number).ToList();

    static void OnBegan(DialogueData npc) { if (npc && npc.npcId != null && npc.npcId.StartsWith(Prefix, StringComparison.Ordinal)) page = 0; }

    static void OnOption(DialogueData npc, string id)
    {
        if (!npc || npc.npcId == null || !npc.npcId.StartsWith(Prefix, StringComparison.Ordinal) || id == null) return;
        var manager = DialogueManager.Instance;
        if (id == Bye) { manager.Close(); return; }
        if (id == Holdings) { manager.PresentNotice(npc, HoldingsText()); return; }
        if (id == Guide)
        {
            manager.PresentNotice(npc, "부지는 1번, 상가는 2번, 주택은 3번 창구에서 상담해 드려요. 사신 매물은 구매가의 " +
                RealEstateCatalog.BuyBackPercent + "%로 저희가 다시 사 드립니다. 사업자 등록을 한 곳은 팔 수 없어요.");
            return;
        }
        if (!TryKindOf(npc, out var kind)) return;
        if (id == Browse) { page = 0; ShowListings(npc, kind); }
        else if (id == Next) { page++; ShowListings(npc, kind); }
        else if (id == SellList) ShowMine(npc, kind);
        else if (id.StartsWith(Pick, StringComparison.Ordinal)) ShowListing(npc, id.Substring(Pick.Length));
        else if (id.StartsWith(Buy, StringComparison.Ordinal)) Purchase(npc, id.Substring(Buy.Length));
        else if (id.StartsWith(SellPick, StringComparison.Ordinal)) ShowSale(npc, id.Substring(SellPick.Length));
        else if (id.StartsWith(Sell, StringComparison.Ordinal)) SellBack(npc, id.Substring(Sell.Length));
    }

    // ---- buying ----

    static void ShowListings(DialogueData npc, RealEstateKind kind)
    {
        var manager = DialogueManager.Instance;
        var free = ForSale(kind);
        if (free.Count == 0) { manager.PresentNotice(npc, "지금은 나와 있는 " + KindName(kind) + " 매물이 없습니다."); return; }
        int pages = (free.Count + PageSize - 1) / PageSize;
        page = ((page % pages) + pages) % pages;
        var options = new List<DialogueOption>();
        foreach (var l in free.Skip(page * PageSize).Take(PageSize))
            options.Add(new DialogueOption(Pick + l.id, l.name + " · " + l.number + "번지 · " + Won(l.price)));
        if (pages > 1) options.Add(new DialogueOption(Next, "다음 목록 (" + (page % pages + 1) + "/" + pages + ")"));
        options.Add(new DialogueOption(Bye, "그만 볼게요"));
        manager.Present(npc, KindName(kind) + " 매물 " + free.Count + "건이 있어요.\n계좌 잔액 " +
            (BankManager.Instance ? BankManager.Instance.Money : 0).ToString("N0") + "원", options.ToArray());
    }

    static void ShowListing(DialogueData npc, string id)
    {
        var manager = DialogueManager.Instance;
        var l = RealEstateCatalog.Find(id);
        if (l == null || !Plots || Plots.GetState(id) != LandPlotState.Available)
        { manager.PresentNotice(npc, "방금 다른 분께 팔린 매물입니다. 다른 매물을 보여 드릴게요."); return; }
        long balance = BankManager.Instance ? BankManager.Instance.Money : 0;
        manager.Present(npc, l.Address + " " + l.name + " (" + l.Size + ")\n가격 " + l.price.ToString("N0") + "원 · 계좌 잔액 " +
            balance.ToString("N0") + "원\n구매하시겠습니까? 대금은 계좌에서 나갑니다.", new[]
        {
            new DialogueOption(Buy + id, "구매한다"),
            new DialogueOption(Browse, "다른 매물 보기"),
            new DialogueOption(Bye, "그만 볼게요")
        });
    }

    static void Purchase(DialogueData npc, string id)
    {
        var manager = DialogueManager.Instance;
        var l = RealEstateCatalog.Find(id);
        if (l == null || !Plots) { manager.PresentNotice(npc, "매물을 찾을 수 없습니다."); return; }
        var result = Plots.TryPurchase(id, l.price);
        string done = l.Address + " " + l.name + " 계약이 끝났습니다. 축하드려요! 지도에서 내 소유로 표시됩니다.";
        if (result == LandPlotResult.Ok && Await(npc, "land.buy", done)) return;
        manager.PresentNotice(npc, result switch
        {
            LandPlotResult.Ok => done,
            LandPlotResult.WrongState => "방금 다른 분께 팔린 매물입니다.",
            LandPlotResult.NotEnoughMoney => "계좌 잔액이 부족합니다. " + l.price.ToString("N0") + "원이 필요해요. (잔액 " +
                (BankManager.Instance ? BankManager.Instance.Money : 0).ToString("N0") + "원)",
            LandPlotResult.SendFailed => "서버에 연결되어 있지 않아 계약하지 못했습니다. 결제는 되지 않았어요.",
            _ => "지금은 계약할 수 없습니다."
        });
    }

    // ---- selling ----

    static void ShowMine(DialogueData npc, RealEstateKind kind)
    {
        var manager = DialogueManager.Instance;
        var mine = Owned(kind);
        var sellable = mine.Where(l => Plots.GetState(l.id) == LandPlotState.Owned).ToList();
        if (sellable.Count == 0)
        {
            manager.PresentNotice(npc, mine.Count > 0 ? "가지고 계신 " + KindName(kind) + "은(는) 모두 사업자 등록이 되어 있어 팔 수 없습니다."
                : "가지고 계신 " + KindName(kind) + "이(가) 없습니다.");
            return;
        }
        var options = sellable.Take(PageSize + 1).Select(l =>
            new DialogueOption(SellPick + l.id, l.name + " · " + l.number + "번지 · " + Won(LandPlotManager.BuyBackPriceOf(l.id)))).ToList();
        options.Add(new DialogueOption(Bye, "그만 볼게요"));
        manager.Present(npc, "어떤 " + KindName(kind) + "을(를) 파시겠어요? 구매가의 " + RealEstateCatalog.BuyBackPercent + "%로 사 드립니다.",
            options.ToArray());
    }

    static void ShowSale(DialogueData npc, string id)
    {
        var manager = DialogueManager.Instance;
        var l = RealEstateCatalog.Find(id);
        if (l == null || !Plots || Plots.ApplySell(id, Me, false) != LandPlotResult.Ok)
        { manager.PresentNotice(npc, "지금은 팔 수 없는 매물입니다."); return; }
        manager.Present(npc, l.Address + " " + l.name + "\n매입가 " + LandPlotManager.BuyBackPriceOf(id).ToString("N0") + "원 (구매가 " +
            LandPlot.PriceOf(id).ToString("N0") + "원의 " + RealEstateCatalog.BuyBackPercent + "%)\n파시겠습니까? 대금은 계좌로 들어갑니다.", new[]
        {
            new DialogueOption(Sell + id, "판다"),
            new DialogueOption(SellList, "다른 매물 보기"),
            new DialogueOption(Bye, "그만 둘게요")
        });
    }

    static void SellBack(DialogueData npc, string id)
    {
        var manager = DialogueManager.Instance;
        var l = RealEstateCatalog.Find(id);
        if (l == null || !Plots) { manager.PresentNotice(npc, "매물을 찾을 수 없습니다."); return; }
        long payout = LandPlotManager.BuyBackPriceOf(id);
        var result = Plots.TrySell(id);
        string done = l.Address + " " + l.name + "을(를) 매입했습니다. " + payout.ToString("N0") + "원을 계좌로 넣어 드렸어요.";
        if (result == LandPlotResult.Ok && Await(npc, "land.sell", done)) return;
        manager.PresentNotice(npc, result switch
        {
            LandPlotResult.Ok => done,
            LandPlotResult.WrongState => "사업자 등록을 한 곳은 팔 수 없습니다.",
            LandPlotResult.NotOwner => "가지고 계신 매물이 아닙니다.",
            LandPlotResult.SendFailed => "서버에 연결되어 있지 않아 처리하지 못했습니다.",
            _ => "지금은 처리할 수 없습니다."
        });
    }

    // ---- reception ----

    static string HoldingsText()
    {
        var mine = Owned();
        var registry = PropertyRegistry.Instance;
        var rooms = registry ? registry.CaptureState()
            .Where(p => p.id != null && p.id.StartsWith(InventoryLedgerRules.StartingDeedPrefix, StringComparison.Ordinal) &&
                        registry.GetOwner(p.id) == GameSession.LocalPlayerName)
            .Select(p => p.id.Substring(InventoryLedgerRules.StartingDeedPrefix.Length) + "호").ToList() : new List<string>();
        if (mine.Count == 0 && rooms.Count == 0) return "아직 가지고 계신 부동산이 없습니다. 창구에서 매물을 둘러보세요.";
        var text = new StringBuilder();
        text.Append("부지 ").Append(mine.Count(l => l.kind == RealEstateKind.Land)).Append("건 · 상가 ")
            .Append(mine.Count(l => l.kind == RealEstateKind.Commercial)).Append("건 · 주택 ")
            .Append(mine.Count(l => l.kind == RealEstateKind.House)).Append("건");
        foreach (var l in mine.Take(5))
            text.Append("\n- ").Append(l.Address).Append(' ').Append(l.name)
                .Append(Plots.GetState(l.id) == LandPlotState.Registered ? " (사업자 등록: " + Plots.GetCompanyName(l.id) + ")" : "");
        if (mine.Count > 5) text.Append("\n외 ").Append(mine.Count - 5).Append("건");
        if (rooms.Count > 0) text.Append("\n고시원 ").Append(string.Join(", ", rooms));
        return text.ToString();
    }

    // ---- a server's answer ----

    // On a server the result comes with the answer; the notice waits for it. False off a server (told now).
    static bool Await(DialogueData npc, string kind, string text)
    {
        if (!SharedWriteGate.OnServer) return false;
        waitingNpc = npc; waitingKind = kind; waitingText = text;
        DialogueManager.Instance.PresentNotice(npc, "계약서를 확인하고 있습니다…");
        return true;
    }

    static void OnAnswered(string kind, string json, string error)
    {
        if (kind == null || kind != waitingKind) return;
        var npc = waitingNpc;
        string text = string.IsNullOrEmpty(error) ? waitingText : error;
        waitingNpc = null; waitingKind = null; waitingText = null;
        if (npc && DialogueManager.Instance) DialogueManager.Instance.PresentNotice(npc, text);
    }
}
