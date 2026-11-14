using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed partial class InventoryUI
{
    static readonly Color DisabledItemTint=new Color(.4f,.4f,.4f,.35f);
    // The full inventory and item selector build the same wallet widget.
    RectTransform BuildWalletDisplay(Transform parent, out TMP_Text value)
    {
        var bank=Panel("BankBalanceButton",parent,new Vector2(285,51),UITheme.Sand,24,12);
        Icon("BankCoin",bank,InventoryGlyphGraphic.Glyph.Coin,new Vector2(30,30),UITheme.Selected).anchoredPosition=new Vector2(-113,0);
        value=Label("BankBalance",bank,"계좌  0원",23,Ink,new Vector2(218,43));
        value.fontStyle=FontStyles.Bold;AutoSize(value,12,23);value.rectTransform.anchoredPosition=new Vector2(20,0);
        return bank;
    }

    public sealed class SelectionView
    {
        public RectTransform Root { get; internal set; }
        public Action Refresh;
    }
    public SelectionView CreateSelectionView(Transform parent,InventoryState inventory,Predicate<ItemStack> eligible,Action<int> selected)
        => CreateSelectionView(parent,inventory,eligible,selected,false,null,null);

    // Compact mode shares the same slots and paging with embedded interactions.
    public SelectionView CreateSelectionView(Transform parent,InventoryState inventory,Predicate<ItemStack> eligible,Action<int> selected,
        bool compact,Action<InventorySlotPointer,int> bindPointer,Func<int,int> visibleCount)
    {
        var root=Panel("InventoryItemSelection",parent,compact?new Vector2(582,176):new Vector2(1036,274),WindowColor,34,17);
        root.anchorMin=root.anchorMax=new Vector2(.5f,0);root.pivot=new Vector2(.5f,0);root.anchoredPosition=new Vector2(0,20);
        root.GetComponent<InventoryRoundedGraphic>().raycastTarget=true;
        TMP_Text balance=null;
        if(!compact){var bank=BuildWalletDisplay(root,out balance);bank.anchoredPosition=new Vector2(0,174);}
        var slots=UIBuild.Rect("SelectionSlots",root,compact?new Vector2(558,136):new Vector2(976,222));slots.anchoredPosition=new Vector2(0,-12);
        var layout=slots.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize=Vector2.one*(compact?66:106);layout.spacing=compact?new Vector2(4,4):new Vector2(16,10);
        layout.constraint=UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=8;layout.childAlignment=TextAnchor.UpperCenter;
        var views=new List<SlotView>();var pointers=new List<InventorySlotPointer>();int page=0;
        var result=new SelectionView{Root=root};
        for(int i=0;i<16;i++)
        {
            int slot=i;
            views.Add(CreateSlot("SelectionSlot_"+i,slots,compact?66:106,i<8?(i+1).ToString():"",()=>selected(page*16+slot)));
            pointers.Add(InventorySlotPointer.AttachStorage(views[i].background.gameObject,owner,i));
        }
        var caption=Label("SelectionHeading",root,"소지품",compact?16:24,Ink,new Vector2(compact?380:500,compact?24:38));caption.rectTransform.anchoredPosition=new Vector2(0,compact?68:112);
        foreach(int direction in new[]{-1,1})
        {
            var button=Panel(direction<0?"SelectionPrevious":"SelectionNext",root,compact?new Vector2(28,24):new Vector2(42,36),SurfaceColor,12,6);
            button.anchoredPosition=new Vector2(direction*(compact?267:464),compact?68:112);
            Label("Label",button,direction<0?"←":"→",compact?16:23,Ink,compact?new Vector2(28,24):new Vector2(42,36));
            MakeButton(button,()=>{page=Mathf.Clamp(page+direction,0,(inventory.Capacity-1)/16);result.Refresh();});
            button.gameObject.SetActive(inventory.Capacity>16);
        }
        result.Refresh=()=>
        {
            if(balance)balance.text="계좌  "+CashService.BankBalance.ToString("N0")+"원";
            for(int i=0;i<views.Count;i++)
            {
                if(bindPointer!=null)bindPointer(pointers[i],page*16+i);
                else pointers[i].Configure(owner,page*16+i,true);
                var stack=inventory.GetSlot(page*16+i);var view=views[i];ApplySlot(view,stack,false,"");
                int count=visibleCount!=null?visibleCount(page*16+i):stack?.Count??0;
                bool allowed=stack!=null && !stack.IsEmpty && count>0 && eligible(stack);
                if(compact)
                {
                    view.icon.enabled=stack!=null&&!stack.IsEmpty&&count>0&&stack.Item.icon;
                    view.emptyLabel.gameObject.SetActive(!view.icon.enabled);
                    view.emptyLabel.text=count>0&&stack!=null&&!stack.IsEmpty?stack.DisplayName:"";
                    AutoSize(view.emptyLabel,8,13);
                    view.count.text=count>1?count.ToString():"";
                }
                view.button.interactable=allowed;view.icon.color=allowed?Color.white:DisabledItemTint;
            }
        };
        result.Refresh();return result;
    }
}
