using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DaldongnePackage
{
    public static object Export()
    {
        string[] files={"Assets/Scenes/DaldongneMap.unity","Assets/Art/Daldongne/DaldongneTown.prefab","Assets/Art/Daldongne/DaldongneMeshes.asset","Assets/Art/Daldongne/DaldongneTown.glb","Assets/Art/Daldongne/README.md","Assets/Art/Daldongne/Daldongne_RenderPipeline.asset","Assets/Scripts/World/DaldongneMapCamera.cs"};
        var deps=AssetDatabase.GetDependencies("Assets/Art/Daldongne/Daldongne_RenderPipeline.asset",true).Where(p=>p.StartsWith("Assets/"));
        string[] paths=files.Concat(deps).Distinct().ToArray();
        AssetDatabase.Refresh();
        AssetDatabase.ExportPackage(paths,"../ArtSource/Daldongne/Daldongne_Unity.unitypackage",ExportPackageOptions.Default);
        return new {files=paths,bytes=new FileInfo("../ArtSource/Daldongne/Daldongne_Unity.unitypackage").Length};
    }
}
