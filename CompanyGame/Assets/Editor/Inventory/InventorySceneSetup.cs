using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Moves the existing bank out of the removed HUD without changing its starting balance.</summary>
public static class InventorySceneSetup
{
    public static int ApplyCurrent(long fallbackStartingBalance)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play mode first.");
        var scene = SceneManager.GetActiveScene();
        var map = scene.GetRootGameObjects().First(g => g.name.StartsWith("Map_"));
        var systems = map.transform.Find("00_Systems");
        if (!systems)
        {
            systems = new GameObject("00_Systems").transform;
            systems.SetParent(map.transform, false);
        }
        var banks = map.GetComponentsInChildren<PropertyManager>(true);
        if (banks.Length == 0)
        {
            var bank = new GameObject("Bank").AddComponent<PropertyManager>();
            bank.transform.SetParent(systems, false);
            var serialized = new SerializedObject(bank);
            serialized.FindProperty("startingMoney").longValue = fallbackStartingBalance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            foreach (var bank in banks)
                if (bank.GetComponentInParent<MoneyUI>()) bank.transform.SetParent(systems, false);
        }
        int removed = 0;
        foreach (var hud in map.GetComponentsInChildren<MoneyUI>(true))
        {
            // This component owns the legacy MoneyCanvas; bank hosts were moved above.
            if (hud.GetComponent<Canvas>()) Object.DestroyImmediate(hud.gameObject);
            else
            {
                var serialized = new SerializedObject(hud);
                var label = serialized.FindProperty("moneyText").objectReferenceValue as TMPro.TMP_Text;
                if (label) Object.DestroyImmediate(label.gameObject);
                Object.DestroyImmediate(hud);
            }
            removed++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return removed;
    }

    public static void ImportCurrencyArt()
    {
        foreach (string name in new[] { "CurrencySilver", "CurrencyGold", "CurrencyBlue", "CurrencyGreen", "CurrencyYellow", "CurrencyRed" })
        {
        string path = "Assets/Resources/Inventory/" + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        }
    }
}
