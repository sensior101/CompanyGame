using System;

public enum BookEditPermission { FullEdit, AddOnly, ReadOnly }

/// <summary>Locks the text present when editing begins, while allowing IME composition
/// and correction of newly inserted text. Only InventoryState can issue a session.</summary>
public sealed class BookEditSession
{
    internal InventoryState inventory;
    internal string instanceId, editorId;
    internal BookInstanceData baseline, expected;
    internal BookEditSession() { }
}

/// <summary>Writing owned by one physical book, never by its shared ItemData asset.</summary>
[Serializable]
public sealed class BookInstanceData
{
    public const int PageCount = 20;
    public string title = "";
    public string authorPlayerId;
    public string authorName;
    public string[] pages = new string[PageCount];
    public BookEditPermission permission;
    public bool isPublished;
    public string libraryLoanId;
    public string libraryWorkId;
    public bool IsLibraryLoan => !string.IsNullOrEmpty(libraryLoanId);

    // Every inventory, selector and trade view uses this book presentation.
    public string Tooltip(string fallbackTitle) =>
        (string.IsNullOrWhiteSpace(title) ? fallbackTitle : title) +
        (isPublished || !string.IsNullOrWhiteSpace(authorName)
            ? "\n저자 : " + (string.IsNullOrWhiteSpace(authorName) ? "미상" : authorName) : "");

    public bool HasContent => Array.Exists(pages ?? Array.Empty<string>(), value => !string.IsNullOrWhiteSpace(value));
    public bool IsAuthor(string playerId) => !string.IsNullOrEmpty(playerId) && playerId == authorPlayerId;
    public bool CanEdit(string playerId) => !IsLibraryLoan && (IsAuthor(playerId) || permission != BookEditPermission.ReadOnly);
    public bool CanRename(string playerId) => !IsLibraryLoan && (IsAuthor(playerId) || permission == BookEditPermission.FullEdit);

    // Insertion anywhere is permitted (including after a template label), but existing
    // characters must remain in order. Validate in the model, not just in the UI.
    public static bool PreservesText(string before, string after)
    {
        before ??= ""; after ??= "";
        int matched = 0;
        for (int i = 0; i < after.Length && matched < before.Length; i++)
            if (after[i] == before[matched]) matched++;
        return matched == before.Length;
    }

    public bool AllowsRevision(BookInstanceData proposed, string playerId)
    {
        if (proposed == null || string.IsNullOrEmpty(playerId)) return false;
        if (IsAuthor(playerId) || permission == BookEditPermission.FullEdit) return true;
        if (title != proposed.title || permission == BookEditPermission.ReadOnly) return false;
        var before = Clone(); var after = proposed.Clone();
        for (int i = 0; i < PageCount; i++)
            if (!PreservesText(before.pages[i], after.pages[i])) return false;
        return true;
    }

    public BookInstanceData Clone()
    {
        var copy = new BookInstanceData { title = title ?? "", authorPlayerId = authorPlayerId,
            authorName = authorName, permission = permission, isPublished = isPublished, libraryLoanId = libraryLoanId, libraryWorkId = libraryWorkId };
        if (pages != null)
            Array.Copy(pages, copy.pages, Math.Min(pages.Length, PageCount));
        for (int i = 0; i < PageCount; i++) copy.pages[i] ??= "";
        return copy;
    }
}
