using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using NarrativeGraybox.Player;
using Object = UnityEngine.Object;

// 추격 시스템 테스트 씬(1층 집)을 코드로 만듭니다. 기존 그레이박스·프로토타입 씬과 별개.
//
//  z=8  ┌──────────┬──────────┬──────────┐
//       │ 침실(막힘)│   복도    │   서재    │   ← 창문(침실·서재, 아버지 전용)
//  z=2.5├──환풍구──┼── 통로 ──┼────문────┤
//       │   거실    │    홀    │   주방    │   ← 창문(거실·주방, 아버지 전용)
// z=-2.5├────문────┼── 통로 ──┼────문────┤
//       │ 욕실(막힘)│   현관    │   창고    │
//  z=-8 └──────────┴──현관문──┴──────────┘
//      x=-10     x=-3.5     x=3.5      x=10       (밖은 마당)
//
//  순환 동선: 홀→주방→창고→현관→홀, 홀→복도→서재→주방→홀
//  막다른 방: 침실(복도에서만), 거실+욕실(홀에서만). 단, 아버지는 환풍구(침실↔거실, 욕실↔현관)·창문으로 들어올 수 있음.
public static class ChaseTestSceneBuilder
{
    public const string TargetScene = "Assets/Scenes/ChaseTest.unity";
    const string TargetFolder = "Assets/Scenes/ChaseTest";
    const string SpriteSheetGuid = "8997f85750edfef479bf5a1765a6ed3f"; // 팀원 TestCharacter4Directions.png

    const float WallThickness = 0.2f;
    const float WallVisualHeight = 1.0f; // 탑뷰에서 방 안이 보이도록 낮게 보이지만
    const float WallBlockHeight = 2.6f;  // 시야를 가리는 충돌체는 사람 키보다 높게
    const float DoorWidth = 1.2f;
    const float LinkReach = 0.9f;        // 벽 중심에서 링크 양 끝까지

    enum Axis { AlongX, AlongZ } // 벽이 뻗는 방향

    struct RoomDef
    {
        public string id, name; public float minX, maxX, minZ, maxZ;
        public RoomDef(string id, string name, float minX, float maxX, float minZ, float maxZ)
        { this.id = id; this.name = name; this.minX = minX; this.maxX = maxX; this.minZ = minZ; this.maxZ = maxZ; }
    }

    static readonly RoomDef[] Rooms =
    {
        new RoomDef("Bedroom", "침실", -10f, -3.5f, 2.5f, 8f),
        new RoomDef("Corridor", "복도", -3.5f, 3.5f, 2.5f, 8f),
        new RoomDef("Study", "서재", 3.5f, 10f, 2.5f, 8f),
        new RoomDef("Living", "거실", -10f, -3.5f, -2.5f, 2.5f),
        new RoomDef("Hall", "홀", -3.5f, 3.5f, -2.5f, 2.5f),
        new RoomDef("Kitchen", "주방", 3.5f, 10f, -2.5f, 2.5f),
        new RoomDef("Bath", "욕실", -10f, -3.5f, -8f, -2.5f),
        new RoomDef("Entry", "현관", -3.5f, 3.5f, -8f, -2.5f),
        new RoomDef("Storage", "창고", 3.5f, 10f, -8f, -2.5f),
    };

    static Material wallMat, floorMat, timberMat, fabricMat, linenMat, glassMat, ventMat;

    [MenuItem("OTK/추격 테스트 씬 다시 만들기")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("추격 테스트 씬 다시 만들기",
                $"{TargetScene}을(를) 새로 만듭니다. 직접 수정한 내용은 사라집니다.", "만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        ChaseTestSceneValidator.Validate(true);
    }

    // Unity.exe -batchmode -executeMethod ChaseTestSceneBuilder.BuildFromCommandLine (0=통과, 2=점검 실패)
    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(ChaseTestSceneValidator.Validate(false) ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Build()
    {
        if (NavMesh.GetAreaFromName(EntryPoint.DoorArea) < 0 || NavMesh.GetAreaFromName(EntryPoint.WindowArea) < 0 || NavMesh.GetAreaFromName(EntryPoint.VentArea) < 0)
            throw new Exception("NavMesh Areas에 Door / Window / Vent가 없습니다 (ProjectSettings/NavMeshAreas.asset).");

        // 씬 파일은 지우지 않고 덮어써서 GUID를 유지합니다 (빌드 설정·참조가 깨지지 않도록).
        AssetDatabase.DeleteAsset(TargetFolder);
        AssetDatabase.CreateFolder("Assets/Scenes", "ChaseTest");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        LoadMaterials();

        // 1) 빛 · 카메라
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var mainCamera = cameraObject.AddComponent<Camera>();
        mainCamera.orthographic = true;
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
        cameraObject.AddComponent<AudioListener>();
        var brain = cameraObject.AddComponent<CinemachineBrain>();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        Quaternion topDown = PrototypeSceneBuilder.TopDownRotation;
        cameraObject.transform.SetPositionAndRotation(new Vector3(-6.75f, 1.5f, 0f) - topDown * Vector3.forward * 20f, topDown);

        // 2) 맵 (바닥·벽·가구) → NavMesh 굽기. 문·통로·캐릭터는 굽고 난 뒤에 만듭니다.
        Transform map = new GameObject("Map").transform;
        Box(map, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(30f, 0.2f, 26f), floorMat);
        BuildWalls(map);
        BuildFurniture(map);
        BakeNavMesh();

        // 3) 구역 (방별 고정 탑뷰 카메라 + 위치 정보)
        Transform zones = new GameObject("Room Cameras").transform;
        foreach (RoomDef room in Rooms)
            PrototypeSceneBuilder.BuildRoomZone(zones, room.id, room.name, room.minX, room.maxX, room.minZ, room.maxZ, priority: 10);
        PrototypeSceneBuilder.BuildRoomZone(zones, "Yard", "마당", -15f, 15f, -13f, 13f, buildingId: "Outside");

        // 4) 출입 경로: 문 · 창문 · 환풍구
        Transform entries = new GameObject("Entry Points").transform;
        Door(entries, "침실 문", Axis.AlongZ, -3.5f, 5.25f);
        Door(entries, "거실 문", Axis.AlongZ, -3.5f, 0f);
        Door(entries, "서재 문", Axis.AlongZ, 3.5f, 5.25f);
        Door(entries, "주방 문", Axis.AlongZ, 3.5f, 0f);
        Door(entries, "창고 문", Axis.AlongZ, 3.5f, -5.25f);
        Door(entries, "서재-주방 문", Axis.AlongX, 2.5f, 6.75f);
        Door(entries, "욕실 문", Axis.AlongX, -2.5f, -6.75f);
        Door(entries, "주방-창고 문", Axis.AlongX, -2.5f, 6.75f);
        Door(entries, "현관문", Axis.AlongX, -8f, 0f);

        Passage(entries, "침실 창문", EntryKind.Window, Axis.AlongX, 8f, -6.75f);
        Passage(entries, "서재 창문", EntryKind.Window, Axis.AlongX, 8f, 6.75f);
        Passage(entries, "거실 창문", EntryKind.Window, Axis.AlongZ, -10f, 1.2f);
        Passage(entries, "주방 창문", EntryKind.Window, Axis.AlongZ, 10f, -1.2f);
        Passage(entries, "침실-거실 환풍구", EntryKind.Vent, Axis.AlongX, 2.5f, -8.5f);
        Passage(entries, "욕실-현관 환풍구", EntryKind.Vent, Axis.AlongZ, -3.5f, -4.5f);

        // 5) 숨는 곳
        Transform hiding = new GameObject("Hiding Spots").transform;
        Hide(hiding, "옷장", HidingKind.Closet, new Vector3(-5f, 0f, 6.9f), capacity: 2);
        Hide(hiding, "침대 밑", HidingKind.UnderBed, new Vector3(-6.85f, 0f, 6.6f), enter: 1.0f, exit: 0.7f, noise: 1.5f);
        Hide(hiding, "커튼", HidingKind.Curtain, new Vector3(-9.3f, 0f, -1.6f), enter: 0.3f, exit: 0.25f, noise: 1.5f, exposed: 1.2f);
        Hide(hiding, "수납장", HidingKind.Closet, new Vector3(8.7f, 0f, 4f));
        Hide(hiding, "창고 수납장", HidingKind.Closet, new Vector3(5f, 0f, -6.7f), capacity: 2);
        Hide(hiding, "샤워 커튼", HidingKind.Curtain, new Vector3(-8.5f, 0f, -5.9f), enter: 0.3f, exit: 0.25f, noise: 1.5f, exposed: 1.2f);

        // 6) 캐릭터 2명 (거실에서 시작)
        GameObject detective = BuildPlayer("탐정 (Player1)", CharacterType.Player1, new Vector3(-6f, 1f, 1.2f), Color.white);
        GameObject assistant = BuildPlayer("조수 (Player2)", CharacterType.Player2, new Vector3(-5f, 1f, 1.8f), new Color(0.65f, 0.82f, 1f));

        // 7) 시험용 더미 추격자 (홀에서 순찰 시작, 경찰 규칙: 창문·환풍구 못 씀)
        BuildTestChaser();

        // 8) 시스템
        PrototypeSceneBuilder.BuildFadeCanvas();
        var systems = new GameObject("Game Systems");
        systems.AddComponent<CharacterManager>().players = new[] { detective, assistant };
        systems.AddComponent<GameStateManager>();
        systems.AddComponent<RoomCameraSync>();
        systems.AddComponent<ChaseDebugOverlay>();

        EditorSceneManager.SaveScene(scene, TargetScene);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[OTK] 추격 테스트 씬 생성 완료: " + TargetScene);
    }

    // ---------------- 맵 ----------------

    static void BuildWalls(Transform map)
    {
        Transform walls = new GameObject("Walls").transform;
        walls.SetParent(map);
        float door = DoorWidth;

        // 바깥벽
        Wall(walls, Axis.AlongX, 8f, -10f, 10f);
        Wall(walls, Axis.AlongX, -8f, -10f, 10f, (0f, door));
        Wall(walls, Axis.AlongZ, -10f, -8f, 8f);
        Wall(walls, Axis.AlongZ, 10f, -8f, 8f);

        // 안쪽 벽 (괄호 = 문/통로 자리: 중심, 폭)
        Wall(walls, Axis.AlongZ, -3.5f, -8f, 8f, (5.25f, door), (0f, door));
        Wall(walls, Axis.AlongZ, 3.5f, -8f, 8f, (5.25f, door), (0f, door), (-5.25f, door));
        Wall(walls, Axis.AlongX, 2.5f, -10f, 10f, (0f, 2f), (6.75f, door));
        Wall(walls, Axis.AlongX, -2.5f, -10f, 10f, (-6.75f, door), (0f, 2f), (6.75f, door));
    }

    static void Wall(Transform parent, Axis axis, float fixedCoord, float from, float to, params (float center, float width)[] gaps)
    {
        var cuts = gaps.OrderBy(g => g.center).ToList();
        float start = from;
        foreach (var gap in cuts)
        {
            WallSegment(parent, axis, fixedCoord, start, gap.center - gap.width * 0.5f);
            start = gap.center + gap.width * 0.5f;
        }
        WallSegment(parent, axis, fixedCoord, start, to);
    }

    static void WallSegment(Transform parent, Axis axis, float fixedCoord, float a, float b)
    {
        float length = b - a;
        if (length <= 0.01f) return;
        float mid = (a + b) * 0.5f;
        Vector3 position = axis == Axis.AlongX ? new Vector3(mid, 0f, fixedCoord) : new Vector3(fixedCoord, 0f, mid);
        Vector3 size = axis == Axis.AlongX ? new Vector3(length + WallThickness, 0f, WallThickness) : new Vector3(WallThickness, 0f, length + WallThickness);
        TallBox(parent, "Wall", position, size, WallVisualHeight, WallBlockHeight, wallMat);
    }

    static void BuildFurniture(Transform map)
    {
        Transform furniture = new GameObject("Furniture").transform;
        furniture.SetParent(map);
        // 침실
        Box(furniture, "침대", new Vector3(-8.2f, 0.25f, 6.6f), new Vector3(1.8f, 0.5f, 2.2f), linenMat);
        TallBox(furniture, "옷장", new Vector3(-5f, 0f, 7.6f), new Vector3(1.6f, 0f, 0.6f), 1.4f, 2.2f, timberMat);
        // 거실
        Box(furniture, "소파", new Vector3(-7f, 0.4f, -0.5f), new Vector3(2.4f, 0.8f, 0.9f), fabricMat);
        TallBox(furniture, "커튼", new Vector3(-9.8f, 0f, -1.6f), new Vector3(0.1f, 0f, 1.0f), 1.6f, 2.4f, fabricMat);
        // 서재
        Box(furniture, "책상", new Vector3(7f, 0.4f, 6.5f), new Vector3(2f, 0.8f, 1f), timberMat);
        TallBox(furniture, "수납장", new Vector3(9.4f, 0f, 4f), new Vector3(0.6f, 0f, 1.4f), 1.4f, 2.2f, timberMat);
        // 주방
        Box(furniture, "조리대", new Vector3(9.3f, 0.45f, 1.25f), new Vector3(1f, 0.9f, 1.5f), timberMat);
        // 홀 (둘레를 돌 수 있는 탁자)
        Box(furniture, "탁자", new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f), timberMat);
        // 창고
        TallBox(furniture, "선반", new Vector3(8.5f, 0f, -7.4f), new Vector3(2.6f, 0f, 0.6f), 1.6f, 2.2f, timberMat);
        TallBox(furniture, "창고 수납장", new Vector3(5f, 0f, -7.4f), new Vector3(1.4f, 0f, 0.6f), 1.6f, 2.2f, timberMat);
        // 욕실
        Box(furniture, "욕조", new Vector3(-8.5f, 0.3f, -7f), new Vector3(2f, 0.6f, 1.2f), linenMat);
        TallBox(furniture, "샤워 커튼", new Vector3(-8.5f, 0f, -6.3f), new Vector3(2f, 0f, 0.06f), 1.6f, 2.4f, fabricMat);
        // 현관
        Box(furniture, "신발장", new Vector3(-2.5f, 0.5f, -7.6f), new Vector3(1.4f, 1f, 0.5f), timberMat);
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.position = center;
        box.transform.localScale = size;
        if (material != null) box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    // 충돌체 없는 표시용 큐브 (로컬 좌표)
    static void Visual(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = localScale;
        if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
    }

    // 보이는 높이는 낮게(탑뷰에서 방 안이 보이도록), 충돌체(시야 가림)는 높게. size.y는 무시.
    static GameObject TallBox(Transform parent, string name, Vector3 floorPosition, Vector3 size, float visualHeight, float blockHeight, Material material)
    {
        GameObject box = Box(parent, name, floorPosition + Vector3.up * (visualHeight * 0.5f), new Vector3(size.x, visualHeight, size.z), material);
        var collider = box.GetComponent<BoxCollider>();
        float scaleY = blockHeight / visualHeight;
        collider.size = new Vector3(1f, scaleY, 1f);
        collider.center = new Vector3(0f, (scaleY - 1f) * 0.5f, 0f);
        return box;
    }

    static void BakeNavMesh()
    {
        var surface = new GameObject("NavMesh").AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(surface.agentTypeID);
        settings.agentRadius = 0.25f;

        var bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(32f, 6f, 28f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.RenderMeshes, 0, new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        data.name = "ChaseTest NavMesh";
        AssetDatabase.CreateAsset(data, TargetFolder + "/NavMesh.asset");

        surface.navMeshData = data;
        surface.RemoveData();
        surface.AddData();
    }

    // ---------------- 출입 경로 ----------------

    // 벽(axis 방향으로 뻗음, fixedCoord 위치)의 along 지점에 놓인 통로의 기준 회전:
    // 로컬 +z = 벽을 따라, 로컬 x = 벽을 가로지름(링크 방향)
    static Quaternion WallRotation(Axis axis) => axis == Axis.AlongZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
    static Vector3 WallPoint(Axis axis, float fixedCoord, float along) =>
        axis == Axis.AlongZ ? new Vector3(fixedCoord, 0f, along) : new Vector3(along, 0f, fixedCoord);

    // 회전문: 빈 부모(경첩)가 회전축, 넉넉한 트리거로 접근 감지, 자식 문짝의 NavMeshObstacle(Carve)이 닫혀 있을 때 길을 막음.
    // 추격자는 같은 자리의 NavMeshLink로 지나감(문을 열고).
    static void Door(Transform parent, string name, Axis axis, float fixedCoord, float along)
    {
        Quaternion rotation = WallRotation(axis);
        Vector3 center = WallPoint(axis, fixedCoord, along);
        Vector3 hinge = center - rotation * Vector3.forward * (DoorWidth * 0.5f);

        var pivot = new GameObject($"DoorHinge ({name})") { tag = "Door" };
        pivot.transform.SetParent(parent);
        pivot.transform.SetPositionAndRotation(hinge, rotation);
        var reach = pivot.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1.2f, DoorWidth * 0.5f);
        reach.size = new Vector3(2.6f, 2.4f, DoorWidth + 0.6f);

        const float visualHeight = 1.1f, blockHeight = 2.4f;
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Door";
        panel.transform.SetParent(pivot.transform, false);
        panel.transform.localPosition = new Vector3(0f, visualHeight * 0.5f, DoorWidth * 0.5f);
        panel.transform.localScale = new Vector3(0.12f, visualHeight, DoorWidth - 0.05f);
        panel.GetComponent<Renderer>().sharedMaterial = timberMat;
        var block = panel.GetComponent<BoxCollider>();
        block.size = new Vector3(1f, blockHeight / visualHeight, 1f);
        block.center = new Vector3(0f, (blockHeight / visualHeight - 1f) * 0.5f, 0f);
        var obstacle = panel.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = Vector3.one;
        obstacle.carving = true;

        var door = pivot.AddComponent<InteractableDoor>();
        door.requiredItemName = "";
        door.playerLockable = true;
        door.lockedMessage = "잠겨 있다.";
        door.openedMessage = "문을 열었다.";
        door.closedMessage = "문을 닫았다.";

        NavMeshLink link = Link(pivot.transform, new Vector3(0f, 0f, DoorWidth * 0.5f), EntryPoint.DoorArea);
        var entry = pivot.AddComponent<EntryPoint>();
        entry.kind = EntryKind.Door;
        entry.displayName = name;
        entry.door = door;
        entry.link = link;
        entry.usableBy = ChaserType.All;
        entry.traverseTime = 0.35f;
        entry.traverseNoise = 0f; // 문 소리는 문이 냄
    }

    // 창문·환풍구: 아버지 전용 통로 (벽을 넘어가거나 기어서 지나감)
    static void Passage(Transform parent, string name, EntryKind kind, Axis axis, float fixedCoord, float along)
    {
        var root = new GameObject($"{(kind == EntryKind.Window ? "Window" : "Vent")} ({name})");
        root.transform.SetParent(parent);
        root.transform.SetPositionAndRotation(WallPoint(axis, fixedCoord, along), WallRotation(axis));

        if (kind == EntryKind.Window)
        {
            Visual(root.transform, "Glass", new Vector3(0f, 0.8f, 0f), new Vector3(WallThickness + 0.04f, 0.5f, 1.2f), glassMat);
        }
        else
        {
            // 벽 양쪽 바닥 가까이에 환풍구 덮개
            Visual(root.transform, "Grate A", new Vector3(-0.13f, 0.2f, 0f), new Vector3(0.05f, 0.35f, 0.6f), ventMat);
            Visual(root.transform, "Grate B", new Vector3(0.13f, 0.2f, 0f), new Vector3(0.05f, 0.35f, 0.6f), ventMat);
        }

        NavMeshLink link = Link(root.transform, Vector3.zero, kind == EntryKind.Window ? EntryPoint.WindowArea : EntryPoint.VentArea);
        var entry = root.AddComponent<EntryPoint>();
        entry.kind = kind;
        entry.displayName = name;
        entry.link = link;
        entry.usableBy = ChaserType.Father;
        entry.traverseTime = kind == EntryKind.Window ? 1.4f : 2.2f;
        entry.traverseNoise = kind == EntryKind.Window ? 4f : 3f;
    }

    static NavMeshLink Link(Transform parent, Vector3 localCenter, string area)
    {
        var linkObject = new GameObject("Link");
        linkObject.transform.SetParent(parent, false);
        linkObject.transform.localPosition = localCenter;
        var link = linkObject.AddComponent<NavMeshLink>();
        link.startPoint = new Vector3(-LinkReach, 0f, 0f);
        link.endPoint = new Vector3(LinkReach, 0f, 0f);
        link.width = 0f;
        link.bidirectional = true;
        link.autoUpdate = false;
        link.area = NavMesh.GetAreaFromName(area);
        return link;
    }

    // ---------------- 숨는 곳 ----------------

    static void Hide(Transform parent, string name, HidingKind kind, Vector3 point, int capacity = 1,
        float enter = 0.6f, float exit = 0.4f, float noise = 3f, float exposed = 0f)
    {
        var go = new GameObject("Hide_" + name);
        go.transform.SetParent(parent);
        go.transform.position = point;
        var reach = go.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        reach.radius = 0.9f;
        reach.center = Vector3.up;

        var spot = go.AddComponent<HidingSpot>();
        spot.kind = kind;
        spot.displayName = name;
        spot.capacity = capacity;
        spot.enterTime = enter;
        spot.exitTime = exit;
        spot.noiseRadius = noise;
        spot.exposedWithin = exposed;
    }

    // ---------------- 캐릭터 ----------------

    static GameObject BuildPlayer(string name, CharacterType type, Vector3 position, Color tint)
    {
        var player = new GameObject(name) { tag = "Player" };
        player.transform.position = position;

        var body = player.AddComponent<CapsuleCollider>();
        body.height = 2f;
        body.radius = 0.35f;
        var rigidbody = player.AddComponent<Rigidbody>(); // 구역·문·숨는 곳 트리거 감지용
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        var agent = player.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1f;
        agent.height = 2f;
        agent.radius = 0.3f;
        agent.speed = 5f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(SpriteSheetGuid));
        var visual = new GameObject("2D Character Visual", typeof(SpriteRenderer));
        visual.transform.SetParent(player.transform, false);
        visual.transform.localPosition = new Vector3(0f, -1f, 0f);
        visual.transform.localScale = Vector3.one * 1.625f;
        visual.GetComponent<SpriteRenderer>().color = tint;
        var sprite = visual.AddComponent<SpriteCharacterVisual>();
        if (sheet != null) sprite.Configure(sheet);
        else Debug.LogWarning("[OTK] 캐릭터 스프라이트 시트를 찾지 못했습니다.");

        var movement = player.AddComponent<PlayerMovement>();
        movement.cameraRelative = true;
        movement.moveSpeed = 3.2f;
        movement.runSpeedMultiplier = 1.75f;
        player.AddComponent<PlayerStamina>();
        player.AddComponent<FootstepNoise>();
        player.AddComponent<Inventory>();
        player.AddComponent<PlayerInteraction>().myType = type;
        player.AddComponent<CharacterStatus>();
        return player;
    }

    static void BuildTestChaser()
    {
        var points = new GameObject("Patrol Points").transform;
        Vector3[] route =
        {
            new Vector3(-1.8f, 0f, -1.5f), new Vector3(6.5f, 0f, -1f), new Vector3(7f, 0f, -4.8f), new Vector3(0f, 0f, -5.2f),
            new Vector3(1.8f, 0f, 1.5f), new Vector3(0f, 0f, 5.2f), new Vector3(6f, 0f, 4.8f), new Vector3(6.5f, 0f, -1f),
        };
        var transforms = new List<Transform>();
        for (int i = 0; i < route.Length; i++)
        {
            var p = new GameObject("Point " + i).transform;
            p.SetParent(points);
            p.position = route[i];
            transforms.Add(p);
        }

        var chaser = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        chaser.name = "시험용 추격자 (경찰 규칙)";
        chaser.transform.position = new Vector3(1.8f, 1f, -1.5f);
        var renderer = chaser.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(timberMat) { name = "ChaserRed", color = new Color(0.85f, 0.15f, 0.15f) };
        AssetDatabase.CreateAsset(renderer.sharedMaterial, TargetFolder + "/ChaserRed.mat");

        // 바라보는 방향 표시
        Visual(chaser.transform, "Facing", new Vector3(0f, 0.5f, 0.45f), new Vector3(0.25f, 0.12f, 0.3f), glassMat);

        var agent = chaser.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1f;
        agent.height = 2f;
        agent.radius = 0.3f;
        agent.angularSpeed = 540f;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.2f;

        var motor = chaser.AddComponent<ChaserMotor>();
        motor.chaserType = ChaserType.Police;
        chaser.AddComponent<ChaserSenses>();
        chaser.AddComponent<TestChaser>().patrolPoints = transforms.ToArray();
    }

    // ---------------- 기타 ----------------

    static void LoadMaterials()
    {
        wallMat = PrototypeSceneBuilder.LoadMaterial("Wall");
        floorMat = PrototypeSceneBuilder.LoadMaterial("Floor");
        timberMat = PrototypeSceneBuilder.LoadMaterial("DemoTimber");
        fabricMat = PrototypeSceneBuilder.LoadMaterial("DemoFabric");
        linenMat = PrototypeSceneBuilder.LoadMaterial("DemoLinen");
        glassMat = PrototypeSceneBuilder.LoadMaterial("TerminalScreen");
        ventMat = PrototypeSceneBuilder.LoadMaterial("Terminal");
    }

    static void AddToBuildSettings()
    {
        // 프로토타입 씬은 첫 번째 그대로 두고, 테스트 씬은 뒤에 추가 (잡혔을 때 씬 재시작에 필요)
        // 경로로 다시 만들어서 GUID를 현재 값으로 맞춥니다.
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != TargetScene)
            .Select(s => new EditorBuildSettingsScene(s.path, s.enabled)).ToList();
        scenes.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
