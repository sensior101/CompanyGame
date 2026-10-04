using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CompanyGame.Editor.Goshiwon
{
    /// <summary>Generates deed sprites + 20 deed ItemData assets, registers the interior scene and adds the
    /// goshiwon entrance/exit to daldongnaemap. Idempotent; run after GoshiwonInteriorBuilder.Build().</summary>
    public static class GoshiwonSetup
    {
        const string SpriteDir = "Assets/Art/Items/Deeds";
        const string ItemDir = "Assets/Resources/Inventory/Deeds";
        const string EntranceName = "Goshiwon Entrance", ExitSpawnName = "Goshiwon Exit Spawn";

        [MenuItem("CompanyGame/Setup/Run Goshiwon Setup")]
        static void RunMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Run();
        }

        public static void Run()
        {
            EnsureFolder(SpriteDir);
            EnsureFolder(ItemDir);
            var house = MakeDeedSprite("deed_house.png", new Color(.8f, .1f, .1f));
            MakeDeedSprite("deed_land.png", new Color(.1f, .35f, .75f));
            foreach (string no in RoomNumbers()) MakeItem(no, house);
            AddBuildScene(GoshiwonInteriorBuilder.ScenePath);
            AssetDatabase.SaveAssets();
            PlaceVillageEntrance();
            Debug.Log("[Goshiwon] Setup complete.");
        }

        static IEnumerable<string> RoomNumbers()
        {
            yield return "101"; yield return "102";
            for (int f = 2; f <= 4; f++)
                for (int r = 1; r <= 6; r++) yield return (f * 100 + r).ToString();
        }

        static void MakeItem(string no, Sprite icon)
        {
            string path = ItemDir + "/deed_house_goshiwon_" + no + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            bool isNew = !item;
            if (isNew) item = ScriptableObject.CreateInstance<ItemData>();
            item.itemId = "deed_house_goshiwon_" + no;
            item.displayName = "고시원 " + no + "호 집문서";
            item.icon = icon;
            item.category = ItemCategory.Document;
            item.maxStack = 1;
            item.propertyId = "goshiwon_" + no;
            item.propertyName = "고시원 " + no + "호";
            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
        }

        // ---- sprites ----
        static Sprite MakeDeedSprite(string file, Color stamp)
        {
            string path = SpriteDir + "/" + file;
            const int w = 256, h = 320;
            var px = new Color[w * h];
            var rng = new System.Random(file.GetHashCode() & 0xffff);
            for (int i = 0; i < px.Length; i++)
            {
                float n = .93f + (float)rng.NextDouble() * .05f;
                px[i] = new Color(n, n * .97f, n * .88f, 1f);
            }
            for (int y = h - 70; y > 100; y -= 14) Bar(px, w, 26, w - 26 - rng.Next(0, 80), y, 5, .25f);
            Bar(px, w, 60, w - 60, h - 36, 14, .15f); // title area
            Blur(px, w, h, 2);
            Ring(px, w, w - 70, 62, 34, stamp);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void Bar(Color[] px, int w, int x0, int x1, int y, int thick, float ink)
        {
            for (int x = x0; x < x1; x++)
                for (int t = 0; t < thick; t++)
                    px[(y + t) * w + x] *= new Color(ink + .3f, ink + .3f, ink + .3f, 1f);
        }

        static void Blur(Color[] px, int w, int h, int passes)
        {
            for (int p = 0; p < passes; p++)
            {
                var src = (Color[])px.Clone();
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        Color s = Color.clear;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++) s += src[(y + dy) * w + x + dx];
                        px[y * w + x] = s / 9f;
                    }
            }
        }

        static void Ring(Color[] px, int w, int cx, int cy, int r, Color c)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    bool ring = d < r && d > r - 4;
                    bool mark = Mathf.Abs(x - cx) < r * .45f && Mathf.Abs(y - cy) < r * .45f && ((x + y) % 7 < 3);
                    if (ring || (d < r && mark)) px[y * w + x] = Color.Lerp(px[y * w + x], c, .85f);
                }
        }

        // ---- scene registration ----
        static void AddBuildScene(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---- daldongnaemap entrance ----
        static void PlaceVillageEntrance()
        {
            var scene = EditorSceneManager.OpenScene(GoshiwonInteriorBuilder.VillagePath, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            foreach (var t in all.Where(t => t && (t.name == EntranceName || t.name == ExitSpawnName)).ToList())
                Object.DestroyImmediate(t.gameObject);
            var building = all.FirstOrDefault(t => t && t.name == "Gosiwon" && t.GetComponentsInChildren<Renderer>(true).Any(r => r.name.StartsWith("20_Doors_")));
            if (!building) throw new System.InvalidOperationException("Gosiwon building with 20_Doors_* renderers not found in daldongnaemap.");
            var renderers = building.GetComponentsInChildren<Renderer>();
            var doorRs = renderers.Where(r => r.name.StartsWith("20_Doors_")).ToList();
            // Some 20_Doors_* meshes are multi-floor stacks; the ground-floor front door has the lowest bounds.min.y.
            var door = doorRs.OrderBy(r => r.bounds.min.y).First().bounds;
            var body = Union(renderers.Where(r => !r.name.StartsWith("Sign") && !r.name.StartsWith("30_") && !r.name.StartsWith("40_")));
            Vector3 d = door.center - body.center; d.y = 0;
            Vector3 outward = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(d.z));
            float half = Mathf.Abs(outward.x) > 0 ? door.extents.x : door.extents.z;
            Vector3 basePos = new Vector3(door.center.x, door.min.y, door.center.z) + outward * (half + .3f);
            basePos.y = GroundY(basePos, door.min.y);
            var parent = all.FirstOrDefault(t => t && t.name == "ConvenienceStore_Connections");
            var entrance = new GameObject(EntranceName).AddComponent<StoreInteractionPoint>();
            entrance.prompt = "들어가기";
            entrance.targetScenePath = GoshiwonInteriorBuilder.ScenePath;
            entrance.targetSpawnId = "goshiwon_entry";
            entrance.radius = 1.4f; entrance.heightTolerance = .9f;
            entrance.transform.position = basePos;
            var exit = new GameObject(ExitSpawnName).AddComponent<MapSpawnPoint>();
            exit.spawnId = "goshiwon_exit";
            Vector3 exitPos = basePos + outward * 1.0f;
            exitPos.y = GroundY(exitPos, basePos.y);
            exit.transform.position = exitPos;
            exit.transform.rotation = Quaternion.LookRotation(outward);
            if (parent) { entrance.transform.SetParent(parent, true); exit.transform.SetParent(parent, true); }
            Debug.Log("[Goshiwon] Door bounds " + door + ", outward " + outward + ", entrance at " + basePos + ", exit at " + exitPos + ", door.min.y " + door.min.y);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static float GroundY(Vector3 p, float fallback)
        {
            Physics.SyncTransforms();
            var origin = new Vector3(p.x, fallback + 1.5f, p.z);
            return Physics.Raycast(origin, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;
        }

        static Bounds Union(IEnumerable<Renderer> rs)
        {
            Bounds b = default; bool first = true;
            foreach (var r in rs) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
