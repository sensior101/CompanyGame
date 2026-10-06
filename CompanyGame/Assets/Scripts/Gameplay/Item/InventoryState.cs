using System;

/// <summary>A read-only view of one stack. Only InventoryState changes quantities.</summary>
public sealed class ItemStack
{
    public ItemData Item { get; internal set; }
    public int Count { get; internal set; }
    public ItemInstance Instance { get; internal set; }
    public string InstanceId
    {
        get => Instance?.instanceId;
        internal set { if (value != null || Instance != null) EnsureInstance().instanceId = value; }
    }
    public BookInstanceData BookData
    {
        get => Instance?.bookData;
        internal set { if (value != null || Instance != null) EnsureInstance().bookData = value; }
    }
    public bool IsEmpty => !Item || Count <= 0;
    public bool IsUniqueBook => !IsEmpty && Item.IsBook && BookData != null &&
        (BookData.isPublished || BookData.HasContent || !string.IsNullOrEmpty(InstanceId));
    public int StackLimit => IsUniqueBook ? 1 : IsEmpty ? 0 : Item.StackLimit;
    public bool CanMerge(ItemStack other) => other != null && !IsEmpty && !other.IsEmpty &&
        Item == other.Item && !IsUniqueBook && !other.IsUniqueBook;
    public string Tooltip => IsUniqueBook && BookData.isPublished
        ? DisplayName + "\n저자 : " + BookData.authorName : DisplayName;
    public string DisplayName => !IsEmpty && Item.IsBook && !string.IsNullOrWhiteSpace(BookData?.title)
        ? BookData.title : IsEmpty ? "" : Item.DisplayName;

    internal ItemStack(ItemData item = null, int count = 0) { Item = item; Count = count; }
    ItemInstance EnsureInstance()
    {
        Instance ??= new ItemInstance();
        Instance.itemId = Item ? Item.itemId : null;
        return Instance;
    }
    internal ItemStack Copy()
    {
        return new ItemStack(Item, Count) { Instance = Instance?.Clone() };
    }
    internal void Clear() { Item = null; Count = 0; Instance = null; }
}

/// <summary>
/// Session inventory independent of scene-local players. The hotbar is a view of
/// slots 0–7, so its contents do not add another eight slots of carrying capacity.
/// </summary>
public sealed class InventoryState
{
    public const int HotbarSize = 8;
    public const int BaseCapacity = 16;
    public const int ExpandedCapacity = 24;
    readonly ItemStack[] slots = new ItemStack[ExpandedCapacity];
    readonly ItemStack[] equipment = new ItemStack[6];

    public int Capacity { get; private set; } = BaseCapacity;
    public int SelectedHotbarIndex { get; private set; }
    public event Action Changed;

    public InventoryState()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = new ItemStack();
        for (int i = 0; i < equipment.Length; i++) equipment[i] = new ItemStack();
    }

    public ItemStack GetSlot(int index) => IsSlot(index) ? slots[index] : null;
    public ItemStack GetEquipment(EquipmentSlot slot) => IsEquipment(slot) ? equipment[(int)slot] : null;

    /// <summary>Publishes edits to a carried book so its persistence owner can save them.</summary>
    public bool NotifyBookChanged(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return false;
        for (int i = 0; i < Capacity; i++)
        {
            var stack = slots[i];
            if (stack.IsEmpty || !stack.Item.IsBook || stack.InstanceId != instanceId) continue;
            NotifyChanged();
            return true;
        }
        return false;
    }

    public bool SelectHotbar(int index)
    {
        if (index < 0 || index >= HotbarSize) return false;
        if (index == SelectedHotbarIndex) return true;
        SelectedHotbarIndex = index;
        NotifyChanged();
        return true;
    }

    /// <summary>Adds the entire requested quantity or leaves the inventory unchanged.</summary>
    public bool CanAdd(ItemData item, int count, out string error)
    {
        error = null;
        if (!item || count <= 0) return Fail("아이템과 수량을 확인해 주세요.", out error);
        long room = 0;
        for (int i = 0; i < Capacity; i++)
        {
            ItemStack stack = slots[i];
            if (stack.IsEmpty) room += item.StackLimit;
            else if (stack.Item == item && !stack.IsUniqueBook) room += Math.Max(0, item.StackLimit - stack.Count);
        }
        if (room < count) return Fail("인벤토리가 가득 찼습니다.", out error);
        if (item.IsCurrency)
        {
            try { checked { long after = CashService.CarriedTotal(this) + item.CurrencyValue * count; } }
            catch (OverflowException) { return Fail("소지 화폐 금액이 처리 가능한 범위를 초과합니다.", out error); }
        }
        return true;
    }

    public bool TryAdd(ItemData item, int count, out string error)
    {
        if (!CanAdd(item, count, out error)) return false;
        int remaining = count;
        // Fill existing stacks first, without needlessly occupying empty slots.
        for (int pass = 0; pass < 2 && remaining > 0; pass++)
        for (int i = 0; i < Capacity && remaining > 0; i++)
        {
            ItemStack stack = slots[i];
            if (pass == 0 ? stack.IsEmpty || stack.Item != item || stack.IsUniqueBook : !stack.IsEmpty) continue;
            int amount = Math.Min(remaining, item.StackLimit - (stack.IsEmpty ? 0 : stack.Count));
            if (amount <= 0) continue;
            if (stack.IsEmpty)
            {
                stack.Item = item; stack.Count = 0;
            }
            stack.Count += amount;
            remaining -= amount;
        }
        NotifyChanged();
        return true;
    }

    /// <summary>Restores or transfers exactly one authored book without changing its identity.</summary>
    public bool TryAddBookInstance(ItemData item, string instanceId, BookInstanceData data, out string error, int preferredSlot = -1)
    {
        error = null;
        if (!item || !item.IsBook || string.IsNullOrWhiteSpace(instanceId) || data == null || !data.isPublished)
            return Fail("책 데이터를 확인해 주세요.", out error);
        for (int i = 0; i < Capacity; i++)
            if (!slots[i].IsEmpty && slots[i].InstanceId == instanceId)
                return Fail("같은 책이 이미 인벤토리에 있습니다.", out error);
        if (IsSlot(preferredSlot) && slots[preferredSlot].IsEmpty)
        {
            slots[preferredSlot] = new ItemStack(item, 1)
            {
                InstanceId = instanceId,
                BookData = data.Clone()
            };
            NotifyChanged();
            return true;
        }
        for (int i = 0; i < Capacity; i++)
        {
            if (!slots[i].IsEmpty) continue;
            slots[i] = new ItemStack(item, 1)
            {
                InstanceId = instanceId,
                BookData = data.Clone()
            };
            NotifyChanged();
            return true;
        }
        return Fail("인벤토리가 가득 찼습니다.", out error);
    }

    /// <summary>Turns exactly one blank into a named book. No changes on capacity failure.</summary>
    public bool TryPublishBook(int blankSlot, BookInstanceData draft, string authorId, string authorName,
        out int resultSlot, out string error)
    {
        resultSlot = -1; error = null;
        var source = GetSlot(blankSlot);
        if (source == null || source.IsEmpty || !source.Item.IsBook || source.IsUniqueBook ||
            draft == null || !draft.HasContent || string.IsNullOrWhiteSpace(draft.title) || string.IsNullOrEmpty(authorId))
            return Fail("내용과 제목을 입력해 주세요.", out error);
        int destination = blankSlot;
        if (source.Count > 1)
        {
            destination = -1;
            for (int i = 0; i < Capacity; i++) if (slots[i].IsEmpty) { destination = i; break; }
            if (destination < 0) return Fail("작성한 책을 보관할 빈 칸이 필요합니다. 원고는 그대로 유지됩니다.", out error);
        }
        var data = draft.Clone();
        data.authorPlayerId = authorId; data.authorName = authorName ?? "Player";
        data.isPublished = true; data.permission = BookEditPermission.FullEdit;
        var book = new ItemStack(source.Item, 1) { InstanceId = Guid.NewGuid().ToString("N"), BookData = data };
        source.Count--;
        slots[destination] = book;
        resultSlot = destination;
        NotifyChanged();
        return true;
    }

    public BookEditSession BeginBookEdit(string id, string editorId)
    {
        var book = FindBook(id);
        if (book == null || string.IsNullOrEmpty(editorId)) return null;
        return new BookEditSession { inventory = this, instanceId = id, editorId = editorId,
            baseline = book.BookData.Clone(), expected = book.BookData };
    }

    public bool TryUpdateBook(string id, BookInstanceData revision, string editorId, out string error,
        BookEditSession session = null)
    {
        error = null;
        var book = FindBook(id);
        var rules = book?.BookData;
        if (session != null)
        {
            if (book == null || session.inventory != this || session.instanceId != id || session.editorId != editorId ||
                !ReferenceEquals(session.expected, book.BookData) || session.baseline.permission != book.BookData.permission)
                return Fail("책이 변경되었습니다. 다시 열어 주세요.", out error);
            rules = session.baseline;
        }
        if (rules == null || !rules.AllowsRevision(revision, editorId))
            return Fail("이 책의 수정 권한으로는 해당 내용을 변경할 수 없습니다.", out error);
        if (string.IsNullOrWhiteSpace(revision.title)) return Fail("제목을 입력해 주세요.", out error);
        var data = revision.Clone();
        // Authorship and permission are never accepted from an editing payload.
        data.authorPlayerId = book.BookData.authorPlayerId; data.authorName = book.BookData.authorName;
        data.permission = book.BookData.permission; data.isPublished = book.BookData.isPublished;
        book.BookData = data;
        if (session != null) session.expected = data;
        NotifyChanged(); return true;
    }

    public bool TrySetBookPermission(string id, BookEditPermission permission, string playerId, out string error)
    {
        error = null; var book = FindBook(id);
        if (book == null || !book.BookData.isPublished || !book.BookData.IsAuthor(playerId) ||
            !Enum.IsDefined(typeof(BookEditPermission), permission))
            return Fail("저자만 다른 플레이어의 권한을 설정할 수 있습니다.", out error);
        book.BookData.permission = permission; NotifyChanged(); return true;
    }

    public ItemStack FindBook(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].IsUniqueBook && slots[i].InstanceId == id) return slots[i];
        return null;
    }

    public bool TryRemove(int index, int count, out string error)
    {
        error = null;
        if (!IsSlot(index) || count <= 0 || slots[index].IsEmpty || slots[index].Count < count)
            return Fail("꺼낼 아이템과 수량을 확인해 주세요.", out error);
        slots[index].Count -= count;
        if (slots[index].Count == 0) slots[index].Clear();
        NotifyChanged();
        return true;
    }

    public bool TryMove(int fromIndex, int toIndex, out string error)
    {
        error = null;
        if (!IsSlot(fromIndex) || !IsSlot(toIndex) || slots[fromIndex].IsEmpty)
            return Fail("이동할 아이템을 선택해 주세요.", out error);
        if (fromIndex == toIndex) return true;
        ItemStack source = slots[fromIndex], target = slots[toIndex];
        if (source.CanMerge(target) && target.Count < target.StackLimit)
        {
            int amount = Math.Min(source.Count, target.StackLimit - target.Count);
            target.Count += amount;
            source.Count -= amount;
            if (source.Count == 0) source.Clear();
        }
        else
        {
            slots[fromIndex] = target;
            slots[toIndex] = source;
        }
        NotifyChanged();
        return true;
    }

    public bool TryMoveAmount(int fromIndex,int toIndex,int count,out string error)
    {
        error=null;
        if(!IsSlot(fromIndex)||!IsSlot(toIndex)||count<=0||slots[fromIndex].IsEmpty||slots[fromIndex].Count<count)
            return Fail("이동할 아이템과 수량을 확인해 주세요.",out error);
        if(count==slots[fromIndex].Count)return TryMove(fromIndex,toIndex,out error);
        if(fromIndex==toIndex)return true;
        var source=slots[fromIndex];var target=slots[toIndex];
        if(!target.IsEmpty && (!source.CanMerge(target) || target.Count+count>target.StackLimit))
            return Fail("빈 칸이나 같은 아이템 칸에 놓아 주세요.",out error);
        if(target.IsEmpty){target.Item=source.Item;target.Count=0;}
        target.Count+=count;source.Count-=count;NotifyChanged();return true;
    }

    /// <summary>Replaced equipment returns to the source slot, even when storage is full.</summary>
    public bool TryEquip(int inventoryIndex, EquipmentSlot slot, out string error)
    {
        error = null;
        if (!IsSlot(inventoryIndex) || !IsEquipment(slot) || slots[inventoryIndex].IsEmpty)
            return Fail("장착할 아이템을 선택해 주세요.", out error);
        ItemStack source = slots[inventoryIndex];
        if (!source.Item.Fits(slot) || source.Count != 1)
            return Fail("이 장착 칸에 맞는 아이템이 아닙니다.", out error);
        slots[inventoryIndex] = equipment[(int)slot];
        equipment[(int)slot] = source;
        NotifyChanged();
        return true;
    }

    public bool TryUnequip(EquipmentSlot slot, out string error)
    {
        error = null;
        if (!IsEquipment(slot) || equipment[(int)slot].IsEmpty)
            return Fail("장착 해제할 아이템을 선택해 주세요.", out error);
        for (int i = 0; i < Capacity; i++)
        {
            if (!slots[i].IsEmpty) continue;
            slots[i] = equipment[(int)slot];
            equipment[(int)slot] = new ItemStack();
            NotifyChanged();
            return true;
        }
        return Fail("장착을 해제하려면 인벤토리 한 칸을 비워 주세요.", out error);
    }

    /// <summary>Drag equipment into a chosen slot; swap only with compatible equipment.</summary>
    public bool TryUnequipTo(EquipmentSlot slot, int inventoryIndex, out string error)
    {
        error = null;
        if (!IsEquipment(slot) || !IsSlot(inventoryIndex) || equipment[(int)slot].IsEmpty)
            return Fail("이동할 장착 아이템과 칸을 확인해 주세요.", out error);
        ItemStack target = slots[inventoryIndex];
        if (!target.IsEmpty && (!target.Item.Fits(slot) || target.Count != 1))
            return Fail("해당 칸의 아이템은 이 위치에 장착할 수 없습니다.", out error);
        slots[inventoryIndex] = equipment[(int)slot];
        equipment[(int)slot] = target;
        NotifyChanged();
        return true;
    }

    public bool TryRemoveEquipment(EquipmentSlot slot, out string error)
    {
        error = null;
        if (!IsEquipment(slot) || equipment[(int)slot].IsEmpty)
            return Fail("장착 해제할 아이템을 선택해 주세요.", out error);
        equipment[(int)slot].Clear();
        NotifyChanged();
        return true;
    }

    /// <summary>Moves a whole or partial stack after destination capacity is confirmed.</summary>
    public bool TryTransferTo(InventoryState destination, int sourceIndex, int count, out string error)
    {
        error = null;
        if (destination == null || ReferenceEquals(this, destination) || !IsSlot(sourceIndex) || count <= 0 ||
            slots[sourceIndex].IsEmpty || slots[sourceIndex].Count < count)
            return Fail("전달할 아이템과 수량을 확인해 주세요.", out error);
        InventoryState sourceDraft = Copy(), destinationDraft = destination.Copy();
        var moving = slots[sourceIndex];
        if (moving.IsUniqueBook)
        {
            if (count != 1 || !destinationDraft.TryAddBookInstance(moving.Item, moving.InstanceId, moving.BookData, out error)) return false;
        }
        else if (!destinationDraft.TryAdd(moving.Item, count, out error)) return false;
        if (!sourceDraft.TryRemove(sourceIndex, count, out error)) return false;
        ReplaceWith(sourceDraft);
        destination.ReplaceWith(destinationDraft);
        NotifyChanged();
        destination.NotifyChanged();
        return true;
    }

    /// <summary>Exchange one exact-face-value cash item for one owned product atomically.</summary>
    public bool TryExchangeCurrency(int sourceIndex, long price, ItemData product, out int resultSlot, out string error)
    {
        resultSlot=-1; error=null;
        if(!IsSlot(sourceIndex) || !product || product.IsCurrency || price<=0)
            return Fail("거래할 화폐와 상품을 확인해 주세요.",out error);
        var payment=slots[sourceIndex];
        if(payment.IsEmpty || !payment.Item.IsCurrency || payment.Item.CurrencyValue!=price)
            return Fail(price.ToString("N0")+"원 화폐 1개를 놓아 주세요.",out error);
        int destination=payment.Count==1 ? sourceIndex : -1;
        if(destination<0)
            for(int i=0;i<Capacity;i++)if(slots[i].IsEmpty){destination=i;break;}
        if(destination<0)return Fail("상품을 받을 인벤토리 한 칸을 비워 주세요.",out error);
        var draft=Copy();
        if(!draft.TryRemove(sourceIndex,1,out error))return false;
        draft.slots[destination]=new ItemStack(product,1);
        ReplaceWith(draft);resultSlot=destination;NotifyChanged();return true;
    }

    // Transactions work on detached copies and publish only after every check succeeds.
    internal InventoryState Copy()
    {
        var copy = new InventoryState();
        copy.ReplaceWith(this);
        return copy;
    }

    internal void ReplaceWith(InventoryState source)
    {
        Capacity = source.Capacity;
        SelectedHotbarIndex = source.SelectedHotbarIndex;
        for (int i = 0; i < slots.Length; i++) slots[i] = source.slots[i].Copy();
        for (int i = 0; i < equipment.Length; i++) equipment[i] = source.equipment[i].Copy();
    }

    internal void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action listener in Changed.GetInvocationList())
        {
            try { listener(); }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
        }
    }

    /// <summary>Future bag purchase integration: unlock exactly one additional row.</summary>
    public bool TryExpandWithBag(out string error)
    {
        error = null;
        if (Capacity == ExpandedCapacity) return Fail("가방 확장이 이미 적용되어 있습니다.", out error);
        Capacity = ExpandedCapacity;
        NotifyChanged();
        return true;
    }

    /// <summary>Consumes a carried bag only after its expansion can be applied.</summary>
    public bool TryUseBag(int inventoryIndex, out string error)
    {
        error = null;
        if (!IsSlot(inventoryIndex) || slots[inventoryIndex].IsEmpty || slots[inventoryIndex].Item.category != ItemCategory.Bag)
            return Fail("가방 아이템이 아닙니다.", out error);
        if (Capacity == ExpandedCapacity) return Fail("가방 확장이 이미 적용되어 있습니다.", out error);
        slots[inventoryIndex].Clear();
        Capacity = ExpandedCapacity;
        NotifyChanged();
        return true;
    }

    bool IsSlot(int index) => index >= 0 && index < Capacity;
    static bool IsEquipment(EquipmentSlot slot) => slot != EquipmentSlot.Accessory && (int)slot >= 0 && (int)slot < 6;
    static bool Fail(string message, out string error) { error = message; return false; }
}
