using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the four drivable cars procedurally (no Blender): meshes, materials, prefabs, VehicleGrade + ItemData assets, icons.
/// Re-runnable: existing assets are overwritten in place so GUIDs and references survive.
/// Prefab layout: Car_&lt;id&gt; -> Body, Wheel_FL/FR/RL/RR. Pivot = point on the ground under the driver seat
/// (left-hand drive), +Z forward, wheel pivots = hub centres, wheel axis = X.
/// </summary>
public static class CarModelBuilder
{
    public const string ArtRoot = "Assets/Art/Items/Vehicles/Cars";
    public const string CarItemsRoot = "Assets/Resources/Inventory/Vehicles/Cars";
    public const string GradeRoot = "Assets/Resources/Inventory/Vehicles/Grades";
    public const float DriverX = -0.37f; // driver seat lateral position in body space (left-hand drive)

    // Body submesh slots (= material order on Body).
    const int Paint = 0, Glass = 1, Trim = 2, Chrome = 3, LightF = 4, LightR = 5;

    public struct Part
    {
        public int sub; public float x, y, z, w, h, d; public bool mirror, round;
    }

    public sealed class CarSpec
    {
        public string id, label, paintHex;
        public int tier;
        public float topSpeedKmh, zeroTo100, brake;
        public float length, width, frontOverhang, wheelbase, seatZ, wheelRadius, wheelWidth, cabinTop;
        public ProfilePoint[] lower, cabin;
        public int roofFrom, roofTo;
        public float[] pillars;
        public Part[] parts;
        public string ItemId => "vehicle:car:" + tier + ":" + id;
        public float FrontAxleZ => length * 0.5f - frontOverhang;
        public float RearAxleZ => FrontAxleZ - wheelbase;
    }

    static ProfilePoint P(float z, float y, float k = 1f) => new ProfilePoint(z, y, k);
    static Part Box(int sub, float x, float y, float z, float w, float h, float d, bool mirror = true)
        => new Part { sub = sub, x = x, y = y, z = z, w = w, h = h, d = d, mirror = mirror };
    static Part Round(int sub, float x, float y, float z, float radius, float depth, bool mirror = true)
        => new Part { sub = sub, x = x, y = y, z = z, w = radius, d = depth, mirror = mirror, round = true };

    // Game-scaled grades from the design (설계 수정 변경 1).
    public static readonly CarSpec[] Specs =
    {
        new CarSpec
        {
            id = "knight", label = "나이트", tier = 1, topSpeedKmh = 60f, zeroTo100 = 12f, brake = 8f, paintHex = "#8DB7C9",
            length = 3.60f, width = 1.60f, frontOverhang = 0.64f, wheelbase = 2.34f, seatZ = 0.10f,
            wheelRadius = 0.30f, wheelWidth = 0.165f, cabinTop = 0.76f,
            lower = new[] { P(-1.80f, .22f, .92f), P(1.80f, .22f, .92f), P(1.80f, .60f, .94f), P(1.64f, .78f, .97f),
                P(.80f, .86f), P(-1.60f, .94f), P(-1.80f, .84f, .94f) },
            cabin = new[] { P(-1.70f, .88f), P(.80f, .82f), P(.30f, 1.47f), P(-1.52f, 1.47f) },
            roofFrom = 2, roofTo = 3, pillars = new[] { -.35f },
            parts = new[]
            {
                Box(Trim, 0, .30f, 1.78f, 1.30f, .16f, .08f), Box(Trim, 0, .52f, 1.80f, .62f, .12f, .04f),
                Box(LightF, .58f, .68f, 1.72f, .30f, .13f, .16f),
                Box(LightR, .62f, .68f, -1.80f, .16f, .30f, .05f),
                Box(Trim, 0, .30f, -1.78f, 1.30f, .14f, .08f),
            },
        },
        new CarSpec
        {
            id = "avande", label = "아방데", tier = 2, topSpeedKmh = 90f, zeroTo100 = 8f, brake = 9f, paintHex = "#E6E1D3",
            length = 4.65f, width = 1.82f, frontOverhang = 0.90f, wheelbase = 2.72f, seatZ = 0.25f,
            wheelRadius = 0.33f, wheelWidth = 0.21f, cabinTop = 0.74f,
            lower = new[] { P(-2.325f, .22f, .93f), P(2.325f, .22f, .93f), P(2.325f, .60f, .95f), P(2.18f, .74f, .97f),
                P(.95f, .86f), P(-1.65f, .92f), P(-2.20f, .98f, .98f), P(-2.325f, .86f, .94f) },
            cabin = new[] { P(-1.62f, .90f), P(.98f, .82f), P(.10f, 1.39f), P(-.40f, 1.42f), P(-.85f, 1.40f) },
            roofFrom = 2, roofTo = 4, pillars = new[] { -.30f },
            parts = new[]
            {
                Box(Trim, 0, .30f, 2.305f, 1.55f, .17f, .08f), Box(Trim, 0, .50f, 2.325f, 1.00f, .15f, .04f),
                Box(Chrome, 0, .585f, 2.325f, 1.00f, .02f, .045f), Box(LightF, .70f, .72f, 2.22f, .46f, .10f, .18f),
                Box(LightR, .70f, .88f, -2.28f, .48f, .10f, .10f), Box(LightR, 0, .88f, -2.30f, .50f, .03f, .04f),
                Box(Trim, 0, .32f, -2.305f, 1.55f, .16f, .08f),
            },
        },
        new CarSpec
        {
            id = "bayern_b5", label = "바이에른 B5", tier = 3, topSpeedKmh = 120f, zeroTo100 = 6f, brake = 10f, paintHex = "#4A5A70",
            length = 4.96f, width = 1.86f, frontOverhang = 0.88f, wheelbase = 2.97f, seatZ = 0.20f,
            wheelRadius = 0.34f, wheelWidth = 0.23f, cabinTop = 0.73f,
            lower = new[] { P(-2.48f, .22f, .93f), P(2.48f, .22f, .93f), P(2.48f, .60f, .95f), P(2.36f, .72f, .97f),
                P(.62f, .92f), P(-1.75f, .97f), P(-2.34f, 1.00f, .98f), P(-2.48f, .88f, .94f) },
            cabin = new[] { P(-1.82f, .92f), P(.70f, .86f), P(-.22f, 1.44f), P(-.70f, 1.465f), P(-1.15f, 1.43f) },
            roofFrom = 2, roofTo = 4, pillars = new[] { -.40f },
            parts = new[]
            {
                Box(Trim, 0, .30f, 2.46f, 1.55f, .17f, .08f),
                Box(Chrome, .125f, .50f, 2.48f, .23f, .18f, .04f), Box(Trim, .125f, .50f, 2.50f, .19f, .14f, .02f),
                Box(LightF, .68f, .69f, 2.36f, .40f, .09f, .20f), Box(Trim, .66f, .40f, 2.48f, .38f, .12f, .04f),
                Box(LightR, .72f, .90f, -2.45f, .38f, .10f, .14f), Box(Trim, 0, .32f, -2.46f, 1.60f, .17f, .08f),
                Box(Chrome, .55f, .28f, -2.49f, .10f, .06f, .05f),
            },
        },
        new CarSpec
        {
            id = "bentler_continental", label = "밴틀러 컨티넨털", tier = 4, topSpeedKmh = 140f, zeroTo100 = 4f, brake = 11f, paintHex = "#2E5C45",
            length = 4.85f, width = 1.90f, frontOverhang = 0.95f, wheelbase = 2.85f, seatZ = 0.15f,
            wheelRadius = 0.36f, wheelWidth = 0.27f, cabinTop = 0.70f,
            lower = new[] { P(-2.425f, .22f, .94f), P(2.425f, .22f, .94f), P(2.425f, .62f, .96f), P(2.30f, .78f, .98f),
                P(1.20f, .90f), P(.35f, .96f), P(-1.50f, 1.03f), P(-2.22f, 1.05f, .98f), P(-2.425f, .92f, .94f) },
            cabin = new[] { P(-1.95f, 1.01f), P(.55f, .93f), P(-.10f, 1.34f), P(-.80f, 1.388f), P(-1.35f, 1.30f) },
            roofFrom = 2, roofTo = 4, pillars = new[] { -.45f },
            parts = new[]
            {
                Box(Trim, 0, .30f, 2.405f, 1.62f, .18f, .08f),
                Box(Chrome, 0, .47f, 2.425f, .85f, .28f, .04f, false), Box(Trim, 0, .47f, 2.447f, .77f, .22f, .02f, false),
                Round(LightF, .62f, .70f, 2.33f, .11f, .12f), Round(Chrome, .62f, .70f, 2.30f, .135f, .08f),
                Box(Trim, .70f, .38f, 2.425f, .42f, .12f, .04f),
                Box(LightR, .72f, .92f, -2.40f, .34f, .14f, .10f), Box(Trim, 0, .32f, -2.405f, 1.62f, .18f, .08f),
                Round(Chrome, .60f, .30f, -2.44f, .05f, .08f),
            },
        },
    };

    [MenuItem("CompanyGame/Vehicles/Build Cars")]
    static void Menu() => Build();

    /// <summary>Entry point for -executeMethod CarModelBuilder.Build.</summary>
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.Exception("Build cars in Edit mode.");
        foreach (var dir in new[] { "Meshes", "Materials", "Prefabs", "Icons" }) Directory.CreateDirectory(ArtRoot + "/" + dir);
        Directory.CreateDirectory(CarItemsRoot);
        Directory.CreateDirectory(GradeRoot);
        AssetDatabase.Refresh();
        var common = BuildCommonMaterials();
        foreach (var s in Specs) BuildCar(s, common);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built " + Specs.Length + " cars under " + ArtRoot);
    }

    static void BuildCar(CarSpec s, Dictionary<string, Material> common)
    {
        var paint = LoadOrCreateMaterial(ArtRoot + "/Materials/Car_Paint_" + s.id + ".mat", s.paintHex, .35f, .62f);
        var body = SaveMesh(ArtRoot + "/Meshes/Car_" + s.id + "_Body.asset", BuildBody(s));
        var wheel = SaveMesh(ArtRoot + "/Meshes/Car_" + s.id + "_Wheel.asset", BuildWheel(s));
        var bodyMats = new[] { paint, common["Glass"], common["Trim"], common["Chrome"], common["LightFront"], common["LightRear"] };
        var prefab = SavePrefab(s, body, wheel, bodyMats, new[] { common["Rubber"], common["Chrome"] });
        var icon = SaveIcon(prefab, ArtRoot + "/Icons/Car_" + s.id + ".png");
        var grade = SaveGrade(s);
        SaveItem(s, prefab, icon, grade);
    }

    // ---------------------------------------------------------------- geometry

    static LowPolyMesh BuildBody(CarSpec s)
    {
        var m = new LowPolyMesh(6) { Offset = new Vector3(-DriverX, 0f, -s.seatZ) };
        float half = s.width * 0.5f;
        float lowMin = s.lower.Min(p => p.y), lowMax = s.lower.Max(p => p.y);
        m.Loft(s.lower, half * .95f, half, lowMin, lowMax, Paint);
        AddGreenhouse(m, s, half);
        foreach (var part in s.parts) AddPart(m, part);
        foreach (var axle in new[] { s.FrontAxleZ, s.RearAxleZ })
            foreach (var side in new[] { -1f, 1f })
            {
                float y = s.wheelRadius + .07f;
                float hw = Mathf.Lerp(half * .95f, half, Mathf.InverseLerp(lowMin, lowMax, y));
                m.Cylinder(new Vector3(side * hw, y, axle), s.wheelRadius * 1.15f, .03f, 10, Quaternion.identity, Trim);
            }
        return m;
    }

    static void AddGreenhouse(LowPolyMesh m, CarSpec s, float half)
    {
        float cb = half * .88f, ct = half * s.cabinTop;
        float yB = s.cabin.Min(p => p.y), yT = s.cabin.Max(p => p.y);
        m.Loft(s.cabin, cb, ct, yB, yT, Glass);
        var chain = s.cabin.Skip(s.roofFrom).Take(s.roofTo - s.roofFrom + 1).ToArray();
        var slab = chain.Select(p => P(p.z, p.y + .012f)).Concat(chain.AsEnumerable().Reverse().Select(p => P(p.z, p.y - .045f))).ToArray();
        m.Loft(slab, ct + .012f, ct + .012f, yB, yT, Paint);
        foreach (var pz in s.pillars)
        {
            var rect = new[] { P(pz - .045f, yB), P(pz + .045f, yB), P(pz + .045f, TopY(s.cabin, pz)), P(pz - .045f, TopY(s.cabin, pz)) };
            m.Loft(rect, cb + .012f, ct + .012f, yB, yT, Paint);
        }
        var baseP = s.cabin[1];
        AddPart(m, Box(Paint, cb + .085f, baseP.y + .10f, baseP.z - .05f, .11f, .09f, .20f));
        AddPart(m, Box(Paint, cb + .01f, baseP.y + .10f, baseP.z - .05f, .07f, .03f, .05f));
    }

    static float TopY(ProfilePoint[] poly, float z)
    {
        float top = float.MinValue;
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Length];
            if (Mathf.Approximately(a.z, b.z) || z < Mathf.Min(a.z, b.z) || z > Mathf.Max(a.z, b.z)) continue;
            top = Mathf.Max(top, Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.z, b.z, z)));
        }
        return top;
    }

    static void AddPart(LowPolyMesh m, Part p)
    {
        var rotZ = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
        for (int i = 0; i < (p.mirror && p.x != 0f ? 2 : 1); i++)
        {
            var c = new Vector3(i == 0 ? p.x : -p.x, p.y, p.z);
            if (p.round) m.Cylinder(c, p.w, p.d, 10, rotZ, p.sub);
            else m.Box(c, new Vector3(p.w, p.h, p.d), p.sub);
        }
    }

    static LowPolyMesh BuildWheel(CarSpec s)
    {
        var m = new LowPolyMesh(2);
        m.Cylinder(Vector3.zero, s.wheelRadius, s.wheelWidth, 12, Quaternion.identity, 0);
        m.Cylinder(Vector3.zero, s.wheelRadius * .62f, s.wheelWidth + .03f, 8, Quaternion.identity, 1);
        return m;
    }

    // ---------------------------------------------------------------- assets

    static Dictionary<string, Material> BuildCommonMaterials()
    {
        string root = ArtRoot + "/Materials/";
        return new Dictionary<string, Material>
        {
            ["Glass"] = LoadOrCreateMaterial(root + "Car_Glass.mat", "#1E2A33", .1f, .88f),
            ["Trim"] = LoadOrCreateMaterial(root + "Car_Trim.mat", "#2B2E30", 0f, .25f),
            ["Chrome"] = LoadOrCreateMaterial(root + "Car_Chrome.mat", "#C4C9CC", .8f, .75f),
            ["Rubber"] = LoadOrCreateMaterial(root + "Car_Rubber.mat", "#222526", 0f, .18f),
            ["LightFront"] = LoadOrCreateMaterial(root + "Car_LightFront.mat", "#F2F0DA", 0f, .8f, "#F2F0DA", .35f),
            ["LightRear"] = LoadOrCreateMaterial(root + "Car_LightRear.mat", "#C9302C", 0f, .7f, "#C9302C", .35f),
        };
    }

    static Material LoadOrCreateMaterial(string path, string hex, float metallic, float smoothness, string emissionHex = null, float emission = 0f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new System.Exception("URP Lit shader missing");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        ColorUtility.TryParseHtmlString(hex, out var color);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (emissionHex != null)
        {
            ColorUtility.TryParseHtmlString(emissionHex, out var glow);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", glow * emission);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh SaveMesh(string path, LowPolyMesh source)
    {
        var fresh = source.Build(Path.GetFileNameWithoutExtension(path));
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
        EditorUtility.CopySerialized(fresh, existing);
        Object.DestroyImmediate(fresh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static GameObject SavePrefab(CarSpec s, Mesh body, Mesh wheel, Material[] bodyMats, Material[] wheelMats)
    {
        var root = new GameObject("Car_" + s.id);
        try
        {
            AddMeshChild(root.transform, "Body", body, bodyMats, Vector3.zero);
            float wx = s.width * .5f + .012f - s.wheelWidth * .5f;
            float sx = -DriverX, sz = -s.seatZ;
            AddMeshChild(root.transform, "Wheel_FL", wheel, wheelMats, new Vector3(sx - wx, s.wheelRadius, s.FrontAxleZ + sz));
            AddMeshChild(root.transform, "Wheel_FR", wheel, wheelMats, new Vector3(sx + wx, s.wheelRadius, s.FrontAxleZ + sz));
            AddMeshChild(root.transform, "Wheel_RL", wheel, wheelMats, new Vector3(sx - wx, s.wheelRadius, s.RearAxleZ + sz));
            AddMeshChild(root.transform, "Wheel_RR", wheel, wheelMats, new Vector3(sx + wx, s.wheelRadius, s.RearAxleZ + sz));
            return PrefabUtility.SaveAsPrefabAsset(root, ArtRoot + "/Prefabs/Car_" + s.id + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void AddMeshChild(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    static VehicleGrade SaveGrade(CarSpec s)
    {
        string path = GradeRoot + "/Grade_" + s.id + ".asset";
        var grade = AssetDatabase.LoadAssetAtPath<VehicleGrade>(path);
        if (!grade) { grade = ScriptableObject.CreateInstance<VehicleGrade>(); AssetDatabase.CreateAsset(grade, path); }
        grade.displayName = s.label;
        grade.topSpeedKmh = s.topSpeedKmh;
        grade.zeroTo100Sec = s.zeroTo100;
        grade.brakeMps2 = s.brake;
        EditorUtility.SetDirty(grade);
        return grade;
    }

    static void SaveItem(CarSpec s, GameObject prefab, Sprite icon, VehicleGrade grade)
    {
        string path = CarItemsRoot + "/Car_" + s.id + ".asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (!item) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, path); }
        item.itemId = s.ItemId; item.displayName = s.label; item.category = ItemCategory.Vehicle; item.maxStack = 1;
        item.vehiclePrefab = prefab; item.vehicleSpeed = Mathf.Max(1f, s.topSpeedKmh / 3.6f); // fallback only; grade drives cars
        item.grade = grade; item.wheelRadius = s.wheelRadius;
        const float scale = .08f;
        item.heldPrefab = prefab; item.heldLocalScale = Vector3.one * scale;
        item.heldLocalEulerAngles = new Vector3(-18f, 60f, -25f);
        item.heldLocalPosition = -(Quaternion.Euler(item.heldLocalEulerAngles) * MeasureBounds(prefab).center) * scale;
        item.icon = icon;
        EditorUtility.SetDirty(item);
    }

    /// <summary>Prefab-local bounds from mesh bounds + child offsets (children have no rotation/scale).</summary>
    public static Bounds MeasureBounds(GameObject prefab)
    {
        Bounds? total = null;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            var b = mf.sharedMesh.bounds;
            b.center += mf.transform.localPosition;
            if (total == null) total = b; else { var t = total.Value; t.Encapsulate(b); total = t; }
        }
        return total ?? new Bounds();
    }

    // ---------------------------------------------------------------- icon

    static Sprite SaveIcon(GameObject prefab, string path)
    {
        var tex = RenderIcon(prefab);
        File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.maxTextureSize = 512;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Difference matting: render on black and on white, alpha = 1 - (white - black). Independent of how URP treats the clear alpha.
    static Texture2D RenderIcon(GameObject prefab)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var cam = pru.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 22f; cam.nearClipPlane = .3f; cam.farClipPlane = 80f;
            pru.lights[0].transform.rotation = Quaternion.Euler(40f, -40f, 0f); pru.lights[0].intensity = 1.4f;
            pru.lights[1].intensity = .6f; pru.ambientColor = new Color(.45f, .45f, .5f);
            pru.AddSingleGO(Object.Instantiate(prefab));
            var b = MeasureBounds(prefab);
            float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * 1.02f;
            cam.transform.position = b.center + new Vector3(.8f, .45f, 1f).normalized * dist; // front 3/4 view
            cam.transform.LookAt(b.center);
            var onBlack = RenderOn(pru, Color.black);
            var onWhite = RenderOn(pru, Color.white);
            var kb = onBlack.GetPixels32(); var kw = onWhite.GetPixels32();
            for (int i = 0; i < kb.Length; i++)
            {
                float a = 1f - (kw[i].r - kb[i].r + kw[i].g - kb[i].g + kw[i].b - kb[i].b) / (3f * 255f);
                a = a < .02f ? 0f : Mathf.Clamp01(a);
                kb[i] = a <= 0f ? new Color32(0, 0, 0, 0) : new Color32((byte)Mathf.Min(255, Mathf.RoundToInt(kb[i].r / a)), (byte)Mathf.Min(255, Mathf.RoundToInt(kb[i].g / a)), (byte)Mathf.Min(255, Mathf.RoundToInt(kb[i].b / a)), (byte)Mathf.RoundToInt(a * 255f));
            }
            Object.DestroyImmediate(onWhite);
            var tex = new Texture2D(onBlack.width, onBlack.height, TextureFormat.ARGB32, false);
            tex.SetPixels32(kb); tex.Apply();
            Object.DestroyImmediate(onBlack);
            return tex;
        }
        finally { pru.Cleanup(); }
    }

    static Texture2D RenderOn(PreviewRenderUtility pru, Color bg)
    {
        pru.camera.backgroundColor = bg;
        pru.BeginStaticPreview(new Rect(0, 0, 512, 512));
        pru.camera.Render();
        return pru.EndStaticPreview();
    }
}
