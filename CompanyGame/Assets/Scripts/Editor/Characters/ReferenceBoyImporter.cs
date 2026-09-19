using UnityEditor;
using UnityEngine;

namespace CompanyGame.Editor.Characters
{
    /// <summary>Uses the same native mesh and palette import as the matching girl.</summary>
    public static class ReferenceBoyImporter
    {
        public const string VisualPath = "Assets/Art/Daldongne/Players/MaleVisual.prefab";

        [MenuItem("Tools/Company Game/Characters/Rebuild Reference Boy")]
        public static void RebuildMenu() => Debug.Log(Rebuild());
        public static string Rebuild() => ReferenceGirlImporter.Rebuild(true);

        [MenuItem("Tools/Company Game/Characters/Validate Reference Boy")]
        public static void ValidateMenu() => Debug.Log(Validate());
        public static string Validate() => ReferenceGirlImporter.Validate(true);
    }
}
