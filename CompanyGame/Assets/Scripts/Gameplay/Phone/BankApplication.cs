using System;

/// <summary>Phone bank app. The balance is BankManager; cash is the coins and notes carried in the inventory.</summary>
public class BankApplication : PhoneAppBase
{
    public event Action Refreshed;

    protected override void OnOpened() => Refreshed?.Invoke();

    /// <summary>Deposits one carried coin or note of exactly this face value. error says why it could not.</summary>
    public bool Deposit(long amount, out string error)
    {
        var inventory = Inventory;
        error = "인벤토리를 찾을 수 없습니다.";
        if (inventory == null) return false;
        for (int i = 0; i < inventory.Capacity; i++)
        {
            var slot = inventory.GetSlot(i);
            if (slot != null && !slot.IsEmpty && slot.Item.IsCurrency && slot.Item.CurrencyValue == amount)
                return CashService.TryDeposit(inventory, i, out error);
        }
        error = amount.ToString("N0") + "원짜리 현금이 없습니다.";
        return false;
    }

    /// <summary>Withdraws one coin or note of this face value (기획안 1-1: 1500원 1장). error says why it could not.</summary>
    public bool Withdraw(long amount, out string error)
    {
        error = "인벤토리를 찾을 수 없습니다.";
        return Inventory != null && CashService.TryWithdraw(Inventory, amount, 1, out error);
    }

    public long GetBankBalance() => BankManager.Instance != null ? BankManager.Instance.Money : 0L;

    public long GetCash() => Inventory != null ? CashService.CarriedTotal(Inventory) : 0L;

    static InventoryState Inventory => InventoryManager.Instance ? InventoryManager.Instance.State : null;
}
