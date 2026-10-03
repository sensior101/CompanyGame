using System;

/// <summary>
/// UI state that gameplay scripts below the UI layer need: whether a menu, chat or drag owns input.
/// The UI layer installs these checks at startup, so Player code never references UI types.
/// </summary>
public static class InputFocus
{
    /// <summary>The inventory is open or still closing.</summary>
    public static Func<bool> InventoryOpen = () => false;

    /// <summary>Chat is open.</summary>
    public static Func<bool> ChatOpen = () => false;

    /// <summary>A menu, chat, drag or consumed key owns input, so attacks and item use must wait.</summary>
    public static Func<bool> GameplayBlocked = () => false;

    /// <summary>The UI used the mouse wheel this frame.</summary>
    public static Func<bool> ScrollCaptured = () => false;
}
