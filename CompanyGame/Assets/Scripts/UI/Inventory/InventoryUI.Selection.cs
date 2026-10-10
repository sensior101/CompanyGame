using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed partial class InventoryUI
{
    // The full inventory and item selector build the same wallet widget.
    RectTransform BuildWalletDisplay(Transform parent, out TMP_Text value)
    {
        var bank=Panel("BankBalanceButton",parent,new Vector2(285,51),new Color(1f,.99f,.96f,.66f),24,12);
        Icon("BankCoin",bank,InventoryGlyphGraphic.Glyph.Coin,new Vector2(30,30),new Color(.94f,.65f,.18f)).anchoredPosition=new Vector2(-113,0);
        value=Label("BankBalance",bank,"지갑  0원",23,Ink,new Vector2(218,43));
        value.fontStyle=FontStyles.Bold;AutoSize(value,12,23);value.rectTransform.anchoredPosition=new Vector2(20,0);
        return bank;
    }

    public sealed class SelectionView
    {
        public RectTransform Root { get; internal set; }
        public Action Refresh;
    }
    public SelectionView CreateSelectionView(Transform parent,InventoryState inventory,Predicate<ItemStack> eligible,Action<int> selected)
    {
        var root=Panel("InventoryItemSelection",parent,new Vector2(1036,274),WindowColor,34,17);
        root.anchorMin=root.anchorMax=new Vector2(.5f,0);root.pivot=new Vector2(.5f,0);root.anchoredPosition=new Vector2(0,20);
        root.GetComponent<InventoryRoundedGraphic>().raycastTarget=true;
        var bank=BuildWalletDisplay(root,out var balance);bank.anchoredPosition=new Vector2(0,174);
        var slots=UIBuild.Rect("SelectionSlots",root,new Vector2(976,222));slots.anchoredPosition=new Vector2(0,-12);
        var layout=slots.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize=new Vector2(106,106);layout.spacing=new Vector2(16,10);
        layout.constraint=UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=8;layout.childAlignment=TextAnchor.UpperCenter;
        var views=new List<SlotView>();var pointers=new List<InventorySlotPointer>();int page=0;
        var result=new SelectionView{Root=root};
        for(int i=0;i<16;i++)
        {
            int slot=i;
            views.Add(CreateSlot("SelectionSlot_"+i,slots,106,i<8?(i+1).ToString():"",()=>selected(page*16+slot)));
            pointers.Add(InventorySlotPointer.AttachStorage(views[i].background.gameObject,owner,i));
        }
        var caption=Label("SelectionHeading",root,"소지품",24,Ink,new Vector2(500,38));caption.rectTransform.anchoredPosition=new Vector2(0,112);
        foreach(int direction in new[]{-1,1})
        {
            var button=Panel(direction<0?"SelectionPrevious":"SelectionNext",root,new Vector2(42,36),SurfaceColor,12,6);
            button.anchoredPosition=new Vector2(direction*464,112);
            Label("Label",button,direction<0?"←":"→",23,Ink,new Vector2(42,36));
            MakeButton(button,()=>{page=Mathf.Clamp(page+direction,0,(inventory.Capacity-1)/16);result.Refresh();});
            button.gameObject.SetActive(inventory.Capacity>16);
        }
        result.Refresh=()=>
        {
            balance.text="지갑  "+CashService.BankBalance.ToString("N0")+"원";
            for(int i=0;i<views.Count;i++)
            {
                pointers[i].Configure(owner,page*16+i,true);
                var stack=inventory.GetSlot(page*16+i);var view=views[i];ApplySlot(view,stack,false,"+");
                bool allowed=stack!=null && !stack.IsEmpty && eligible(stack);
                view.button.interactable=allowed;view.icon.color=allowed?Color.white:new Color(.4f,.4f,.4f,.35f);
            }
        };
        result.Refresh();return result;
    }
}
