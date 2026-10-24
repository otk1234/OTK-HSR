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

// 아지트 씬: 남매가 숨어 지내는 집 다락방 (임시 맵, 레벨 담당 씬이 오면 프리팹으로 옮김).
// 임무 사이에 장비·현황을 점검하는 거점. 추격자 없음(항상 추리 상태), 방 하나 = 아이소메트릭 카메라 하나.
// 벽·캐릭터 도우미는 추격 테스트 씬 생성기와 같이 씀.
//
//  z=3.5 ┌────────────────────────────┐   북쪽·동쪽 = 지붕 경사 (카메라 반대편이라 보임)
//        │      [칠판]       [작업대]   │   칠판: 아버지의 살인 추적 (사건별 단서·추리·개입·다음 표적)
//        │                       [상자] │   상자: 남매 공용 소지품 보관 (칸 제한 없음)
//        │  [매트리스] [지도 테이블][램프] │   작업대: 장비·현황 점검. 매트리스 = 휴식, 남매는 쉬는 자리 5곳 중 하나에
//        │       [해치]                 │   해치: 아래층으로 (지금은 표시만, 임무 이동은 나중에)
//  z=-3.5└────────────────────────────┘
//       x=-5                         x=5
public static class HideoutSceneBuilder
{
    public const string TargetScene = "Assets/Scenes/Hideout.unity";
    const string TargetFolder = "Assets/Scenes/Hideout";

    public const string RoomId = "Attic";
    public const string BoardName = "칠판";
    public const string StashName = "소지품 상자";
    public const string DeskName = "작업대";
    public const string HatchName = "해치";
    public const string TableName = "지도 테이블";

    const float MinX = -5f, MaxX = 5f, MinZ = -3.5f, MaxZ = 3.5f;
    const float CameraFill = 0.78f;   // 구역 전체가 들어가는 기본 크기 대비 (작을수록 가까이)

    static Material plankMat, beamMat, roofMat, boardMat, chalkMat, trunkMat, metalMat, lampMat, paperMat, mapMat, riverMat, roadMat, blockMat;

    [MenuItem("OTK/아지트 씬 다시 만들기")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("아지트 씬 다시 만들기",
                $"{TargetScene}을(를) 새로 만듭니다. 직접 수정한 내용은 사라집니다.", "만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        HideoutSceneValidator.Validate(true);
    }

    [MenuItem("OTK/아지트 씬 열기")]
    public static void OpenScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.LookRotation(new Vector3(1f, -1f, 1f)), 6f);
        }
    }

    // 직접 플레이: 아지트 씬을 열고 바로 플레이 시작 (메뉴, 또는 Unity.exe -executeMethod HideoutSceneBuilder.Play)
    [MenuItem("OTK/아지트 플레이")]
    public static void Play()
    {
        // 에디터가 막 켜졌을 때 다른 시작 처리(기본 씬 열기 등)가 끝난 뒤에 열도록 한 번 미룸
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
            Debug.Log("[아지트 플레이] 씬을 열고 플레이 시작");
            EditorApplication.EnterPlaymode();
        };
    }

    // Unity.exe -batchmode -executeMethod HideoutSceneBuilder.BuildFromCommandLine (0=통과, 2=점검 실패)
    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(HideoutSceneValidator.Validate(false) ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // 명령줄: -executeMethod HideoutSceneBuilder.CaptureOverview -overviewPath <파일> (위에서 본 배치)
    public static void CaptureOverview() => B.CaptureScene(TargetScene, "Hideout_overview.png", 5f);

    // 명령줄: -executeMethod HideoutSceneBuilder.CaptureGameView -overviewPath <파일> (게임 카메라 = 아이소메트릭 그대로)
    public static void CaptureGameView()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-overviewPath");
        string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Hideout_game.png";
        EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        CinemachineCamera roomCam = Object.FindObjectsByType<CameraTrigger>().First(t => t.buildingId == "Hideout").virtualCamera.GetComponent<CinemachineCamera>();   // 다락방 (임시 임무 장소 말고)
        var go = new GameObject("Capture Camera");
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = roomCam.Lens.OrthographicSize;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
        go.transform.SetPositionAndRotation(roomCam.transform.position, roomCam.transform.rotation);

        var target = new RenderTexture(1600, 900, 24);
        camera.targetTexture = target;
        camera.aspect = 16f / 9f;
        ShaderUtil.allowAsyncCompilation = false;
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(go);
        Debug.Log("[OTK] 아지트 게임 화면 캡처 저장: " + path);
        EditorApplication.Exit(0);
    }

    // 명령줄: -executeMethod HideoutSceneBuilder.CaptureCloseups -overviewPath <폴더> (칠판·상자·작업대 확대 화면, 예시 기록 채운 상태, 저장 안 함)
    public static void CaptureCloseups()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-overviewPath");
        string folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : ".";
        EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        HideoutSampleData.Fill(Object.FindAnyObjectByType<HideoutDirector>().sampleCase);
        Object.FindAnyObjectByType<Chalkboard>().RefreshPhotos();
        Object.FindAnyObjectByType<MissionTable>().RefreshPins();

        foreach (HideoutStation station in Object.FindObjectsByType<HideoutStation>())
        {
            var focus = station.GetComponent<FocusableObject>();
            var go = new GameObject("Capture Camera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = focus.FocusedViewSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            go.transform.SetPositionAndRotation(focus.CameraPosition, Quaternion.LookRotation(focus.LookPosition - focus.CameraPosition, Vector3.up));
            var target = new RenderTexture(1600, 900, 24);
            camera.targetTexture = target;
            camera.aspect = 16f / 9f;
            ShaderUtil.allowAsyncCompilation = false;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, $"closeup_{station.GetType().Name}.png"), image.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(go);
        }
        Debug.Log("[OTK] 아지트 확대 화면 캡처 저장: " + folder);
        EditorApplication.Exit(0);
    }

    public static void Build()
    {
        // 씬 파일은 지우지 않고 덮어써서 GUID를 유지합니다.
        AssetDatabase.DeleteAsset(TargetFolder);
        AssetDatabase.CreateFolder("Assets/Scenes", "Hideout");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        B.assetFolder = TargetFolder;
        try
        {
            B.CreateMaterials();
            CreateMaterials();

            // 1) 빛 · 카메라 (다락방이라 조금 어둡고 따뜻하게)
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.transform.rotation = Quaternion.Euler(50f, -40f, 0f);

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

            // 2) 맵 (바닥·벽·가구) → NavMesh 굽기 → 표시용 바닥·지붕
            Transform map = new GameObject("Map").transform;
            B.Box(map, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(14f, 0.2f, 11f), B.groundMat);
            BuildWalls(map);
            Transform hideout = new GameObject("Hideout").transform;
            BuildFurniture(map, hideout);
            MissionSite[] sites = BuildMissionSites(map);
            B.BakeNavMesh("Hideout NavMesh", new Bounds(new Vector3(40f, 1f, 0f), new Vector3(100f, 6f, 16f)));
            BuildDecor(map);
            HideoutSpot[] spots = BuildRestAndSpots(hideout);

            // 3) 구역: 다락방 하나 = 아이소메트릭 카메라 하나
            Transform zones = new GameObject("Room Cameras").transform;
            CameraTrigger attic = PrototypeSceneBuilder.BuildRoomZone(zones, RoomId, "다락방", MinX, MaxX, MinZ, MaxZ,
                PrototypeSceneBuilder.View.Isometric, priority: 10, buildingId: "Hideout");
            // 방이 화면 가로를 꽉 채우도록 당겨 찍음 (위아래로는 앞쪽 벽 모서리와 지붕 끝만 잘림, 사용자 요청 2026-10-24)
            var atticCam = attic.virtualCamera.GetComponent<CinemachineCamera>();
            LensSettings lens = atticCam.Lens;
            lens.OrthographicSize *= CameraFill;
            atticCam.Lens = lens;
            // 임시 임무 장소: 장소마다 구역 하나 (건물이 다르므로 남매끼리 물건을 건넬 수 없음)
            foreach (MissionSite site in sites)
            {
                Vector3 c = site.transform.position;
                PrototypeSceneBuilder.BuildRoomZone(zones, "Site_" + site.missionId, site.name, c.x - SiteHalf.x, c.x + SiteHalf.x, c.z - SiteHalf.y, c.z + SiteHalf.y,
                    PrototypeSceneBuilder.View.Isometric, priority: 10, buildingId: site.missionId);
            }

            // 4) 캐릭터 2명 (0번 = 여동생, 1번 = 오빠). 해치 옆에서 시작
            GameObject sister = B.BuildPlayer(B.PlayerName(CharacterType.Player1), CharacterType.Player1, new Vector3(-2.6f, 1f, -1.3f), Color.white);
            GameObject brother = B.BuildPlayer(B.PlayerName(CharacterType.Player2), CharacterType.Player2, new Vector3(-0.6f, 1f, -2.4f), new Color(0.65f, 0.82f, 1f));

            // 팀원 조사·HUD (소지품 R)
            var hud = new GameObject("HUD").AddComponent<SimpleGameHud>();
            var hudData = new SerializedObject(hud);
            hudData.FindProperty("date").stringValue = "임무 사이";
            hudData.FindProperty("time").stringValue = "--:--";
            hudData.FindProperty("location").stringValue = "다락방 (아지트)";
            hudData.FindProperty("character").stringValue = CharacterRoster.NameOf(CharacterType.Player1);
            hudData.ApplyModifiedPropertiesWithoutUndo();
            foreach (GameObject player in new[] { sister, brother })
                player.AddComponent<PlayerInteractor3D>().Configure(inspectionCamera, null, hud);
            inspectionCamera.Configure(sister.transform, new Vector3(-6f, 8f, -10.392305f));
            occlusionFader.Configure(sister.transform);

            // 구역 카메라가 켜지기 전 한 순간도 같은 각도로
            Quaternion iso = Quaternion.LookRotation(new Vector3(1f, -1f, 1f));
            cameraObject.transform.SetPositionAndRotation(Vector3.zero - iso * Vector3.forward * 20f, iso);

            // 5) 시스템 (추격자 없음 → GameStateManager는 항상 추리 상태)
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
            integration.completeMessage = "다락방. 다음 일을 준비하자.";
            var director = systems.AddComponent<HideoutDirector>();
            director.sampleCase = AssetDatabase.LoadAssetAtPath<CaseDeduction>(CaseDataPath);
            director.missionTable = builtTable;
            systems.AddComponent<HideoutResidents>().spots = spots;
            builtTable.sites = sites;
            GuiltSystemValidator.EnsureTableAsset(); // 예시 기록의 종용 죄책감 행동이 행동 표에 있도록

            EditorSceneManager.SaveScene(scene, TargetScene);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[OTK] 아지트 씬 생성 완료: " + TargetScene);
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
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, MaxZ, MinX, MaxX);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongX, MinZ, MinX, MaxX);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, MinX, MinZ, MaxZ);
        B.Wall(walls, B.outerWallMat, B.Axis.AlongZ, MaxX, MinZ, MaxZ);
    }

    // 길을 막는 가구 (NavMesh 굽기 전에 만듦). 칠판·상자·작업대는 Hideout 아래의 조사 지점(확대) + 가구 본체
    static void BuildFurniture(Transform map, Transform hideout)
    {
        Transform furniture = new GameObject("Furniture").transform;
        furniture.SetParent(map);

        // 칠판: 북쪽 벽 앞, 이동식 받침. 칠판 면(앞면 z=3.0)에 사진이 붙음
        Transform boardRoot = StationRoot(hideout, BoardName, new Vector3(-1.2f, 1.54f, 3.0f));
        GameObject board = B.TallBox(boardRoot, BoardName + " 본체", new Vector3(-1.2f, 0f, 3.1f), new Vector3(2.6f, 0f, 0.25f), 0.35f, 1.6f, beamMat);
        B.Visual(board.transform, "칠판 면", new Vector3(0f, 3.9f, -0.2f), new Vector3(0.95f, 4.2f, 0.4f), boardMat);
        B.Visual(board.transform, "분필 받침", new Vector3(0f, 1.6f, -1.4f), new Vector3(0.95f, 0.3f, 1.2f), beamMat);
        var surface = new GameObject("사진 붙일 면").transform;
        surface.SetParent(boardRoot, false);   // 칠판 앞면 가운데 (= 조사 지점 위치)

        // 소지품 상자: 동쪽 벽 앞의 낡은 트렁크
        Transform stashRoot = StationRoot(hideout, StashName, new Vector3(4.35f, 0.35f, 0.2f));
        GameObject stash = B.TallBox(stashRoot, StashName + " 본체", new Vector3(4.35f, 0f, 0.2f), new Vector3(0.75f, 0f, 1.3f), 0.6f, 0.7f, trunkMat);
        B.Visual(stash.transform, "띠", new Vector3(0f, 0f, 0.3f), new Vector3(1.04f, 1.04f, 0.08f), metalMat);
        B.Visual(stash.transform, "띠", new Vector3(0f, 0f, -0.3f), new Vector3(1.04f, 1.04f, 0.08f), metalMat);
        B.Visual(stash.transform, "자물쇠", new Vector3(-0.52f, 0.2f, 0f), new Vector3(0.06f, 0.25f, 0.12f), metalMat);

        // 작업대: 북동쪽 구석, 위에 공구·손전등
        Transform deskRoot = StationRoot(hideout, DeskName, new Vector3(3.2f, 0.85f, 3.0f));
        GameObject desk = B.TallBox(deskRoot, DeskName + " 본체", new Vector3(3.2f, 0f, 3.0f), new Vector3(1.8f, 0f, 0.75f), 0.85f, 0.95f, B.woodMat);
        B.Visual(desk.transform, "공구", new Vector3(-0.25f, 0.56f, 0f), new Vector3(0.25f, 0.1f, 0.35f), metalMat);
        B.Visual(desk.transform, "손전등", new Vector3(0.2f, 0.56f, -0.1f), new Vector3(0.12f, 0.1f, 0.15f), lampMat);
        B.Visual(desk.transform, "공책", new Vector3(0.05f, 0.53f, 0.15f), new Vector3(0.15f, 0.04f, 0.3f), paperMat);

        // 쉬는 자리 (지금은 기능 없음)
        B.TallBox(furniture, "매트리스", new Vector3(-3.7f, 0f, 0.6f), new Vector3(1.6f, 0f, 2.1f), 0.25f, 0.3f, B.linenMat);
        B.TallBox(furniture, "짐 상자", new Vector3(4.3f, 0f, -2.8f), new Vector3(0.8f, 0f, 0.7f), 0.6f, 0.7f, trunkMat);
        B.TallBox(furniture, "짐 상자", new Vector3(3.5f, 0f, -3.0f), new Vector3(0.6f, 0f, 0.5f), 0.45f, 0.5f, trunkMat);

        // 기능: 조사 지점(E → 확대) + 확대된 동안의 개발용 창. 확대 카메라는 가구 정면에서
        var chalkboard = Station<Chalkboard>(boardRoot, Vector3.back, distance: 3f, height: 0.6f, viewSize: 0.95f);
        var caseData = AssetDatabase.LoadAssetAtPath<CaseDeduction>(CaseDataPath);
        chalkboard.knownCases = caseData != null ? new[] { caseData } : new CaseDeduction[0];
        chalkboard.photoSurface = surface;
        chalkboard.surfaceSize = new Vector2(2.4f, 1.4f);
        chalkboard.photoMaterial = B.CreateMaterial("Hideout_Photo", new Color(0.86f, 0.86f, 0.82f));
        chalkboard.victimMaterial = B.CreateMaterial("Hideout_VictimPhoto", new Color(0.72f, 0.62f, 0.48f));
        chalkboard.noteMaterial = B.CreateMaterial("Hideout_Note", new Color(0.96f, 0.86f, 0.45f));
        chalkboard.outlineMaterial = B.CreateMaterial("Hideout_Outline", new Color(1f, 0.85f, 0.2f));
        chalkboard.stringMaterial = B.CreateMaterial("Hideout_RedString", new Color(0.78f, 0.12f, 0.12f));
        Station<HideoutStash>(stashRoot, Vector3.left, distance: 2.6f, height: 2.2f, viewSize: 0.95f);
        Station<StatusDesk>(deskRoot, Vector3.back, distance: 2.6f, height: 1.8f, viewSize: 1.05f);
        builtTable = BuildMissionTable(hideout, caseData);
    }

    // ---------------- 휴식 · 쉬는 자리 · 남매 대화 ----------------

    static TalkLine L(CharacterType who, string text) => new TalkLine { speaker = who, text = text };
    static Talk T(params TalkLine[] lines) => new Talk { lines = lines };

    // 매트리스 = 휴식. 쉬는 자리 = 조작하지 않는 캐릭터가 머무는 곳 + 남매 대화 (대사는 임시, 기획이 인스펙터에서 고침)
    // 쉬는 자리의 대화 범위(반경 1m)가 매트리스 휴식 범위와 겹치지 않게 둠 (PlayerInteraction은 근처 대상을 하나만 기억)
    static HideoutSpot[] BuildRestAndSpots(Transform hideout)
    {
        var rest = new GameObject("매트리스 (휴식)");
        rest.transform.SetParent(hideout);
        rest.transform.position = new Vector3(-3.7f, 0f, 0.6f);
        var reach = rest.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1f, 0f);
        reach.size = new Vector3(2.6f, 2f, 3.1f);
        var restSpot = rest.AddComponent<RestSpot>();
        restSpot.displayName = "매트리스에서";
        restSpot.noiseRadius = 0f;

        const CharacterType S = CharacterType.Player1, B_ = CharacterType.Player2;
        var spots = new List<HideoutSpot>
        {
            // 여동생: 통찰·추리
            Spot(hideout, S, "칠판 앞에서 사건을 본다", new Vector3(-1.1f, 0f, 2.15f), 0f,
                new[] { T(L(S, "...아빠가 왜 이 사람들을 골랐는지, 아직 모르겠어."), L(B_, "너무 붙잡고 있지 마. 눈 빠지겠다.")),
                        T(L(S, "칠판에 붙일 게 늘어날수록, 아빠가 낯설어져."), L(B_, "...그래도 우리 아빠야.")) },
                new[] { T(L(S, "302호. 찻잔이 두 개였어. 아빠는 그 사람이랑 차를 마셨던 거야."), L(B_, "아는 사이였다는 거지."), L(S, "응. 그래서 더 무서워.")) }),
            Spot(hideout, S, "지도 테이블 옆에서 고민한다", new Vector3(0.6f, 0f, 0.65f), 180f,
                new[] { T(L(S, "다음엔 어디로 갈지... 오빠 생각은 어때?"), L(B_, "네가 정해. 난 문 따는 거나 할게.")),
                        T(L(S, "지도로 보니까 이 도시, 생각보다 좁네."), L(B_, "그러니까 아빠도 멀리 못 갔을 거야.")) },
                new[] { T(L(S, "수첩에 적힌 대로라면, 다음은 박 선생 댁이야."), L(B_, "목요일 밤 9시. 시간이 없네.")) }),
            // 오빠: 몸·기계
            Spot(hideout, B_, "작업대 의자에 앉아 기계를 만진다", new Vector3(3.2f, 0f, 2.2f), 0f,
                new[] { T(L(B_, "이 녹음기, 조금만 손보면 쓸 만하겠는데."), L(S, "그걸로 뭐 하게?"), L(B_, "언젠간 쓸 데가 있겠지.")),
                        T(L(B_, "공구가 하나 빠졌어. 너 혹시 만졌냐?"), L(S, "아니. 오빠가 어제 상자에 넣었잖아.")) },
                new[] { T(L(B_, "302호 인터폰, 전선만 끊겨 있었어. 아빠가 끊은 거겠지."), L(S, "아빠는 기계를 잘 몰랐는데..."), L(B_, "...그러게.")) }),
            Spot(hideout, B_, "상자 옆 벽에 기대 쉰다", new Vector3(4.55f, 0f, -1.4f), 270f,
                new[] { T(L(B_, "잠깐만 이러고 있을게. 다리가 좀 쑤셔.")),
                        T(L(B_, "여기 다락방, 어릴 때 숨바꼭질하던 데잖아."), L(S, "...그땐 아빠가 술래였지.")) },
                new[] { T(L(B_, "그 집 책장, 생각보다 무거웠어."), L(S, "덕분에 수첩을 찾았잖아.")) }),
            Spot(hideout, B_, "해치 옆에서 망을 본다", new Vector3(-0.6f, 0f, -2.6f), 0f,
                new[] { T(L(B_, "아래층에서 소리 나면 바로 말해."), L(S, "오빠가 망보고 있잖아.")) },
                new[] { T(L(B_, "경찰이 우리 얼굴을 봤으면 어쩌지."), L(S, "아직은 괜찮아. 아직은.")) }),
        };
        return spots.ToArray();
    }

    static HideoutSpot Spot(Transform parent, CharacterType owner, string label, Vector3 position, float facing, Talk[] talks, Talk[] afterCase)
    {
        var go = new GameObject($"쉬는 자리: {CharacterRoster.NameOf(owner)} — {label}");
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facing, 0f));
        var reach = go.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1f, 0f);
        reach.radius = 1f;
        var spot = go.AddComponent<HideoutSpot>();
        spot.owner = owner;
        spot.label = label;
        spot.talks = talks;
        spot.talksAfterCase = afterCase;
        spot.noiseRadius = 0f;
        return spot;
    }

    // ---------------- 지도 테이블 (임무 고르기) ----------------

    static MissionTable builtTable;
    const string MissionFolder = "Assets/Data/Missions";
    static readonly Vector2 SiteHalf = new Vector2(4f, 3f);

    // 방 가운데의 파란 테이블 + 도시 지도 (예전 소파 자리). 확대하면 위에서 내려다봄
    static MissionTable BuildMissionTable(Transform hideout, CaseDeduction caseData)
    {
        var center = new Vector3(0.6f, 0.82f, -0.6f);
        Transform root = StationRoot(hideout, TableName, center);
        B.TallBox(root, TableName + " 본체", new Vector3(center.x, 0f, center.z), new Vector3(2.2f, 0f, 1.5f), 0.8f, 0.85f, B.sofaMat);

        // 도시 지도 (2.0 × 1.3, 위 = 북쪽): 바탕·강·도로·건물 블록
        var surface = new GameObject("지도 면").transform;
        surface.SetParent(root, false);
        B.Visual(surface, "지도 바탕", new Vector3(0f, 0.005f, 0f), new Vector3(2.0f, 0.01f, 1.3f), mapMat);
        B.Visual(surface, "강", new Vector3(-0.15f, 0.012f, 0.05f), new Vector3(0.12f, 0.004f, 1.3f), riverMat);
        foreach (float z in new[] { -0.35f, 0.18f })
            B.Visual(surface, "도로", new Vector3(0f, 0.012f, z), new Vector3(2.0f, 0.004f, 0.05f), roadMat);
        foreach (float x in new[] { -0.6f, 0.4f, 0.8f })
            B.Visual(surface, "도로", new Vector3(x, 0.012f, 0f), new Vector3(0.05f, 0.004f, 1.3f), roadMat);
        var random = new System.Random(7);
        for (float x = -0.9f; x < 0.95f; x += 0.2f)
            for (float z = -0.55f; z < 0.6f; z += 0.2f)
            {
                if (Mathf.Abs(x + 0.15f) < 0.12f || random.NextDouble() < 0.3) continue;   // 강 위·빈터는 비움
                float h = 0.015f + (float)random.NextDouble() * 0.03f;
                B.Visual(surface, "건물", new Vector3(x, 0.01f + h * 0.5f, z), new Vector3(0.11f, h, 0.11f), blockMat);
            }

        // 확대: 위에서 거의 수직으로. 아래쪽 조사 창·출발 막대에 지도가 가리지 않게 화면 위쪽에 오도록 중심을 남쪽으로
        var look = new GameObject("확대 중심").transform;
        look.SetParent(root, false);
        look.localPosition = new Vector3(0f, 0f, -0.56f);
        var anchor = new GameObject("확대 카메라 자리").transform;
        anchor.SetParent(root, false);
        anchor.localPosition = new Vector3(0f, 3f, -0.83f);

        var area = root.gameObject.AddComponent<InspectionArea>();
        area.Title = TableName;
        area.Items = new InspectionItem[0];
        root.GetComponent<FocusableObject>().ConfigureFocusView(anchor, look, 1.25f);

        var table = root.gameObject.AddComponent<MissionTable>();
        table.places = EnsureMissionAssets();
        table.knownCases = caseData != null ? new[] { caseData } : new CaseDeduction[0];
        table.mapSurface = surface;
        table.mapHalfSize = new Vector2(1.0f, 0.65f);
        table.homePinMaterial = B.CreateMaterial("Hideout_PinHome", new Color(1f, 0.85f, 0.15f));
        table.missionPinMaterial = B.CreateMaterial("Hideout_PinMission", new Color(0.9f, 0.12f, 0.12f));
        table.pastPinMaterial = B.CreateMaterial("Hideout_PinPast", new Color(0.2f, 0.45f, 0.95f));
        table.needleMaterial = metalMat;
        table.outlineMaterial = B.CreateMaterial("Hideout_PinOutline", new Color(1f, 1f, 1f));
        return table;
    }

    // 장소 데이터 (Assets/Data/Missions). 이미 있으면 기획이 고친 값을 그대로 둠
    static MissionDefinition[] EnsureMissionAssets()
    {
        if (!AssetDatabase.IsValidFolder(MissionFolder)) AssetDatabase.CreateFolder("Assets/Data", "Missions");
        return new[]
        {
            Mission("Home", "home", "우리 집", MissionDefinition.Kind.Home, "", new Vector2(-0.72f, -0.5f),
                "우리 집. 아빠가 사라진 뒤로 남매는 다락방에 숨어 지낸다."),
            Mission("Case302", "case302_site", "302호", MissionDefinition.Kind.PastCase, "case302", new Vector2(0.06f, 0.5f),
                "302호. 아빠가 한정우(임시)를 죽인 곳."),
            Mission("ParkTeacher", "park_teacher", "박 선생 댁", MissionDefinition.Kind.Mission, "", new Vector2(0.66f, 0.1f),
                "피해자 수첩 마지막 장에 적힌 곳. '목요일 밤 9시, 박 선생 댁.'\n아빠가 원래 노리던 다음 표적이다. (임시)"),
            Mission("Neighbor", "case302_neighbor", "302호 옆집", MissionDefinition.Kind.Mission, "", new Vector2(0.28f, 0.56f),
                "302호 옆집. '옆집 사람이 그날 밤 다 봤다'는 메모가 아빠 눈에 들어갔다면,\n아빠는 이제 이 사람을 노린다. (임시)"),
        };
    }

    static MissionDefinition Mission(string file, string id, string name, MissionDefinition.Kind kind, string caseId, Vector2 position, string description)
    {
        string path = $"{MissionFolder}/{file}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<MissionDefinition>();
        asset.id = id;
        asset.displayName = name;
        asset.kind = kind;
        asset.caseId = caseId;
        asset.mapPosition = position;
        asset.description = description;
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // 임시 임무 장소 2곳 (빨간 핀 임무): 다락방 카메라에 안 보이게 멀리 동쪽. 진짜 스테이지 씬이 생기면 씬 이동으로 바뀜
    static MissionSite[] BuildMissionSites(Transform map)
    {
        var result = new List<MissionSite>();
        foreach ((string id, string name, float x, Color floor) in new[]
                 {
                     ("park_teacher", "박 선생 댁 (임시)", 50f, new Color(0.55f, 0.6f, 0.5f)),
                     ("case302_neighbor", "302호 옆집 (임시)", 70f, new Color(0.6f, 0.55f, 0.48f)),
                 })
        {
            var site = new GameObject(name).AddComponent<MissionSite>();
            site.transform.SetParent(map);
            site.transform.position = new Vector3(x, 0f, 0f);
            site.missionId = id;
            B.Box(site.transform, "바닥", new Vector3(x, -0.1f, 0f), new Vector3(SiteHalf.x * 2f + 1f, 0.2f, SiteHalf.y * 2f + 1f), B.groundMat);
            B.Visual(site.transform, "바닥색", new Vector3(0f, 0.005f, 0f), new Vector3(SiteHalf.x * 2f, 0.01f, SiteHalf.y * 2f), B.CreateMaterial("Site_" + id, floor));
            B.Wall(site.transform, B.outerWallMat, B.Axis.AlongX, SiteHalf.y, x - SiteHalf.x, x + SiteHalf.x);
            B.Wall(site.transform, B.outerWallMat, B.Axis.AlongX, -SiteHalf.y, x - SiteHalf.x, x + SiteHalf.x);
            B.Wall(site.transform, B.outerWallMat, B.Axis.AlongZ, x - SiteHalf.x, -SiteHalf.y, SiteHalf.y);
            B.Wall(site.transform, B.outerWallMat, B.Axis.AlongZ, x + SiteHalf.x, -SiteHalf.y, SiteHalf.y);
            B.TallBox(site.transform, "가구", new Vector3(x + 2.2f, 0f, 1.8f), new Vector3(1.6f, 0f, 0.7f), 0.8f, 0.9f, B.woodMat);
            B.TallBox(site.transform, "가구", new Vector3(x - 2.5f, 0f, -1.5f), new Vector3(0.8f, 0f, 1.6f), 0.6f, 0.7f, B.linenMat);
            var spawns = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                spawns[i] = new GameObject("도착 " + (i + 1)).transform;
                spawns[i].SetParent(site.transform);
                spawns[i].position = new Vector3(x - 0.6f + i * 1.2f, 0f, -1.8f);
            }
            site.spawnPoints = spawns;
            result.Add(site);
        }
        return result.ToArray();
    }

    const string CaseDataPath = "Assets/Data/Cases/Case302_Deduction.asset";

    // 조사 지점 뼈대 (크기 1, 확대 카메라가 바라보는 가운데). 가구 본체는 이 아래에 둠 → 팀원 가림 판정이 본체를 같은 조사 지점으로 봄
    static Transform StationRoot(Transform parent, string name, Vector3 viewCenter)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent);
        root.position = viewCenter;
        return root;
    }

    // 팀원 조사 지점(InspectionArea + FocusableObject) + 내 기능. front = 확대 카메라가 놓일 방향
    static T Station<T>(Transform root, Vector3 front, float distance, float height, float viewSize) where T : HideoutStation
    {
        var area = root.gameObject.AddComponent<InspectionArea>(); // FocusableObject는 RequireComponent로 같이 붙음
        area.Title = root.name;
        area.Items = new InspectionItem[0];
        root.GetComponent<FocusableObject>().ConfigureFocusView(front, distance, height, viewSize);
        return root.gameObject.AddComponent<T>();
    }

    // 표시만 (굽고 난 뒤): 바닥 판자, 지붕 경사, 램프, 해치
    static void BuildDecor(Transform map)
    {
        Transform decor = new GameObject("Decor").transform;
        decor.SetParent(map);
        B.Visual(decor, "바닥 판자", new Vector3(0f, 0.005f, 0f), new Vector3(MaxX - MinX, 0.01f, MaxZ - MinZ), plankMat);
        for (float x = MinX + 1f; x < MaxX; x += 1f)
            B.Visual(decor, "판자 줄", new Vector3(x, 0.012f, 0f), new Vector3(0.03f, 0.004f, MaxZ - MinZ), beamMat);

        // 지붕 경사: 카메라 반대편인 북쪽·동쪽 벽 위에서 바깥으로 기울어 올라감 (단면도처럼 — 방 안을 가리지 않음)
        var northRoof = new GameObject("지붕 경사 (북)").transform;
        northRoof.SetParent(decor);
        northRoof.SetPositionAndRotation(new Vector3(0f, 1.0f, MaxZ + 0.1f), Quaternion.Euler(-40f, 0f, 0f));
        B.Visual(northRoof, "지붕", new Vector3(0f, 0f, 1.1f), new Vector3(MaxX - MinX + 0.2f, 0.08f, 2.2f), roofMat);
        for (float x = MinX + 0.5f; x < MaxX; x += 1.5f)
            B.Visual(northRoof, "서까래", new Vector3(x, 0.08f, 1.1f), new Vector3(0.14f, 0.12f, 2.2f), beamMat);

        var eastRoof = new GameObject("지붕 경사 (동)").transform;
        eastRoof.SetParent(decor);
        eastRoof.SetPositionAndRotation(new Vector3(MaxX + 0.1f, 1.0f, 0f), Quaternion.Euler(0f, 0f, 40f));
        B.Visual(eastRoof, "지붕", new Vector3(1.1f, 0f, 0f), new Vector3(2.2f, 0.08f, MaxZ - MinZ + 0.2f), roofMat);
        B.Visual(eastRoof, "둥근 창", new Vector3(1.2f, 0.05f, 1.2f), new Vector3(0.7f, 0.02f, 0.7f), lampMat);

        // 작업대 의자 (오빠의 쉬는 자리, 표시만 — 굽고 난 뒤라 길을 막지 않음)
        B.Visual(decor, "의자 앉는 곳", new Vector3(3.2f, 0.45f, 2.15f), new Vector3(0.5f, 0.06f, 0.45f), beamMat);
        B.Visual(decor, "의자 등받이", new Vector3(3.2f, 0.75f, 1.92f), new Vector3(0.5f, 0.55f, 0.06f), beamMat);
        foreach ((float dx, float dz) in new[] { (-0.2f, -0.18f), (0.2f, -0.18f), (-0.2f, 0.18f), (0.2f, 0.18f) })
            B.Visual(decor, "의자 다리", new Vector3(3.2f + dx, 0.21f, 2.15f + dz), new Vector3(0.05f, 0.42f, 0.05f), beamMat);

        // 램프 (지도 테이블 옆)
        B.Visual(decor, "램프 기둥", new Vector3(2.0f, 0.6f, -1.2f), new Vector3(0.06f, 1.2f, 0.06f), metalMat);
        B.Visual(decor, "램프 갓", new Vector3(2.0f, 1.25f, -1.2f), new Vector3(0.4f, 0.25f, 0.4f), lampMat);
        var lamp = new GameObject("램프 빛").AddComponent<Light>();
        lamp.transform.SetParent(decor);
        lamp.transform.position = new Vector3(2.0f, 1.2f, -1.2f);
        lamp.type = LightType.Point;
        lamp.range = 5f;
        lamp.intensity = 1.6f;
        lamp.color = new Color(1f, 0.78f, 0.5f);

        // 해치: 아래층으로 내려가는 바닥 문 (지금은 표시만)
        var hatch = new GameObject(HatchName).transform;
        hatch.SetParent(GameObject.Find("Hideout").transform);
        hatch.position = new Vector3(-1.6f, 0f, -1.9f);   // 남서쪽 구석은 앞쪽 벽에 가려서 방 안쪽으로
        B.Visual(hatch, "해치 문", new Vector3(0f, 0.015f, 0f), new Vector3(1.1f, 0.02f, 1.1f), beamMat);
        B.Visual(hatch, "손잡이", new Vector3(0.35f, 0.03f, 0f), new Vector3(0.08f, 0.02f, 0.3f), metalMat);
    }

    static void CreateMaterials()
    {
        plankMat = B.CreateMaterial("Hideout_Plank", new Color(0.52f, 0.40f, 0.29f));
        beamMat = B.CreateMaterial("Hideout_Beam", new Color(0.30f, 0.22f, 0.15f));
        roofMat = B.CreateMaterial("Hideout_Roof", new Color(0.40f, 0.33f, 0.27f));
        boardMat = B.CreateMaterial("Hideout_Board", new Color(0.13f, 0.22f, 0.17f));
        chalkMat = B.CreateMaterial("Hideout_Chalk", new Color(0.92f, 0.92f, 0.88f));
        trunkMat = B.CreateMaterial("Hideout_Trunk", new Color(0.45f, 0.28f, 0.16f));
        metalMat = B.CreateMaterial("Hideout_Metal", new Color(0.42f, 0.43f, 0.45f));
        lampMat = B.CreateMaterial("Hideout_Lamp", new Color(0.98f, 0.86f, 0.55f));
        paperMat = B.CreateMaterial("Hideout_Paper", new Color(0.93f, 0.90f, 0.80f));
        mapMat = B.CreateMaterial("Hideout_Map", new Color(0.88f, 0.84f, 0.72f));
        riverMat = B.CreateMaterial("Hideout_MapRiver", new Color(0.45f, 0.62f, 0.78f));
        roadMat = B.CreateMaterial("Hideout_MapRoad", new Color(0.66f, 0.63f, 0.58f));
        blockMat = B.CreateMaterial("Hideout_MapBlock", new Color(0.62f, 0.55f, 0.46f));
    }

    static void AddToBuildSettings()
    {
        // 프로토타입 씬은 첫 번째 그대로 두고 뒤에 추가
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != TargetScene)
            .Select(s => new EditorBuildSettingsScene(s.path, s.enabled)).ToList();
        scenes.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
