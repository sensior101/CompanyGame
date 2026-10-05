using System;
using System.IO;
using CompanyGame.World.Maps;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds the playable Handae HQ lobby and wires it to the existing district map.</summary>
public static class HandaeLobbyBuilder
{
    public const string ScenePath = "Assets/Scenes/Interiors/HandaeHQLobby.unity";
    const string DistrictPath = "Assets/Scenes/Maps/BusinessDistrict.unity";
    const string FutureSecondFloorPath = "Assets/Scenes/Interiors/HandaeHQFloor2.unity";
    const string Art = "Assets/Art/WorldDistricts/HandaeHQ/Lobby";
    const string FontPath = "Assets/Art/font/NotoSansKR-Regular SDF.asset";
    static Transform root, architecture, furniture, details, gameplay, lighting;
    static Material marble, darkMarble, wallStone, charcoal, bronze, glass, accentGlass, warm, fabric, leather, matte, led, mirror;
    static TMP_FontAsset font;
    public const float PlanScale = .02835f;
    public const float StaffLandingExtension = 3f;
    static float ReceptionCenterZ => P(730,413).z + StaffLandingExtension;
    static float ReceptionShiftZ => ReceptionCenterZ - 1.407f;

    [MenuItem("CompanyGame/Setup/Build Handae HQ Lobby")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before building the lobby.");
        var current = SceneManager.GetActiveScene();
        if (current.isDirty) throw new InvalidOperationException("Save the current scene before building the lobby.");
        EnsureFolder("Assets/Scenes/Interiors");
        EnsureFolder(Art + "/Materials");
        EnsureFolder(Art + "/Meshes");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        CreateMaterials();
        HandaeLobbyModels.EnsureReady();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("Map_HandaeHQLobby").transform;
        architecture = Child(root, "10_Architecture");
        furniture = Child(root, "20_Furniture");
        details = Child(root, "30_Details");
        gameplay = Child(root, "40_Gameplay");
        lighting = Child(root, "50_Lighting");
        BuildShell();
        BuildReception();
        BuildLounge();
        BuildRestroom();
        BuildGates();
        BuildElevatorAndStairs();
        BuildGameplay();
        BuildLighting();
        HandaeLobbyWorldSetup.Configure(root);
        HandaeLobbyPrefabOrganizer.Organize(root);
        HandaeLobbyNPCBuilder.Place(root);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save lobby scene.");
        AddToBuild();
        LinkDistrict();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Handae] Playable HQ lobby built and linked: " + ScenePath);
    }

    // Coordinates are traced from the supplied 1448 x 1086 reference image.
    // Retain the supplied plan's ordering, enlarge it by 35%, and add 3m behind the gates.
    public static Vector3 PlanPoint(float px,float py,float height=0)
        => V((px-730f)*PlanScale,height,(480f-py)*PlanScale + StaffLandingExtension*Mathf.Clamp01((385f-py)/55f));
    static Vector3 P(float px,float py,float height=0) => PlanPoint(px,py,height);
    static GameObject PlanBox(Transform parent,string name,float x0,float y0,float x1,float y1,float bottom,float height,Material material,bool collision=false)
    {
        var north=P(x0,y0); var south=P(x1,y1);
        return Box(parent,name,V((north.x+south.x)*.5f,bottom+height*.5f,(north.z+south.z)*.5f),
            V(Mathf.Abs(south.x-north.x),height,Mathf.Abs(north.z-south.z)),material,collision);
    }
    static void WallX(string name,float px,float py0,float py1,float height=11.3f)
        => PlanBox(architecture,name,px-5,py0,px+5,py1,0,height,wallStone,true);
    static void WallZ(string name,float py,float px0,float px1,float height=11.3f)
        => PlanBox(architecture,name,px0,py-5,px1,py+5,0,height,wallStone,true);

    static void BuildShell()
    {
        PlanBox(architecture,"Polished marble floor",70,25,1390,890,-.26f,.26f,marble,true);
        PlanBox(architecture,"Triple height ceiling",70,25,1390,890,11.3f,.24f,charcoal,true);
        WallZ("North marble boundary",75,70,1204);
        WallZ("Stair north boundary",30,1204,1390);
        WallX("North stair return",1204,30,75);
        WallX("West outer boundary",70,75,890);
        WallX("East outer boundary",1390,30,890);
        for(int px=75;px<=1385;px+=105)
            PlanBox(details,"Fine marble joint X",px,78,px+.55f,885,.004f,.004f,wallStone);
        for(int py=90;py<885;py+=105)
            PlanBox(details,"Fine marble joint Z",75,py,1385,py+.55f,.004f,.004f,wallStone);

        // A single facade door opens directly to the lobby. The arrival threshold is outside it.
        PlanBox(architecture,"Front west fixed glass",75,882,610,888,0,4.2f,glass,true);
        PlanBox(architecture,"Front east fixed glass",850,882,1385,888,0,4.2f,glass,true);
        PlanBox(architecture,"Front upper transom",75,882,1385,888,4.2f,7.1f,glass,true);
        foreach(float px in new[]{75f,290f,440f,610f,850f,1010f,1385f})
            PlanBox(details,"Facade dark mullion",px-3,879,px+3,891,0,11.3f,charcoal);
        foreach(float px in new[]{475f,985f})
            PlanBox(architecture,"Entrance marble pier",px-27,870,px+27,910,0,11.3f,wallStone,true);
        PlanBox(details,"Entrance bronze transom",602,878,858,890,3.32f,.12f,bronze);
        var door=Child(gameplay,"Main entrance automatic sliding doors"); door.localPosition=P(730,885);
        float halfDoor=60f*PlanScale;
        var left=Box(door,"Left sliding glass leaf",V(-halfDoor,1.62f,0),V(halfDoor*2f-.03f,3.2f,.085f),accentGlass,true);
        var right=Box(door,"Right sliding glass leaf",V(halfDoor,1.62f,0),V(halfDoor*2f-.03f,3.2f,.085f),accentGlass,true);
        Box(left.transform,"Left handle",V(.44f,0,-.7f),V(.025f,.26f,.08f),bronze);
        Box(right.transform,"Right handle",V(-.44f,0,-.7f),V(.025f,.26f,.08f),bronze);
        var automatic=door.gameObject.AddComponent<LobbyAutomaticDoor>();
        automatic.leftLeaf=left.transform; automatic.rightLeaf=right.transform;
        automatic.openRadius=2.4f; automatic.closeRadius=4.4f; automatic.slideDistance=halfDoor*2f; automatic.speed=4.6f;
        PlanBox(details,"Entrance interior mat",610,825,850,878,.005f,.018f,matte);
        PlanBox(architecture,"Exterior arrival threshold",468,885,992,1075,-.25f,.25f,wallStone,true);
        // Enclose the arrival vestibule up to the lobby ceiling; keep the automatic door side open.
        Box(architecture,"Exterior threshold west bound",P(468,980,5.65f),V(.3f,11.3f,190f*PlanScale+.3f),charcoal,true);
        Box(architecture,"Exterior threshold east bound",P(992,980,5.65f),V(.3f,11.3f,190f*PlanScale+.3f),charcoal,true);
        Box(architecture,"Exterior threshold south bound",P(730,1075,5.65f),V(524f*PlanScale+.3f,11.3f,.3f),charcoal,true);

        // Only the two boundaries shown around the secured upper-right zone.
        WallX("Staff zone west partition",974,75,401);
        PlanBox(details,"Staff partition dark edge",968,75,979,401,0,3.2f,charcoal);
        for(int py=450;py<=830;py+=95)
        {
            PlanBox(details,"East wall dark vertical panel",1377,py-9,1384,py+9,0,11.1f,charcoal);
            PlanBox(details,"East warm vertical reveal",1375,py-11,1377,py-9,0,10.8f,warm);
        }
        for(int px=340;px<=1300;px+=160)
            PlanBox(details,"Recessed ceiling light line",px,90,px+1.2f,850,11.26f,.025f,warm);
    }

    static void BuildReception()
    {
        // The tall wall's visible base is at image y=340; the area behind is solid core, not an extra room.
        PlanBox(architecture,"Central structural logo core",480,75,970,340,0,11.3f,wallStone,true);
        float north=P(730,341).z;
        Box(architecture,"Logo wall dark surround",V(0,5.65f,north),V(5.65f,11.05f,.2f),charcoal);
        Box(architecture,"Logo wall central marble",V(0,5.65f,north-.16f),V(5.22f,10.85f,.16f),marble);
        foreach(float side in new[]{-1f,1f})
        {
            Box(architecture,"Logo wall side marble",V(side*4.05f,5.65f,north),V(1.15f,11.15f,.4f),wallStone);
            for(int i=0;i<13;i++)
                Box(details,"Dark vertical timber flute",V(side*(2.95f+i*.055f),5.65f,north-.18f),V(.035f,10.9f,.16f),charcoal);
            Box(details,"Logo warm vertical reveal",V(side*2.72f,5.65f,north-.26f),V(.028f,10.8f,.07f),warm);
        }
        for(int i=0;i<3;i++)
        {
            float h=1.05f+i*.65f;
            Box(details,"Handae ascending logo bar "+(i+1),V(-.85f+i*.75f,6.45f+h*.5f,north-.31f),V(.52f,h,.14f),charcoal);
        }
        Text(details,"한대건설",V(0,5.65f,north-.3f),V(5.1f,.8f,1),.68f,new Color(.10f,.11f,.12f),TextAlignmentOptions.Center);
        Text(details,"HANDAE CONSTRUCTION",V(0,5.06f,north-.3f),V(5.1f,.35f,1),.19f,new Color(.22f,.22f,.21f),TextAlignmentOptions.Center);

        // Open-backed half annulus, traced to x=558..902, y=413..567 in the reference.
        float centerZ=ReceptionCenterZ;
        MeshObject(furniture,"U-shaped reception marble body",DeskRing("ReceptionReferenceBody",3.62f,3.16f,2.98f,2.52f,centerZ,.12f,1.12f),darkMarble,true);
        MeshObject(furniture,"U-shaped reception stone counter",DeskRing("ReceptionReferenceCounter",3.73f,3.25f,2.88f,2.42f,centerZ,1.12f,1.24f),marble,false);
        MeshObject(details,"Reception curved warm toe light",DeskRing("ReceptionReferenceLight",3.625f,3.165f,3.57f,3.11f,centerZ,.11f,.15f),warm,false);
        Box(furniture,"Reception internal worktop",V(0,.77f,-.25f+ReceptionShiftZ),V(4.4f,.07f,.55f),charcoal);
        for(int i=0;i<2;i++)
        {
            float x=i==0?-1.23f:1.23f;
            Box(furniture,"Reception terminal",V(x,1.04f,-.32f+ReceptionShiftZ),V(.62f,.38f,.05f),charcoal);
            Box(furniture,"Terminal stand",V(x,.85f,-.32f+ReceptionShiftZ),V(.045f,.2f,.1f),bronze);
            Armchair(x,.6f+ReceptionShiftZ,0, .76f);
        }
        foreach(float side in new[]{-1f,1f})
        {
            TableLamp(details,V(side*3.36f,1.24f,centerZ-.04f),.58f);
            Planter(details,P(side<0?558:902,366),.73f);
        }
        AddLight(V(0,8.5f,north-1.8f),new Color(1f,.94f,.86f),4.5f,12f);
        CardKey();
    }

    static void CardKey()
    {
        string path=Art+"/HandaeEmployeeCard.asset";
        var card=AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if(!card)
        {
            card=ScriptableObject.CreateInstance<ItemData>(); card.itemId=EmployeeGate.CardItemId;
            AssetDatabase.CreateAsset(card,path);
        }
        card.itemId=EmployeeGate.CardItemId;
        card.displayName="한대건설 id카드"; card.category=ItemCategory.Document; card.maxStack=1;
        card.icon=CardIcon();
        card.heldPrefab=HeldCardPrefab();
        card.heldLocalEulerAngles=new Vector3(5f,0f,16f);
        card.heldLocalScale=Vector3.one;
        EditorUtility.SetDirty(card);
        var pickup=Child(gameplay,"Reception temporary ID card issue"); pickup.localPosition=V(2.55f,1.27f,-.35f+ReceptionShiftZ);
        var model=Object.Instantiate(card.heldPrefab,pickup,false); model.name="ID card with blue strap";
        model.transform.localScale=Vector3.one*2.1f;
        pickup.gameObject.AddComponent<EmployeeCardPickup>().card=card;
    }

    static Sprite CardIcon()
    {
        string path=Art+"/HandaeIDCardFront.png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer && (importer.textureType!=TextureImporterType.Sprite || importer.spriteImportMode!=SpriteImportMode.Single))
        { importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
          importer.alphaIsTransparency=false; importer.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static GameObject HeldCardPrefab()
    {
        string path=Art+"/EmployeeCardHeld.prefab";
        var front=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/HandaeIDCardFront.png");
        var frontMat=Mat("ID_Card_Blurred_Front",Color.white,.6f,0,front);
        frontMat.SetTextureScale("_BaseMap",Vector2.one); EditorUtility.SetDirty(frontMat);
        var strapMat=Mat("ID_Blue_Strap_Fabric",new Color(.025f,.19f,.58f),.25f,0);
        var root=new GameObject("Handae employee card held");
        Box(root.transform,"Card body",V(0,0,0),V(.16f,.008f,.103f),charcoal);
        var face=Box(root.transform,"Blurred blue and white ID face",V(0,.005f,0),V(.154f,.002f,.098f),frontMat);
        face.transform.localRotation=Quaternion.Euler(0,180f,0);
        Box(root.transform,"Metal lanyard clip",V(0,.007f,.057f),V(.027f,.006f,.019f),bronze);
        var left=Box(root.transform,"Blue lanyard left",V(-.019f,.01f,.16f),V(.011f,.004f,.22f),strapMat);
        left.transform.localRotation=Quaternion.Euler(0,-9f,0);
        var right=Box(root.transform,"Blue lanyard right",V(.019f,.01f,.16f),V(.011f,.004f,.22f),strapMat);
        right.transform.localRotation=Quaternion.Euler(0,9f,0);
        Box(root.transform,"Blue lanyard loop",V(0,.01f,.267f),V(.054f,.004f,.012f),strapMat);
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void BuildLounge()
    {
        // The supplied three-seat sofa keeps its human scale against the restroom wall.
        var sofa=HandaeLobbyModels.Place(furniture,"Sofa","Waiting sofa (sofa_company2.fbx)",P(326,750),90f);
        var sofaBounds=HandaeLobbyModels.WorldBounds(sofa);
        sofa.transform.position+=Vector3.right*(P(293,750).x+.025f-sofaBounds.min.x);
        Cylinder(furniture,"Waiting round side table",P(397,744,.43f),V(.86f,.085f,.86f),darkMarble,true);
        Cylinder(furniture,"Waiting table metal pedestal",P(397,744,.23f),V(.14f,.42f,.14f),bronze);
        Planter(details,P(320,638),.62f); Planter(details,P(320,832),.62f);

        // Four chairs in two opposing pairs around the long table. No rug in the reference.
        int loungeChair=0;
        foreach(float py in new[]{700f,795f})
        {
            HandaeLobbyModels.Place(furniture,"Armchair","Lounge armchair "+(++loungeChair)+" (armchair.fbx)",P(1100,py),90f);
            HandaeLobbyModels.Place(furniture,"Armchair","Lounge armchair "+(++loungeChair)+" (armchair.fbx)",P(1270,py),-90f);
        }
        PlanBox(furniture,"Lounge long rectangular marble table",1154,668,1224,834,.4f,.11f,darkMarble,true);
        PlanBox(furniture,"Lounge recessed table plinth",1163,679,1215,823,.1f,.3f,charcoal);
        Planter(details,P(1189,751,.52f),.25f);
        Planter(details,P(1326,578),.79f); Planter(details,P(1328,848),.85f);
        TableLamp(details,P(1324,657),1.5f);
        AddLight(P(1342,740,2.8f),new Color(1f,.94f,.84f),1.8f,6f);
    }

    static void Armchair(float x,float z,float yaw,float scale=1f)
    {
        var group=Child(furniture,"Lobby armchair"); group.localPosition=V(x,0,z); group.localRotation=Quaternion.Euler(0,yaw,0); group.localScale=Vector3.one*scale;
        Box(group,"Slim chair frame",V(0,.26f,0),V(1.2f,.18f,1.14f),bronze,true);
        Box(group,"Seat cushion",V(0,.46f,0),V(1.1f,.22f,.95f),leather,true);
        Box(group,"Back rest",V(0,.83f,.5f),V(1.18f,.73f,.18f),leather,true);
        Box(group,"Left arm",V(-.55f,.68f,0),V(.13f,.42f,1.08f),fabric);
        Box(group,"Right arm",V(.55f,.68f,0),V(.13f,.42f,1.08f),fabric);
        foreach(float sx in new[]{-.47f,.47f}) foreach(float sz in new[]{-.44f,.44f})
            Cylinder(group,"Bronze chair leg",V(sx,.12f,sz),V(.05f,.24f,.05f),bronze);
    }

    static void TableLamp(Transform parent,Vector3 basePoint,float height)
    {
        Cylinder(parent,"Lamp bronze base",basePoint+V(0,.025f,0),V(.28f,.025f,.28f),bronze);
        Cylinder(parent,"Lamp slim stem",basePoint+V(0,height*.45f,0),V(.025f,height*.45f,.025f),bronze);
        Cylinder(parent,"Warm lamp shade",basePoint+V(0,height*.85f,0),V(.25f,height*.18f,.25f),warm);
    }

    static void BuildRestroom()
    {
        // Shared east opening is deliberately wall-free from image y=383 to y=546.
        WallX("Women restroom east wall",288,75,383,3.25f);
        WallX("Men restroom east wall",288,546,856,3.25f);
        WallZ("Men restroom south wall",856,75,288,3.25f);
        RestroomDoor("Women",383,true,"여자 화장실");
        RestroomDoor("Men",546,false,"남자 화장실");
        PlanBox(architecture,"Women restroom lowered ceiling",75,75,288,383,3.25f,.14f,charcoal,true);
        PlanBox(architecture,"Men restroom lowered ceiling",75,546,288,856,3.25f,.14f,charcoal,true);
        PlanBox(details,"Women restroom dark tile floor",76,78,282,378,.006f,.016f,darkMarble);
        PlanBox(details,"Men restroom dark tile floor",76,552,282,850,.006f,.016f,darkMarble);
        PlanBox(details,"Shared restroom entrance marble",75,389,285,540,.004f,.014f,wallStone);
        Planter(details,P(103,477),.5f); Planter(details,P(251,477),.5f);
        BuildRestroomFixtures("Women",new[]{112f,183f,254f,325f},new[]{137f,199f,261f});
        BuildRestroomFixtures("Men",new[]{590f,669f,748f},new[]{621f,684f,746f,808f});
        Planter(details,P(252,337),.44f); Planter(details,P(108,816),.44f);
        AddLight(P(190,225,3.05f),new Color(1f,.96f,.91f),2.7f,6.4f);
        AddLight(P(190,704,3.05f),new Color(1f,.96f,.91f),2.7f,6.4f);
        AddLight(P(195,463,3.1f),new Color(1f,.94f,.86f),1.7f,4f);

        // The sole additional room is the small staff-only room explicitly present in the plan.
        WallX("Staff-only room east wall",443,75,275,3.3f);
        WallZ("Staff-only room south left jamb",275,288,345,3.3f);
        WallZ("Staff-only room south right jamb",275,403,443,3.3f);
        PlanBox(architecture,"Staff-only room lintel",345,270,403,280,2.75f,.55f,wallStone,true);
        PlanBox(architecture,"Staff-only room lowered ceiling",291,75,443,275,3.3f,.14f,charcoal,true);
        var door=Box(architecture,"Staff-only hinged door locked",P(374,274,1.34f),V(58f*PlanScale-.05f,2.68f,.085f),fabric,true);
        Box(details,"Staff-only door handle",P(392,280,1.08f),V(.18f,.055f,.07f),bronze);
        Text(details,"직원 외\n출입금지",P(374,281,2.0f),V(1.14f,.7f,1),.19f,Color.white,TextAlignmentOptions.Center);
        AddLight(P(365,170,3.05f),new Color(1f,.94f,.87f),1.3f,4f);
    }

    static void RestroomDoor(string prefix,float py,bool northRoom,string label)
    {
        // Keep the open leaf clear of the restroom aisle and the first toilet approach.
        const float left=150,right=214;
        WallZ(prefix+" restroom door left jamb",py,75,left,3.25f);
        WallZ(prefix+" restroom door right jamb",py,right,288,3.25f);
        PlanBox(architecture,prefix+" restroom door lintel",left,py-5,right,py+5,2.7f,.55f,wallStone,true);
        foreach(float px in new[]{left,right}) PlanBox(details,prefix+" dark door casing",px-2,py-7,px+2,py+7,0,2.73f,charcoal);
        var hinge=Child(architecture,prefix+" open hinged restroom door"); hinge.localPosition=P(left,py);
        hinge.localRotation=Quaternion.Euler(0,northRoom?-90f:180f,0);
        if(!northRoom) hinge.localPosition+=Vector3.back*.19f;
        Box(hinge,prefix+" open door leaf",V(.65f,1.31f,0),V(1.3f,2.62f,.065f),wallStone,true);
        Box(hinge,prefix+" door pull",V(1.12f,1.08f,-.05f),V(.16f,.055f,.05f),bronze);
        var pos=P(250,py+(northRoom?7:-7),2.05f);
        Text(details,label,pos,V(1.18f,.75f,1),.22f,Color.white,TextAlignmentOptions.Center);
        if(!northRoom) details.GetChild(details.childCount-1).rotation=Quaternion.Euler(0,180,0);
    }

    static void BuildRestroomFixtures(string prefix,float[] toiletRows,float[] sinkRows)
    {
        int index=0;
        foreach(float py in toiletRows)
        {
            HandaeLobbyModels.Place(furniture,"Toilet",prefix+" toilet "+(++index)+" (toilet.fbx)",P(92,py),90f);
            PlanBox(furniture,prefix+" cubicle side screen",79,py+23,150,py+27,.07f,2.28f,charcoal,true);
            // Cubicle fronts are open toward the central aisle, so every toilet can be approached.
            PlanBox(details,prefix+" cubicle end post",147,py+23,151,py+27,0,2.4f,bronze);
        }
        index=0;
        foreach(float py in sinkRows)
        {
            HandaeLobbyModels.Place(furniture,"Sink",prefix+" sink "+(++index)+" (sink.fbx)",P(268,py,.67f),-90f);
            Box(furniture,prefix+" floating sink support",P(273.5f,py,.56f),V(.58f,.20f,.56f),charcoal,true);
            PlanBox(details,prefix+" mirror",279,py-22,281,py+22,1.24f,1.08f,mirror);
            PlanBox(details,prefix+" mirror light",277,py-22,279,py+22,2.34f,.025f,warm);
        }
    }

    static void BuildGates()
    {
        // Four shared pylons form exactly three flap lanes at the reference gate line.
        const float row=385;
        PlanBox(architecture,"Gate west fixed sidelight",979,row-4,1027,row+4,0,1.75f,accentGlass,true);
        PlanBox(architecture,"Gate east fixed sidelight",1301,row-4,1320,row+4,0,1.75f,accentGlass,true);
        PlanBox(architecture,"Gate east marble return",1320,331,1385,405,0,2.4f,wallStone,true);
        InvisibleBox(gameplay,"Gate west security boundary",P(1003,row,2),V(48f*PlanScale,4,.18f));
        float halfLane=43f*PlanScale;
        float wingWidth=halfLane-.20f;
        for(int i=0;i<3;i++)
        {
            float centerPx=1078+i*86;
            var gate=Child(gameplay,"Employee card gate "+(i+1)); gate.localPosition=P(centerPx,row);
            var left=Box(gate,"Left moving glass wing",V(-(wingWidth+.02f)*.5f,1.04f,0),V(wingWidth,1.32f,.07f),accentGlass,true);
            var right=Box(gate,"Right moving glass wing",V((wingWidth+.02f)*.5f,1.04f,0),V(wingWidth,1.32f,.07f),accentGlass,true);
            Box(gate,"Shared metal gate pylon "+(i+1),V(-halfLane,.67f,0),V(.30f,1.34f,1.32f),charcoal,true);
            Box(gate,"Card reader face",V(-halfLane,1.38f,-.46f),V(.27f,.10f,.23f),charcoal,true);
            var status=Box(gate,"Blue green red gate status",V(-halfLane,1.1f,-.68f),V(.075f,.42f,.025f),led);
            var component=gate.gameObject.AddComponent<EmployeeGate>();
            component.leftWing=left.transform; component.rightWing=right.transform; component.wingSlideDistance=wingWidth-.06f;
            component.ledRenderers=new[]{status.GetComponent<Renderer>()};
            component.passageBlocker=InvisibleBox(gate,"Full-height card access blocker",V(0,2,0),V(halfLane*2f-.28f,4,.16f));
            if(i==2)
            {
                Box(gate,"Shared metal gate pylon 4",V(halfLane,.67f,0),V(.30f,1.34f,1.32f),charcoal,true);
                var finalLed=Box(gate,"Fourth gate status strip",V(halfLane,1.1f,-.68f),V(.075f,.42f,.025f),led);
                component.ledRenderers=new[]{status.GetComponent<Renderer>(),finalLed.GetComponent<Renderer>()};
            }
        }
        InvisibleBox(gameplay,"Gate east security boundary",P(1310.5f,row,2),V(19f*PlanScale,4,.18f));
    }

    static void BuildElevatorAndStairs()
    {
        // Elevator is LEFT and staircase RIGHT, entirely north of the gate line.
        PlanBox(architecture,"Elevator solid shaft infill",979,75,1196,205,0,11.3f,wallStone,true);
        PlanBox(architecture,"Elevator west flush wall",979,205,1020,215,0,11.3f,wallStone,true);
        PlanBox(architecture,"Elevator east flush wall",1170,205,1196,215,0,11.3f,wallStone,true);
        PlanBox(architecture,"Elevator left enclosure",1004,76,1020,205,0,3.75f,wallStone,true);
        PlanBox(architecture,"Elevator right enclosure",1170,76,1186,205,0,3.75f,wallStone,true);
        PlanBox(architecture,"Elevator roof",1004,76,1186,205,3.65f,.18f,charcoal,true);
        PlanBox(architecture,"Elevator bronze portal",1010,198,1180,207,0,3.55f,bronze,true);
        PlanBox(details,"Elevator dark recess",1026,207,1164,210,0,3.24f,charcoal);
        PlanBox(details,"Elevator left steel leaf",1032,210,1094,214,.025f,3.1f,mirror);
        PlanBox(details,"Elevator right steel leaf",1097,210,1158,214,.025f,3.1f,mirror);
        PlanBox(details,"Elevator call plate",1168,210,1176,215,1.02f,.44f,charcoal);
        Text(details,"엘리베이터",P(1095,215,3.44f),V(3.4f,.4f,1),.25f,Color.white,TextAlignmentOptions.Center);

        PlanBox(architecture,"Stairs left dark side wall",1196,30,1221,301,0,4.5f,charcoal,true);
        PlanBox(architecture,"Stairs right stone side wall",1362,30,1385,301,0,4.5f,wallStone,true);
        PlanBox(details,"Stair dark bottom landing",1222,215,1361,298,.005f,.025f,darkMarble);
        const int count=18; const float rise=.185f;
        float south=P(1291,213).z,run=(P(1291,38).z-south)/count;
        for(int i=0;i<count;i++)
        {
            float z=south+i*run;
            Box(architecture,"Stair visual tread "+(i+1),V(P(1291,0).x,(i+1)*rise-.075f,z),V(139f*PlanScale,.15f,run+.012f),wallStone,true);
            Box(details,"Stair metal nosing",V(P(1291,0).x,(i+1)*rise+.005f,z-run*.47f),V(139f*PlanScale,.016f,.035f),bronze);
        }
        InvisibleBox(gameplay,"Transparent stair access barrier",P(1291,298,2),V(146f*PlanScale,4,.15f));
        foreach(float px in new[]{1226f,1357f})
        {
            float railRun=175f*PlanScale;
            float railRise=count*rise;
            var rail=Box(details,"Stair continuous handrail",P(px,126,2.6f),V(.055f,.055f,Mathf.Sqrt(railRun*railRun+railRise*railRise)),bronze);
            rail.transform.rotation=Quaternion.Euler(-Mathf.Atan2(railRise,railRun)*Mathf.Rad2Deg,0,0);
        }
        Text(details,"계단",P(1291,298,3.25f),V(2.8f,.42f,1),.27f,Color.white,TextAlignmentOptions.Center);
        var marker=Child(gameplay,"Stairs to separate second-floor map"); marker.localPosition=P(1291,330,.1f);
        var stairs=marker.gameObject.AddComponent<FloorStairs>(); stairs.floor=1; stairs.topFloor=2;
        stairs.radius=.78f; stairs.allowSpace=true; stairs.targetScenePath=FutureSecondFloorPath; stairs.targetSpawnId="stairs_arrival";
        Spawn("floor_1",P(1291,330,.12f),0);
    }

    static void BuildGameplay()
    {
        Spawn("default",P(730,983,.12f),0);
        Spawn("handae_entry",P(730,983,.12f),0);
        var exit=Child(gameplay,"Return to Business District"); exit.localPosition=P(730,1042,.2f);
        var portal=exit.gameObject.AddComponent<MapPortal>(); portal.targetScenePath=DistrictPath;
        portal.targetSpawnId="handae_exit"; portal.displayName="한대건설 밖으로 나가기"; portal.interactionRadius=.75f;
        var camera=Child(root,"60_PlayerCamera").gameObject; camera.tag="MainCamera";
        camera.transform.position=P(730,983,1.6f); camera.AddComponent<Camera>();
        var controller=camera.AddComponent<PlayerCameraController>(); controller.firstPerson=true;
        controller.firstPersonEyeHeight=1.6f; controller.firstPersonFOV=72f; controller.initialPitch=0; controller.enableObstacleFade=false;
    }

    static void BuildLighting()
    {
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.59f,.585f,.57f);
        RenderSettings.fog=false; RenderSettings.skybox=null;
        for(int xi=-2;xi<=2;xi++) for(int zi=-1;zi<=1;zi++)
        {
            float x=xi*6.4f,z=zi*8.1f+1.5f;
            Cylinder(details,"Recessed downlight",V(x,11.24f,z),V(.24f,.02f,.24f),warm);
            AddLight(V(x,9.7f,z),new Color(1f,.95f,.88f),3.8f,16f);
        }
        AddLight(P(730,915,3.7f),new Color(1f,.95f,.87f),2f,6f);
        AddLight(P(1190,335,3.3f),new Color(1f,.95f,.87f),2.3f,7f);
    }

    static void LinkDistrict()
    {
        var scene=EditorSceneManager.OpenScene(DistrictPath,OpenSceneMode.Single);
        var rootObject=GameObject.Find("Map_BusinessDistrict");
        if(!rootObject) throw new InvalidOperationException("BusinessDistrict root is missing.");
        var existing=rootObject.transform.Find("Handae HQ Lobby Portal");
        var existingSpawn=rootObject.transform.Find("Spawn_handae_exit");
        var linked=existing?existing.GetComponent<MapPortal>():null;
        var spawnPoint=existingSpawn?existingSpawn.GetComponent<MapSpawnPoint>():null;
        if(linked&&spawnPoint&&linked.targetScenePath==ScenePath&&linked.targetSpawnId=="handae_entry"&&
            spawnPoint.spawnId=="handae_exit"&&(existing.position-V(2f,.4f,199.15f)).sqrMagnitude<.0001f&&
            (existingSpawn.position-V(2f,.45f,198.2f)).sqrMagnitude<.0001f)
        {
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            return;
        }
        if(existing) Object.DestroyImmediate(existing.gameObject);
        if(existingSpawn) Object.DestroyImmediate(existingSpawn.gameObject);
        var marker=Child(rootObject.transform,"Handae HQ Lobby Portal");
        marker.position=V(2f,.4f,199.15f);
        var portal=marker.gameObject.AddComponent<MapPortal>(); portal.targetScenePath=ScenePath;
        portal.targetSpawnId="handae_entry"; portal.displayName="한대건설 본사 로비 들어가기"; portal.interactionRadius=2.5f;
        var spawn=Child(rootObject.transform,"Spawn_handae_exit"); spawn.position=V(2f,.45f,198.2f);
        spawn.gameObject.AddComponent<MapSpawnPoint>().spawnId="handae_exit";
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BusinessDistrict link.");
        EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
    }

    static void AddToBuild()
    {
        var scenes=new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if(!scenes.Exists(s=>s.path==ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
        EditorBuildSettings.scenes=scenes.ToArray();
    }

    static void CreateMaterials()
    {
        var texture=MarbleTexture();
        marble=Mat("Polished_Ivory_Marble",new Color(.84f,.84f,.82f),.79f,.02f,texture);
        darkMarble=Mat("Graphite_Marble",new Color(.18f,.2f,.22f),.78f,.04f,texture);
        wallStone=Mat("Silver_Grey_Stone",new Color(.74f,.74f,.72f),.64f,.03f,texture);
        charcoal=Mat("Charcoal_Metal",new Color(.055f,.062f,.071f),.6f,.72f);
        bronze=Mat("Dark_Bronze",new Color(.35f,.27f,.2f),.68f,.78f);
        glass=Mat("Curtain_Glass",new Color(.71f,.8f,.82f,.16f),.93f,0,null,true);
        accentGlass=Mat("Gate_And_Door_Glass",new Color(.66f,.81f,.86f,.24f),.92f,0,null,true);
        warm=Mat("Warm_Indirect_Light",new Color(1f,.90f,.74f),.45f,0,null,false,2.5f);
        fabric=Mat("Graphite_Upholstery",new Color(.19f,.2f,.2f),.27f,0);
        leather=Mat("Soft_Charcoal_Leather",new Color(.29f,.31f,.32f),.34f,0);
        matte=Mat("Lounge_Rug",new Color(.27f,.28f,.27f),.15f,0);
        led=Mat("Gate_Blue_LED",new Color(.04f,.4f,1f),.35f,0,null,false,3f);
        mirror=Mat("Brushed_Steel_Mirror",new Color(.71f,.75f,.77f),.9f,.85f);
    }

    static Texture2D MarbleTexture()
    {
        string path=Art+"/Materials/MarbleVein.png";
        // Rebuild the small repeatable texture so the art source and material stay in sync.
        {
            string absolute=Path.Combine(Application.dataPath,"Art/WorldDistricts/HandaeHQ/Lobby/Materials/MarbleVein.png");
            const int n=512; var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float fx=x/(float)n,fy=y/(float)n;
                float warp=(Mathf.PerlinNoise(fx*3.6f,fy*3.1f)-.5f)*.52f+
                           (Mathf.PerlinNoise(fx*10.7f,fy*11.4f)-.5f)*.08f;
                float field=fx*1.7f+fy*.42f+warp;
                float vein=Mathf.Pow(Mathf.Clamp01(1f-Mathf.Abs(Mathf.Sin(field*6.28318f))/.075f),2f);
                float secondary=Mathf.Pow(Mathf.Clamp01(1f-Mathf.Abs(Mathf.Sin((field*.78f+fy*.23f)*6.28318f))/.04f),2f);
                float grain=(Mathf.PerlinNoise(fx*25f,fy*25f)-.5f)*.085f;
                float v=Mathf.Clamp01(.94f+grain-vein*.12f-secondary*.04f);
                tex.SetPixel(x,y,new Color(v,v*.995f,v*.985f,1));
            }
            tex.Apply(); File.WriteAllBytes(absolute,tex.EncodeToPNG());
            Object.DestroyImmediate(tex); AssetDatabase.ImportAsset(path);
        }
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer && importer.wrapMode!=TextureWrapMode.Repeat) { importer.wrapMode=TextureWrapMode.Repeat; importer.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string name,Color color,float smooth,float metallic,Texture texture=null,bool transparent=false,float emission=0)
    {
        string path=Art+"/Materials/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat) { mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.name=name; AssetDatabase.CreateAsset(mat,path); }
        mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",smooth); mat.SetFloat("_Metallic",metallic);
        if(texture) { mat.SetTexture("_BaseMap",texture); mat.SetTextureScale("_BaseMap",new Vector2(3f,3f)); }
        if(transparent)
        {
            mat.SetFloat("_Surface",1); mat.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite",0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue=(int)RenderQueue.Transparent;
        }
        if(emission>0) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",color*emission); }
        EditorUtility.SetDirty(mat); return mat;
    }

    static Mesh DeskRing(string name,float outerX,float outerZ,float innerX,float innerZ,float centerZ,float y0,float y1)
    {
        const int segments=64;
        var vertices=new System.Collections.Generic.List<Vector3>();
        var uv=new System.Collections.Generic.List<Vector2>();
        var triangles=new System.Collections.Generic.List<int>();
        Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>
        {
            int n=vertices.Count; vertices.AddRange(new[]{a,b,c,d});
            uv.AddRange(new[]{new Vector2(a.x,a.z+a.y),new Vector2(b.x,b.z+b.y),new Vector2(c.x,c.z+c.y),new Vector2(d.x,d.z+d.y)});
            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        };
        for(int i=0;i<segments;i++)
        {
            float a=i*Mathf.PI/segments,b=(i+1)*Mathf.PI/segments;
            Vector3 o0=V(outerX*Mathf.Cos(a),y0,centerZ-outerZ*Mathf.Sin(a)),o1=V(outerX*Mathf.Cos(b),y0,centerZ-outerZ*Mathf.Sin(b));
            Vector3 i0=V(innerX*Mathf.Cos(a),y0,centerZ-innerZ*Mathf.Sin(a)),i1=V(innerX*Mathf.Cos(b),y0,centerZ-innerZ*Mathf.Sin(b));
            Vector3 up=V(0,y1-y0,0);
            quad(o0,o1,o1+up,o0+up); quad(i1,i0,i0+up,i1+up);
            quad(o0+up,o1+up,i1+up,i0+up); quad(i0,i1,o1,o0);
            if(i==0) quad(i0,o0,o0+up,i0+up);
            if(i==segments-1) quad(o1,i1,i1+up,o1+up);
        }
        var mesh=new Mesh {name=name}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return SaveMesh(mesh,name);
    }

    static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Art+"/Meshes/"+name+".asset"; var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved) { EditorUtility.CopySerialized(mesh,saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); return saved; }
        AssetDatabase.CreateAsset(mesh,path); return mesh;
    }

    static BoxCollider InvisibleBox(Transform parent,string name,Vector3 position,Vector3 size)
    {
        var go=Child(parent,name); go.localPosition=position;
        var collider=go.gameObject.AddComponent<BoxCollider>(); collider.size=size; return collider;
    }

    static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material,bool collision)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(collision) go.AddComponent<MeshCollider>().sharedMesh=mesh;
        return go;
    }

    static void Planter(Transform parent,Vector3 p,float size)
    {
        Cylinder(parent,"Dark stone planter",p+V(0,size*.36f,0),V(size,size*.36f,size),charcoal,true);
        Cylinder(parent,"Planter soil",p+V(0,size*.725f,0),V(size*.88f,.012f,size*.88f),matte);
        var green=Mat("Greenery",new Color(.13f,.27f,.09f),.28f,0);
        string path=Art+"/Meshes/PlanterLeaves.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh)
        {
            var vertices=new System.Collections.Generic.List<Vector3>(); var tris=new System.Collections.Generic.List<int>();
            var random=new System.Random(723);
            for(int i=0;i<34;i++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2f;
                float h=.8f+(float)random.NextDouble()*.85f;
                var dir=V(Mathf.Cos(angle),.15f,Mathf.Sin(angle)); var side=V(-dir.z,0,dir.x)*.115f;
                var start=V(0,h,0); var middle=start+dir*.36f+Vector3.up*.14f; var tip=start+dir*(.54f+(float)random.NextDouble()*.22f);
                int n=vertices.Count; vertices.AddRange(new[]{start,middle-side,middle+Vector3.up*.05f,middle+side,tip});
                int[] faces={0,2,1,0,3,2,1,2,4,2,3,4};
                foreach(int f in faces) tris.Add(n+f);
                for(int t=0;t<faces.Length;t+=3) { tris.Add(n+faces[t]); tris.Add(n+faces[t+2]); tris.Add(n+faces[t+1]); }
            }
            mesh=new Mesh {name="PlanterLeaves",vertices=vertices.ToArray(),triangles=tris.ToArray()}; mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path);
        }
        var leaves=MeshObject(parent,"Architectural plant foliage",mesh,green,false); leaves.transform.localPosition=p; leaves.transform.localScale=Vector3.one*size;
    }

    static void AddLight(Vector3 p,Color color,float intensity,float range)
    {
        var go=Child(lighting,"Warm architectural light"); go.position=p;
        var light=go.gameObject.AddComponent<Light>(); light.type=LightType.Point; light.color=color;
        light.intensity=intensity; light.range=range; light.shadows=LightShadows.None;
    }

    static void Text(Transform parent,string content,Vector3 pos,Vector3 size,float fontSize,Color color,TextAlignmentOptions alignment)
    {
        var go=Child(parent,"Sign " + content); go.position=pos;
        var text=go.gameObject.AddComponent<TextMeshPro>(); if(font) text.font=font;
        text.text=content; text.fontSize=fontSize*13f; text.color=color; text.alignment=alignment;
        text.textWrappingMode=TextWrappingModes.NoWrap;
        text.enableAutoSizing=true; text.fontSizeMax=text.fontSize; text.fontSizeMin=text.fontSize*.65f;
        text.rectTransform.sizeDelta=new Vector2(size.x,size.y);
        text.ForceMeshUpdate();
    }

    static void Spawn(string id,Vector3 pos,float yaw)
    {
        var go=Child(gameplay,"Spawn_"+id); go.position=pos; go.rotation=Quaternion.Euler(0,yaw,0);
        go.gameObject.AddComponent<MapSpawnPoint>().spawnId=id;
    }

    static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 size,Material material,bool collision=false)
        => Primitive(parent,PrimitiveType.Cube,name,pos,size,material,collision);
    static GameObject Cylinder(Transform parent,string name,Vector3 pos,Vector3 size,Material material,bool collision=false)
        => Primitive(parent,PrimitiveType.Cylinder,name,pos,size,material,collision);
    static GameObject Primitive(Transform parent,PrimitiveType type,string name,Vector3 pos,Vector3 size,Material material,bool collision)
    {
        var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false);
        go.transform.localPosition=pos; go.transform.localScale=size;
        go.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Transform Child(Transform parent,string name)
    {
        var go=new GameObject(name).transform; go.SetParent(parent,false); return go;
    }
    static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path)) return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
}
