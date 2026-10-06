using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Checks book identity through the inventory and trade paths without touching a live save.</summary>
public static class BookModelQA
{
    [Serializable]
    sealed class Report
    {
        public bool passed;
        public int checks;
        public List<string> failures = new List<string>();
    }

    [MenuItem("Tools/Company Game/Inventory/Validate Books")]
    static void Menu() => Debug.Log(Run());

    public static void RunCli() => Debug.Log(Run());

    public static string Run()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.failures.Add("Run in Edit mode.");
            return JsonUtility.ToJson(report, true);
        }
        var item = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Resources/Inventory/Books/BlankBook.asset");
        if (!item) { report.failures.Add("Book asset missing"); return JsonUtility.ToJson(report, true); }
        return RunWithItem(item);
    }

    public static string RunWithItem(ItemData item)
    {
        var report = new Report();
        Check(report, item.itemId == "book_blank" && item.IsBook && item.StackLimit == 99 && item.price == 1000,
            "Blank book type, 99 limit and 1000 price");
        var inventory = new InventoryState();
        Check(report, inventory.TryAdd(item, 100, out _) && inventory.GetSlot(0).Count == 99 &&
            inventory.GetSlot(1).Count == 1, "100 blanks occupy 99 + 1");
        Check(report, inventory.GetSlot(0).BookData == null && inventory.GetSlot(0).InstanceId == null,
            "Blank stack has no author or instance");
        var empty = new BookInstanceData { title = "Empty" };
        Check(report, !inventory.TryPublishBook(0, empty, "author-a", "Author A", out _, out _) &&
            inventory.GetSlot(0).Count == 99, "Untouched blank cannot publish");
        var draft = new BookInstanceData { title = "농부 이야기" };
        draft.pages[0] = "이름 : \n투자금액 : \n투자기간 : \n전달사항 : ";
        Check(report, inventory.TryPublishBook(0, draft, "author-a", "Author A", out int slot, out _) &&
            inventory.GetSlot(0).Count == 98 && slot == 2, "Publish splits exactly one blank");
        var book = inventory.GetSlot(slot); string id = book.InstanceId;
        Check(report, book.Count == 1 && book.StackLimit == 1 && book.BookData.isPublished &&
            book.BookData.authorPlayerId == "author-a" && book.BookData.permission == BookEditPermission.FullEdit,
            "Published identity, author, default permissions");
        Check(report, inventory.TryAdd(item, 1, out _) && inventory.GetSlot(0).Count == 99 && book.Count == 1,
            "Adding blanks never merges a written book");
        Check(report, inventory.TryMoveAmount(0, 3, 5, out _) && inventory.GetSlot(3).Count == 5 &&
            inventory.GetSlot(3).BookData == null, "Blank stack splitting");
        Check(report, inventory.TryMove(slot, 3, out _) && inventory.GetSlot(3).InstanceId == id &&
            inventory.GetSlot(slot).Count == 5, "Written book swaps instead of merging");
        slot = 3;
        var revised = inventory.FindBook(id).BookData.Clone();
        revised.authorPlayerId = "forged"; revised.authorName = "Forged";
        revised.pages[1] = "Another player's edit"; revised.permission = BookEditPermission.ReadOnly;
        Check(report, inventory.TryUpdateBook(id, revised, "other-b", out _) &&
            inventory.FindBook(id).BookData.authorPlayerId == "author-a" &&
            inventory.FindBook(id).BookData.authorName == "Author A" &&
            inventory.FindBook(id).BookData.permission == BookEditPermission.FullEdit,
            "FullEdit preserves original author and permission");
        Check(report, !inventory.TrySetBookPermission(id, BookEditPermission.ReadOnly, "other-b", out _),
            "Only author may change permission");
        Check(report, inventory.TrySetBookPermission(id, BookEditPermission.AddOnly, "author-a", out _), "Set AddOnly");
        revised = inventory.FindBook(id).BookData.Clone();
        revised.pages[0] = revised.pages[0].Replace("이름 : ", "이름 : Customer ");
        Check(report, inventory.TryUpdateBook(id, revised, "other-b", out _), "AddOnly permits insertion inside template");
        revised.pages[0] = "삭제";
        Check(report, !inventory.TryUpdateBook(id, revised, "other-b", out _), "AddOnly rejects deletion/replacement");
        revised = inventory.FindBook(id).BookData.Clone(); revised.title = "Changed title";
        Check(report, !inventory.TryUpdateBook(id, revised, "other-b", out _), "AddOnly locks title");
        var edit = inventory.BeginBookEdit(id, "other-b");
        revised = inventory.FindBook(id).BookData.Clone(); revised.pages[2] = "ㅎ";
        Check(report, inventory.TryUpdateBook(id, revised, "other-b", out _, edit), "AddOnly begins Korean input");
        revised.pages[2] = "한";
        Check(report, inventory.TryUpdateBook(id, revised, "other-b", out _, edit), "IME may compose newly added characters");
        revised.pages[2] = "한국";
        Check(report, inventory.TryUpdateBook(id, revised, "other-b", out _, edit), "New text can be corrected in same editing session");
        revised.pages[0] = "Removed template";
        Check(report, !inventory.TryUpdateBook(id, revised, "other-b", out _, edit), "Session still protects original template");
        edit = inventory.BeginBookEdit(id, "other-b");
        revised = inventory.FindBook(id).BookData.Clone(); revised.pages[2] = "";
        Check(report, !inventory.TryUpdateBook(id, revised, "other-b", out _, edit), "Reopened book locks previous additions");
        Check(report, inventory.TrySetBookPermission(id, BookEditPermission.ReadOnly, "author-a", out _) &&
            !inventory.TryUpdateBook(id, revised, "other-b", out _) &&
            inventory.TryUpdateBook(id, revised, "author-a", out _), "ReadOnly rejects other players, author can rename");
        var ground = new InventoryState();
        Check(report, inventory.TryTransferTo(ground, slot, 1, out _) && ground.GetSlot(0).InstanceId == id,
            "Drop preserves written identity");
        var recipient = new InventoryState();
        Check(report, ground.TryTransferTo(recipient, 0, 1, out _) && recipient.GetSlot(0).BookData.authorName == "Author A",
            "Pickup/transfer keeps original author");
        Check(report, inventory.TryTransferTo(ground, 0, 20, out _) && ground.GetSlot(0).Count == 20 &&
            !ground.GetSlot(0).IsUniqueBook, "Drop blank stack");
        var trade = new TradeSession(recipient, Array.Empty<TradeOffer>());
        Check(report, trade.TryPickUp(0, out _) && trade.TryPlace(4, out _) && recipient.GetSlot(4).InstanceId == id,
            "Trade placement keeps writing and identity");
        Check(report, trade.TryPickUp(4, out _) && trade.TryCancel(out _) && recipient.GetSlot(4).InstanceId == id,
            "Trade cancellation restores written book");
        var saved = new BookSaveService.SavedBook { itemId = item.itemId, instanceId = id,
            count = 1, slot = 3, data = recipient.GetSlot(4).BookData.Clone() };
        var serialized = JsonUtility.FromJson<BookSaveService.SavedBook>(JsonUtility.ToJson(saved));
        var restored = new InventoryState();
        Check(report, BookSaveService.RestoreBook(restored, item, serialized, out _) &&
            restored.GetSlot(3).InstanceId == id && restored.GetSlot(3).BookData.authorName == "Author A" &&
            restored.GetSlot(3).BookData.permission == BookEditPermission.ReadOnly,
            "Serialized written book restores identity, author and permissions");
        saved = new BookSaveService.SavedBook { itemId = item.itemId, count = 99 };
        serialized = JsonUtility.FromJson<BookSaveService.SavedBook>(JsonUtility.ToJson(saved));
        Check(report, BookSaveService.RestoreBook(restored, item, serialized, out _) && restored.GetSlot(0).Count == 99,
            "Serialized blank stack retains quantity");
        Check(report, TradeOffer.TryCreateBookTemplate(recipient.GetSlot(4), "author-a", 1000, out var offer),
            "Author creates reusable sale template");
        Check(report, !TradeOffer.TryCreateBookTemplate(recipient.GetSlot(4), "other-b", 1000, out _),
            "Non-author cannot register source template");
        var customer = new InventoryState();
        customer.TryAdd(CashService.GetCurrency(1000), 2, out _);
        var sale = new TradeSession(customer, new[] { offer });
        Check(report, sale.TryTakeOffer(0, out _) && sale.TryPlace(2, out _) &&
            sale.TryTakeOffer(0, out _) && sale.TryPlace(3, out _), "Same template sells repeatedly");
        var copyA = customer.GetSlot(2); var copyB = customer.GetSlot(3);
        Check(report, copyA.InstanceId != copyB.InstanceId && copyA.InstanceId != id && copyA.Count == 1 &&
            copyA.BookData.authorPlayerId == "author-a" && copyA.BookData.permission == BookEditPermission.ReadOnly,
            "Sold copies have unique IDs and retain original author/permission");
        copyA.BookData.pages[0] = "Independent";
        Check(report, copyB.BookData.pages[0] != "Independent" && offer.get.bookTemplate.pages[0] != "Independent" &&
            recipient.GetSlot(4).BookData.pages[0] != "Independent", "Sold books and source are deep copies");
        var full = new InventoryState(); full.TryAdd(item, 99 * full.Capacity, out _);
        Check(report, !full.TryPublishBook(0, draft, "author-a", "A", out _, out _) && full.GetSlot(0).Count == 99,
            "Full inventory rejects split without losing a blank");
        full.TryRemove(0, 98, out _);
        Check(report, full.TryPublishBook(0, draft, "author-a", "A", out int lastSlot, out _) && lastSlot == 0,
            "Single blank publishes in place even when full");
        Check(report, !full.TryAddBookInstance(item, Guid.NewGuid().ToString("N"), recipient.GetSlot(4).BookData, out _),
            "Written transfer needs a distinct empty slot");
        report.passed = report.failures.Count == 0;
        return JsonUtility.ToJson(report, true);
    }

    static void Check(Report report, bool passed, string name)
    {
        report.checks++;
        if (!passed) report.failures.Add(name);
    }
}
