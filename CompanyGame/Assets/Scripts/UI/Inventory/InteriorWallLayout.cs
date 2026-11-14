using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Interior partition geometry and records. Ownership and persistence use the existing services.</summary>
public static class InteriorWallLayout
{
    static readonly Dictionary<string, PlacedWallSave> records = new();
    static readonly Dictionary<string, GameObject> models = new();
    static Material finish;
    public static int Count => records.Count;
    public static PlacedWallSave Copy(PlacedWallSave value) => value == null ? null : new PlacedWallSave
    { id = value.id, scene = value.scene, property = value.property, start = value.start, end = value.end, height = value.height, thickness = value.thickness };
    public static PlacedWallSave Get(string id) => id != null && records.TryGetValue(id, out var value) ? Copy(value) : null;
    public static GameObject Model(string id) => id != null && models.TryGetValue(id, out var model) ? model : null;
    public static string Find(Transform hit)
    {
        foreach (var pair in models) if (pair.Value && hit.IsChildOf(pair.Value.transform)) return pair.Key;
        return null;
    }
    public static bool Shape(PlacedWallSave wall, PropertyZone zone, out Vector3 position, out Quaternion rotation, out Vector3 size)
    {
        position = default; rotation = Quaternion.identity; size = default;
        if (wall == null || !zone || wall.property != zone.propertyId || wall.scene != zone.gameObject.scene.path ||
            string.IsNullOrEmpty(wall.id) || !Finite(wall.start) || !Finite(wall.end) ||
            !float.IsFinite(wall.height) || !float.IsFinite(wall.thickness) ||
            wall.height < .3f || wall.height > zone.size.y - .02f || wall.thickness < .05f || wall.thickness > .4f) return false;
        var a = wall.start; var b = wall.end;
        if (Mathf.Abs(a.y) > .001f || Mathf.Abs(b.y) > .001f) return false;
        var direction = b - a;
        if (direction.magnitude < .1f || (Mathf.Abs(direction.x) > .001f && Mathf.Abs(direction.z) > .001f)) return false;
        // Ownable zones use metre-scale horizontal coordinates, just like saved furniture.
        if ((zone.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f) return false;
        position = zone.transform.TransformPoint((a + b) * .5f + Vector3.up * (.015f + wall.height * .5f));
        rotation = Quaternion.LookRotation(zone.transform.TransformDirection(direction), Vector3.up);
        size = new Vector3(wall.thickness, wall.height, direction.magnitude);
        return true;
    }
    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    static bool Joins(PlacedWallSave a, PlacedWallSave b)
    {
        if (a.property != b.property || a.scene != b.scene) return false;
        foreach (var p in new[] { a.start, a.end })
            foreach (var q in new[] { b.start, b.end })
                if ((p - q).sqrMagnitude < .0001f)
                {
                    Vector3 da = ((p - a.start).sqrMagnitude < .0001f ? a.end : a.start) - p;
                    Vector3 db = ((q - b.start).sqrMagnitude < .0001f ? b.end : b.start) - q;
                    // An L joint or two ends meeting is allowed; overlapping parallel runs are not.
                    return Vector3.Dot(da.normalized, db.normalized) < .01f;
                }
        return false;
    }
    public static bool CanPlace(PlacedWallSave wall, PropertyZone zone)
    {
        if (!FurniturePlacementController.CanEdit(zone) || !Shape(wall, zone, out var p, out var r, out var size)) return false;
        return FurniturePlacementController.FitsVolume(zone, new Bounds(Vector3.zero, size), p, r, collider =>
        {
            var otherId = Find(collider.transform);
            return otherId != null && (otherId == wall.id || Joins(wall, records[otherId]));
        });
    }
    public static bool TryPut(PlacedWallSave wall, PropertyZone zone)
    {
        if (!CanPlace(wall, zone)) return false;
        records[wall.id] = Copy(wall); Create(wall, zone); WorldSaveRunner.RequestSave(); return true;
    }
    public static bool TryRemove(string id, PropertyZone zone)
    {
        var wall = Get(id);
        if (!FurniturePlacementController.CanEdit(zone) || wall == null || wall.property != zone.propertyId || wall.scene != zone.gameObject.scene.path) return false;
        records.Remove(id); RemoveModel(id); WorldSaveRunner.RequestSave(); return true;
    }
    static void RemoveModel(string id)
    {
        if (models.TryGetValue(id, out var model) && model)
        {
            model.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(model); else UnityEngine.Object.DestroyImmediate(model);
        }
        models.Remove(id);
    }
    static void Create(PlacedWallSave wall, PropertyZone zone)
    {
        if (!Shape(wall, zone, out var p, out var r, out var size)) return;
        RemoveModel(wall.id);
        if (!finish)
        {
            finish = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            finish.name = "Interior partition warm white";
            finish.SetColor("_BaseColor", new Color(.92f, .90f, .85f)); finish.SetFloat("_Smoothness", .2f);
        }
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube); model.name = "InteriorWall_" + wall.id;
        SceneManager.MoveGameObjectToScene(model, zone.gameObject.scene);
        model.transform.SetPositionAndRotation(p, r); model.transform.localScale = size;
        model.GetComponent<Renderer>().sharedMaterial = finish;
        var obstacle = model.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = Vector3.one; obstacle.carving = true;
        models[wall.id] = model;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        records.Clear(); models.Clear(); finish = null; SceneManager.sceneLoaded -= RestoreScene;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        WorldSave.CaptureInteriorWalls = Capture; WorldSave.RestoreInteriorWalls = Restore;
        SceneManager.sceneLoaded += RestoreScene;
    }
    public static List<PlacedWallSave> Capture()
    {
        var result = new List<PlacedWallSave>(); foreach (var wall in records.Values) result.Add(Copy(wall)); return result;
    }
    public static void Restore(List<PlacedWallSave> saved)
    {
        foreach (var id in new List<string>(models.Keys)) RemoveModel(id);
        records.Clear();
        if (saved != null) foreach (var wall in saved)
            if (wall != null && !string.IsNullOrEmpty(wall.id)) records[wall.id] = Copy(wall);
        for (int i = 0; i < SceneManager.sceneCount; i++) RestoreScene(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }
    static void RestoreScene(Scene scene, LoadSceneMode mode)
    {
        if (!FurniturePlacementController.IsLocalWorld || !scene.isLoaded) return;
        foreach (var wall in records.Values)
        {
            if (wall.scene != scene.path || Model(wall.id)) continue;
            foreach (var zone in PropertyZone.Active)
                if (zone.gameObject.scene == scene && zone.propertyId == wall.property) { Create(wall, zone); break; }
        }
    }
}
