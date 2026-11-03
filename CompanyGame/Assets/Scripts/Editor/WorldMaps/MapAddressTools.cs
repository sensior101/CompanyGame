using System;
using System.Collections.Generic;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Assigns addresses only to authored parcel boundaries. Re-running preserves every existing address.</summary>
    public static class MapAddressTools
    {
        public static readonly string[] Scenes = { "daldongnaemap", "DaldongnePocketGarden", "NightlifeDistrict", "CivicDistrict", "BusinessDistrict", "CoastalResidentialDistrict", "LuxuryResidentialDistrict" };
        public static readonly string[] Districts = { "달동네", "동네 정원", "상업 지구", "공공 지구", "업무 지구", "해안 주거지", "고급 주거지" };

        [MenuItem("Tools/CompanyGame/Map/Assign parcel addresses")]
        public static void AssignAll()
        {
            var report = new List<string>();
            foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                int district = Array.IndexOf(Scenes, name);
                if (district < 0) continue;
                var scene = SceneManager.GetSceneByPath(entry.path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (!opened && scene.isDirty) throw new InvalidOperationException("Save the edited scene before assigning addresses: " + name);
                if (opened) scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                try
                {
                    var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                    var sites = all.Where(t => t.parent && (t.parent.name == "Parcel Sites" ||
                        (t.parent.name == "BusinessReferenceLayout" && (t.name.StartsWith("VacantPlot_") || t.name.StartsWith("CompanyParcel_") || t.name.StartsWith("RetailParcel_") || t.name == "HeadquartersParcel")))).ToArray();
                    int next = sites.Select(t => t.GetComponent<MapParcel>()).Where(p => p).Select(p => p.number).DefaultIfEmpty((district + 1) * 100).Max() + 1;
                    foreach (var site in sites.OrderBy(t => t.name, StringComparer.Ordinal))
                    {
                        var p = site.GetComponent<MapParcel>();
                        if (p) continue;
                        var renderers = site.GetComponentsInChildren<MeshRenderer>(true);
                        if (renderers.Length == 0) throw new InvalidOperationException("Parcel has no boundary mesh: " + site.name);
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                        p = site.gameObject.AddComponent<MapParcel>();
                        p.parcelId = name + ":" + site.name;
                        p.district = Districts[district]; p.number = next++;
                        var land = site.GetComponent<LandPlot>();
                        p.landPlotId = land ? land.Id : p.parcelId;
                        p.localCenter = site.InverseTransformPoint(bounds.center);
                        p.size = new Vector2(bounds.size.x, bounds.size.z);
                        EditorUtility.SetDirty(p);
                    }
                    var parcels = sites.Select(t => t.GetComponent<MapParcel>()).ToArray();
                    if (parcels.Select(p => p.number).Distinct().Count() != parcels.Length || parcels.Select(p => p.parcelId).Distinct().Count() != parcels.Length)
                        throw new InvalidOperationException("Duplicate parcel addresses: " + name);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    report.Add(name + ": " + parcels.Length);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            Debug.Log("Map addresses assigned: " + string.Join(", ", report));
        }
    }
}
