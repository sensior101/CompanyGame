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
public static partial class CashService
{
    // 최소 화폐 금액: 0원
    public const long MinimumFaceValue = 0;

    static readonly Dictionary<long, ItemData> definitions =
        new Dictionary<long, ItemData>();

    static bool transactionInProgress;

    public static long BankBalance =>
        BankManager.EnsureInstance()
            ? BankManager.Instance.BankBalance
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

    static bool Fail(
        string message,
        out string error)
    {
        error = message;
        return false;
    }
}
