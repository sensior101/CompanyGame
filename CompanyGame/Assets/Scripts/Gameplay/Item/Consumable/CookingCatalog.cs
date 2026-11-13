using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Food recipes only. Ownership and atomic changes use the existing InventoryState.</summary>
[CreateAssetMenu(menuName="Company Game/Cooking Catalog")]
public sealed class CookingCatalog : ScriptableObject
{
    [Serializable] public sealed class Ingredient { public ItemData item; public int count=1; }
    [Serializable] public sealed class Recipe { public string id; public ItemData output; public Ingredient[] ingredients; public bool unlockRequired; }
    [Serializable] public sealed class Cell { public int slot; public string fingerprint; }
    [Serializable] public sealed class Selection { public Cell[] cells=new Cell[9]; public int destination=-1; }
    public Recipe[] recipes=Array.Empty<Recipe>();
    public bool allowUnlockRecipes;
    static CookingCatalog loaded;
    public static CookingCatalog Current => loaded ? loaded : loaded=Resources.Load<CookingCatalog>("Cooking/Catalog");
    public static string Serialize(Selection selection)
    {var copy=new Selection{destination=selection.destination};for(int i=0;i<9;i++)copy.cells[i]=selection.cells[i]??new Cell{slot=-1};return JsonUtility.ToJson(copy);}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => loaded=null;
    public List<Recipe> Match(InventoryState bag, Selection selection)
    {
        var matches=new List<Recipe>();
        if(!Selected(bag,selection,out var totals,out _,out _))return matches;
        foreach(var recipe in recipes)
        {
            if(recipe==null||!recipe.output||(recipe.unlockRequired&&!allowUnlockRecipes)||recipe.ingredients==null)continue;
            var needed=new Dictionary<ItemData,int>(); bool valid=true;
            foreach(var ingredient in recipe.ingredients)
            {if(ingredient==null||!ingredient.item||ingredient.count<1){valid=false;break;}needed.TryGetValue(ingredient.item,out int count);needed[ingredient.item]=count+ingredient.count;}
            if(!valid||needed.Count==0||needed.Count!=totals.Count)continue;
            foreach(var pair in needed)if(!totals.TryGetValue(pair.Key,out int count)||count!=pair.Value){valid=false;break;}
            if(valid)matches.Add(recipe);
        }
        return matches;
    }
    static bool Selected(InventoryState bag,Selection selection,out Dictionary<ItemData,int> totals,out Dictionary<int,int> slots,out string error)
    {
        totals=new();slots=new();error="재료를 다시 확인해 주세요.";
        if(bag==null||selection?.cells==null||selection.cells.Length!=9||selection.destination < -1||selection.destination>=bag.Capacity)return false;
        foreach(var cell in selection.cells)
        {
            if(cell==null||(cell.slot==-1&&string.IsNullOrEmpty(cell.fingerprint)))continue;var stack=bag.GetSlot(cell.slot);
            if(stack==null||stack.IsEmpty||stack.IsSpoiled||string.IsNullOrEmpty(cell.fingerprint)||FurnitureStorageRules.Fingerprint(stack)!=cell.fingerprint)return false;
            slots.TryGetValue(cell.slot,out int used);if(++used>stack.Count)return false;slots[cell.slot]=used;
            totals.TryGetValue(stack.Item,out int amount);totals[stack.Item]=amount+1;
        }
        if(totals.Count==0)return false;error=null;return true;
    }
    public bool TryCook(InventoryState bag,string recipeId,Selection selection,out string error)
    {
        if(!Selected(bag,selection,out _,out var used,out error))return false;
        var recipe=Match(bag,selection).Find(r=>r.id==recipeId);
        if(recipe==null){error="이 재료로 만들 수 없는 요리입니다.";return false;}
        var draft=bag.Copy();foreach(var pair in used)if(!draft.TryRemove(pair.Key,pair.Value,out error))return false;
        if(selection.destination>=0)
        {if(!draft.TryPlaceStack(selection.destination,new ItemStack(recipe.output,1),false,out _,out error))return false;}
        else if(!draft.TryAdd(recipe.output,1,out error))return false;
        bag.ReplaceWith(draft);bag.NotifyChanged();error=null;return true;
    }
}
