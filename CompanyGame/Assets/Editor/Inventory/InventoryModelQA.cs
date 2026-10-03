using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises disposable models and item definitions; never touches the player's session.</summary>
public static class InventoryModelQA
{
    [Serializable]
    public class Report
    {
        public bool passed;
        public int checks;
        public List<string> failures = new List<string>();
    }

    [MenuItem("Tools/Company Game/Inventory/Validate Model")]
    static void ValidateMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        var report = new Report();
        var created = new List<ItemData>();
        try
        {
            ItemData shirt = Item("Shirt", ItemCategory.Top, created);
            ItemData otherShirt = Item("Other shirt", ItemCategory.Top, created);
            ItemData shoes = Item("Shoes", ItemCategory.Shoes, created);
            ItemData pet = Item("Pet", ItemCategory.Pet, created);
            ItemData bag = Item("Bag", ItemCategory.Bag, created);
            ItemData coins = Item("Stack", ItemCategory.General, created); coins.maxStack = 10;
            ItemData plain = Item("Single", ItemCategory.General, created);
            var inventory = new InventoryState();
            Check(report, inventory.Capacity == 16 && InventoryState.HotbarSize == 8, "Default capacity / hotbar");
            Check(report, inventory.GetSlot(16) == null && inventory.GetSlot(-1) == null, "Locked and invalid slots inaccessible");
            int changed = 0; inventory.Changed += () => changed++;
            Check(report, inventory.TryAdd(shirt, 1, out _) && inventory.GetSlot(0).Item == shirt, "Add item");
            Check(report, !inventory.TryEquip(0, EquipmentSlot.Shoes, out _) && inventory.GetSlot(0).Item == shirt, "Wrong equipment category rejected intact");
            Check(report, inventory.TryEquip(0, EquipmentSlot.Top, out _) && inventory.GetSlot(0).IsEmpty && inventory.GetEquipment(EquipmentSlot.Top).Item == shirt, "Equip top");
            inventory.TryAdd(otherShirt, 1, out _);
            Check(report, inventory.TryEquip(0, EquipmentSlot.Top, out _) && inventory.GetSlot(0).Item == shirt && inventory.GetEquipment(EquipmentSlot.Top).Item == otherShirt, "Equipment replacement swaps without loss");
            Check(report, inventory.TryUnequip(EquipmentSlot.Top, out _) && inventory.GetSlot(1).Item == otherShirt, "Unequip to empty storage");
            inventory.TryAdd(shoes, 1, out _);
            Check(report, inventory.TryMove(0, 2, out _) && inventory.GetSlot(0).Item == shoes && inventory.GetSlot(2).Item == shirt, "Swap occupied inventory slots");
            Check(report, inventory.TryMove(2, 15, out _) && inventory.GetSlot(2).IsEmpty && inventory.GetSlot(15).Item == shirt, "Move to last base slot");
            Check(report, !inventory.TryMove(15, 16, out _) && inventory.GetSlot(15).Item == shirt, "Cannot move into locked row");
            inventory.TryAdd(pet, 1, out _);
            Check(report, inventory.TryEquip(2, EquipmentSlot.Pet, out _) && inventory.GetEquipment(EquipmentSlot.Pet).Item == pet, "Equip pet separately");
            Check(report, !inventory.TryEquip(0, (EquipmentSlot)99, out _) && inventory.GetEquipment((EquipmentSlot)(-1)) == null, "Invalid equipment enum rejected");
            Check(report, inventory.SelectHotbar(7) && inventory.SelectedHotbarIndex == 7 && !inventory.SelectHotbar(8), "Hotbar selection bounded to eight");
            Check(report, !inventory.TryRemove(0, 2, out _) && inventory.GetSlot(0).Count == 1, "Over-removal cannot destroy item");
            Check(report, inventory.TryRemove(0, 1, out _) && inventory.GetSlot(0).IsEmpty, "Remove last item clears slot");
            Check(report, changed >= 10, "Change notifications delivered");

            var full = new InventoryState();
            full.TryAdd(shirt, 1, out _); full.TryEquip(0, EquipmentSlot.Top, out _);
            Check(report, full.TryAdd(plain, 16, out _), "Fill sixteen slots");
            Check(report, !full.TryAdd(plain, 1, out _) && Count(full, plain) == 16, "Full add fails atomically");
            Check(report, !full.TryUnequip(EquipmentSlot.Top, out _) && full.GetEquipment(EquipmentSlot.Top).Item == shirt, "Full unequip retains equipment");
            full.TryRemove(3, 1, out _); full.TryAdd(otherShirt, 1, out _);
            Check(report, full.TryEquip(3, EquipmentSlot.Top, out _) && full.GetSlot(3).Item == shirt, "Full inventory equipment replacement succeeds");
            Check(report, !full.TryAdd(null, 1, out _) && !full.TryAdd(plain, 0, out _) && !full.TryRemove(0, -1, out _), "Invalid quantities / missing item rejected");

            var stacks = new InventoryState();
            Check(report, stacks.TryAdd(coins, 12, out _) && stacks.GetSlot(0).Count == 10 && stacks.GetSlot(1).Count == 2, "Stack limit divides quantity");
            Check(report, stacks.TryRemove(0, 5, out _) && stacks.TryMove(1, 0, out _) && stacks.GetSlot(0).Count == 7 && stacks.GetSlot(1).IsEmpty, "Stack merge clears empty source");
            stacks.TryAdd(coins, 7, out _); stacks.TryRemove(0, 2, out _);
            Check(report, stacks.TryMove(1, 0, out _) && stacks.GetSlot(0).Count == 10 && stacks.GetSlot(1).Count == 2, "Partial merge preserves remainder");
            Check(report, !stacks.TryAdd(coins, int.MaxValue, out _) && Count(stacks, coins) == 12, "Oversized add fails without overflow / partial changes");

            var expanded = new InventoryState();
            expanded.TryAdd(bag, 1, out _);
            Check(report, expanded.TryUseBag(0, out _) && expanded.Capacity == 24 && expanded.GetSlot(0).IsEmpty, "Bag consumes once and unlocks exactly eight slots");
            Check(report, expanded.GetSlot(23) != null && expanded.GetSlot(24) == null, "Expanded upper bound");
            expanded.TryAdd(bag, 1, out _);
            Check(report, !expanded.TryUseBag(0, out _) && expanded.GetSlot(0).Item == bag, "Duplicate bag not consumed");
            Check(report, !expanded.TryExpandWithBag(out _) && expanded.Capacity == 24, "Capacity capped at twenty-four");
            Check(report, expanded.TryAdd(plain, 23, out _) && !expanded.TryAdd(plain, 1, out _), "Expanded capacity honored");
            var directExpansion = new InventoryState();
            Check(report, directExpansion.TryExpandWithBag(out _) && directExpansion.Capacity == 24, "Purchase expansion API");
            Check(report, !directExpansion.TryUseBag(0, out _), "Non-bag cannot expand");
            report.passed = report.failures.Count == 0;
        }
        catch (Exception exception) { report.failures.Add(exception.ToString()); }
        finally { foreach (var item in created) UnityEngine.Object.DestroyImmediate(item); }
        return JsonUtility.ToJson(report, true);
    }

    static ItemData Item(string name, ItemCategory category, List<ItemData> created)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.name = item.displayName = item.itemId = name;
        item.category = category;
        created.Add(item);
        return item;
    }

    static int Count(InventoryState inventory, ItemData item)
    {
        int count = 0;
        for (int i = 0; i < inventory.Capacity; i++)
            if (inventory.GetSlot(i).Item == item) count += inventory.GetSlot(i).Count;
        return count;
    }

    static void Check(Report report, bool passed, string name)
    {
        report.checks++;
        if (!passed) report.failures.Add(name);
    }
}
