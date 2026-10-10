using System;
using TMPro;
using UnityEngine;

/// <summary>Reusable, filtered two-row inventory handoff. The existing inventory builds its visuals.</summary>
public sealed class InventoryItemSelector : MonoBehaviour
{
    static InventoryItemSelector active;
    public static bool IsOpen => active;
    public static InventoryItemSelector Active => active;
    public TradeSession Handoff { get; private set; }
    public ItemStack SelectedItem => Handoff?.HeldStack;
    InventoryUI.SelectionView view;
    Predicate<ItemStack> filter;
    Action<InventoryItemSelector> selected;
    Action cancelled;
    Func<bool> available;
    readonly ControlLock controls=new ControlLock();
    PlayerMovement player;
    bool closed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => active=null;
    public static InventoryItemSelector Open(PlayerInventory owner,Predicate<ItemStack> filter,
        Action<InventoryItemSelector> selected,Action cancelled,Func<bool> available)
    {
        if(active || !owner || !owner.UserInterface)return null;
        var go=new GameObject("InventoryItemSelector",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
        var selector=go.AddComponent<InventoryItemSelector>();active=selector;
        selector.filter=filter;selector.selected=selected;selector.cancelled=cancelled;selector.available=available;
        selector.Handoff=new TradeSession(owner.Inventory,Array.Empty<TradeOffer>());
        selector.player=owner.GetComponent<PlayerMovement>();selector.controls.Hold(selector.player);
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=180;
        var scale=go.GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;
        selector.view=owner.UserInterface.CreateSelectionView(go.transform,owner.Inventory,filter,selector.Select);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,SceneLoadManager.CurrentMap);
        return selector;
    }
    public void Select(int index)
    {
        if(closed || SelectedItem!=null)return;
        var stack=Handoff.Inventory.GetSlot(index);
        if(stack==null || stack.IsEmpty || !Handoff.TryPickUpForHandoff(index,filter,out _))return;
        view.Root.gameObject.SetActive(false);selected?.Invoke(this);
    }
    void Update()
    {
        if(SceneLoadManager.IsLoading || (available!=null && !available()) || GameInput.CancelPressed){Cancel();return;}
        view.Refresh();
        var rect=(RectTransform)transform;
        view.Root.localScale=Vector3.one*Mathf.Min(1,(rect.rect.width-36)/1036f,(rect.rect.height*.45f)/340f);
    }
    public void Cancel()
    {
        if(closed)return;
        if(!Handoff.TryCancel(out _))return;
        var callback=cancelled;Close();callback?.Invoke();
    }
    public void Close()
    {
        if(closed)return;
        if(Handoff.HasCursorItem && !Handoff.TryCancel(out _))return;
        closed=true;
        // Release dialogue first when it shares this selector's control ownership.
        if(DialogueManager.Instance)DialogueManager.Instance.Close();
        controls.Release(player,!SceneLoadManager.IsLoading);if(active==this)active=null;
        gameObject.SetActive(false);Destroy(gameObject);
        PlayerInventory.ConsumeSpaceThisFrame();
    }
    void OnDestroy()
    {
        Handoff?.TryCancel(out _);
        if(!closed)controls.Release(player,!SceneLoadManager.IsLoading);
        if(active==this)active=null;
    }
}
