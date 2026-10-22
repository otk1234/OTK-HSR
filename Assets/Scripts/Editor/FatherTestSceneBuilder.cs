using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Cinemachine;
using Object = UnityEngine.Object;
using B = ChaseTestSceneBuilder;

// 아버지 AI 테스트 씬(1층 집 + 마당)을 코드로 만듭니다. 벽·문·창문·숨는 곳 도우미는 추격 테스트 씬 생성기와 같이 씀.
//
//  z=9  ┌───창문────┬──────────┬───창문────┐
//       │  아이방    │   안방    │   서재    │   북쪽 방은 모두 복도에서 문 하나로만 들어감
//       │(환풍구·창문)│ (우회로 없음)│ (창문만)  │    → 잠긴 문 앞에서: 아이방=짧은 우회, 서재=마당을 도는 먼 우회, 안방=우회 불가(파괴)
//  z=3  ├──문─환풍구─┼────문────┼────문────┤
//       │ 서쪽 복도  ═  중앙 홀   ═  동쪽 복도  │   ═ = 문 없는 통로 (복도는 한 줄로 이어짐)
// 현관문 │            │  (탁자)   │           │
//  z=-3 ├────문────┼────문────┼──문─환풍구┤
//       │   주방    문   식당    문  세탁실   창문
//       │           │          │           │   남쪽 방들은 서로 문으로 이어져 복도와 나란한 두 번째 길 → 순환 동선(예측·길목 차단)
//  z=-9 └───창문────┴──────────┴──────────┘
//      x=-12      x=-4       x=4        x=12      (밖은 마당, 현관문은 서쪽)
public static class FatherTestSceneBuilder
{
    public const string TargetScene = "Assets/Scenes/FatherTest.unity";
    const string TargetFolder = "Assets/Scenes/FatherTest";

    struct RoomDef
    {
        public string id, name; public float minX, maxX, minZ, maxZ; public Color floor;
        public RoomDef(string id, string name, float minX, float maxX, float minZ, float maxZ, Color floor)
        { this.id = id; this.name = name; this.minX = minX; this.maxX = maxX; this.minZ = minZ; this.maxZ = maxZ; this.floor = floor; }
    }

    static readonly RoomDef[] Rooms =
    {
        new RoomDef("KidRoom",   "아이방",   -12f, -4f,  3f,  9f, new Color(0.66f, 0.56f, 0.44f)),
        new RoomDef("Master",    "안방",     -4f,  4f,  3f,  9f, new Color(0.56f, 0.42f, 0.34f)),
        new RoomDef("Study",     "서재",      4f, 12f,  3f,  9f, new Color(0.34f, 0.42f, 0.37f)),
        new RoomDef("WestHall",  "서쪽 복도", -12f, -4f, -3f,  3f, new Color(0.60f, 0.55f, 0.47f)),
        new RoomDef("CenterHall","중앙 홀",   -4f,  4f, -3f,  3f, new Color(0.64f, 0.61f, 0.56f)),
        new RoomDef("EastHall",  "동쪽 복도",  4f, 12f, -3f,  3f, new Color(0.57f, 0.53f, 0.46f)),
        new RoomDef("Kitchen",   "주방",     -12f, -4f, -9f, -3f, new Color(0.78f, 0.75f, 0.62f)),
        new RoomDef("Dining",    "식당",      -4f,  4f, -9f, -3f, new Color(0.68f, 0.58f, 0.44f)),
        new RoomDef("Laundry",   "세탁실",     4f, 12f, -9f, -3f, new Color(0.58f, 0.67f, 0.71f)),
    };

    static Material fatherMat;

    [MenuItem("OTK/아버지 테스트 씬 다시 만들기")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("아버지 테스트 씬 다시 만들기",
                $"{TargetScene}을(를) 새로 만듭니다. 직접 수정한 내용은 사라집니다.", "만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        FatherTestSceneValidator.Validate(true);
    }

    [MenuItem("OTK/아버지 테스트 씬 열기")]
    public static void OpenTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 12f);
        }
    }

    // Unity.exe -batchmode -executeMethod FatherTestSceneBuilder.BuildFromCommandLine (0=통과, 2=점검 실패)
    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(FatherTestSceneValidator.Validate(false) ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // 명령줄: -executeMethod FatherTestSceneBuilder.CaptureOverview -overviewPath <파일>
    public static void CaptureOverview() => B.CaptureScene(TargetScene, "FatherTest_overview.png", 12.5f);

    public static void Build()
    {
        if (NavMesh.GetAreaFromName(EntryPoint.DoorArea) < 0 || NavMesh.GetAreaFromName(EntryPoint.WindowArea) < 0 || NavMesh.GetAreaFromName(EntryPoint.VentArea) < 0)
            throw new Exception("NavMesh Areas에 Door / Window / Vent가 없습니다 (ProjectSettings/NavMeshAreas.asset).");

        // 씬 파일은 지우지 않고 덮어써서 GUID를 유지합니다.
        AssetDatabase.DeleteAsset(TargetFolder);
        AssetDatabase.CreateFolder("Assets/Scenes", "FatherTest");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        B.assetFolder = TargetFolder;
        try
        {
            B.CreateMaterials();
            fatherMat = B.CreateMaterial("Father", new Color(0.42f, 0.16f, 0.48f));

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
            cameraObject.transform.SetPositionAndRotation(new Vector3(8f, 1.5f, 6f) - topDown * Vector3.forward * 20f, topDown);

            // 2) 맵 → NavMesh 굽기 (문·통로·캐릭터는 굽고 난 뒤에)
            Transform map = new GameObject("Map").transform;
            B.Box(map, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(34f, 0.2f, 28f), B.groundMat);
            BuildWalls(map);
            BuildFurniture(map);
            B.BakeNavMesh("FatherTest NavMesh");
            BuildFloors(map);

            // 3) 구역
            Transform zones = new GameObject("Room Cameras").transform;
            foreach (RoomDef room in Rooms)
                PrototypeSceneBuilder.BuildRoomZone(zones, room.id, room.name, room.minX, room.maxX, room.minZ, room.maxZ, priority: 10);
            PrototypeSceneBuilder.BuildRoomZone(zones, "Yard", "마당", -17f, 17f, -14f, 14f, buildingId: "Outside");

            // 4) 출입 경로
            Transform entries = new GameObject("Entry Points").transform;
            B.Door(entries, "아이방 문", B.Axis.AlongX, 3f, -8f);
            B.Door(entries, "안방 문", B.Axis.AlongX, 3f, 0f);
            B.Door(entries, "서재 문", B.Axis.AlongX, 3f, 8f);
            B.Door(entries, "주방 문", B.Axis.AlongX, -3f, -8f);
            B.Door(entries, "식당 문", B.Axis.AlongX, -3f, 0f);
            B.Door(entries, "세탁실 문", B.Axis.AlongX, -3f, 8f);
            B.Door(entries, "주방-식당 문", B.Axis.AlongZ, -4f, -6f);
            B.Door(entries, "식당-세탁실 문", B.Axis.AlongZ, 4f, -6f);
            B.Door(entries, "현관문", B.Axis.AlongZ, -12f, 0f);

            B.Passage(entries, "아이방 창문", EntryKind.Window, B.Axis.AlongX, 9f, -8f);
            B.Passage(entries, "서재 창문", EntryKind.Window, B.Axis.AlongX, 9f, 8f);
            B.Passage(entries, "주방 창문", EntryKind.Window, B.Axis.AlongX, -9f, -8f);
            B.Passage(entries, "세탁실 창문", EntryKind.Window, B.Axis.AlongZ, 12f, -6f);
            B.Passage(entries, "아이방 환풍구", EntryKind.Vent, B.Axis.AlongX, 3f, -5.5f);
            B.Passage(entries, "세탁실 환풍구", EntryKind.Vent, B.Axis.AlongX, -3f, 10.5f);

            // 5) 숨는 곳 (9곳)
            Transform hiding = new GameObject("Hiding Spots").transform;
            B.Hide(hiding, "아이방 침대 밑", HidingKind.UnderBed, new Vector3(-9.15f, 0f, 7.2f), enter: 1.0f, exit: 0.7f, noise: 1.5f);
            B.Hide(hiding, "장난감 상자", HidingKind.Closet, new Vector3(-6f, 0f, 7.6f));
            B.Hide(hiding, "안방 옷장", HidingKind.Closet, new Vector3(-2.5f, 0f, 7.9f), capacity: 2);
            B.Hide(hiding, "안방 침대 밑", HidingKind.UnderBed, new Vector3(1.6f, 0f, 7.4f), enter: 1.0f, exit: 0.7f, noise: 1.5f);
            B.Hide(hiding, "서재 커튼", HidingKind.Curtain, new Vector3(10.5f, 0f, 8.3f), enter: 0.3f, exit: 0.25f, noise: 1.5f, exposed: 1.2f);
            B.Hide(hiding, "현관 수납장", HidingKind.Closet, new Vector3(-10.5f, 0f, 2.0f));
            B.Hide(hiding, "팬트리", HidingKind.Closet, new Vector3(-5f, 0f, -7.9f), capacity: 2);
            B.Hide(hiding, "식탁 밑", HidingKind.UnderBed, new Vector3(0f, 0f, -7.6f), enter: 0.8f, exit: 0.5f, noise: 1.5f);
            B.Hide(hiding, "세탁실 수납장", HidingKind.Closet, new Vector3(7f, 0f, -7.9f));

            // 6) 캐릭터 2명 (서로 떨어진 방에서 시작: 조작하지 않는 캐릭터도 노려지는지 보기 좋도록)
            GameObject detective = B.BuildPlayer("탐정 (Player1)", CharacterType.Player1, new Vector3(6f, 1f, 4.5f), Color.white);
            GameObject assistant = B.BuildPlayer("조수 (Player2)", CharacterType.Player2, new Vector3(-7f, 1f, -5f), new Color(0.65f, 0.82f, 1f));

            // 7) 아버지 (중앙 홀에서 시작)
            BuildFather(new Vector3(0f, 1f, -1.2f));

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
            Debug.Log("[OTK] 아버지 테스트 씬 생성 완료: " + TargetScene);
        }
        finally
        {
            B.assetFolder = "Assets/Scenes/ChaseTest";
        }
    }

    // ---------------- 맵 ----------------

    static void BuildWalls(Transform map)
    {
        Transform walls = new GameObject("Walls").transform;
        walls.SetParent(map);
        float door = B.DoorWidth;
        const float arch = 2.4f; // 복도 사이 통로

        // 바깥벽 (현관문은 서쪽)
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, 9f, -12f, 12f);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, -9f, -12f, 12f);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, -12f, -9f, 9f, (0f, door));
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, 12f, -9f, 9f);

        // 안쪽 벽 (괄호 = 문/통로 자리: 중심, 폭)
        B.Wall(walls, B.wallMat, B.Axis.AlongX, 3f, -12f, 12f, (-8f, door), (0f, door), (8f, door));
        B.Wall(walls, B.wallMat, B.Axis.AlongX, -3f, -12f, 12f, (-8f, door), (0f, door), (8f, door));
        B.Wall(walls, B.wallMat, B.Axis.AlongZ, -4f, -9f, 9f, (0f, arch), (-6f, door));
        B.Wall(walls, B.wallMat, B.Axis.AlongZ, 4f, -9f, 9f, (0f, arch), (-6f, door));
    }

    static void BuildFurniture(Transform map)
    {
        Transform furniture = new GameObject("Furniture").transform;
        furniture.SetParent(map);
        // 아이방
        B.Box(furniture, "아이 침대", new Vector3(-10.5f, 0.25f, 7.2f), new Vector3(1.8f, 0.5f, 2.2f), B.linenMat);
        B.TallBox(furniture, "장난감 상자", new Vector3(-6f, 0f, 8.4f), new Vector3(1.2f, 0f, 0.8f), 0.7f, 1.0f, B.hideMat);
        // 안방
        B.Box(furniture, "큰 침대", new Vector3(0f, 0.25f, 7.4f), new Vector3(2.2f, 0.5f, 2.4f), B.linenMat);
        B.TallBox(furniture, "안방 옷장", new Vector3(-2.5f, 0f, 8.6f), new Vector3(1.6f, 0f, 0.6f), 1.4f, 2.2f, B.hideMat);
        // 서재
        B.Box(furniture, "책상", new Vector3(8f, 0.4f, 5.6f), new Vector3(2f, 0.8f, 1f), B.woodMat);
        B.TallBox(furniture, "책장", new Vector3(11.6f, 0f, 5.5f), new Vector3(0.6f, 0f, 2.6f), 1.6f, 2.2f, B.woodMat);
        B.TallBox(furniture, "서재 커튼", new Vector3(10.5f, 0f, 8.85f), new Vector3(1.0f, 0f, 0.1f), 1.6f, 2.4f, B.curtainMat);
        // 복도
        B.TallBox(furniture, "현관 수납장", new Vector3(-10.5f, 0f, 2.6f), new Vector3(1.4f, 0f, 0.5f), 1.4f, 2.2f, B.hideMat);
        B.Box(furniture, "신발장", new Vector3(-11.6f, 0.5f, -2.2f), new Vector3(0.5f, 1f, 1.2f), B.woodMat);
        B.Box(furniture, "홀 탁자", new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f), B.woodMat);
        B.Box(furniture, "화분 선반", new Vector3(11.6f, 0.5f, 0f), new Vector3(0.5f, 1f, 1.4f), B.woodMat);
        // 주방
        B.Box(furniture, "조리대", new Vector3(-11.4f, 0.45f, -6f), new Vector3(1f, 0.9f, 3f), B.woodMat);
        B.TallBox(furniture, "팬트리", new Vector3(-5f, 0f, -8.6f), new Vector3(1.4f, 0f, 0.6f), 1.6f, 2.2f, B.hideMat);
        // 식당
        B.Box(furniture, "식탁", new Vector3(0f, 0.4f, -6.5f), new Vector3(2.4f, 0.8f, 1.2f), B.woodMat);
        // 세탁실
        B.Box(furniture, "세탁기", new Vector3(11.4f, 0.45f, -8.4f), new Vector3(1f, 0.9f, 1f), B.tubMat);
        B.TallBox(furniture, "세탁실 수납장", new Vector3(7f, 0f, -8.6f), new Vector3(1.4f, 0f, 0.6f), 1.6f, 2.2f, B.hideMat);
    }

    static void BuildFloors(Transform map)
    {
        Transform floors = new GameObject("Floors").transform;
        floors.SetParent(map);
        foreach (RoomDef room in Rooms)
        {
            Material material = B.CreateMaterial("Floor_" + room.id, room.floor);
            var center = new Vector3((room.minX + room.maxX) * 0.5f, 0.005f, (room.minZ + room.maxZ) * 0.5f);
            B.Visual(floors, "Floor_" + room.name, center, new Vector3(room.maxX - room.minX, 0.01f, room.maxZ - room.minZ), material);
        }
    }

    // ---------------- 아버지 ----------------

    static void BuildFather(Vector3 position)
    {
        var chaser = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        chaser.name = "아버지";
        chaser.transform.position = position;
        chaser.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        chaser.GetComponent<Renderer>().sharedMaterial = fatherMat;
        B.Visual(chaser.transform, "Facing", new Vector3(0f, 0.5f, 0.45f), new Vector3(0.25f, 0.12f, 0.3f), B.glassMat);

        var agent = chaser.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1f;
        agent.height = 2f;
        agent.radius = 0.3f;
        agent.angularSpeed = 540f;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.2f;

        var motor = chaser.AddComponent<ChaserMotor>();
        motor.chaserType = ChaserType.Father;
        motor.walkSpeed = 2.0f;   // 경찰보다 조금 느리지만 머리를 씀
        motor.runSpeed = 4.1f;   // 플레이어 달리기 5.6: 스태미나를 잘 쓰면 따돌릴 수 있도록

        var senses = chaser.AddComponent<ChaserSenses>();
        senses.viewDistance = 10f;
        senses.viewAngle = 120f;
        senses.hearingMultiplier = 1.2f;

        chaser.AddComponent<FatherAI>();
    }

    static void AddToBuildSettings()
    {
        // 잡혔을 때 씬 재시작에 필요. 기존 순서는 그대로 두고 뒤에 추가.
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != TargetScene)
            .Select(s => new EditorBuildSettingsScene(s.path, s.enabled)).ToList();
        scenes.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
