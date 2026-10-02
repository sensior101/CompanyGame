using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Disposable edit-mode checks for conservation, rejected withdrawals, cash trades and equipment drags.</summary>
public static class CashModelQA
{
    [Serializable]
    public sealed class Report
    {
        public bool passed;
        public int checks;
        public List<string> failures = new List<string>();
    }

    [MenuItem("Tools/Company Game/Inventory/Validate Cash Model")]
    static void MenuRun() { Debug.Log(Run()); }

    public static string Run()
    {
        var report = new Report();
        if (Application.isPlaying)
        {
            report.failures.Add("Run in edit mode so the live bank is untouched.");
            return JsonUtility.ToJson(report, true);
        }
        var bankHost = new GameObject("Cash QA disposable bank") { hideFlags = HideFlags.HideAndDontSave };
        var bank = bankHost.AddComponent<PropertyManager>();
        var items = new List<ItemData>();
        try
        {
            foreach (long amount in new long[] { 0, 1, 99, 100, 999, 1000, 9999, 10000, 99999, 100000, 999999, 1000000, 9999999, 10000000, long.MaxValue })
            {
                CurrencyDesign expected = amount < 1000 ? CurrencyDesign.SilverCoin : amount < 10000 ? CurrencyDesign.GoldCoin :
                    amount < 100000 ? CurrencyDesign.BlueNote : amount < 1000000 ? CurrencyDesign.GreenNote :
                    amount < 10000000 ? CurrencyDesign.YellowNote : CurrencyDesign.RedNote;
                Check(report, CashService.DesignFor(amount) == expected, "Currency design boundary " + amount);
            }
            ItemData note = CashService.GetCurrency(1800);
            Check(report, note.IsCurrency && note.CurrencyValue == 1800 && note.StackLimit == 99 && note == CashService.GetCurrency(1800), "Arbitrary currency value / reusable definition / stacking");
            Check(report, note.DisplayName == "1,800원 동전" && CashService.DesignFor(1800) == CurrencyDesign.GoldCoin &&
                CashService.GetCurrency(9999).DisplayName == "9,999원 동전" && CashService.GetCurrency(10000).DisplayName == "10,000원 지폐", "1800 is a gold coin; names switch at 10000 won");
            Check(report, note.icon && CashService.GetCurrency(100).icon && CashService.GetCurrency(10000).icon &&
                CashService.GetCurrency(100000).icon && CashService.GetCurrency(1000000).icon && CashService.GetCurrency(10000000).icon, "All six coin and note sprites exist");
            bool rejectedDefinition = false;
            try { CashService.GetCurrency(-1); } catch (ArgumentOutOfRangeException) { rejectedDefinition = true; }
            Check(report, rejectedDefinition, "Invalid denomination definition rejected");

            bank.SetMoney(100000);
            var inventory = new InventoryState();
            bool atomicNotification = false, reentrantRejected = false;
            Action<long, long, MoneyChangeReason> bankObserver = (balance, delta, reason) =>
            {
                if (reason != MoneyChangeReason.Withdrawal) return;
                atomicNotification = balance + CashService.CarriedTotal(inventory) == 100000;
                reentrantRejected = !CashService.TryWithdraw(bank, inventory, 100, 1, out _);
            };
            bank.MoneyChanged += bankObserver;
            Check(report, CashService.TryWithdraw(bank, inventory, 1800, 3, out _) && bank.Money == 94600 && CashService.CarriedTotal(inventory) == 5400, "1800 won x 3 debits exactly 5400");
            Check(report, atomicNotification && reentrantRejected && inventory.GetSlot(0).Count == 3, "Observers see committed transfer; reentrant withdrawal cannot duplicate");
            bank.MoneyChanged -= bankObserver;

            Check(report, !CashService.TryWithdraw(bank, inventory, -1, 1, out _) && !CashService.TryWithdraw(bank, inventory, -1800, 1, out _), "Negative face values rejected");
            Check(report, !CashService.TryWithdraw(bank, inventory, 1800, 0, out _) && !CashService.TryWithdraw(bank, inventory, 1800, -1, out _), "Zero / negative quantities rejected");
            Check(report, !CashService.TryWithdraw(bank, inventory, long.MaxValue, 2, out _) && bank.Money == 94600 && CashService.CarriedTotal(inventory) == 5400, "Checked withdrawal multiplication cannot overflow or lose money");
            Check(report, !CashService.TryWithdraw(bank, inventory, 100000, 1, out _) && !CashService.TryWithdraw(bank, inventory, 100000, 1, out _) && inventory.GetSlot(0).Count == 3 && bank.Money == 94600, "Repeated unaffordable submissions mint nothing");
            Check(report, !CashService.TryWithdraw(bank, null, 100, 1, out _) && !CashService.TryWithdraw(null, inventory, 100, 1, out _), "Missing account / inventory rejected");

            var depositInventory = new InventoryState();
            depositInventory.TryAdd(note, 3, out _);
            depositInventory.TryAdd(CashService.GetCurrency(10000), 2, out _);
            bool depositAtomic = false, depositReentryBlocked = false;
            Action<long, long, MoneyChangeReason> depositObserver = (balance, delta, reason) =>
            {
                if (reason != MoneyChangeReason.Deposit) return;
                depositAtomic = delta == 5400 && balance == 100000 && depositInventory.GetSlot(0).IsEmpty &&
                    depositInventory.GetSlot(1).Count == 2;
                depositReentryBlocked = !CashService.TryDeposit(bank, depositInventory, 1, out _) &&
                    !CashService.TryWithdraw(bank, depositInventory, 100, 1, out _);
            };
            bank.MoneyChanged += depositObserver;
            Check(report, CashService.TryDeposit(bank, depositInventory, 0, 3, out _) && bank.Money == 100000 &&
                CashService.CarriedTotal(depositInventory) == 20000, "Deposit removes only selected 1800 x 3 stack and credits exactly 5400");
            Check(report, depositAtomic && depositReentryBlocked, "Deposit observers see both committed balances and cannot reenter transaction");
            bank.MoneyChanged -= depositObserver;
            Check(report, !CashService.TryDeposit(bank, depositInventory, 0, out _) &&
                !CashService.TryDeposit(bank, depositInventory, -1, out _) && bank.Money == 100000 &&
                depositInventory.GetSlot(1).Count == 2, "Empty or invalid slot cannot be deposited again or consume another stack");
            Check(report, !CashService.TryDeposit(bank, null, 0, out _) && !CashService.TryDeposit(null, depositInventory, 1, out _),
                "Missing deposit account or inventory changes nothing");
            bank.SetMoney(long.MaxValue - 19999);
            Check(report, !CashService.TryDeposit(bank, depositInventory, 1, 2, out _) &&
                bank.Money == long.MaxValue - 19999 && depositInventory.GetSlot(1).Count == 2,
                "Bank overflow rejects deposit without consuming currency");
            bank.SetMoney(long.MaxValue - 20000);
            Check(report, CashService.TryDeposit(bank, depositInventory, 1, 2, out _) && bank.Money == long.MaxValue &&
                CashService.CarriedTotal(depositInventory) == 0, "Deposit supports the exact maximum bank balance and releases rejected transaction guard");
            bank.SetMoney(94600);

            var singleDeposit = new InventoryState();
            singleDeposit.TryAdd(note, 3, out _);
            singleDeposit.TryAdd(CashService.GetCurrency(1000), 2, out _);
            Check(report, CashService.TryDeposit(bank, singleDeposit, 0, out _) && bank.Money == 96400 &&
                singleDeposit.GetSlot(0).Count == 2 && singleDeposit.GetSlot(1).Count == 2,
                "Default deposit consumes exactly one unit and preserves remaining selected and other stacks");
            Check(report, !CashService.TryDeposit(bank, singleDeposit, 0, 0, out _) &&
                !CashService.TryDeposit(bank, singleDeposit, 0, -1, out _) &&
                !CashService.TryDeposit(bank, singleDeposit, 0, 3, out _) && bank.Money == 96400 &&
                singleDeposit.GetSlot(0).Count == 2 && singleDeposit.GetSlot(1).Count == 2,
                "Invalid or excessive deposit quantities cannot consume any currency");
            Check(report, CashService.TryDeposit(bank, singleDeposit, 0, 2, out _) && bank.Money == 100000 &&
                singleDeposit.GetSlot(0).IsEmpty && singleDeposit.GetSlot(1).Count == 2,
                "Explicit multi-unit deposit consumes only requested remaining units");
            Check(report, !CashService.TryDeposit(bank, singleDeposit, 0, out _) && bank.Money == 100000 &&
                singleDeposit.GetSlot(1).Count == 2, "Depleted selection never falls through to another currency slot");
            bank.SetMoney(94600);

            ItemData plain = Item("General", ItemCategory.General, items);
            var full = new InventoryState(); full.TryAdd(plain, 16, out _);
            Check(report, !CashService.TryDeposit(bank, full, 0, out _) && Count(full, plain) == 16 && bank.Money == 94600,
                "Ordinary items cannot become bank money");
            Check(report, !CashService.TryWithdraw(bank, full, 1800, 1, out _) && bank.Money == 94600 && Count(full, plain) == 16, "Full inventory leaves bank and items intact");
            var stacks = new InventoryState();
            bank.SetMoney(200000);
            Check(report, CashService.TryWithdraw(bank, stacks, 1800, 100, out _) && stacks.GetSlot(0).Count == 99 && stacks.GetSlot(1).Count == 1 && bank.Money == 20000, "Requested notes span stack limit without changing value");

            var huge = new InventoryState(); bank.SetMoney(long.MaxValue);
            Check(report, CashService.TryWithdraw(bank, huge, long.MaxValue, 1, out _) && bank.Money == 0 && CashService.CarriedTotal(huge) == long.MaxValue, "Maximum supported face value transfers intact");
            bank.AddMoney(100);
            Check(report, !CashService.TryWithdraw(bank, huge, 100, 1, out _) && bank.Money == 100 && CashService.CarriedTotal(huge) == long.MaxValue, "Carried total overflow rejects withdrawal before debit");

            var receiver = new InventoryState();
            Check(report, !CashService.TryTransfer(stacks, full, 0, 2, out _) && stacks.GetSlot(0).Count == 99, "Full trade recipient cannot consume sender notes");
            long combined = CashService.CarriedTotal(stacks) + CashService.CarriedTotal(receiver);
            Check(report, CashService.TryTransfer(stacks, receiver, 0, 2, out _) && stacks.GetSlot(0).Count == 97 && receiver.GetSlot(0).Count == 2 &&
                CashService.CarriedTotal(stacks) + CashService.CarriedTotal(receiver) == combined, "Cash stack transfer conserves denomination and total");
            Check(report, !CashService.TryTransfer(receiver, receiver, 0, 1, out _) && !CashService.TryTransfer(receiver, stacks, 0, 3, out _) && receiver.GetSlot(0).Count == 2, "Self-transfer and over-transfer rejected intact");
            Check(report, !CashService.TryTransfer(receiver, huge, 0, 1, out _) && receiver.GetSlot(0).Count == 2, "Recipient cash overflow cannot consume sender cash");

            var ground = new InventoryState();
            Check(report, receiver.TryTransferTo(ground, 0, 2, out _) && receiver.GetSlot(0).IsEmpty && ground.GetSlot(0).Count == 2, "Drop holder receives same physical stack");
            Check(report, !ground.TryTransferTo(full, 0, 2, out _) && ground.GetSlot(0).Count == 2, "Full pickup leaves dropped stack available");
            Check(report, ground.TryTransferTo(receiver, 0, 2, out _) && !ground.TryTransferTo(receiver, 0, 2, out _) && receiver.GetSlot(0).Count == 2, "Pickup cannot duplicate an already consumed world stack");

            bank.SetMoney(1000000);
            var payment = new InventoryState(); payment.TryAdd(CashService.GetCurrency(1000), 3, out _);
            Check(report, CashService.TryPayNpc(payment, 1800, out _) && CashService.CarriedTotal(payment) == 1200 && bank.Money == 1000000, "NPC payment consumes carried cash and returns exact physical change");
            Check(report, !CashService.TryPayNpc(payment, 1800, out _) && CashService.CarriedTotal(payment) == 1200 && bank.Money == 1000000, "NPC cannot silently spend bank balance");
            var indivisible = new InventoryState(); indivisible.TryAdd(note, 1, out _);
            Check(report, CashService.TryPayNpc(indivisible, 1750, out _) && CashService.CarriedTotal(indivisible) == 50 &&
                indivisible.GetSlot(0).Item.CurrencyValue == 50, "Payment returns exact 50 won silver change without rounding");

            var payer = new InventoryState(); payer.TryAdd(CashService.GetCurrency(10000), 1, out _);
            var merchant = new InventoryState();
            Check(report, CashService.TryPay(payer, merchant, 1800, out _) && CashService.CarriedTotal(payer) == 8200 && CashService.CarriedTotal(merchant) == 1800, "Peer or merchant settlement preserves total across both inventories");
            Check(report, !CashService.TryPay(payer, full, 1000, out _) && CashService.CarriedTotal(payer) == 8200, "Failed settlement changes neither payer nor recipient");

            var guardedPayer = new InventoryState(); guardedPayer.TryAdd(CashService.GetCurrency(1000), 4, out _);
            var guardedRecipient = new InventoryState();
            int payNotifications = 0;
            bool paymentReentryBlocked = false;
            Action paymentObserver = () =>
            {
                payNotifications++;
                paymentReentryBlocked = !CashService.TryPay(guardedPayer, guardedRecipient, 100, out _) &&
                    !CashService.TryPayNpc(guardedPayer, 100, out _) &&
                    !CashService.TryTransfer(guardedPayer, guardedRecipient, 0, 1, out _);
            };
            guardedPayer.Changed += paymentObserver;
            Check(report, CashService.TryPay(guardedPayer, guardedRecipient, 1000, out _) && paymentReentryBlocked && payNotifications == 1 &&
                CashService.CarriedTotal(guardedPayer) == 3000 && CashService.CarriedTotal(guardedRecipient) == 1000, "Payment observers cannot trigger recursive transfers or duplicate charges");
            guardedPayer.Changed -= paymentObserver;
            Check(report, CashService.TryPayNpc(guardedPayer, 1000, out _) && CashService.CarriedTotal(guardedPayer) == 2000, "Transaction guard releases after successful payment");
            Check(report, !CashService.TryPayNpc(guardedPayer, 3000, out _) && CashService.TryTransfer(guardedPayer, guardedRecipient, 0, 1, out _) &&
                CashService.CarriedTotal(guardedPayer) == 1000 && CashService.CarriedTotal(guardedRecipient) == 2000, "Rejected payment releases guard without changing balances");

            ValidateZeroCurrency(report, bank);

            ItemData shirt = Item("Top A", ItemCategory.Top, items), otherShirt = Item("Top B", ItemCategory.Top, items);
            ItemData accessory = Item("Legacy accessory", ItemCategory.Accessory, items), pet = Item("Pet", ItemCategory.Pet, items);
            var gear = new InventoryState(); gear.TryAdd(shirt, 1, out _); gear.TryEquip(0, EquipmentSlot.Top, out _);
            Check(report, gear.TryUnequipTo(EquipmentSlot.Top, 7, out _) && gear.GetSlot(7).Item == shirt && gear.GetEquipment(EquipmentSlot.Top).IsEmpty, "Equipment drag targets exact empty inventory cell");
            gear.TryEquip(7, EquipmentSlot.Top, out _); gear.TryAdd(otherShirt, 1, out _);
            Check(report, gear.TryUnequipTo(EquipmentSlot.Top, 0, out _) && gear.GetSlot(0).Item == shirt && gear.GetEquipment(EquipmentSlot.Top).Item == otherShirt, "Equipment drag swaps compatible occupied cell");
            gear.TryAdd(plain, 1, out _);
            Check(report, !gear.TryUnequipTo(EquipmentSlot.Top, 1, out _) && gear.GetEquipment(EquipmentSlot.Top).Item == otherShirt && gear.GetSlot(1).Item == plain, "Incompatible equipment drag loses nothing");
            Check(report, gear.TryRemoveEquipment(EquipmentSlot.Top, out _) && !gear.TryRemoveEquipment(EquipmentSlot.Top, out _) && gear.GetEquipment(EquipmentSlot.Top).IsEmpty, "Equipment drop removes exactly once");
            gear.TryAdd(accessory, 1, out _);
            Check(report, !gear.TryEquip(2, EquipmentSlot.Accessory, out _) && gear.GetEquipment(EquipmentSlot.Accessory) == null && !accessory.Fits(EquipmentSlot.Accessory), "Retired accessory slot is unavailable in model");
            gear.TryAdd(pet, 1, out _);
            Check(report, (int)EquipmentSlot.Pet == 5 && gear.TryEquip(3, EquipmentSlot.Pet, out _) && gear.GetEquipment(EquipmentSlot.Pet).Item == pet, "Pet retains serialized enum id and independent slot");
            report.passed = report.failures.Count == 0;
        }
        catch (Exception exception) { report.failures.Add(exception.ToString()); report.passed = false; }
        finally
        {
            foreach (ItemData item in items) UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(bankHost);
        }
        return JsonUtility.ToJson(report, true);
    }

    static void ValidateZeroCurrency(Report report, PropertyManager bank)
    {
        long previousBalance = bank.Money;
        bank.SetMoney(0);
        int bankNotifications = 0;
        Action<long, long, MoneyChangeReason> bankObserver = (balance, delta, reason) => bankNotifications++;
        bank.MoneyChanged += bankObserver;
        try
        {
            ItemData fake = CashService.GetCurrency(0);
            Check(report, fake.IsCurrency && fake.CurrencyValue == 0 && fake.StackLimit == 99 &&
                fake.DisplayName == "0원 동전" && fake.icon && fake.icon == CashService.GetCurrency(100).icon &&
                CashService.DesignFor(0) == CurrencyDesign.SilverCoin,
                "Zero-won fake currency uses the silver coin artwork and normal stack limit");

            var zeroInventory = new InventoryState();
            int inventoryNotifications = 0;
            bool reentryBlocked = false;
            Action inventoryObserver = () =>
            {
                inventoryNotifications++;
                reentryBlocked = !CashService.TryWithdraw(bank, zeroInventory, 0, 1, out _) &&
                    !CashService.TryDeposit(bank, zeroInventory, 0, out _);
            };
            zeroInventory.Changed += inventoryObserver;
            Check(report, CashService.TryWithdraw(bank, zeroInventory, 0, 100, out _) && bank.Money == 0 &&
                zeroInventory.GetSlot(0).Count == 99 && zeroInventory.GetSlot(1).Count == 1 && CashService.CarriedTotal(zeroInventory) == 0,
                "Empty bank can withdraw zero-won coins across two normal stacks without creating value");
            zeroInventory.Changed -= inventoryObserver;
            Check(report, inventoryNotifications == 1 && bankNotifications == 0 && reentryBlocked,
                "Zero withdrawal publishes inventory once, leaves bank notifications silent, and blocks reentry");
            Check(report, !CashService.TryWithdraw(bank, zeroInventory, 0, 0, out _) &&
                !CashService.TryWithdraw(bank, zeroInventory, 0, -1, out _) &&
                !CashService.TryWithdraw(bank, zeroInventory, 0, int.MaxValue, out _) &&
                !CashService.TryWithdraw(bank, zeroInventory, -1, 1, out _) && Count(zeroInventory, fake) == 100,
                "Zero-face withdrawal still rejects invalid quantities, capacity overflow and negative values");

            var fullZero = new InventoryState();
            Check(report, CashService.TryWithdraw(bank, fullZero, 0, 16 * 99, out _) &&
                !CashService.TryWithdraw(bank, fullZero, 0, 1, out _) && Count(fullZero, fake) == 16 * 99 && bank.Money == 0,
                "Free fake coins cannot exceed inventory carrying capacity");
            var peer = new InventoryState();
            Check(report, CashService.TryTransfer(zeroInventory, peer, 0, 2, out _) &&
                zeroInventory.GetSlot(0).Count == 97 && peer.GetSlot(0).Count == 2 &&
                CashService.CarriedTotal(zeroInventory) + CashService.CarriedTotal(peer) == 0,
                "Players can transfer fake coins as items with no monetary value");
            Check(report, !CashService.TryTransfer(peer, fullZero, 0, 1, out _) && peer.GetSlot(0).Count == 2,
                "Full recipient cannot destroy transferred zero-won currency");
            Check(report, CashService.TryDeposit(bank, peer, 0, out _) && peer.GetSlot(0).Count == 1 && bank.Money == 0 &&
                CashService.TryDeposit(bank, peer, 0, out _) && peer.GetSlot(0).IsEmpty &&
                !CashService.TryDeposit(bank, peer, 0, out _) && bankNotifications == 0,
                "Zero coin deposit consumes one at a time without credit, bank event or duplicate removal");

            var merchant = new InventoryState();
            Check(report, !CashService.TryPayNpc(zeroInventory, 1, out _) &&
                !CashService.TryPay(zeroInventory, merchant, 1, out _) && Count(zeroInventory, fake) == 98 &&
                CashService.CarriedTotal(merchant) == 0 && bank.Money == 0,
                "Fake coins cannot pay a positive NPC or player price");
            Check(report, !CashService.TryPayNpc(zeroInventory, 0, out _) &&
                !CashService.TryPay(zeroInventory, merchant, 0, out _) && merchant.GetSlot(0).IsEmpty && Count(zeroInventory, fake) == 98,
                "Zero settlement cannot silently mint recipient currency; fake coins use explicit item transfer");

            zeroInventory.TryAdd(CashService.GetCurrency(1), 3, out _);
            Check(report, CashService.TryPayNpc(zeroInventory, 2, out _) && CashService.CarriedTotal(zeroInventory) == 1 &&
                Count(zeroInventory, fake) == 98, "Payments skip zero-won stacks and use only positive coins");
            Check(report, CashService.TryPay(zeroInventory, merchant, 1, out _) && CashService.CarriedTotal(zeroInventory) == 0 &&
                CashService.CarriedTotal(merchant) == 1 && Count(zeroInventory, fake) == 98,
                "Mixed real and fake currency transfers preserve real value without consuming fake coins");

            bank.SetMoney(long.MaxValue);
            bankNotifications = 0;
            Check(report, CashService.TryDeposit(bank, zeroInventory, 0, out _) && bank.Money == long.MaxValue &&
                Count(zeroInventory, fake) == 97 && bankNotifications == 0,
                "Zero coin can be deposited even when the bank is at its maximum balance");
            bank.SetMoney(100);
            var smallCoins = new InventoryState();
            Check(report, CashService.TryWithdraw(bank, smallCoins, 99, 1, out _) &&
                CashService.TryWithdraw(bank, smallCoins, 1, 1, out _) && bank.Money == 0 &&
                CashService.CarriedTotal(smallCoins) == 100 && smallCoins.GetSlot(0).Item.CurrencyValue == 99 &&
                smallCoins.GetSlot(1).Item.CurrencyValue == 1, "Values from 1 to 99 won withdraw as positive silver coins");
        }
        finally
        {
            bank.MoneyChanged -= bankObserver;
            bank.SetMoney(previousBalance);
        }
    }

    static ItemData Item(string name, ItemCategory category, List<ItemData> items)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemId = item.name = item.displayName = name;
        item.category = category;
        items.Add(item);
        return item;
    }

    static int Count(InventoryState state, ItemData item)
    {
        int count = 0;
        for (int i = 0; i < state.Capacity; i++) if (state.GetSlot(i).Item == item) count += state.GetSlot(i).Count;
        return count;
    }

    static void Check(Report report, bool pass, string description)
    {
        report.checks++;
        if (!pass) report.failures.Add(description);
    }
}
