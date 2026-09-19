using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using CompanyGame.Daldongne;

public static class DaldongnePlayersDeliver
{
    public static object Export()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play first.");
        AssetDatabase.Refresh();
        if(EditorUtility.scriptCompilationFailed)throw new Exception("Compilation errors.");
        int meshes=0;
        foreach(string sex in new[]{"Female","Male"})
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Daldongne/Players/Player"+sex+".prefab");
            if(!p || !p.GetComponent<CharacterController>() || !p.GetComponent<PlayerMovement>())throw new Exception("Invalid playable prefab.");
            var a=p.GetComponent<DaldongnePlayerAppearance>();if(!a || !a.female || !a.male)throw new Exception("Missing variant references.");
            foreach(var mf in p.GetComponentsInChildren<MeshFilter>(true))
            {if(!mf.sharedMesh || !EditorUtility.IsPersistent(mf.sharedMesh))throw new Exception("Unpersisted mesh.");meshes++;}
            foreach(var r in p.GetComponentsInChildren<MeshRenderer>(true))
                if(!r.sharedMaterial || !EditorUtility.IsPersistent(r.sharedMaterial))throw new Exception("Unpersisted material.");
        }
        string output=Path.GetFullPath("../ArtSource/Daldongne/DaldongnePlayers.unitypackage");
        AssetDatabase.ExportPackage(new[]{"Assets/Art/Daldongne/Players","Assets/Scripts/World/DaldongnePlayerAppearance.cs","Assets/Scripts/World/DaldongneAvatarMotion.cs","Assets/Scripts/Player/PlayerMovement.cs","Assets/Scripts/World/DaldongneMapCamera.cs"},output,ExportPackageOptions.Recurse);
        return new{passed=true,prefabs=2,persistentMeshReferences=meshes,package=output,bytes=new FileInfo(output).Length,compilationFailed=EditorUtility.scriptCompilationFailed};
    }
}
