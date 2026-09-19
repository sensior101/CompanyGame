using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class DaldongneWarmPackage
{
    public static object Export()
    {
        string[] files={"Assets/Scenes/daldongnaemap.unity",
          "Assets/Art/Daldongne/WarmVillage/DaldongneWarmTown.prefab",
          "Assets/Art/Daldongne/WarmVillage/DaldongneWarmMeshes.asset",
          "Assets/Art/Daldongne/WarmVillage/DaldongneWarmTown.glb",
          "Assets/Art/Daldongne/WarmVillage/WarmVillageVolume.asset",
          "Assets/Art/Daldongne/WarmVillage/README.md",
          "Assets/Art/Daldongne/Daldongne_RenderPipeline.asset",
          "Assets/Scripts/World/DaldongneMapCamera.cs",
          "Assets/Scripts/Player/PlayerMovement.cs"};
        var deps=AssetDatabase.GetDependencies("Assets/Art/Daldongne/Daldongne_RenderPipeline.asset",true).Where(p=>p.StartsWith("Assets/"));
        string[] paths=files.Concat(deps).Distinct().ToArray();
        AssetDatabase.Refresh();
        string path="../ArtSource/Daldongne/DaldongneWarm_Unity.unitypackage";
        AssetDatabase.ExportPackage(paths,path,ExportPackageOptions.Default);
        return new {files=paths,bytes=new FileInfo(path).Length};
    }
}
