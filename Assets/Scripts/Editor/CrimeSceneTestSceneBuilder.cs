using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Cinemachine;
using NarrativeGraybox.CameraSystem;
using NarrativeGraybox.Interaction;
using NarrativeGraybox.UI;
using Object = UnityEngine.Object;
using B = ChaseTestSceneBuilder;

// 범행 현장 테스트 씬: 아버지가 이미 살인을 저지르고 떠난 아파트(임시 사건 "302호 살인 사건").
// 조사는 팀원 조사 시스템(InspectionArea, E 조사 → 클로즈업 → 클릭)을 쓰고, 단서 기록·타이머·캐릭터 전용 상호작용은 내 코드.
// 벽·문·캐릭터 도우미는 추격 테스트 씬 생성기와 같이 씀. 추격자는 아직 없음 (제한 시간만 흐름).
//
//  z=6  ┌──────────┬──────┬───현관문──┐
//       │   침실    │ 욕실  │   현관    │   책장(오빠가 밀어 치움) 뒤에 피해자 수첩
//       │ (책장)    │(수건) │ (인터폰)  │   인터폰 녹화기(오빠가 고침) → 녹화 영상
//  z=1  ├───문─────┴─문────┴───문─────┤
//       │        거실        ║   주방   │   ║ = 문 없는 통로
//       │  (시체·피·재떨이)    ║ (찻잔)   │   시체 손톱 밑 섬유는 여동생에게만 보임
//  z=-6 └────────────────────┴─────────┘
//      x=-8                 x=1       x=8
public static class CrimeSceneTestSceneBuilder
{
    public const string TargetScene = "Assets/Scenes/CrimeSceneTest.unity";
    const string TargetFolder = "Assets/Scenes/CrimeSceneTest";

    struct RoomDef
    {
        public string id, name; public float minX, maxX, minZ, maxZ; public Color floor;
        public RoomDef(string id, string name, float minX, float maxX, float minZ, float maxZ, Color floor)
        { this.id = id; this.name = name; this.minX = minX; this.maxX = maxX; this.minZ = minZ; this.maxZ = maxZ; this.floor = floor; }
    }

    static readonly RoomDef[] Rooms =
    {
        new RoomDef("Bedroom",  "침실", -8f, -1f,  1f, 6f, new Color(0.60f, 0.48f, 0.37f)),
        new RoomDef("Bath",     "욕실", -1f,  3f,  1f, 6f, new Color(0.58f, 0.67f, 0.71f)),
        new RoomDef("Entrance", "현관",  3f,  8f,  1f, 6f, new Color(0.45f, 0.45f, 0.47f)),
        new RoomDef("Living",   "거실", -8f,  1f, -6f, 1f, new Color(0.70f, 0.60f, 0.45f)),
        new RoomDef("Kitchen",  "주방",  1f,  8f, -6f, 1f, new Color(0.78f, 0.75f, 0.62f)),
    };

    static Material bloodMat, clothesMat, skinMat, towelMat, paperMat, glassMat, machineMat, redLightMat, greenLightMat, fiberMat, cupMat, strokeMat;

    [MenuItem("OTK/범행 현장 테스트 씬 다시 만들기")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("범행 현장 테스트 씬 다시 만들기",
                $"{TargetScene}을(를) 새로 만듭니다. 직접 수정한 내용은 사라집니다.", "만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        CrimeSceneTestSceneValidator.Validate(true);
    }

    [MenuItem("OTK/범행 현장 테스트 씬 열기")]
    public static void OpenTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 9f);
        }
    }

    // Unity.exe -batchmode -executeMethod CrimeSceneTestSceneBuilder.BuildFromCommandLine (0=통과, 2=점검 실패)
    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(CrimeSceneTestSceneValidator.Validate(false) ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // 명령줄: -executeMethod CrimeSceneTestSceneBuilder.CaptureOverview -overviewPath <파일>
    public static void CaptureOverview() => B.CaptureScene(TargetScene, "CrimeSceneTest_overview.png", 8.5f);

    public static void Build()
    {
        if (NavMesh.GetAreaFromName(EntryPoint.DoorArea) < 0)
            throw new Exception("NavMesh Areas에 Door가 없습니다 (ProjectSettings/NavMeshAreas.asset).");

        // 씬 파일은 지우지 않고 덮어써서 GUID를 유지합니다.
        AssetDatabase.DeleteAsset(TargetFolder);
        AssetDatabase.CreateFolder("Assets/Scenes", "CrimeSceneTest");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        B.assetFolder = TargetFolder;
        try
        {
            B.CreateMaterials();
            CreateMaterials();

            // 1) 빛 · 카메라 (평소엔 구역 고정 카메라, 조사할 때만 팀원 클로즈업 카메라)
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var mainCamera = cameraObject.AddComponent<Camera>();
            mainCamera.orthographic = true;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            cameraObject.AddComponent<AudioListener>();
            var brain = cameraObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            var inspectionCamera = cameraObject.AddComponent<IsometricCameraFollow>();
            var occlusionFader = cameraObject.AddComponent<CameraOcclusionFader>();

            // 2) 맵 (바닥·벽·가구) → NavMesh 굽기. 문·증거·책장·캐릭터는 굽고 난 뒤에 만듭니다.
            Transform map = new GameObject("Map").transform;
            B.Box(map, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(22f, 0.2f, 18f), B.groundMat);
            BuildWalls(map);
            BuildFurniture(map);
            B.BakeNavMesh("CrimeSceneTest NavMesh");
            BuildFloors(map);

            // 3) 구역
            Transform zones = new GameObject("Room Cameras").transform;
            foreach (RoomDef room in Rooms)
                PrototypeSceneBuilder.BuildRoomZone(zones, room.id, room.name, room.minX, room.maxX, room.minZ, room.maxZ, priority: 10);
            PrototypeSceneBuilder.BuildRoomZone(zones, "Outside", "아파트 복도", -11f, 11f, -9f, 9f, buildingId: "Outside");

            // 4) 문
            Transform entries = new GameObject("Entry Points").transform;
            B.Door(entries, "침실 문", B.Axis.AlongX, 1f, -4.5f);
            B.Door(entries, "욕실 문", B.Axis.AlongX, 1f, 0f);
            B.Door(entries, "현관 중문", B.Axis.AlongX, 1f, 5.5f);
            B.Door(entries, "현관문", B.Axis.AlongX, 6f, 5.5f);

            // 5) 범행 흔적 · 조사 지점 · 캐릭터 전용 상호작용
            Transform evidence = new GameObject("Evidence").transform;
            BuildEvidence(evidence);

            // 6) 캐릭터 2명 (0번 = 여동생, 1번 = 오빠). 현관에서 시작
            GameObject sister = B.BuildPlayer(B.PlayerName(CharacterType.Player1), CharacterType.Player1, new Vector3(4.6f, 1f, 4.4f), Color.white);
            GameObject brother = B.BuildPlayer(B.PlayerName(CharacterType.Player2), CharacterType.Player2, new Vector3(5.6f, 1f, 4.9f), new Color(0.65f, 0.82f, 1f));

            // 팀원 조사·HUD
            var hud = new GameObject("HUD").AddComponent<SimpleGameHud>();
            var hudData = new SerializedObject(hud);
            hudData.FindProperty("date").stringValue = "사건 당일";
            hudData.FindProperty("time").stringValue = "21:10";
            hudData.FindProperty("location").stringValue = "현관";
            hudData.FindProperty("character").stringValue = CharacterRoster.NameOf(CharacterType.Player1);
            hudData.ApplyModifiedPropertiesWithoutUndo();
            foreach (GameObject player in new[] { sister, brother })
                player.AddComponent<PlayerInteractor3D>().Configure(inspectionCamera, null, hud);
            inspectionCamera.Configure(sister.transform, new Vector3(-6f, 8f, -10.392305f));
            occlusionFader.Configure(sister.transform);

            // 첫 구역 카메라가 켜지기 전 한 순간도 탑뷰로 보이도록
            Quaternion topDown = PrototypeSceneBuilder.TopDownRotation;
            cameraObject.transform.SetPositionAndRotation(new Vector3(5.5f, 1.5f, 3.5f) - topDown * Vector3.forward * 20f, topDown);

            // 7) 시스템
            PrototypeSceneBuilder.BuildFadeCanvas();
            DialogueManager dialogue = PrototypeSceneBuilder.BuildDialogueCanvas();
            var systems = new GameObject("Game Systems");
            systems.AddComponent<CharacterManager>().players = new[] { sister, brother };
            systems.AddComponent<GameStateManager>();

            var integration = systems.AddComponent<PrototypeIntegration>();
            integration.hud = hud;
            integration.inspectionCamera = inspectionCamera;
            integration.occlusionFader = occlusionFader;
            integration.cameraBrain = brain;
            integration.dialogue = dialogue;
            integration.objectives = new PrototypeIntegration.Objective[0];
            integration.completeMessage = "경찰이 오기 전에 현장을 조사해 단서를 모으자.";

            var timer = systems.AddComponent<CrimeSceneTimer>();
            var director = systems.AddComponent<CrimeSceneDirector>();
            director.timer = timer;
            var board = systems.AddComponent<DeductionBoard>();
            director.board = board;
            board.deduction = EnsureDeductionAsset();
            board.timer = timer;
            systems.AddComponent<InterventionPlanner>().board = board;
            GuiltSystemValidator.EnsureTableAsset(); // 개입 죄책감 행동(종용·가담·막기)이 행동 표에 있도록

            EditorSceneManager.SaveScene(scene, TargetScene);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[OTK] 범행 현장 테스트 씬 생성 완료: " + TargetScene);
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

        // 바깥벽 (현관문은 북쪽)
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, 6f, -8f, 8f, (5.5f, door));
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, -6f, -8f, 8f);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, -8f, -6f, 6f);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, 8f, -6f, 6f);

        // 안쪽 벽 (괄호 = 문/통로 자리: 중심, 폭)
        B.Wall(walls, B.wallMat, B.Axis.AlongX, 1f, -8f, 8f, (-4.5f, door), (0f, door), (5.5f, door));
        B.Wall(walls, B.wallMat, B.Axis.AlongZ, 1f, -6f, 1f, (-2.5f, 2f));   // 거실 ║ 주방
        B.Wall(walls, B.wallMat, B.Axis.AlongZ, -1f, 1f, 6f);                // 침실 | 욕실
        B.Wall(walls, B.wallMat, B.Axis.AlongZ, 3f, 1f, 6f);                 // 욕실 | 현관
    }

    // 길을 막는 가구 (NavMesh 굽기 전에 만듦)
    static void BuildFurniture(Transform map)
    {
        Transform furniture = new GameObject("Furniture").transform;
        furniture.SetParent(map);
        B.TallBox(furniture, "소파", new Vector3(-7.35f, 0f, -3f), new Vector3(0.9f, 0f, 2.2f), 0.6f, 0.9f, B.sofaMat);
        B.TallBox(furniture, "거실 탁자", new Vector3(-5.9f, 0f, -3f), new Vector3(0.8f, 0f, 1.2f), 0.4f, 0.5f, B.woodMat);
        B.TallBox(furniture, "TV장", new Vector3(-3.5f, 0f, -5.7f), new Vector3(2f, 0f, 0.45f), 0.5f, 0.6f, B.woodMat);
        B.TallBox(furniture, "주방 조리대", new Vector3(7.6f, 0f, -2.8f), new Vector3(0.7f, 0f, 4.8f), 0.9f, 1f, B.woodMat);
        B.TallBox(furniture, "식탁", new Vector3(4.4f, 0f, -3f), new Vector3(1.3f, 0f, 1f), 0.75f, 0.8f, B.woodMat);
        B.TallBox(furniture, "침대", new Vector3(-3.2f, 0f, 4.6f), new Vector3(1.6f, 0f, 2.1f), 0.5f, 0.6f, B.linenMat);
        B.TallBox(furniture, "협탁", new Vector3(-4.6f, 0f, 5.55f), new Vector3(0.5f, 0f, 0.45f), 0.55f, 0.6f, B.woodMat);
        B.TallBox(furniture, "옷장", new Vector3(-1.6f, 0f, 5.6f), new Vector3(0.9f, 0f, 0.6f), 1.6f, 2f, B.hideMat);
        B.TallBox(furniture, "욕조", new Vector3(2.2f, 0f, 4.4f), new Vector3(1f, 0f, 2f), 0.55f, 0.6f, B.tubMat);
        B.TallBox(furniture, "세면대", new Vector3(-0.45f, 0f, 5.5f), new Vector3(0.6f, 0f, 0.5f), 0.85f, 0.9f, B.tubMat);
        B.TallBox(furniture, "신발장", new Vector3(3.4f, 0f, 5.3f), new Vector3(0.5f, 0f, 1.2f), 1f, 1.1f, B.woodMat);
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

    // ---------------- 범행 흔적 (임시 사건) ----------------

    static void BuildEvidence(Transform root)
    {
        // 시체: 거실 바닥에 엎드려 쓰러짐 (머리는 동쪽)
        InspectionArea body = Area(root, "시체", new Vector3(-3.6f, 0f, -3.4f), Vector3.back, 1.9f);
        InspectionItem torso = Item(body, "body", "시체",
            "집주인 한정우(임시). 거실 바닥에 엎드려 쓰러져 있다. 몸이 아직 조금 따뜻하다. 범행은 얼마 전이다.",
            new Vector3(-3.6f, 0.13f, -3.4f), new Vector3(0.95f, 0.26f, 0.5f), clothesMat);
        Visual(torso.transform.parent, "다리", new Vector3(-4.55f, 0.1f, -3.25f), new Vector3(0.95f, 0.2f, 0.42f), clothesMat);
        Visual(torso.transform.parent, "팔", new Vector3(-3.4f, 0.08f, -2.95f), new Vector3(0.7f, 0.14f, 0.16f), skinMat);
        // 설명 = 누구나 보는 사실, Insight(...) = 여동생의 통찰 (일부는 추리 근거로 기록, 사용자 결정 2026-10-23)
        Insight(Item(body, "head_wound", "뒤통수의 상처",
            "둔기로 한 번 강하게 맞은 상처. 몸싸움이나 저항한 흔적은 없다.",
            new Vector3(-2.95f, 0.14f, -3.4f), new Vector3(0.3f, 0.26f, 0.3f), skinMat, PrimitiveType.Sphere),
            "등을 보일 만큼 믿은 사람이었다. 아는 사이다.", AcquaintanceEvidence, AcquaintanceTitle);
        Insight(Item(body, "navy_fiber", "손톱 밑 섬유",
            "손톱 밑에 남색 털실이 끼어 있다.",
            new Vector3(-3.05f, 0.09f, -2.95f), new Vector3(0.12f, 0.05f, 0.12f), fiberMat, visibleTo: CharacterTrait.Investigation),
            "...아빠의 남색 외투와 같은 색이다.");
        Visual(root, "피 웅덩이", new Vector3(-2.6f, 0.012f, -3.5f), new Vector3(1.1f, 0.004f, 0.9f), bloodMat, PrimitiveType.Cylinder);

        // 이어진 핏자국: 시체 옆 → 욕실 문 (조사 지점은 문 트리거와 겹치지 않게 문에서 떨어뜨림)
        InspectionArea trail = Area(root, "핏자국", new Vector3(-1.2f, 0f, -1.3f), Vector3.back, 1.8f);
        Insight(Item(trail, "blood_trail", "이어진 핏자국",
            "시체 옆에서 욕실 쪽으로 핏방울이 이어진다.",
            new Vector3(-1.1f, 0.012f, -1.15f), new Vector3(0.35f, 0.006f, 0.35f), bloodMat, PrimitiveType.Cylinder),
            "범인이 피 묻은 손으로 욕실에 간 것 같다.");
        Vector3[] drops = { new(-2.2f, 0f, -2.8f), new(-1.8f, 0f, -2.2f), new(-1.45f, 0f, -1.65f), new(-0.6f, 0f, -0.4f), new(-0.2f, 0f, 0.3f) };
        foreach (Vector3 d in drops)
            Visual(root, "핏방울", d + Vector3.up * 0.011f, new Vector3(0.22f, 0.004f, 0.22f), bloodMat, PrimitiveType.Cylinder);

        // 흉기: 깨진 유리 재떨이 (거실 탁자 옆 바닥)
        InspectionArea weapon = Area(root, "깨진 재떨이", new Vector3(-5.2f, 0f, -1.9f), Vector3.back, 1.4f);
        Insight(Item(weapon, "weapon_ashtray", "깨진 유리 재떨이",
            "묵직한 유리 재떨이가 깨져 있다. 모서리에 피와 머리카락이 묻어 있다.",
            new Vector3(-5.2f, 0.05f, -1.9f), new Vector3(0.32f, 0.08f, 0.32f), glassMat, PrimitiveType.Cylinder),
            "흉기다. 집에 있던 물건을 집어 들었다.");
        Visual(root, "재떨이 조각", new Vector3(-4.95f, 0.02f, -1.7f), new Vector3(0.12f, 0.03f, 0.08f), glassMat);
        Visual(root, "재떨이 조각", new Vector3(-5.4f, 0.02f, -1.65f), new Vector3(0.08f, 0.03f, 0.1f), glassMat);

        // 식탁 위 찻잔 두 개
        InspectionArea table = Area(root, "식탁", new Vector3(4.4f, 0.8f, -3f), Vector3.back, 1.5f);
        Insight(Item(table, "two_cups", "찻잔 두 개",
            "찻잔 두 개. 하나는 반쯤 남았다.",
            new Vector3(4.15f, 0.86f, -3f), new Vector3(0.14f, 0.1f, 0.14f), cupMat, PrimitiveType.Cylinder),
            "피해자는 범인에게 차를 대접했다. 아는 사이였다.", AcquaintanceEvidence, AcquaintanceTitle);
        Visual(root, "찻잔", new Vector3(4.65f, 0.86f, -2.85f), new Vector3(0.14f, 0.1f, 0.14f), cupMat, PrimitiveType.Cylinder);

        // 욕조에 버린 피 묻은 수건
        InspectionArea tub = Area(root, "욕조", new Vector3(2.2f, 0.55f, 4.4f), Vector3.left, 1.6f);
        Insight(Item(tub, "bloody_towel", "피 묻은 수건",
            "피를 닦아 낸 수건이 욕조에 버려져 있다.",
            new Vector3(2.2f, 0.58f, 4.2f), new Vector3(0.6f, 0.06f, 0.4f), towelMat),
            "범인은 서두르지 않고 손을 씻고 나갔다.");
        Visual(root, "수건 핏자국", new Vector3(2.15f, 0.615f, 4.15f), new Vector3(0.25f, 0.01f, 0.2f), bloodMat);

        // 책장 뒤 바닥의 피해자 수첩 → 오빠가 책장을 밀어야 보임
        InspectionArea behindShelf = Area(root, "책장 뒤 바닥", new Vector3(-7.4f, 0f, 3.2f), Vector3.right, 1.4f);
        Insight(Item(behindShelf, "victim_diary", "피해자 수첩",
            "책장과 벽 사이에 떨어진 수첩. 마지막 장: '목요일 밤 9시, 박 선생 댁. 그 일을 털어놓을 것.'",
            new Vector3(-7.45f, 0.03f, 3.2f), new Vector3(0.22f, 0.04f, 0.3f), paperMat),
            "다음은 박 선생일지도 모른다.");
        behindShelf.gameObject.SetActive(false);

        HeavyObject shelf = BuildHeavyObject(root, "책장", new Vector3(-7.45f, 0f, 3.2f), new Vector3(0.5f, 1.8f, 1.6f), new Vector3(0f, 0f, 1.7f));
        shelf.revealWhenMoved = new[] { behindShelf.gameObject };

        // 고장 난 인터폰 녹화기 → 오빠가 고쳐야 녹화 영상을 볼 수 있음
        InspectionArea video = Area(root, "인터폰 녹화 영상", new Vector3(7.75f, 1.3f, 3.5f), Vector3.left, 0.9f, height: 0.4f, distance: 2.2f);
        Insight(Item(video, "intercom_video", "녹화 영상",
            "오늘 밤 8시 40분. 모자를 눌러쓴 남색 외투의 남자가 들어온다.",
            new Vector3(7.77f, 1.3f, 3.5f), new Vector3(0.03f, 0.26f, 0.36f), cupMat),
            "걸음걸이가... 아빠와 똑같다.", GaitEvidence, GaitTitle);
        video.gameObject.SetActive(false);

        RepairableMachine recorder = BuildMachine(root, "인터폰 녹화기", new Vector3(7.85f, 1.3f, 3.5f));
        recorder.revealWhenRepaired = new[] { video.gameObject };

        // ---- 개입 지점 (남매가 정한 방향의 것만 쓸 수 있음, 사건마다 한 번) ----
        // 종용: 침실 협탁의 피해자 휴대전화 (정보 조작 → 여동생이 빠름)
        Visual(root, "피해자 휴대전화", new Vector3(-4.6f, 0.57f, 5.55f), new Vector3(0.1f, 0.02f, 0.18f), machineMat);
        BuildIntervention(root, "피해자 휴대전화", new Vector3(-4.6f, 0f, 4.9f), new Vector3(1.6f, 2f, 1.4f), InterventionKind.Incite, "문자 조작하기",
            "피해자 휴대전화로 박 선생에게 '목요일 약속 장소를 인적 없는 공원으로 바꾸자'는 문자를 보냈다. 아버지가 그곳을 택하게 된다.",
            CharacterTrait.Investigation, 2.5f, 5f, "incite_crime", "문자를 보냈다. 아빠는... 그곳으로 갈 것이다.");

        // 가담: 시체를 욕실로 옮겨 숨김 (힘 → 오빠가 빠름, 여동생은 오래 걸림)
        Intervention hideBody = BuildIntervention(root, "시체", new Vector3(-3.6f, 0f, -3.4f), new Vector3(2.4f, 2f, 2f), InterventionKind.Abet, "욕실로 옮겨 숨기기",
            "시체를 욕실로 옮겨 숨기고 현장을 정리했다. 발견이 늦어진다.",
            CharacterTrait.Strength, 3.5f, 9f, "abet_crime", "시체를 옮겼다... 이제 돌이킬 수 없다.");
        hideBody.hideWhenDone = new[] { body.gameObject };
        hideBody.noiseRadius = 6f;

        // 막기: 식탁 위 편지지로 박 선생에게 경고 편지 (정보 → 여동생이 빠름)
        Visual(root, "편지지", new Vector3(4.75f, 0.79f, -2.72f), new Vector3(0.22f, 0.01f, 0.3f), paperMat);
        Visual(root, "펜", new Vector3(4.92f, 0.8f, -2.7f), new Vector3(0.02f, 0.02f, 0.16f), machineMat);
        BuildIntervention(root, "편지지", new Vector3(4.4f, 0f, -1.9f), new Vector3(1.8f, 2f, 1.4f), InterventionKind.Prevent, "경고 편지 쓰기",
            "박 선생에게 '목요일 밤에는 누구도 집에 들이지 마세요'라는 경고 편지를 썼다.",
            CharacterTrait.Investigation, 2.5f, 5f, "prevent_crime", "경고 편지를 썼다. 박 선생에게 전해야 한다.");
    }

    static Intervention BuildIntervention(Transform root, string name, Vector3 floor, Vector3 reachSize, InterventionKind kind, string actionLabel,
        string summary, CharacterTrait skilledTrait, float skilledTime, float otherTime, string guiltActionId, string doneMessage)
    {
        var go = new GameObject($"개입 ({Intervention.KindName(kind)}): {name}");
        go.transform.SetParent(root);
        go.transform.position = floor;
        var reach = go.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1f, 0f);
        reach.size = reachSize;

        var intervention = go.AddComponent<Intervention>();
        intervention.displayName = name;
        intervention.kind = kind;
        intervention.actionLabel = actionLabel;
        intervention.summary = summary;
        intervention.skilledTrait = skilledTrait;
        intervention.skilledTime = skilledTime;
        intervention.otherTime = otherTime;
        intervention.guiltActionId = guiltActionId;
        intervention.doneMessage = doneMessage;
        intervention.noiseRadius = 2f;
        return intervention;
    }

    static HeavyObject BuildHeavyObject(Transform root, string name, Vector3 floor, Vector3 size, Vector3 pushOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root);
        go.transform.position = floor;
        var reach = go.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1f, 0f);
        reach.size = new Vector3(size.x + 2.2f, 2f, size.z + 1.4f);

        GameObject body = B.Box(go.transform, name + " 본체", floor + Vector3.up * (size.y * 0.5f), size, B.woodMat);
        var obstacle = body.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        B.Visual(body.transform, "선반", new Vector3(0.3f, 0.15f, 0f), new Vector3(0.6f, 0.04f, 0.92f), B.hideMat);
        B.Visual(body.transform, "선반", new Vector3(0.3f, -0.15f, 0f), new Vector3(0.6f, 0.04f, 0.92f), B.hideMat);

        var heavy = go.AddComponent<HeavyObject>();
        heavy.displayName = name;
        heavy.requiredTrait = CharacterTrait.Strength;
        heavy.failMessage = "너무 무거워서 꿈쩍도 하지 않는다... 오빠에게 부탁하자.";
        heavy.pushOffset = pushOffset;
        heavy.noiseRadius = 6f;
        return heavy;
    }

    static RepairableMachine BuildMachine(Transform root, string name, Vector3 wallPoint)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root);
        go.transform.position = new Vector3(wallPoint.x - 0.6f, 0f, wallPoint.z);
        var reach = go.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1f, 0f);
        reach.size = new Vector3(2f, 2f, 1.8f);

        Visual(go.transform, "녹화기 본체", wallPoint, new Vector3(0.12f, 0.4f, 0.5f), machineMat);
        Visual(go.transform, "벗겨진 덮개", new Vector3(wallPoint.x - 0.3f, 0.03f, wallPoint.z + 0.35f), new Vector3(0.3f, 0.02f, 0.4f), machineMat);
        Renderer light = Visual(go.transform, "표시등", wallPoint + new Vector3(-0.07f, 0.14f, 0.16f), new Vector3(0.04f, 0.06f, 0.06f), redLightMat);

        var machine = go.AddComponent<RepairableMachine>();
        machine.displayName = name;
        machine.requiredTrait = CharacterTrait.Mechanics;
        machine.failMessage = "녹화기 덮개 안의 전선이 끊어져 있다. 나는 고칠 줄 모른다... 오빠라면 할 수 있을지도.";
        machine.repairedMessage = "끊어진 전선을 이어 붙였다. 녹화 화면이 다시 켜졌다.";
        machine.indicator = light;
        machine.repairedMaterial = greenLightMat;
        machine.noiseRadius = 3f;
        return machine;
    }

    // 조사 지점 (팀원 InspectionArea + FocusableObject). front = 클로즈업 카메라가 놓일 방향
    static InspectionArea Area(Transform root, string title, Vector3 center, Vector3 front, float viewSize, float height = 2.6f, float distance = 3f)
    {
        var go = new GameObject("조사: " + title);
        go.transform.SetParent(root);
        go.transform.position = center;
        var area = go.AddComponent<InspectionArea>(); // FocusableObject는 RequireComponent로 같이 붙음
        area.Title = title;
        area.Items = new InspectionItem[0];
        go.GetComponent<FocusableObject>().ConfigureFocusView(front, distance, height, viewSize);
        return area;
    }

    // 조사 항목 하나 = 팀원 InspectionItem(클릭 대상, 마우스를 올리면 외곽선) + 내 Clue(단서 기록)
    static InspectionItem Item(InspectionArea area, string clueId, string title, string description, Vector3 position, Vector3 size,
        Material material, PrimitiveType shape = PrimitiveType.Cube, CharacterTrait visibleTo = CharacterTrait.None)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = title;
        go.transform.SetParent(area.transform);
        go.transform.position = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;

        var stroke = GameObject.CreatePrimitive(shape);
        stroke.name = "Outline";
        Object.DestroyImmediate(stroke.GetComponent<Collider>());
        stroke.transform.SetParent(go.transform, false);
        stroke.transform.localScale = Vector3.one * 1.18f;
        stroke.GetComponent<Renderer>().sharedMaterial = strokeMat;

        var item = go.AddComponent<InspectionItem>();
        item.Configure(title, description, false, new[] { stroke.GetComponent<Renderer>() });
        var clue = go.AddComponent<Clue>();
        clue.clueId = clueId;
        clue.visibleToTrait = visibleTo;

        area.Items = area.Items.Concat(new[] { item }).ToArray();
        return item;
    }

    // 여동생 통찰로만 얻는 추리 근거 (Case302_Deduction의 정답 근거)
    public const string AcquaintanceEvidence = "insight_acquaintance", AcquaintanceTitle = "통찰: 아는 사이";
    public const string GaitEvidence = "insight_gait", GaitTitle = "통찰: 아빠의 걸음걸이";

    // 조사 항목에 통찰 문장(+ 근거)을 붙임
    static InspectionItem Insight(InspectionItem item, string text, string evidenceId = null, string evidenceTitle = null)
    {
        var insight = item.gameObject.AddComponent<TraitInsight>();
        insight.requiredTrait = CharacterTrait.Insight;
        insight.label = "통찰";
        insight.extraText = text;
        insight.evidenceId = evidenceId;
        insight.evidenceTitle = evidenceTitle;
        return item;
    }

    // 충돌체 없는 표시용 도형 (월드 좌표)
    static Renderer Visual(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    static void CreateMaterials()
    {
        bloodMat      = B.CreateMaterial("Blood",       new Color(0.42f, 0.03f, 0.03f));
        clothesMat    = B.CreateMaterial("Body_Clothes", new Color(0.26f, 0.28f, 0.34f));
        skinMat       = B.CreateMaterial("Body_Skin",   new Color(0.82f, 0.70f, 0.60f));
        towelMat      = B.CreateMaterial("Towel",       new Color(0.92f, 0.90f, 0.86f));
        paperMat      = B.CreateMaterial("Paper",       new Color(0.93f, 0.90f, 0.78f));
        glassMat      = B.CreateMaterial("Ashtray",     new Color(0.62f, 0.78f, 0.80f));
        machineMat    = B.CreateMaterial("Machine",     new Color(0.18f, 0.18f, 0.20f));
        redLightMat   = B.CreateMaterial("Light_Red",   new Color(0.95f, 0.10f, 0.10f));
        greenLightMat = B.CreateMaterial("Light_Green", new Color(0.15f, 0.90f, 0.30f));
        fiberMat      = B.CreateMaterial("Navy_Fiber",  new Color(0.10f, 0.14f, 0.40f));
        cupMat        = B.CreateMaterial("Cup",         new Color(0.95f, 0.95f, 0.93f));
        strokeMat     = PrototypeSceneBuilder.LoadMaterial("InspectStroke");
    }

    // 추리 데이터는 씬 폴더 밖에 두어, 씬을 다시 만들어도 기획이 고친 내용이 남게 함 (없을 때만 임시 사건으로 만듦)
    public const string DeductionAsset = "Assets/Data/Cases/Case302_Deduction.asset";

    public static CaseDeduction EnsureDeductionAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<CaseDeduction>(DeductionAsset);
        if (asset != null) return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder("Assets/Data/Cases")) AssetDatabase.CreateFolder("Assets/Data", "Cases");
        asset = ScriptableObject.CreateInstance<CaseDeduction>();
        asset.FillCase302();
        AssetDatabase.CreateAsset(asset, DeductionAsset);
        AssetDatabase.SaveAssets();
        Debug.Log("[OTK] 임시 사건 추리 데이터 생성: " + DeductionAsset);
        return asset;
    }

    static void AddToBuildSettings()
    {
        // 프로토타입 씬은 첫 번째 그대로 두고, 테스트 씬은 뒤에 추가 (잡혔을 때 씬 재시작에 필요)
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != TargetScene)
            .Select(s => new EditorBuildSettingsScene(s.path, s.enabled)).ToList();
        scenes.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
