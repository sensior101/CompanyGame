using System;
using System.Collections.Generic;
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
    /// <summary>Builds Assets/Scenes/Interiors/GoshiwonInterior.unity (4 stacked floors, 20 empty rooms) from prefabs it
    /// regenerates in Art/Goshiwon/Prefabs: GoshiwonRoom (one empty room), GoshiwonFloor (a guest floor numbered as 2F; the
    /// 3F and 4F instances are renumbered), GoshiwonGroundFloor (lobby, exit, rooms 101-102), GoshiwonStairs (one storey of
    /// the stair), GoshiwonShell (slabs and stair shaft) and GoshiwonOwner. Their box meshes are sub-assets of
    /// Art/Goshiwon/Meshes/GoshiwonBoxes.asset. Room doors, zones and spawns stay in the scene.
    /// Per floor (y0 = (floor-1)*3, ceiling 2.5): corridor x -0.8..0.8, z 1.2..11.1 (window at the north end), 3x3 m rooms
    /// on both sides (right = +x), stair hall z -0.2..1.2. A walkable U-stair fills the shaft z -3.8..-0.2: from each hall the
    /// right half climbs to a mid landing (y0+1.5) and the left half comes up from the floor below. Room doors teleport.
    /// Look: papered corridor with wood wainscot and carpet; plain cream rooms with plank floors, white trim and curtains.</summary>
    public static class GoshiwonInteriorBuilder
    {
        public const string ScenePath = "Assets/Scenes/Interiors/GoshiwonInterior.unity";
        public const string VillagePath = "Assets/Scenes/daldongnaemap.unity";
        const string ArtRoot = "Assets/Art/Goshiwon";
        const string PrefabRoot = ArtRoot + "/Prefabs";
        const string MeshPath = ArtRoot + "/Meshes/GoshiwonBoxes.asset";
        const string FontPath = "Assets/Art/font/NotoSansKR-Regular SDF.asset";
        const string OwnerVisualPath = "Assets/Art/Daldongne/Players/FemaleVisual.prefab";
        const int Floors = 4, Risers = 9;
        // Risers stay under the player's 0.23 m step offset and the 29 degree walk ramp under its 45 degree slope limit.
        const float FloorGap = 3f, H = 2.5f, RailH = .9f, Rise = FloorGap / 2 / Risers, Tread = .3f, FlightW = 1.25f;
        const float HallZ = -.2f, MidZ = -2.6f, ShaftZ = -3.8f, CorridorZ = 1.2f, EndZ = 11.1f;
        static readonly float[] RoomZ = { 3.0f, 6.2f, 9.4f };
        static readonly Color Gold = new Color(.95f, .85f, .5f);
        static readonly Color TubeLight = new Color(.92f, 1f, .94f), WarmLight = new Color(1f, .9f, .78f);
        static readonly Vector2[] BerryOffsets = { new Vector2(6, -3), new Vector2(10, 1), new Vector2(6, 4) };
        static readonly Dictionary<Vector3, Mesh> boxes = new Dictionary<Vector3, Mesh>();
        static readonly Dictionary<string, Mesh> storedBoxes = new Dictionary<string, Mesh>();
        static Transform doors, spawns;
        static GameObject roomPrefab;
        static TMP_FontAsset font;
        static Material wallpaper, wainscot, trim, carpet, ceiling, roomWall, roomFloor, roomTrim, crown, doorWood, doorFrame,
            brass, plate, terrazzo, nosing, stairWall, tube, housing, lamp, cityView, frosted, curtain, chrome, apron, badge;

        [MenuItem("CompanyGame/Setup/Build Goshiwon Interior")]
        static void BuildMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        public static void Build()
        {
            EnsureFolder(ArtRoot + "/Materials");
            EnsureFolder(ArtRoot + "/Textures");
            EnsureFolder(ArtRoot + "/Meshes");
            EnsureFolder(PrefabRoot);
            EnsureFolder("Assets/Scenes/Interiors");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            LoadBoxes();
            CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Each prefab is assembled in the empty scene, saved and removed. Rooms nest in both floor prefabs.
            roomPrefab = SavePrefab(BuildRoom());
            var shell = SavePrefab(BuildShell());
            var groundFloor = SavePrefab(BuildFloor(1));
            var guestFloor = SavePrefab(BuildFloor(2));
            var stairs = SavePrefab(BuildStairs());
            var owner = SavePrefab(BuildOwner());
            RemoveUnusedBoxes();

            var root = new GameObject("Map_GoshiwonInterior").transform;
            new GameObject("00_Systems").transform.SetParent(root, false);
            var world = Child(root, "10_World");
            var gameplay = Child(root, "20_Gameplay");
            var npcs = Child(gameplay, "NPCs");
            doors = Child(gameplay, "Doors");
            spawns = Child(gameplay, "SpawnPoints");
            var presentation = Child(root, "30_Presentation");
            Place(shell, world, "Shell", Vector3.zero, 0f);
            for (int n = 1; n <= Floors; n++)
            {
                float y0 = (n - 1) * FloorGap;
                var floor = Place(n == 1 ? groundFloor : guestFloor, world, "Floor_" + n, new Vector3(0, y0, 0), 0f);
                if (n > 2) FloorTexts(floor, n);
                FloorGameplay(n, y0);
            }
            for (int n = 1; n < Floors; n++)
                Place(stairs, world, "Stairs " + n + "F-" + (n + 1) + "F", new Vector3(0, (n - 1) * FloorGap, 0), 0f);
            Place(owner, npcs, "Goshiwon Owner", new Vector3(-3.1f, 0, 5.7f), 90f);
            BuildCamera(Child(presentation, "Cameras"));
            SetupLighting();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Goshiwon] Interior built: " + ScenePath);
        }

        static Transform BuildShell()
        {
            // Each slab is the floor above and, from below, the ceiling. The stair shaft runs the full height.
            var shell = new GameObject("GoshiwonShell").transform;
            float top = Floors * FloorGap;
            for (int level = 0; level <= Floors; level++)
                Span(shell, "Slab " + level, -4.2f, 4.2f, level * FloorGap - .5f, level * FloorGap, HallZ, 11.3f, ceiling, true);
            Span(shell, "Shaft Ground", -1.5f, 1.5f, -.5f, 0, ShaftZ - .2f, HallZ, ceiling, true);
            Span(shell, "Shaft Roof", -1.5f, 1.5f, top - .5f, top, ShaftZ - .2f, HallZ, ceiling, true);
            foreach (int s in new[] { -1, 1 })
                Span(shell, "Shaft Wall", s * 1.3f, s * 1.5f, -.5f, top, ShaftZ - .2f, HallZ, stairWall, true);
            Span(shell, "Shaft Wall South", -1.5f, 1.5f, -.5f, top, ShaftZ - .2f, ShaftZ, stairWall, true);
            Span(shell, "Center Wall", -.05f, .05f, 0, top - .5f, MidZ, HallZ, stairWall, true);
            // Floor 1: closed cupboard under the first return flight. Top floor: rail along the open right half.
            DressedWall(shell, "Under-stair Wall", -1.3f, -.05f, HallZ - .1f, HallZ, Vector3.forward);
            float yTop = (Floors - 1) * FloorGap;
            Span(shell, "Top Balustrade", .05f, 1.3f, yTop, yTop + .95f, HallZ, HallZ + .08f, wainscot, true);
            Span(shell, "Top Balustrade Cap", .05f, 1.3f, yTop + .95f, yTop + 1.01f, HallZ - .015f, HallZ + .095f, trim, false);
            return shell;
        }

        /// <summary>A floor at y 0: n = 1 is the ground floor, any other n the guest floor numbered n.</summary>
        static Transform BuildFloor(int n)
        {
            var fw = new GameObject(n == 1 ? "GoshiwonGroundFloor" : "GoshiwonFloor").transform;
            Span(fw, "Corridor Carpet", -.8f, .8f, 0, .02f, CorridorZ, EndZ, carpet, false);
            Span(fw, "Hall Carpet", -1.3f, 1.3f, 0, .02f, HallZ, CorridorZ, carpet, false);
            foreach (int s in new[] { -1, 1 })
            {
                if (n == 1 && s < 0) BuildLobby(fw);
                else
                {
                    DressedWall(fw, "Corridor Wall", s * .8f, s * .9f, CorridorZ, EndZ, new Vector3(-s, 0, 0));
                    Span(fw, "Room Side Wall", s * .9f, s * 1f, 0, H, CorridorZ + .1f, EndZ, roomWall, true);
                    foreach (float z in new[] { 1.4f, 4.6f, 7.8f, 11f })
                        Span(fw, "Divider", s * 1f, s * 4f, 0, H, z - .1f, z + .1f, roomWall, true);
                }
                Span(fw, "Outer Wall", s * 4f, s * 4.2f, 0, H, HallZ, 11.3f, roomWall, true);
                DressedWall(fw, "Hall Wall", s * 1.3f, s * 1.5f, HallZ, CorridorZ + .1f, new Vector3(-s, 0, 0));
                DressedWall(fw, "Hall Return", s * .9f, s * 1.3f, CorridorZ, CorridorZ + .1f, Vector3.back);
            }
            DressedWall(fw, "End Wall", -1f, 1f, EndZ, EndZ + .2f, Vector3.back);
            CityWindow(Anchor(fw, "End Window", new Vector3(0, 0, EndZ), 180f), .9f, 1.05f, 1.05f);
            foreach (float z in RoomZ) Fixture(fw, new Vector3(0, H, z));
            Fixture(fw, new Vector3(0, H, .55f));
            HallSigns(fw, n);
            if (n == 1)
            {
                var exit = Anchor(fw, "Exit Door", new Vector3(.8f, 0, RoomZ[2] - .9f), -90f);
                DoorVisual(exit, false);
                Span(exit, "Exit Plate", -.2f, .2f, 2.2f, 2.36f, 0, .02f, plate, false);
                Label(exit, "Exit Text", "출구", new Vector3(0, 2.28f, .025f), 180f, 1.3f);
            }
            // Left-hand rooms are the right-hand room turned around its centre.
            foreach (var (no, s, k) in Rooms(n))
                Place(roomPrefab, fw, "Room " + no, new Vector3(s * 2.5f, 0, RoomZ[k]), s > 0 ? 0f : 180f);
            FloorTexts(fw, n);
            return fw;
        }

        /// <summary>Rooms of floor n as (number, side, slot): floor 1 has 101-102 on the right (the exit takes the third
        /// slot and the lobby the left side); other floors have n01-n03 on the right and n04-n06 on the left.</summary>
        static IEnumerable<(int no, int s, int k)> Rooms(int n)
        {
            for (int k = 0; k < 3; k++)
            {
                if (n == 1 && k == 2) yield break;
                yield return (n * 100 + k + 1, 1, k);
                if (n > 1) yield return (n * 100 + k + 4, -1, k);
            }
        }

        static void HallSigns(Transform fw, int n)
        {
            // Wood header over the stair openings (up on the right half, down on the left) and a post on the centre wall end.
            Span(fw, "Stair Header", -1.3f, 1.3f, 2.2f, H, HallZ, HallZ + .1f, doorFrame, false);
            Span(fw, "Stair Post", -.08f, .08f, 0, 2.2f, HallZ - .04f, HallZ + .04f, doorFrame, false);
            var header = Anchor(fw, "Stair Signs", new Vector3(0, 0, HallZ + .1f), 0f);
            Label(header, "Up Sign", "", new Vector3(FlightW / 2 + .05f, 2.35f, .005f), 180f, 1.5f);
            if (n > 1) Label(header, "Down Sign", "", new Vector3(-FlightW / 2 - .05f, 2.35f, .005f), 180f, 1.5f);
            var plaque = Anchor(fw, "Floor Plaque", new Vector3(-1.3f, 0, .5f), 90f);
            Span(plaque, "Plate", -.3f, .3f, 1.45f, 1.85f, 0, .02f, plate, false);
            Label(plaque, "Floor Number", "", new Vector3(0, 1.65f, .025f), 180f, 2.6f);
        }

        /// <summary>Writes floor n's numbers on the stair signs, the plaque and the room doors. The 3F and 4F instances of
        /// the 2F-numbered floor prefab get the same call, which stores the new numbers as overrides.</summary>
        static void FloorTexts(Transform floor, int n)
        {
            var up = floor.Find("Stair Signs/Up Sign");
            if (n < Floors) SetText(up, "↑ " + (n + 1) + "층");
            else { up.gameObject.SetActive(false); Record(up.gameObject); }
            if (n > 1) SetText(floor.Find("Stair Signs/Down Sign"), "↓ " + (n - 1) + "층");
            SetText(floor.Find("Floor Plaque/Floor Number"), n + "층");
            foreach (Transform room in floor)
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(room.gameObject)) continue; // only rooms nest in a floor
                string no = (n * 100 + int.Parse(room.name.Substring("Room ".Length)) % 100).ToString();
                room.name = "Room " + no;
                Record(room.gameObject);
                SetText(room.Find("Corridor Door/Number"), no);
            }
        }

        static void FloorGameplay(int n, float y0)
        {
            var fd = Child(doors, "Floor_" + n);
            Spawn("floor_" + n, new Vector3(0, y0 + .05f, .6f), 0f);
            foreach (var (no, s, k) in Rooms(n)) RoomGameplay(fd, no, s, k, y0);
            if (n > 1) return;
            float dz = RoomZ[2] - .9f;
            DoorPoint(fd, "Exit Door", new Vector3(.7f, y0, dz), "나가기", VillagePath, "goshiwon_exit", 1.2f);
            Spawn("goshiwon_entry", new Vector3(-.2f, y0 + .05f, dz), 180f);
            Spawn("default", new Vector3(-.2f, y0 + .05f, dz), 180f);
        }

        static void BuildLobby(Transform fw)
        {
            // Floor 1 left: the reception lobby opens to the corridor (z 4.2..7.2) and is dressed like the corridor.
            foreach (var (z0, z1) in new[] { (CorridorZ, 4.2f), (7.2f, EndZ) })
            {
                DressedWall(fw, "Corridor Wall", -.8f, -.9f, z0, z1, Vector3.right);
                DressedWall(fw, "Lobby Wall", -.9f, -1f, Mathf.Max(z0, CorridorZ + .1f), z1, Vector3.left);
            }
            Span(fw, "Lobby Lintel", -.8f, -1f, 2.1f, H, 4.2f, 7.2f, wallpaper, true);
            Span(fw, "Lobby Casing Head", -.77f, -1.03f, 2.08f, 2.16f, 4.12f, 7.28f, doorFrame, false);
            Span(fw, "Lobby Casing", -.77f, -1.03f, 0, 2.08f, 4.12f, 4.22f, doorFrame, false);
            Span(fw, "Lobby Casing", -.77f, -1.03f, 0, 2.08f, 7.18f, 7.28f, doorFrame, false);
            foreach (float z in new[] { 1.4f, 11f }) Span(fw, "Divider", -1f, -4f, 0, H, z - .1f, z + .1f, roomWall, true);
            DressedWall(fw, "Lobby Back Wall", -3.9f, -4f, 1.5f, 10.9f, Vector3.right);
            DressedWall(fw, "Lobby South Wall", -1f, -3.9f, 1.5f, 1.6f, Vector3.forward);
            DressedWall(fw, "Lobby North Wall", -1f, -3.9f, 10.8f, 10.9f, Vector3.back);
            Span(fw, "Lobby Carpet", -.8f, -3.9f, 0, .02f, 1.6f, 10.8f, carpet, false); // runs under the opening
            Span(fw, "Front Desk", -2.5f, -1.9f, 0, 1f, 4.5f, 6.9f, doorWood, true);
            Span(fw, "Front Desk Top", -2.56f, -1.84f, 1f, 1.05f, 4.44f, 6.96f, doorFrame, false);
            var sign = Anchor(fw, "Reception Sign", new Vector3(-3.9f, 0, 5.7f), 90f);
            Span(sign, "Plate", -.35f, .35f, 2.1f, 2.34f, 0, .02f, plate, false); // above the owner's head
            Label(sign, "Reception Text", "접수", new Vector3(0, 2.22f, .025f), 180f, 2f);
            CityWindow(Anchor(fw, "Lobby Window", new Vector3(-3.9f, 0, 8.6f), 90f), 1.2f, 1f, 1.1f);
            Fixture(fw, new Vector3(-2.45f, H, 5.7f));
        }

        static Transform BuildOwner()
        {
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(OwnerVisualPath);
            if (!visual) throw new FileNotFoundException("Owner avatar missing", OwnerVisualPath);
            // ConvenienceStoreInterior's ConvenienceClerk make-up (avatar, apron and name badge on a 1.0138 root) on the
            // female avatar. Both avatars are 1.8 tall and keep their front within the apron's back face.
            var owner = new GameObject("GoshiwonOwner").transform;
            owner.localScale = Vector3.one * 1.0138f;
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(visual, owner);
            Primitive(owner, PrimitiveType.Cube, "Staff Apron", new Vector3(0, 1f, .17f), new Vector3(.4f, .47f, .045f), apron);
            Primitive(owner, PrimitiveType.Cube, "Staff Name Badge", new Vector3(-.1f, 1.16f, .2f), new Vector3(.11f, .055f, .008f), badge);
            float top = 1.75f;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, owner.InverseTransformPoint(r.bounds.max).y);
            var head = Child(owner, "SpeechAnchor");
            head.localPosition = Vector3.up * (top + .15f);
            var data = owner.gameObject.AddComponent<DialogueData>();
            data.npcId = "goshiwon_owner";
            data.displayName = "고시원 주인";
            data.line = "어서 와요. 위층 방은 복도 끝 계단으로 올라가면 돼요.";
            data.head = head;
            data.headOffset = Vector3.zero;
            data.radius = 2.5f;
            return owner;
        }

        /// <summary>One empty room around its centre at floor level with the door side toward -x (a right-hand room).
        /// The door number stays blank here; each placed room writes its own.</summary>
        static Transform BuildRoom()
        {
            var room = new GameObject("GoshiwonRoom").transform;
            Span(room, "Floor", -1.5f, 1.5f, 0, .02f, -1.5f, 1.5f, roomFloor, false);
            Span(room, "Ceiling", -1.5f, 1.5f, H - .02f, H, -1.5f, 1.5f, roomWall, false);
            Perimeter(room, "Baseboard", -1.5f, 1.5f, -1.5f, 1.5f, .02f, .1f, .015f, roomTrim);
            Perimeter(room, "Crown", -1.5f, 1.5f, -1.5f, 1.5f, H - .09f, .07f, .025f, crown);
            RoomWindow(Anchor(room, "Window", new Vector3(1.5f, 0, 0), -90f));
            Primitive(room, PrimitiveType.Cylinder, "Ceiling Lamp", new Vector3(0, H - .05f, 0), new Vector3(.38f, .03f, .38f), lamp);
            AddLight(room, new Vector3(0, 1.7f, 0), WarmLight, 1f, 4.5f);
            DoorVisual(Anchor(room, "Corridor Door", new Vector3(-1.7f, 0, -.9f), -90f), true);
            DoorVisual(Anchor(room, "Inner Door", new Vector3(-1.5f, 0, -.9f), 90f), false);
            return room;
        }

        static void RoomGameplay(Transform fd, int no, int s, int k, float y0)
        {
            float c = RoomZ[k], dz = s > 0 ? c - .9f : c + .9f;
            string id = no.ToString();
            DoorPoint(fd, "Door_" + id, new Vector3(s * .7f, y0, dz), id + "호 들어가기", "", "room_" + id, 1.2f);
            Spawn("door_" + id, new Vector3(-s * .2f, y0 + .05f, dz), 0f);
            Spawn("room_" + id, new Vector3(s * 3f, y0 + .05f, c), s > 0 ? 90f : -90f);
            DoorPoint(fd, "RoomExit_" + id, new Vector3(s * 1.3f, y0, dz), "나가기", "", "door_" + id, .9f);
            var zone = Child(fd, "Zone_" + id);
            zone.position = new Vector3(s * 2.5f, y0, c);
            var pz = zone.gameObject.AddComponent<PropertyZone>();
            pz.propertyId = "goshiwon_" + id;
            pz.displayName = "고시원 " + id + "호";
            pz.size = new Vector3(3f, 2.6f, 3f);
            if (no == 202) pz.seedOwner = "이웃";
        }

        /// <summary>One storey of the U-stair from a floor at y 0 to the next: up flight, mid landing, return flight.</summary>
        static Transform BuildStairs()
        {
            var level = new GameObject("GoshiwonStairs").transform;
            float ym = FloorGap / 2;
            Flight(level, "Flight Up", FlightW / 2 + .05f, HallZ, -1, 0);
            Flight(level, "Flight Return", -FlightW / 2 - .05f, MidZ, 1, ym);
            Span(level, "Mid Landing", -1.3f, 1.3f, ym - .2f, ym, ShaftZ, MidZ, terrazzo, true);
            Span(level, "Mid Landing Nosing", -1.3f, 1.3f, ym, ym + .008f, MidZ - .05f, MidZ, nosing, false);
            var wall = Anchor(level, "Landing Wall", new Vector3(0, ym, ShaftZ), 0f);
            CityWindow(wall, .8f, .9f, 1f);
            Block(wall, "Bulkhead Lamp", new Vector3(0, 2.25f, .04f), new Vector3(.3f, .14f, .08f), housing, false);
            Block(wall, "Bulkhead Glow", new Vector3(0, 2.25f, .083f), new Vector3(.24f, .09f, .006f), lamp, false);
            AddLight(wall, new Vector3(0, 2f, .5f), WarmLight, 1f, 4.5f);
            return level;
        }

        /// <summary>A 9-riser flight going up along z*dir: visual steps over a sloped soffit, walked on through one invisible
        /// ramp whose top runs half a riser under the nosings, so feet stay within 8 cm of the treads.</summary>
        static void Flight(Transform p, string name, float cx, float zStart, int dir, float yStart)
        {
            var f = Child(p, name);
            float x0 = cx - FlightW / 2, x1 = cx + FlightW / 2;
            for (int i = 1; i < Risers; i++)
            {
                float front = zStart + dir * (i - 1) * Tread, top = yStart + i * Rise;
                Span(f, "Step " + i, x0, x1, top - Rise - .06f, top, front, front + dir * Tread, terrazzo, false);
                Span(f, "Nosing " + i, x0, x1, top, top + .008f, front, front + dir * .05f, nosing, false);
            }
            float k = Rise / Tread, run = Risers * Tread, along = run / Mathf.Cos(Mathf.Atan(k));
            var rot = Quaternion.LookRotation(new Vector3(0, Rise, dir * Tread));
            Vector3 up = rot * Vector3.up;
            // Soffit top runs through the steps' bottom back corners and tucks under the upper landing.
            var soffit = new Vector3(cx, yStart - Rise - .06f + run / 2 * k, zStart + dir * run / 2);
            Block(f, "Soffit", soffit - up * .06f, new Vector3(FlightW, .12f, along), stairWall, false, rot);
            float sMid = (run - Tread) / 2; // ramp spans half a tread onto the lower landing to half a tread into the upper one
            var ramp = Child(f, "Walk Ramp");
            ramp.localPosition = new Vector3(cx, yStart + Rise / 2 + sMid * k, zStart + dir * sMid) - up * .1f;
            ramp.localRotation = rot;
            ramp.gameObject.AddComponent<BoxCollider>().size = new Vector3(FlightW, .2f, along);
            float sRail = 3.5f * Tread; // handrails on the shaft wall and the centre wall, 0.9 m over the nosings
            foreach (int side in new[] { -1, 1 })
                Block(f, "Handrail", new Vector3(cx + side * (FlightW / 2 - .05f), yStart + Rise + .9f + sRail * k, zStart + dir * sRail),
                    new Vector3(.05f, .05f, 7 * Tread * along / run), trim, false, rot);
        }

        /// <summary>Wood wainscot under a chair rail and wallpaper above, from the floor up; trims stand proud on the `face` side.</summary>
        static void DressedWall(Transform p, string name, float x0, float x1, float z0, float z1, Vector3 face)
        {
            Span(p, name + " Wainscot", x0, x1, 0, RailH, z0, z1, wainscot, true);
            Span(p, name + " Paper", x0, x1, RailH, H, z0, z1, wallpaper, true);
            if (face.x != 0)
            {
                float fx = face.x > 0 ? Mathf.Max(x0, x1) : Mathf.Min(x0, x1);
                Span(p, name + " Rail", fx, fx + face.x * .03f, RailH - .03f, RailH + .03f, z0, z1, trim, false);
                Span(p, name + " Base", fx, fx + face.x * .02f, 0, .12f, z0, z1, trim, false);
            }
            else
            {
                float fz = face.z > 0 ? Mathf.Max(z0, z1) : Mathf.Min(z0, z1);
                Span(p, name + " Rail", x0, x1, RailH - .03f, RailH + .03f, fz, fz + face.z * .03f, trim, false);
                Span(p, name + " Base", x0, x1, 0, .12f, fz, fz + face.z * .02f, trim, false);
            }
        }

        static void Perimeter(Transform p, string name, float x0, float x1, float z0, float z1, float y, float h, float d, Material m)
        {
            float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1);
            Span(p, name, xa, xb, y, y + h, z0, z0 + d, m, false);
            Span(p, name, xa, xb, y, y + h, z1 - d, z1, m, false);
            Span(p, name, xa, xa + d, y, y + h, z0 + d, z1 - d, m, false);
            Span(p, name, xb - d, xb, y, y + h, z0 + d, z1 - d, m, false);
        }

        // Wall fittings below are built in an anchor frame: origin on the wall face at floor level, +z out of the wall.

        static void DoorVisual(Transform a, bool numbered)
        {
            const float w = .86f, h = 2f;
            Block(a, "Door Leaf", new Vector3(0, h / 2, .02f), new Vector3(w, h, .04f), doorWood, false);
            foreach (int side in new[] { -1, 1 })
                Block(a, "Casing", new Vector3(side * (w / 2 + .04f), h / 2, .03f), new Vector3(.08f, h, .06f), doorFrame, false);
            Block(a, "Casing Head", new Vector3(0, h + .04f, .03f), new Vector3(w + .16f, .08f, .06f), doorFrame, false);
            Primitive(a, PrimitiveType.Sphere, "Knob", new Vector3(w / 2 - .1f, .95f, .07f), Vector3.one * .065f, brass);
            if (!numbered) return;
            Block(a, "Number Plate", new Vector3(0, 1.6f, .045f), new Vector3(.2f, .085f, .01f), plate, false);
            Label(a, "Number", "", new Vector3(0, 1.6f, .052f), 180f, .7f);
        }

        static void RoomWindow(Transform a)
        {
            const float w = 1f, h = 1.1f, bottom = 1f, bar = .06f;
            float top = bottom + h, cy = bottom + h / 2, rod = top + .2f;
            Block(a, "Frosted Glass", new Vector3(0, cy, .01f), new Vector3(w, h, .02f), frosted, false);
            Block(a, "Frame Top", new Vector3(0, top + bar / 2, .03f), new Vector3(w + 2 * bar, bar, .06f), roomTrim, false);
            Block(a, "Frame Bottom", new Vector3(0, bottom - bar / 2, .03f), new Vector3(w + 2 * bar, bar, .06f), roomTrim, false);
            Block(a, "Mullion", new Vector3(0, cy, .025f), new Vector3(.03f, h, .03f), roomTrim, false);
            for (int i = 1; i <= 2; i++) Block(a, "Transom", new Vector3(0, bottom + h * i / 3, .025f), new Vector3(w, .03f, .03f), roomTrim, false);
            Block(a, "Sill", new Vector3(0, bottom - bar - .02f, .07f), new Vector3(w + .3f, .04f, .14f), roomTrim, false);
            Block(a, "Curtain Rod", new Vector3(0, rod, .17f), new Vector3(w + .8f, .025f, .025f), chrome, false);
            foreach (int side in new[] { -1, 1 })
            {
                Block(a, "Frame Side", new Vector3(side * (w + bar) / 2, cy, .03f), new Vector3(bar, h, .06f), roomTrim, false);
                Primitive(a, PrimitiveType.Sphere, "Rod Finial", new Vector3(side * (w + .8f) / 2, rod, .17f), Vector3.one * .055f, chrome);
                Block(a, "Curtain", new Vector3(side * (w / 2 + .12f), (rod + .55f) / 2, .17f), new Vector3(.46f, rod - .55f, .03f), curtain, false);
            }
        }

        static void CityWindow(Transform a, float w, float h, float bottom)
        {
            const float bar = .07f;
            float cy = bottom + h / 2;
            var view = Primitive(a, PrimitiveType.Quad, "City View", new Vector3(0, cy, .012f), new Vector3(w, h, 1f), cityView);
            view.transform.localRotation = Quaternion.Euler(0, 180f, 0); // a quad faces -z; turn it out of the wall
            Block(a, "Frame Top", new Vector3(0, bottom + h + bar / 2, .03f), new Vector3(w + 2 * bar, bar, .06f), doorFrame, false);
            Block(a, "Frame Bottom", new Vector3(0, bottom - bar / 2, .03f), new Vector3(w + 2 * bar, bar, .06f), doorFrame, false);
            foreach (int side in new[] { -1, 1 })
                Block(a, "Frame Side", new Vector3(side * (w + bar) / 2, cy, .03f), new Vector3(bar, h, .06f), doorFrame, false);
            Block(a, "Mullion", new Vector3(0, cy, .022f), new Vector3(.035f, h, .02f), doorFrame, false);
            Block(a, "Sill", new Vector3(0, bottom - bar - .02f, .06f), new Vector3(w + .24f, .04f, .12f), doorFrame, false);
        }

        static void Fixture(Transform p, Vector3 ceilingPoint)
        {
            Block(p, "Fluorescent Housing", ceilingPoint + Vector3.down * .03f, new Vector3(.34f, .06f, 1.25f), housing, false);
            Block(p, "Fluorescent Diffuser", ceilingPoint + Vector3.down * .062f, new Vector3(.27f, .006f, 1.16f), tube, false);
            AddLight(p, ceilingPoint + Vector3.down * .3f, TubeLight, 1.1f, 4.5f);
        }

        static void AddLight(Transform parent, Vector3 p, Color color, float intensity, float range)
        {
            var go = Child(parent, "Point Light");
            go.localPosition = p;
            var l = go.gameObject.AddComponent<Light>();
            l.type = LightType.Point; l.color = color;
            l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        static void Label(Transform parent, string name, string text, Vector3 p, float yaw, float size)
        {
            var go = Child(parent, name);
            go.localPosition = p;
            go.localRotation = Quaternion.Euler(0, yaw, 0);
            var t = go.gameObject.AddComponent<TextMeshPro>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = Gold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(1f, .3f);
        }

        static void SetText(Transform label, string text)
        {
            var t = label.GetComponent<TextMeshPro>();
            t.text = text;
            Record(t);
        }

        static void DoorPoint(Transform parent, string name, Vector3 p, string prompt, string path, string spawnId, float radius)
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
            RenderSettings.ambientLight = new Color(.27f, .26f, .25f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        // ---- prefabs ----

        static GameObject SavePrefab(Transform root)
        {
            string path = PrefabRoot + "/" + root.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, path, out bool saved);
            Object.DestroyImmediate(root.gameObject);
            if (!saved) throw new IOException("Could not save " + path);
            return prefab;
        }

        static Transform Place(GameObject prefab, Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).transform;
            t.name = name;
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0, yaw, 0);
            Record(t.gameObject);
            Record(t);
            return t;
        }

        /// <summary>Keeps a scripted edit on a placed prefab as an override (plain objects need nothing).</summary>
        static void Record(Object o)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        // ---- geometry ----

        static GameObject Span(Transform p, string name, float x0, float x1, float y0, float y1, float z0, float z1, Material m, bool collider)
            => Block(p, name, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), m, collider);

        /// <summary>Box with UVs in metres, so tiled textures keep one scale on every wall size.</summary>
        static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        static void LoadBoxes()
        {
            boxes.Clear();
            storedBoxes.Clear();
            if (!AssetDatabase.LoadMainAssetAtPath(MeshPath))
                AssetDatabase.CreateAsset(new Mesh { name = "GoshiwonBoxes" }, MeshPath); // empty main object; the boxes are sub-assets
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(MeshPath))
                if (o is Mesh m) storedBoxes[m.name] = m;
        }

        static void RemoveUnusedBoxes()
        {
            var used = new HashSet<Mesh>(boxes.Values);
            foreach (var m in storedBoxes.Values)
                if (!used.Contains(m)) { AssetDatabase.RemoveObjectFromAsset(m); Object.DestroyImmediate(m, true); }
        }

        static Mesh BoxMesh(Vector3 s)
        {
            s = new Vector3(Mathf.Round(s.x * 1000) / 1000, Mathf.Round(s.y * 1000) / 1000, Mathf.Round(s.z * 1000) / 1000);
            if (boxes.TryGetValue(s, out var mesh)) return mesh;
            var v = new List<Vector3>(24); var n = new List<Vector3>(24); var uv = new List<Vector2>(24); var t = new List<int>(36);
            // u x w = -normal keeps every face front-facing and its texture unmirrored.
            void Face(Vector3 normal, Vector3 u, Vector3 w, float su, float sw)
            {
                int i = v.Count;
                Vector3 c = Vector3.Scale(normal, s) * .5f, du = u * su * .5f, dw = w * sw * .5f;
                v.Add(c - du - dw); v.Add(c + du - dw); v.Add(c + du + dw); v.Add(c - du + dw);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(su, 0)); uv.Add(new Vector2(su, sw)); uv.Add(new Vector2(0, sw));
                for (int k = 0; k < 4; k++) n.Add(normal);
                t.Add(i); t.Add(i + 3); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }
            Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y);
            Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y);
            Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z);
            Face(Vector3.down, Vector3.right, Vector3.back, s.x, s.z);
            // Refills the stored mesh of this size in place, so prefabs keep their mesh references across rebuilds.
            string name = FormattableString.Invariant($"Box {s.x:0.###}x{s.y:0.###}x{s.z:0.###}");
            if (!storedBoxes.TryGetValue(name, out mesh))
            {
                mesh = new Mesh { name = name };
                AssetDatabase.AddObjectToAsset(mesh, MeshPath);
            }
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            boxes[s] = mesh;
            return mesh;
        }

        static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = Child(parent, name);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0, yaw, 0);
            return t;
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ---- materials and generated textures ----

        static void CreateMaterials()
        {
            wallpaper = Mat("Wallpaper", Color.white, .05f, Tex("Wallpaper", 256, 256, Vines), .64f);
            wainscot = Mat("Wainscot", Color.white, .3f, Tex("Wainscot", 128, 128, Boards), .64f);
            trim = Mat("DarkWood", new Color(.2f, .12f, .07f), .35f);
            carpet = Mat("Carpet", Color.white, 0f, Tex("Carpet", 128, 128, CarpetWeave), .48f);
            ceiling = Mat("Ceiling", Color.white, 0f, Tex("Ceiling", 128, 128, Popcorn), .5f);
            roomWall = Mat("RoomWall", new Color(.95f, .92f, .86f), .1f);
            roomFloor = Mat("RoomFloor", Color.white, .25f, Tex("RoomFloor", 256, 256, Planks), 1.2f);
            roomTrim = Mat("RoomTrim", new Color(.97f, .96f, .93f), .3f);
            crown = Mat("CrownWood", new Color(.66f, .55f, .43f), .25f);
            var grain = Tex("DoorWood", 128, 128, DoorGrain);
            doorWood = Mat("Door", Color.white, .3f, grain, .5f);
            doorFrame = Mat("DoorFrame", new Color(.7f, .66f, .62f), .3f, grain, .5f);
            brass = Mat("Brass", new Color(.82f, .64f, .3f), .55f, metallic: .5f);
            plate = Mat("Plate", new Color(.12f, .09f, .07f), .3f);
            terrazzo = Mat("Terrazzo", Color.white, .4f, Tex("Terrazzo", 128, 128, Chips), .4f);
            nosing = Mat("Nosing", new Color(.16f, .14f, .13f), .2f);
            stairWall = Mat("StairWall", new Color(.85f, .81f, .72f), .1f);
            tube = Mat("Fluorescent", Color.white, .5f, emission: new Color(1f, 1f, .97f) * 1.3f);
            housing = Mat("FixtureHousing", new Color(.88f, .88f, .86f), .4f);
            lamp = Mat("WarmLamp", new Color(1f, .95f, .85f), .3f, emission: new Color(1f, .88f, .7f) * 1.6f);
            cityView = Mat("CityNight", Color.black, .6f, emission: Color.white);
            cityView.SetTexture("_EmissionMap", Tex("CityNight", 256, 256, City, TextureWrapMode.Clamp));
            frosted = Mat("FrostedGlass", new Color(.25f, .25f, .25f), .7f, emission: new Color(.86f, .88f, .9f));
            curtain = Mat("Curtain", Color.white, 0f, Tex("Curtain", 64, 8, Folds), .16f);
            chrome = Mat("Chrome", new Color(.78f, .78f, .76f), .6f, metallic: .6f);
            apron = Mat("OwnerApron", new Color(.48f, .2f, .22f), .15f);
            badge = Mat("Badge", new Color(.95f, .93f, .86f), .3f);
        }

        static Material Mat(string name, Color color, float smoothness, Texture2D tex = null, float tile = 1f, Color emission = default, float metallic = 0f)
        {
            string path = ArtRoot + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = !m;
            if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.color = color;
            m.mainTexture = tex;
            m.mainTextureScale = Vector2.one / tile;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.maxColorComponent > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Writes a seeded procedural texture (same bytes every build) and imports it.</summary>
        static Texture2D Tex(string name, int w, int h, Func<int, int, Color> pixel, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            string path = ArtRoot + "/Textures/" + name + ".png";
            var colors = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = pixel(x, y);
                    c.a = 1f;
                    colors[y * w + x] = c;
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels(colors);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Trailing teal vines with round leaves and berries on khaki paper, like the reference corridor (256 px = 0.64 m).
        static Color Vines(int x, int y)
        {
            Color c = new Color(.76f, .71f, .56f) * (.96f + .05f * Noise(x / 2f, y / 32f, 128, 8, 1));
            float leaf = 0, tone = 0, berry = 0;
            for (int v = 0; v < 2; v++)
                for (int oy = -256; oy <= 256; oy += 256)
                {
                    float x0 = 64 + 128 * v, px = x + .5f, py = y + .5f - oy - v * 128; // second vine dropped half a tile
                    leaf = Mathf.Max(leaf, Mathf.Clamp01(1.6f - Mathf.Abs(px - VineX(x0, py))) * .9f);
                    for (int j = 0; j < 8; j++)
                    {
                        float ny = j * 32 + 16, nx = VineX(x0, ny);
                        if (Mathf.Abs(py - ny) > 30 || Mathf.Abs(px - nx) > 30) continue;
                        int side = j % 2 == 0 ? 1 : -1;
                        float l = Leaf(px, py, nx + side * 9.8f, ny + 6.9f, 6.5f, 12.5f, -side * 55f);
                        if (l > leaf) { leaf = l; tone = j % 4 < 2 ? 0 : 1; }
                        if (side > 0) continue;
                        foreach (var o in BerryOffsets) berry = Mathf.Max(berry, Mathf.Clamp01(2.6f - Vector2.Distance(new Vector2(px, py), new Vector2(nx + o.x, ny + o.y))));
                    }
                }
            c = Color.Lerp(c, Color.Lerp(new Color(.34f, .48f, .45f), new Color(.43f, .57f, .51f), tone), leaf * .9f);
            return Color.Lerp(c, new Color(.29f, .41f, .39f), berry * .85f);
        }

        static float VineX(float x0, float y) => x0 + 10 * Mathf.Sin(y / 256f * 2 * Mathf.PI);

        /// <summary>Coverage of an ellipse leaf (semi-axes a across, b along) turned by deg, with a pale midrib.</summary>
        static float Leaf(float x, float y, float cx, float cy, float a, float b, float deg)
        {
            float r = deg * Mathf.Deg2Rad, dx = x - cx, dy = y - cy;
            float lx = dx * Mathf.Cos(r) + dy * Mathf.Sin(r), ly = -dx * Mathf.Sin(r) + dy * Mathf.Cos(r);
            float d = Mathf.Sqrt(lx * lx / (a * a) + ly * ly / (b * b));
            float fill = Mathf.Clamp01((1f - d) * Mathf.Min(a, b) * .9f + .5f);
            return Mathf.Abs(lx) < .8f && d < .85f ? fill * .55f : fill;
        }

        static Color Boards(int x, int y)
        {
            Color c = new Color(.33f, .2f, .12f) * (.88f + .24f * Hash(x / 32, 0, 7)) * (.85f + .3f * Noise(x / 2f, y / 16f, 64, 8, 3));
            int bx = x % 32;
            return bx < 2 ? c * .45f : bx < 4 ? c * 1.12f : c; // V-groove between 0.16 m boards
        }

        static Color CarpetWeave(int x, int y)
        {
            bool across = (x / 64 + y / 64) % 2 == 0;
            float stripe = ((across ? x : y) % 64) / 8 % 2;
            Color c = Color.Lerp(new Color(.4f, .25f, .15f), new Color(.5f, .33f, .2f), stripe) * (.88f + .2f * Hash(x, y, 11));
            return x % 64 < 2 || y % 64 < 2 ? c * .72f : c;
        }

        static Color Popcorn(int x, int y) => new Color(.84f, .82f, .77f) * (.88f + .1f * Noise(x / 4f, y / 4f, 32, 32, 21) + .06f * Hash(x, y, 22));

        static Color Planks(int x, int y)
        {
            int row = y / 32, along = (x + row * 53 % 128) % 256; // 0.15 m boards, staggered 0.6 m lengths
            Color c = new Color(.77f, .72f, .65f) * (.9f + .16f * Hash(row * 2 + along / 128, 0, 31)) * (.9f + .16f * Noise(x / 16f, y / 2f, 16, 128, 32));
            return y % 32 == 0 || along % 128 == 0 ? c * .7f : c;
        }

        static Color DoorGrain(int x, int y) => new Color(.4f, .25f, .15f) * (.85f + .3f * Noise(x / 2f, y / 16f, 64, 8, 61));

        static Color Chips(int x, int y)
        {
            float h = Hash(x / 3, y / 3, 42);
            if (h > .94f) return new Color(.33f, .32f, .31f);
            if (h > .9f) return new Color(.56f, .47f, .4f);
            if (h < .05f) return new Color(.92f, .91f, .88f);
            return new Color(.7f, .69f, .66f) * (.94f + .1f * Noise(x / 8f, y / 8f, 16, 16, 41));
        }

        static Color Folds(int x, int y) => new Color(.98f, .96f, .91f) * (.9f + .1f * Mathf.Cos(x / 64f * 2 * Mathf.PI));

        // Night skyline with lit windows for the corridor and stair windows (emission only).
        static Color City(int x, int y)
        {
            int col = x / 18, wx = x % 18, wy = y % 10;
            float near = 60 + 110 * Hash(col, 0, 51), far = 95 + 60 * Hash(x / 11, 1, 52);
            if (y < near)
            {
                bool window = wy >= 3 && wy < 7 && (wx >= 3 && wx < 6 || wx >= 10 && wx < 13);
                if (!window) return new Color(.05f, .06f, .09f);
                int id = col * 2 + (wx >= 10 ? 1 : 0);
                return Hash(id, y / 10, 53) > .6f ? Color.Lerp(new Color(1f, .74f, .38f), new Color(.95f, .9f, .72f), Hash(id, y / 10, 54)) : new Color(.09f, .11f, .17f);
            }
            if (y < far) return new Color(.08f, .08f, .14f);
            if (Hash(x, y, 55) > .996f) return new Color(.75f, .75f, .85f);
            return Color.Lerp(new Color(.2f, .14f, .26f), new Color(.03f, .05f, .12f), (y - 90) / 165f);
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffu) / 65535f;
            }
        }

        /// <summary>Value noise that repeats every px by py cells, so textures tile.</summary>
        static float Noise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx *= fx * (3 - 2 * fx); fy *= fy * (3 - 2 * fy);
            int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
            return Mathf.Lerp(Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx), Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx), fy);
        }

        static int Mod(int v, int m) => (v % m + m) % m;

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
