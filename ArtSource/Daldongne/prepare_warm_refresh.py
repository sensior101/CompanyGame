from pathlib import Path
p=Path(__file__).resolve().parents[2]/'CompanyGame/AgentScripts'
code=(p/'DaldongneWarmImport.cs').read_text(encoding='utf-8-sig')
code=code.replace('class DaldongneWarmImport','class DaldongneWarmRefresh')
start=code.index('        // Never overwrite')
end=code.index('        DaldongneWarmPolish.ValidatePipeline();',start)
code=code[:start]+'''        // Refresh only this generated warm-map asset family; preserve the bundle GUID.
'''+code[end:]
code=code.replace('        AssetDatabase.CreateAsset(meshes[0],Bundle);','''        var previous=AssetDatabase.LoadAllAssetsAtPath(Bundle);
        var main=AssetDatabase.LoadMainAssetAtPath(Bundle) as Mesh;
        if(main!=null)
        {
            EditorUtility.CopySerialized(meshes[0],main);
            UnityEngine.Object.DestroyImmediate(meshes[0]);meshes[0]=main;
            foreach(var asset in previous)if(asset!=main)UnityEngine.Object.DestroyImmediate(asset,true);
            EditorUtility.SetDirty(main);
        }
        else AssetDatabase.CreateAsset(meshes[0],Bundle);''')
polish=(p/'DaldongneWarmPolish.cs').read_text(encoding='utf-8-sig')
lines=(code+'\n'+polish).splitlines();usings=list(dict.fromkeys(l for l in lines if l.startswith('using ')))
(p/'DaldongneWarmRefresh.cs').write_text('\n'.join(usings+[l for l in lines if not l.startswith('using ')]),encoding='utf-8')
print('Prepared GUID-preserving warm model refresh.')
