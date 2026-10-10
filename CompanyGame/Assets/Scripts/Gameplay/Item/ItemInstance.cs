using System;

/// <summary>Unique mutable data for one physical item; book writing is never stored on ItemData.</summary>
[Serializable]
public sealed class ItemInstance
{
    public string instanceId;
    public string itemId;
    public BookInstanceData bookData;

    public ItemInstance Clone() => new ItemInstance
    {
        instanceId = instanceId,
        itemId = itemId,
        bookData = bookData?.Clone()
    };
}
