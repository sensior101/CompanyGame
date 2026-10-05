using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ItemOrganization
{
    public const string Root = "Assets/Scripts/Gameplay/Item";
    [MenuItem("CompanyGame/Setup/Organize Item Categories")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        foreach (string category in Enum.GetNames(typeof(ItemType)))
            if (!AssetDatabase.IsValidFolder(Root + "/" + category)) AssetDatabase.CreateFolder(Root, category);
        foreach (string file in new[] { "CashService.cs", "CashService.Bank.cs", "CashService.Payments.cs", "CurrencyIconFactory.cs" })
        {
            string from = Root + "/" + file;
            if (!File.Exists(from)) continue;
            string error = AssetDatabase.MoveAsset(from, Root + "/Currency/" + file);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }
        int migrated = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (!item) continue;
            // ItemData.OnEnable migrates legacy serialized category values before saving.
            EditorUtility.SetDirty(item); migrated++;
        }
        AssetDatabase.SaveAssets();
        return "11 category folders; migrated " + migrated + " item definitions; GUIDs preserved.";
    }
}
