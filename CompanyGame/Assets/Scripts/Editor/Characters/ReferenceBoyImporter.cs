using UnityEditor;
using UnityEngine;

namespace CompanyGame.Editor.Characters
{
    /// <summary>Male counterpart of ReferenceGirlImporter (the Meshy articulated body).</summary>
    public static class ReferenceBoyImporter
    {
        public const string VisualPath = "Assets/Art/Daldongne/Players/MaleVisual.prefab";

        [MenuItem("Tools/Company Game/Characters/Rebuild Male Character")]
        public static void RebuildMenu() => Debug.Log(Rebuild());
        public static string Rebuild() => ReferenceGirlImporter.Rebuild(true);

        [MenuItem("Tools/Company Game/Characters/Validate Male Character")]
        public static void ValidateMenu() => Debug.Log(Validate());
        public static string Validate() => ReferenceGirlImporter.Validate(true);
    }
}
