using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class CashService
{
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

        if (!RequireAuthority(out error)) return false;

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

        if (quantity <= 0 || quantity > stack.Count)
            return Fail("거래할 화폐 수량을 확인해 주세요.", out error);

        transactionInProgress = true;

        try
        {
            long amount = checked(stack.Item.CurrencyValue * (long)quantity);
            bool transferred = source.TryTransferTo(
                recipient,
                sourceIndex,
                quantity,
                out error);
            if (transferred)
            {
                NotifyTransaction(source, -amount, MoneyChangeReason.Spent, "현금 전달");
                NotifyTransaction(recipient, amount, MoneyChangeReason.Earned, "현금 수령");
            }
            return transferred;
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
        if (!RequireAuthority(out error)) return false;
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
            NotifyTransaction(inventory, -amount, MoneyChangeReason.Purchase, "NPC 거래");

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

        if (!RequireAuthority(out error)) return false;

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
            if (amount == 0) return payer != null;
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
            NotifyTransaction(payer, -amount, MoneyChangeReason.Purchase, "현금 거래 대금");
            NotifyTransaction(recipient, amount, MoneyChangeReason.Sale, "현금 거래 대금");

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

        // Free transactions require no zero-value currency item.
        if (inventory == null ||
            amount < 0)
        {
            return Fail(
                "결제 금액은 0원 이상이어야 합니다.",
                out error);
        }

        if (amount == 0) { draft = inventory.Copy(); return true; }

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
}
