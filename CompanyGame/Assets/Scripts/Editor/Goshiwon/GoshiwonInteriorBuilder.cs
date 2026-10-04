using System.IO;
using CompanyGame.World.Maps;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.Goshiwon
{
    /// <summary>Builds Assets/Scenes/Interiors/GoshiwonInterior.unity from primitives (4 stacked floors, 20 rooms).
    /// Layout per floor (y0 = (floor-1)*4): corridor x -0.8..0.8, z 0..11.1 (stairs at z=0, window at z=11.1),
    /// rooms 3x3 m on both sides (right = +x). Doors are painted panels; travel is teleport only.</summary>
    public static class GoshiwonInteriorBuilder
    {
        public const string ScenePath = "Assets/Scenes/Interiors/GoshiwonInterior.unity";
        public const string VillagePath = "Assets/Scenes/daldongnaemap.unity";
        const string ArtRoot = "Assets/Art/Goshiwon";
        const string FontPath = "Assets/Art/font/NotoSansKR-Regular SDF.asset";
        const float FloorGap = 4f, H = 2.5f, CorridorLen = 11.1f;
        static readonly float[] RoomZ = { 3.0f, 6.2f, 9.4f };
        static Transform world, gameplay, doors, spawns, npcs;
        static TMP_FontAsset font;

        [MenuItem("CompanyGame/Setup/Build Goshiwon Interior")]
        static void BuildMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        public static void Build()
        {
            EnsureFolder(ArtRoot + "/Materials");
            EnsureFolder("Assets/Scenes/Interiors");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Map_GoshiwonInterior").transform;
            new GameObject("00_Systems").transform.SetParent(root, false);
            world = Child(root, "10_World");
            gameplay = Child(root, "20_Gameplay");
            npcs = Child(gameplay, "NPCs");
            doors = Child(gameplay, "Doors");
            spawns = Child(gameplay, "SpawnPoints");
            var presentation = Child(root, "30_Presentation");
            for (int floor = 1; floor <= 4; floor++) BuildFloor(floor);
            BuildCamera(Child(presentation, "Cameras"));
            SetupLighting();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Goshiwon] Interior built: " + ScenePath);
        }

        static void BuildFloor(int n)
        {
            float y0 = (n - 1) * FloorGap;
            var fw = Child(world, "Floor_" + n);
            var fd = Child(doors, "Floor_" + n);
            var wall = Mat("Wallpaper", new Color(.55f, .68f, .5f), 0, Wallpaper());
            var cream = Mat("RoomWall", new Color(.86f, .82f, .7f));
            var wood = Mat("DarkWood", new Color(.2f, .12f, .07f));
            var carpet = Mat("Carpet", new Color(.33f, .2f, .12f));
            var vinyl = Mat("RoomFloor", new Color(.62f, .5f, .33f));
            var slab = Mat("Slab", new Color(.55f, .55f, .52f));
            float mid = CorridorLen / 2;
            Box(fw, "Slab", new Vector3(0, y0 - .1f, mid), new Vector3(8.6f, .2f, 11.5f), slab, true);
            Box(fw, "Ceiling", new Vector3(0, y0 + H + .1f, mid), new Vector3(8.6f, .2f, 11.5f), slab, true);
            Box(fw, "Corridor Carpet", new Vector3(0, y0 + .01f, mid), new Vector3(1.6f, .02f, CorridorLen), carpet, false);
            foreach (int s in new[] { -1, 1 })
            {
                Box(fw, "Outer Wall", new Vector3(s * 4.1f, y0 + H / 2, mid), new Vector3(.2f, H, 11.5f), cream, true);
                Box(fw, "Room Floor", new Vector3(s * 2.5f, y0 + .01f, 6.2f), new Vector3(3f, .02f, 9.4f), vinyl, false);
                foreach (float z in new[] { 1.4f, 4.6f, 7.8f, 11f })
                    if (!(n == 1 && s < 0 && z > 1.5f && z < 10.9f))
                        Box(fw, "Divider", new Vector3(s * 2.5f, y0 + H / 2, z), new Vector3(3f, H, .2f), cream, true);
                Box(fw, "Wainscot", new Vector3(s * .78f, y0 + .45f, mid), new Vector3(.04f, .9f, CorridorLen), wood, false);
            }
            Box(fw, "End Wall South", new Vector3(0, y0 + H / 2, -.1f), new Vector3(2f, H, .2f), wall, true);
            Box(fw, "End Wall North", new Vector3(0, y0 + H / 2, 11.2f), new Vector3(2f, H, .2f), wall, true);
            Window(fw, new Vector3(0, y0 + 1.5f, 11.08f), new Vector3(1f, 1f, .02f), false, 0);
            // Right corridor wall is solid; left wall has a lobby opening on floor 1.
            Box(fw, "Corridor Wall R", new Vector3(.9f, y0 + H / 2, mid), new Vector3(.2f, H, CorridorLen), wall, true);
            if (n == 1) LobbyWall(fw, y0, wall);
            else Box(fw, "Corridor Wall L", new Vector3(-.9f, y0 + H / 2, mid), new Vector3(.2f, H, CorridorLen), wall, true);
            for (int i = 0; i < 3; i++) Fluorescent(fw, new Vector3(0, y0 + H - .03f, 1.8f + i * 3.7f), 2f, 5.5f);
            BuildStairs(n, y0, fw);
            if (n == 1) BuildGroundFloor(fw, fd, y0);
            else for (int k = 0; k < 3; k++)
            {
                Room(fw, fd, n, n * 100 + k + 1, 1, k, y0);
                Room(fw, fd, n, n * 100 + k + 4, -1, k, y0);
            }
        }

        static void LobbyWall(Transform fw, float y0, Material wall)
        {
            // Opening z 4.2..7.2 (lintel above 2.0 m) so the lobby with the front desk is open to the corridor.
            Box(fw, "Corridor Wall L1", new Vector3(-.9f, y0 + H / 2, 2.1f), new Vector3(.2f, H, 4.2f), wall, true);
            Box(fw, "Corridor Wall L2", new Vector3(-.9f, y0 + H / 2, 9.15f), new Vector3(.2f, H, 3.9f), wall, true);
            Box(fw, "Lobby Lintel", new Vector3(-.9f, y0 + 2.25f, 5.7f), new Vector3(.2f, .5f, 3f), wall, true);
        }

        static void BuildGroundFloor(Transform fw, Transform fd, float y0)
        {
            Room(fw, fd, 1, 101, 1, 0, y0);
            Room(fw, fd, 1, 102, 1, 1, y0);
            float dz = RoomZ[2] - .9f;
            Door(fd, "Exit Door", new Vector3(.7f, y0, dz), "나가기", VillagePath, "goshiwon_exit", 1.2f);
            DoorPanel(fw, 1, dz, y0, "Exit");
            Spawn("goshiwon_entry", new Vector3(-.2f, y0 + .05f, dz), 180f);
            Spawn("default", new Vector3(-.2f, y0 + .05f, dz), 180f);
            Box(fw, "Front Desk", new Vector3(-2.2f, y0 + .5f, 5.7f), new Vector3(.6f, 1f, 2.4f), Mat("DarkWood", Color.black), true);
            Box(fw, "Lobby Panel", new Vector3(-2.5f, y0 + H - .03f, 6.2f), new Vector3(1.2f, .05f, .3f), Mat("Fluorescent", Color.white, 3f), false);
            AddLight(fw, new Vector3(-2.5f, y0 + 2.3f, 6.2f), 1.5f, 4.5f);
            Label(fw, "접수", new Vector3(-3.95f, y0 + 2f, 5.7f), -90f, 4f);
            Window(fw, new Vector3(-3.98f, y0 + 1.5f, 8.6f), new Vector3(.02f, 1f, 1.2f), true, -1);
            var lady = Child(npcs, "Landlady (주인아주머니)");
            lady.position = new Vector3(-3.1f, y0, 5.7f);
            lady.rotation = Quaternion.Euler(0, 90, 0);
            Primitive(lady, PrimitiveType.Capsule, "Body", new Vector3(0, .9f, 0), new Vector3(.5f, .9f, .5f), Mat("LadyDress", new Color(.55f, .25f, .35f)));
            Primitive(lady, PrimitiveType.Sphere, "Head", new Vector3(0, 1.85f, 0), Vector3.one * .3f, Mat("LadySkin", new Color(.9f, .72f, .6f)));
        }

        static void BuildStairs(int n, float y0, Transform fw)
        {
            var stairs = Child(gameplay, "Stairs_Floor_" + n);
            stairs.position = new Vector3(0, y0, .6f);
            var fs = stairs.gameObject.AddComponent<FloorStairs>();
            fs.floor = n; fs.topFloor = 4; fs.radius = 1.2f;
            Spawn("floor_" + n, new Vector3(0, y0 + .05f, .7f), 0f);
            var mat = Mat("Slab", new Color(.55f, .55f, .52f));
            for (int i = 0; i < 3; i++)
                Box(fw, "Step", new Vector3(0, y0 + .05f + i * .1f, .25f - i * .08f), new Vector3(1.4f, .1f, .16f), mat, false);
            Label(fw, "계단", new Vector3(0, y0 + 2.1f, .05f), 180f, 3f);
        }

        static void Room(Transform fw, Transform fd, int floor, int no, int side, int k, float y0)
        {
            float c = RoomZ[k], dz = side > 0 ? c - .9f : c + .9f;
            string id = no.ToString();
            Door(fd, "Door_" + id, new Vector3(side * .7f, y0, dz), id + "호 들어가기", "", "room_" + id, 1.2f);
            DoorPanel(fw, side, dz, y0, id);
            Spawn("door_" + id, new Vector3(-side * .2f, y0 + .05f, dz), 0f);
            Spawn("room_" + id, new Vector3(side * 3f, y0 + .05f, c), side > 0 ? 90f : -90f);
            Door(fd, "RoomExit_" + id, new Vector3(side * 1.3f, y0, dz), "나가기", "", "door_" + id, .9f);
            Box(fw, "Inner Door " + id, new Vector3(side * 1.03f, y0 + 1f, dz), new Vector3(.06f, 2f, .9f), Mat("Door", new Color(.4f, .26f, .15f)), false);
            var zone = Child(fd, "Zone_" + id);
            zone.position = new Vector3(side * 2.5f, y0, c);
            var pz = zone.gameObject.AddComponent<PropertyZone>();
            pz.propertyId = "goshiwon_" + id;
            pz.displayName = "고시원 " + id + "호";
            pz.size = new Vector3(3f, 2.6f, 3f);
            if (no == 202) pz.seedOwner = "이웃";
            Window(fw, new Vector3(side * 3.98f, y0 + 1.5f, c), new Vector3(.02f, 1f, 1.2f), true, side);
            Fluorescent(fw, new Vector3(side * 2.5f, y0 + H - .03f, c), 1.5f, 4f);
        }

        static void DoorPanel(Transform fw, int side, float z, float y0, string label)
        {
            Box(fw, "Door " + label, new Vector3(side * .77f, y0 + 1f, z), new Vector3(.06f, 2f, .9f), Mat("Door", new Color(.4f, .26f, .15f)), false);
            if (label == "Exit") { Label(fw, "출구", new Vector3(side * .73f, y0 + 2.2f, z), side * 90f, 2.5f); return; }
            Box(fw, "Plate " + label, new Vector3(side * .78f, y0 + 2.2f, z), new Vector3(.04f, .22f, .5f), Mat("DarkWood", Color.black), false);
            Label(fw, label, new Vector3(side * .745f, y0 + 2.2f, z), side * 90f, 3f);
        }

        static void Window(Transform fw, Vector3 p, Vector3 size, bool alongX, int side)
        {
            Box(fw, "Window Frame", p + (alongX ? new Vector3(side * .01f, 0, 0) : new Vector3(0, 0, .01f)), size + new Vector3(alongX ? 0 : .12f, .12f, alongX ? .12f : 0), Mat("DarkWood", Color.black), false);
            Box(fw, "Window Glass", p + (alongX ? new Vector3(-side * .01f, 0, 0) : new Vector3(0, 0, -.01f)), size, Mat("NightGlass", new Color(.05f, .09f, .2f), 1f), false);
        }

        static void Fluorescent(Transform fw, Vector3 p, float intensity, float range)
        {
            Box(fw, "Fluorescent Panel", p, new Vector3(.3f, .05f, 1.2f), Mat("Fluorescent", Color.white, 3f), false);
            AddLight(fw, p - new Vector3(0, .25f, 0), intensity, range);
        }

        static void AddLight(Transform parent, Vector3 p, float intensity, float range)
        {
            var go = Child(parent, "Point Light");
            go.position = p;
            var l = go.gameObject.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(.9f, 1f, .92f);
            l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        static void Label(Transform parent, string text, Vector3 p, float yaw, float size)
        {
            var go = Child(parent, "Label " + text);
            go.position = p;
            go.rotation = Quaternion.Euler(0, yaw, 0);
            var t = go.gameObject.AddComponent<TextMeshPro>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = new Color(.95f, .85f, .5f);
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = new Vector2(1f, .3f);
        }

        static void Door(Transform parent, string name, Vector3 p, string prompt, string path, string spawnId, float radius)
        {
            var go = Child(parent, name);
            go.position = p;
            var d = go.gameObject.AddComponent<StoreInteractionPoint>();
            d.prompt = prompt; d.targetScenePath = path; d.targetSpawnId = spawnId; d.radius = radius;
        }

        static void Spawn(string id, Vector3 p, float yaw)
        {
            var go = Child(spawns, "Spawn_" + id);
            go.position = p;
            go.rotation = Quaternion.Euler(0, yaw, 0);
            go.gameObject.AddComponent<MapSpawnPoint>().spawnId = id;
        }

        static void BuildCamera(Transform parent)
        {
            var go = Child(parent, "GoshiwonPlayerCamera");
            go.tag = "MainCamera";
            go.position = new Vector3(0, 1.6f, 9.4f);
            go.rotation = Quaternion.Euler(0, 180, 0);
            var cam = go.gameObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.03f, .03f, .04f);
            var pc = go.gameObject.AddComponent<PlayerCameraController>(); // same first-person setup as ConvenienceStoreInterior
            pc.firstPerson = true; pc.initialPitch = 0f; pc.firstPersonEyeHeight = 1.6f;
            pc.firstPersonFOV = 75f; pc.thirdPersonFOV = 50f; pc.distance = 1.4f; pc.targetHeight = 1.4f;
            pc.enableObstacleFade = false;
        }

        static void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.3f, .3f, .28f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
            => Primitive(parent, PrimitiveType.Cube, name, pos, size, mat, collider);

        static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 size, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Material Mat(string name, Color color, float emission = 0, Texture2D tex = null)
        {
            string path = ArtRoot + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            m.SetFloat("_Smoothness", .1f);
            if (tex) { m.mainTexture = tex; m.mainTextureScale = new Vector2(8, 2); }
            if (emission > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", (name == "NightGlass" ? new Color(.1f, .2f, .5f) : color) * emission);
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D Wallpaper()
        {
            string path = ArtRoot + "/Wallpaper.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(64, 64);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        bool stripe = x % 16 < 3;
                        bool diamond = Mathf.Abs(x % 16 - 8) + Mathf.Abs(y % 16 - 8) < 3;
                        float v = stripe ? .8f : diamond ? .7f : 1f;
                        t.SetPixel(x, y, new Color(v, v, v));
                    }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
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
