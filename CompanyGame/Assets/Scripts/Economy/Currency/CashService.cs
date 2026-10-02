using System;
using System.Collections.Generic;
using UnityEngine;

public enum CurrencyDesign
{
    SilverCoin = 0,
    Coin = SilverCoin,
    GoldCoin = 1,
    BlueNote = 2,
    GreenNote = 3,
    YellowNote = 4,
    RedNote = 5
}

/// <summary>
/// Local session transactions.
/// Bank money and physical currency are never interchangeable implicitly.
/// </summary>
public static class CashService
{
    // 최소 화폐 금액: 0원
    public const long MinimumFaceValue = 0;

    static readonly Dictionary<long, ItemData> definitions =
        new Dictionary<long, ItemData>();

    static bool transactionInProgress;

    public static long BankBalance =>
        PropertyManager.EnsureInstance()
            ? PropertyManager.Instance.BankBalance
            : 0;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        foreach (ItemData item in definitions.Values)
        {
            if (item)
                UnityEngine.Object.Destroy(item);
        }

        definitions.Clear();
        transactionInProgress = false;
    }


    // 금액에 따라 화폐 디자인 결정
    public static CurrencyDesign DesignFor(long faceValue)
    {
        if (faceValue < MinimumFaceValue)
            throw new ArgumentOutOfRangeException(nameof(faceValue));

        // 0 ~ 999원
        if (faceValue < 1000)
            return CurrencyDesign.SilverCoin;

        // 1,000 ~ 9,999원
        if (faceValue < 10000)
            return CurrencyDesign.GoldCoin;

        // 10,000 ~ 99,999원
        if (faceValue < 100000)
            return CurrencyDesign.BlueNote;

        // 100,000 ~ 999,999원
        if (faceValue < 1000000)
            return CurrencyDesign.GreenNote;

        // 1,000,000 ~ 9,999,999원
        if (faceValue < 10000000)
            return CurrencyDesign.YellowNote;

        // 10,000,000원 이상
        return CurrencyDesign.RedNote;
    }


    // 해당 금액의 화폐 ItemData 반환
    public static ItemData GetCurrency(long faceValue)
    {
        if (faceValue < MinimumFaceValue)
            throw new ArgumentOutOfRangeException(nameof(faceValue));

        if (definitions.TryGetValue(
                faceValue,
                out ItemData existing) &&
            existing)
        {
            return existing;
        }

        var item = ScriptableObject.CreateInstance<ItemData>();

        item.hideFlags = HideFlags.HideAndDontSave;
        item.ConfigureCurrency(faceValue);
        item.icon = CurrencyIconFactory.GetSprite(faceValue);

        definitions[faceValue] = item;

        return item;
    }


    // 인벤토리에 들어있는 현금 총액
    public static long CarriedTotal(
        InventoryState inventory)
    {
        if (inventory == null)
            return 0;

        long total = 0;

        checked
        {
            for (int i = 0;
                 i < inventory.Capacity;
                 i++)
            {
                ItemStack stack =
                    inventory.GetSlot(i);

                if (stack != null &&
                    !stack.IsEmpty &&
                    stack.Item.IsCurrency)
                {
                    total +=
                        stack.Item.CurrencyValue *
                        stack.Count;
                }
            }
        }

        return total;
    }


    // 지갑 → 인벤토리 출금
    public static bool TryWithdraw(
        InventoryState inventory,
        long faceValue,
        int quantity,
        out string error)
    {
        return TryWithdraw(
            PropertyManager.EnsureInstance(),
            inventory,
            faceValue,
            quantity,
            out error);
    }


    /// <summary>
    /// Publishes a completed bank debit and
    /// inventory addition together,
    /// or changes neither.
    /// </summary>
    public static bool TryWithdraw(
        PropertyManager bank,
        InventoryState inventory,
        long faceValue,
        int quantity,
        out string error)
    {
        error = null;

        if (transactionInProgress)
        {
            return Fail(
                "이전 거래를 처리 중입니다.",
                out error);
        }

        if (!bank || inventory == null)
        {
            return Fail(
                "지갑 또는 인벤토리를 찾을 수 없습니다.",
                out error);
        }

        if (faceValue < MinimumFaceValue ||
            quantity <= 0)
        {
            return Fail(
                "한 장의 금액은 0원 이상, 장수는 1 이상으로 입력해 주세요.",
                out error);
        }


        long total;

        try
        {
            total =
                checked(faceValue * quantity);
        }
        catch (OverflowException)
        {
            return Fail(
                "출금 금액이 처리 가능한 범위를 초과합니다.",
                out error);
        }


        if (total > 0 && !bank.CanAfford(total))
        {
            return Fail(
                "지갑 잔액이 부족합니다.",
                out error);
        }


        transactionInProgress = true;

        try
        {
            InventoryState draft =
                inventory.Copy();

            if (!draft.TryAdd(
                    GetCurrency(faceValue),
                    quantity,
                    out error))
            {
                return false;
            }


            if (total > 0 && !bank.TryDebitSilently(total))
            {
                return Fail(
                    "지갑 잔액이 부족합니다.",
                    out error);
            }


            inventory.ReplaceWith(draft);

            if (total > 0) bank.NotifyWithdrawal(total);
            inventory.NotifyChanged();

            return true;
        }
        finally
        {
            transactionInProgress = false;
        }
    }


    // 인벤토리 → 지갑 입금
    public static bool TryDeposit(
        InventoryState inventory,
        int sourceIndex,
        out string error)
    {
        return TryDeposit(
            PropertyManager.EnsureInstance(),
            inventory,
            sourceIndex,
            1,
            out error);
    }


    public static bool TryDeposit(
        InventoryState inventory,
        int sourceIndex,
        int quantity,
        out string error)
    {
        return TryDeposit(
            PropertyManager.EnsureInstance(),
            inventory,
            sourceIndex,
            quantity,
            out error);
    }


    public static bool TryDeposit(
        PropertyManager bank,
        InventoryState inventory,
        int sourceIndex,
        out string error)
    {
        return TryDeposit(bank, inventory, sourceIndex, 1, out error);
    }


    /// <summary>
    /// Returns the requested units from only the selected cash stack to the bank,
    /// publishing both changes together.
    /// </summary>
    public static bool TryDeposit(
        PropertyManager bank,
        InventoryState inventory,
        int sourceIndex,
        int quantity,
        out string error)
    {
        error = null;


        if (transactionInProgress)
        {
            return Fail(
                "이전 거래를 처리 중입니다.",
                out error);
        }


        if (!bank || inventory == null)
        {
            return Fail(
                "지갑 또는 인벤토리를 찾을 수 없습니다.",
                out error);
        }


        ItemStack stack =
            inventory.GetSlot(sourceIndex);


        if (stack == null ||
            stack.IsEmpty ||
            !stack.Item.IsCurrency)
        {
            return Fail(
                "입금할 화폐를 선택해 주세요.",
                out error);
        }

        if (quantity <= 0 || quantity > stack.Count)
            return Fail("입금할 화폐 수량을 확인해 주세요.", out error);


        long total;

        try
        {
            total =
                checked(
                    stack.Item.CurrencyValue *
                    quantity);
        }
        catch (OverflowException)
        {
            return Fail(
                "입금 금액이 처리 가능한 범위를 초과합니다.",
                out error);
        }


        transactionInProgress = true;

        try
        {
            InventoryState draft =
                inventory.Copy();


            if (!draft.TryRemove(
                    sourceIndex,
                    quantity,
                    out error))
            {
                return false;
            }


            if (total > 0 && !bank.TryCreditSilently(total))
            {
                return Fail(
                "지갑에 입금할 수 있는 금액을 초과합니다.",
                    out error);
            }


            inventory.ReplaceWith(draft);

            if (total > 0) bank.NotifyDeposit(total);
            inventory.NotifyChanged();

            return true;
        }
        finally
        {
            transactionInProgress = false;
        }
    }


    /// <summary>
    /// Transfer physical notes/coins
    /// to another player or merchant inventory
    /// without bank access.
    /// </summary>
    public static bool TryTransfer(
        InventoryState source,
        InventoryState recipient,
        int sourceIndex,
        int quantity,
        out string error)
    {
        error = null;


        if (transactionInProgress)
        {
            return Fail(
                "이전 거래를 처리 중입니다.",
                out error);
        }


        ItemStack stack =
            source?.GetSlot(sourceIndex);


        if (stack == null ||
            stack.IsEmpty ||
            !stack.Item.IsCurrency)
        {
            return Fail(
                "거래할 화폐를 선택해 주세요.",
                out error);
        }


        transactionInProgress = true;

        try
        {
            return source.TryTransferTo(
                recipient,
                sourceIndex,
                quantity,
                out error);
        }
        finally
        {
            transactionInProgress = false;
        }
    }


    /// <summary>
    /// NPC purchase settlement.
    /// The requested value leaves circulation;
    /// change stays physical currency.
    /// </summary>
    public static bool TryPayNpc(
        InventoryState inventory,
        long amount,
        out string error)
    {
        if (transactionInProgress)
        {
            return Fail(
                "이전 거래를 처리 중입니다.",
                out error);
        }


        transactionInProgress = true;

        try
        {
            if (!PlanPayment(
                    inventory,
                    amount,
                    out InventoryState draft,
                    out error))
            {
                return false;
            }


            inventory.ReplaceWith(draft);
            inventory.NotifyChanged();

            return true;
        }
        finally
        {
            transactionInProgress = false;
        }
    }


    public static bool TryPay(
        InventoryState payer,
        InventoryState recipient,
        long amount,
        out string error)
    {
        error = null;


        if (transactionInProgress)
        {
            return Fail(
                "이전 거래를 처리 중입니다.",
                out error);
        }


        if (recipient == null ||
            ReferenceEquals(
                payer,
                recipient))
        {
            return Fail(
                "거래 상대를 확인해 주세요.",
                out error);
        }


        transactionInProgress = true;

        try
        {
            if (!PlanPayment(
                    payer,
                    amount,
                    out InventoryState payerDraft,
                    out error))
            {
                return false;
            }


            InventoryState recipientDraft =
                recipient.Copy();


            if (!recipientDraft.TryAdd(
                    GetCurrency(amount),
                    1,
                    out error))
            {
                return false;
            }


            payer.ReplaceWith(payerDraft);
            recipient.ReplaceWith(recipientDraft);

            payer.NotifyChanged();
            recipient.NotifyChanged();

            return true;
        }
        finally
        {
            transactionInProgress = false;
        }
    }


    // 실제 현금 결제 계산
    static bool PlanPayment(
        InventoryState inventory,
        long amount,
        out InventoryState draft,
        out string error)
    {
        draft = null;
        error = null;


        // 0원 화폐는 아이템으로 전달하며, 금액 결제에는 사용할 수 없습니다.
        if (inventory == null ||
            amount <= 0)
        {
            return Fail(
                "결제 금액은 1원 이상이어야 합니다.",
                out error);
        }


        long available =
            CarriedTotal(inventory);


        if (available < amount)
        {
            return Fail(
                "소지 화폐가 부족합니다. 지갑에서 먼저 출금해 주세요.",
                out error);
        }


        InventoryState workingCopy =
            inventory.Copy();

        draft = workingCopy;


        var currencyIndices =
            new List<int>();


        for (int i = 0;
             i < draft.Capacity;
             i++)
        {
            ItemStack stack =
                draft.GetSlot(i);

            if (stack != null &&
                !stack.IsEmpty &&
                stack.Item.IsCurrency && stack.Item.CurrencyValue > 0)
            {
                currencyIndices.Add(i);
            }
        }

        currencyIndices.Sort((a, b) =>
        {
            long av =
                workingCopy
                    .GetSlot(a)
                    .Item
                    .CurrencyValue;

            long bv =
                workingCopy
                    .GetSlot(b)
                    .Item
                    .CurrencyValue;


            if ((av == amount) !=
                (bv == amount))
            {
                return av == amount
                    ? -1
                    : 1;
            }


            return bv.CompareTo(av);
        });


        long paid = 0;


        foreach (int index in currencyIndices)
        {
            if (paid >= amount)
                break;


            ItemStack stack =
                draft.GetSlot(index);

            if (stack.Item.CurrencyValue <= 0)
                continue;


            long remaining =
                amount - paid;


            int count =
                (int)Math.Min(
                    stack.Count,
                    (remaining - 1) /
                    stack.Item.CurrencyValue + 1);


            paid =
                checked(
                    paid +
                    stack.Item.CurrencyValue *
                    count);


            draft.TryRemove(
                index,
                count,
                out _);
        }


        long change =
            paid - amount;

        if (change > 0)
        {
            if (!draft.TryAdd(
                    GetCurrency(change),
                    1,
                    out error))
            {
                return false;
            }
        }


        return true;
    }


    static bool Fail(
        string message,
        out string error)
    {
        error = message;
        return false;
    }
}
