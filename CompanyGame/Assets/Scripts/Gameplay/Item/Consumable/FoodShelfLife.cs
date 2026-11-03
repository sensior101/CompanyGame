/// <summary>
/// How long each food keeps. Out of a fridge every food spoils at the next morning (06:00 of the day after it was
/// made or bought); in a fridge it keeps for its listed days (FoodMenu) and spoils at 06:00 of the last one.
/// </summary>
public static class FoodShelfLife
{
    /// <summary>Food and drink spoil; medicine and everything else do not.</summary>
    public static bool IsPerishable(ItemData item) =>
        item && item.itemType == ItemType.Consumable && item.consumableType != ConsumableType.Medicine;

    /// <summary>Days the item keeps in a fridge: the food menu, else the asset's shelfLifeHours, else 1.</summary>
    public static int FridgeDaysOf(ItemData item)
    {
        if (!item) return 0;
        if (FoodMenu.TryGet(item, out var entry)) return entry.fridgeDays;
        return item.shelfLifeHours > 0f ? UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(item.shelfLifeHours / 24f)) : 1;
    }

    /// <summary>Game minute the food spoils at. Out of a fridge: the next morning; in one: the morning <c>days</c> later.</summary>
    public static long ExpiresAt(ItemData item, GameTime stored, bool refrigerated)
    {
        int days = refrigerated ? FridgeDaysOf(item) : 1;
        return new GameTime(stored.absoluteDay + days, GameTime.DayStartHour).TotalMinutes;
    }

    /// <summary>
    /// Moves a whole food stack into a fridge. It then keeps until the morning <c>days</c> after it was made, so taking it
    /// out and putting it back never adds days. A spoiled stack stays spoiled.
    /// </summary>
    public static bool TryPutInFridge(InventoryState from, int slot, InventoryState fridge, out string error)
    {
        var stack = from?.GetSlot(slot);
        if (stack == null || stack.IsEmpty || !IsPerishable(stack.Item) || fridge == null) { error = "냉장고에는 음식만 넣을 수 있어요."; return false; }
        long expires = stack.IsSpoiled ? stack.ExpiresAt : new GameTime(stack.MadeDay + FridgeDaysOf(stack.Item), GameTime.DayStartHour).TotalMinutes;
        return Move(from, slot, fridge, expires, out error);
    }

    /// <summary>Takes a whole stack out; outside the fridge it spoils the next morning (or sooner, if the fridge date is sooner).</summary>
    public static bool TryTakeFromFridge(InventoryState fridge, int slot, InventoryState to, out string error)
    {
        var stack = fridge?.GetSlot(slot);
        if (stack == null || stack.IsEmpty || to == null) { error = "꺼낼 음식을 선택해 주세요."; return false; }
        long expires = stack.ExpiresAt > 0 ? System.Math.Min(stack.ExpiresAt, ExpiresAt(stack.Item, Now, false)) : 0;
        return Move(fridge, slot, to, expires, out error);
    }

    static bool Move(InventoryState from, int slot, InventoryState to, long expires, out string error)
    {
        var stack = from.GetSlot(slot);
        ItemData item = stack.Item; int count = stack.Count, made = stack.MadeDay;
        if (!to.TryAdd(item, count, out error, null, expires, made)) return false;
        return from.TryRemove(slot, count, out error);
    }

    /// <summary>Now on the running clock, or day 0 when no clock is up (editor tools, tests).</summary>
    public static GameTime Now => GameClock.Current?.Now ?? default;
}
