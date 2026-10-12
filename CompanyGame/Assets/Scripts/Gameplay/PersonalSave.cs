using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class PersonalSaveData
{
    public int version = 1;
    public long bankBalance;
    public StockPortfolio portfolio = new StockPortfolio();
}

/// <summary>
/// Per-player state that never travels with the shared world: bank balance and stock holdings.
/// Wallet cash stays in the inventory (InventorySnapshot) and is never merged with the bank balance.
/// Online it is uploaded by the net layer (personal_json); offline it is kept in player/&lt;LocalPlayerId&gt;.json.
/// </summary>
public static class PersonalSave
{
    /// <summary>Tests point this at a scratch folder.</summary>
    public static string DirOverride;

    public static string PathFor(string playerId) =>
        System.IO.Path.Combine(DirOverride ?? System.IO.Path.Combine(ProfileSlot.DataRoot, "player"), SafeName(playerId) + ".json");

    public static string Path => PathFor(GameSession.LocalPlayerId);

    static string SafeName(string id)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) id = (id ?? "").Replace(c, '_');
        return string.IsNullOrEmpty(id) ? "player" : id;
    }

    public static string Export()
    {
        var data = new PersonalSaveData();
        if (BankManager.Instance) data.bankBalance = BankManager.Instance.Money;
        if (StockMarketManager.Instance) data.portfolio = StockMarketManager.Instance.Exchange.CapturePortfolio();
        return JsonUtility.ToJson(data);
    }

    public static bool Import(string json)
    {
        PersonalSaveData d;
        try { d = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PersonalSaveData>(json); }
        catch (Exception e) { Debug.LogWarning("PersonalSave: unreadable data. " + e.Message); return false; }
        if (d == null || d.version != 1) return false;
        var bank = BankManager.EnsureInstance();
        if (bank) bank.RestoreBalance(d.bankBalance);
        if (StockMarketManager.Instance) StockMarketManager.Instance.Exchange.RestorePortfolio(d.portfolio);
        return true;
    }

    public static bool SaveToDisk() => SaveToDisk(Path);

    public static bool SaveToDisk(string path)
    {
        try { SaveManager.WriteJsonFile(path, Export()); return true; }
        catch (Exception e) { Debug.LogWarning("PersonalSave: save failed. " + e.Message); return false; }
    }

    public static bool LoadFromDisk() => LoadFromDisk(Path);

    public static bool LoadFromDisk(string path)
    {
        try { return File.Exists(path) && Import(File.ReadAllText(path)); }
        catch (Exception e) { Debug.LogWarning("PersonalSave: load failed. " + e.Message); return false; }
    }

    // --- identity (per account on this PC) ---

    /// <summary>Personal state and inventory as they were before any save was loaded: what a brand-new account starts with.</summary>
    public static string BaselinePersonal { get; private set; }
    public static string BaselineInventory { get; private set; }

    public static void CaptureBaseline()
    {
        if (BaselinePersonal != null) return;
        BaselinePersonal = Export();
        if (InventoryManager.Instance) BaselineInventory = InventorySnapshot.Export(InventoryManager.Instance.State);
    }

    /// <summary>Bank, portfolio and inventory back to a new account's start. Wallet cash lives in the inventory, the bank stays separate.</summary>
    public static void ResetToFresh()
    {
        if (BaselinePersonal != null) Import(BaselinePersonal);
        if (BaselineInventory != null && InventoryManager.Instance) InventorySnapshot.Import(InventoryManager.Instance.State, BaselineInventory);
    }

    /// <summary>
    /// A different account logs in on this PC: the previous account's data is saved to its own file and the new
    /// account starts fresh (or from its own file). It never inherits the previous bank or inventory.
    /// </summary>
    public static void SwitchAccount(string oldId, string newId)
    {
        SaveToDisk(PathFor(oldId));
        ResetToFresh();
        LoadFromDisk(PathFor(newId));
    }

    /// <summary>
    /// First login on this PC: the person's offline progress becomes the account's. The per-PC file is copied
    /// to the account file; authored books (inventory and library) and library records move to the account id.
    /// </summary>
    public static void MigrateIdentity(string oldId, string newId)
    {
        try
        {
            string from = PathFor(oldId), to = PathFor(newId);
            if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
        }
        catch (Exception e) { Debug.LogWarning("PersonalSave: migrate failed. " + e.Message); }
        string name = GameSession.LocalPlayerName;
        var inv = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (inv != null)
        {
            for (int i = 0; i < inv.Capacity; i++) Retarget(inv.GetSlot(i)?.BookData, oldId, newId, name);
            for (int e = 0; e < 6; e++) Retarget(inv.GetEquipment((EquipmentSlot)e)?.BookData, oldId, newId, name);
        }
        foreach (var w in LibraryCatalog.Works) Retarget(w.book, oldId, newId, name);
        foreach (var l in LibraryCatalog.Loans) if (l.borrowerId == oldId) l.borrowerId = newId;
        var oldAccount = LibraryCatalog.GetAccount(oldId);
        var account = LibraryCatalog.GetAccount(newId);
        if (account.fines == 0 && account.pendingPayment == 0 && account.penaltyPoints == 0)
        { account.fines = oldAccount.fines; account.pendingPayment = oldAccount.pendingPayment; account.penaltyPoints = oldAccount.penaltyPoints; }
        if (inv != null) LibraryCatalog.ConnectLocal(inv);
    }

    static void Retarget(BookInstanceData book, string oldId, string newId, string name)
    {
        if (book == null || book.authorPlayerId != oldId) return;
        book.authorPlayerId = newId;
        book.authorName = name;
    }
}
