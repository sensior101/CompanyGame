using System;
using UnityEngine;

/// <summary>
/// Applies a shared-world request on the server (ServerWorld, ServerLedger) as the account of its connection.
/// The managers' own Try* methods send it while on a server (SharedWriteGate); this is the other half.
/// The first request wins; a later conflicting one fails and its sender gets a Korean message.
/// </summary>
public static class WorldOps
{
    const string WrongActor = "요청한 사람이 올바르지 않아 거부했습니다.";

    /// <summary>
    /// Returns null on success, else a Korean failure message.
    /// <paramref name="from"/> is the account id the server attached; the actor named inside the op must equal it.
    /// </summary>
    public static string Apply(string from, string kind, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json);
        if (a == null) return null;
        if (string.IsNullOrEmpty(from) || !ActorMatches(kind, a, from)) return WrongActor;
        SharedWriteGate.Applying = true;
        try { return Run(kind, a, from); }
        catch (Exception e) { Debug.LogException(e); return "요청을 처리하지 못했습니다."; }
        finally { SharedWriteGate.Applying = false; }
    }

    // Every op that names an actor (owner, author, reporter, buyer) must name the sender. Land ops use `from` itself.
    static bool ActorMatches(string kind, SharedOpArgs a, string from)
    {
        switch (kind)
        {
            case "prop.reg": case "prop.ab": case "prop.lock": case "prop.dis": case "company.add": case "sns.post": case "sns.like": return a.b == from;
            case "report.sub": return a.c == from;
            default: return true;
        }
    }

    static string Run(string kind, SharedOpArgs a, string from)
    {
        switch (kind)
        {
            case "prop.reg": return Fail(PropertyRegistry.Instance && PropertyRegistry.Instance.TryRegister(a.a, a.b), "이미 다른 플레이어가 소유하고 있습니다.");
            case "prop.ab": return Fail(PropertyRegistry.Instance && PropertyRegistry.Instance.TryAbandon(a.a, a.b), "소유권을 포기할 수 없습니다.");
            case "prop.lock": return Fail(PropertyRegistry.Instance && PropertyRegistry.Instance.TrySetVehicleLocked(a.a, a.b, a.f), "잠금 상태를 바꿀 수 없습니다.");
            case "prop.dis": return Fail(PropertyRegistry.Instance && PropertyRegistry.Instance.TrySetVehicleDismantleUnlocked(a.a, a.b, a.f), "해체 허용 상태를 바꿀 수 없습니다.");
            case "land.buy": return Fail(LandPlotManager.Instance && LandPlotManager.Instance.ApplyPurchase(a.a, from), "이미 다른 플레이어가 구매한 땅입니다.");
            case "land.reg": return LandMessage(LandPlotManager.Instance ? LandPlotManager.Instance.ApplyRegisterBusiness(a.a, a.b, a.c, true, from) : LandPlotResult.NotFound);
            case "company.add": return CreateCompany(a);
            case "sns.post": return PostMessage(SnsManager.Instance ? SnsManager.Instance.PostAs(a.b, a.c, a.a) : SnsPostResult.EmptyText);
            case "sns.like": if (SnsManager.Instance) SnsManager.Instance.LikeAs(a.a, a.b); return null;
            case "report.sub": return Report(a);
            default: return null; // unknown kinds come from newer clients; ignore
        }
    }

    static string Fail(bool ok, string message) => ok ? null : message;

    static string CreateCompany(SharedOpArgs a)
    {
        var data = CompanyManager.Sanitize(JsonUtility.FromJson<CompanyData>(a.a));
        bool ok = CompanyManager.Instance && data != null && CompanyManager.Instance.TryCreatePlayerCompany(data, a.b, out _);
        return Fail(ok, "회사를 설립할 수 없습니다. 이미 있는 회사이거나 벌점이 너무 높습니다.");
    }

    static string Report(SharedOpArgs a)
    {
        if (!ReportManager.Instance || !int.TryParse(a.d, out int type)) return "신고를 접수하지 못했습니다.";
        var result = ReportManager.Instance.Service.Submit(a.c, a.a, (CrimeType)type, a.b, System.Math.Min(System.Math.Max(a.n, 0L), 1000000000L), out _);
        return result == ReportSubmitResult.Submitted ? null : "신고를 접수하지 못했습니다. 잠시 후 다시 시도해 주세요.";
    }

    static string LandMessage(LandPlotResult r)
    {
        switch (r)
        {
            case LandPlotResult.Ok: return null;
            case LandPlotResult.InvalidName: return "회사 이름 형식이 올바르지 않습니다.";
            case LandPlotResult.InvalidInitials: return "회사 약칭 형식이 올바르지 않습니다.";
            case LandPlotResult.NameTaken: return "이미 사용 중인 회사 이름입니다.";
            case LandPlotResult.NotOwner: return "내가 산 땅이 아니어서 등록할 수 없습니다.";
            default: return "이 땅에는 사업자 등록을 할 수 없습니다.";
        }
    }

    static string PostMessage(SnsPostResult r)
    {
        switch (r)
        {
            case SnsPostResult.Success: return null;
            case SnsPostResult.EmptyText: return "내용을 입력해 주세요.";
            case SnsPostResult.TooLong: return "글이 너무 깁니다.";
            default: return "글을 너무 자주 올릴 수 없습니다.";
        }
    }
}
