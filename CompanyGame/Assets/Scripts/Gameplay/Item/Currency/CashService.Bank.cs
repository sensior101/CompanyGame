using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class CashService
{
    // 지갑 → 인벤토리 출금
    public static bool TryWithdraw(
        InventoryState inventory,
        long faceValue,
        int quantity,
        out string error)
    {
        return TryWithdraw(
            BankManager.EnsureInstance(),
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
        BankManager bank,
        InventoryState inventory,
        long faceValue,
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
            BankManager.EnsureInstance(),
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
            BankManager.EnsureInstance(),
            inventory,
            sourceIndex,
            quantity,
            out error);
    }

    public static bool TryDeposit(
        BankManager bank,
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
        BankManager bank,
        InventoryState inventory,
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
}
