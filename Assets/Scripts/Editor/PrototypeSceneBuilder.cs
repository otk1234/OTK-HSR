using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using NarrativeGraybox.CameraSystem;
using NarrativeGraybox.Interaction;
using NarrativeGraybox.Player;
using NarrativeGraybox.UI;
using Object = UnityEngine.Object;

// 팀원의 그레이박스 씬(Graybox.unity)을 복사한 뒤, 내 게임 시스템을 얹어 프로토타입 씬을 만듭니다.
// 원본 Graybox.unity는 건드리지 않습니다. 팀원이 맵을 고쳐 보내면 메뉴를 다시 실행하면 됩니다.
public static class PrototypeSceneBuilder
{
    public const string SourceScene = "Assets/Map_Graybox/Scenes/Graybox.unity";
    public const string TargetScene = "Assets/Scenes/Prototype.unity";
    const string TargetFolder = "Assets/Scenes/Prototype";
    const string MaterialFolder = "Assets/Map_Graybox/Generated/Materials";

    const float AgentRadius = 0.25f;     // 문 폭(약 1.2m)을 지나갈 수 있는 굵기
    const float CameraDistance = 20f;    // 직교 카메라라 거리는 화면 크기에 영향 없음
    const float MinViewSize = 2.6f;
    const float ScreenAspect = 16f / 9f;

    public enum View { TopDown, Isometric }

    // 카메라 각도는 팀원의 시점 비교 씬과 같게 맞췄습니다.
    public static readonly Quaternion TopDownRotation = Quaternion.LookRotation(-new Vector3(0f, 16f, -10f));     // Test_TopView
    static readonly Quaternion IsometricRotation = Quaternion.LookRotation(-new Vector3(-12f, 12f, -12f)); // Test_Isometric_2F

    struct Room
    {
        public string id, name;
        public float minX, maxX, minZ, maxZ;
        public View view;
        public int priority;

        public Room(string id, string name, float minX, float maxX, float minZ, float maxZ, View view = View.TopDown, int priority = 0)
        {
            this.id = id; this.name = name; this.minX = minX; this.maxX = maxX; this.minZ = minZ; this.maxZ = maxZ;
            this.view = view; this.priority = priority;
        }

        public Vector3 Center => new Vector3((minX + maxX) * 0.5f, 1.5f, (minZ + maxZ) * 0.5f);
    }

    // 그레이박스 1층 벽 배치 기준 구역 (x, z 범위)
    // 기본은 고정 탑뷰, 특정 구역만 아이소메트릭: 구역별 View 값을 바꾸고 메뉴를 다시 실행하면 됩니다.
    static readonly Room[] Rooms =
    {
        new Room("LivingKitchen", "거실 · 주방", -5f, 6f, -7f, 4f),
        new Room("Wing", "계단 복도", 6f, 10f, -7f, 1.4f, View.Isometric),
        new Room("Guest", "손님방", 7f, 10f, -7f, -1.5f, priority: 10), // 복도 구역 안에 겹쳐 있으므로 우선순위를 높임
        new Room("Office", "서재", 6f, 10f, 1.4f, 4f),
        new Room("Pantry", "다용도실", -8f, -5f, -2f, 1.6f),
        new Room("Bathroom", "욕실", -5f, -2f, 1.6f, 4f),
        new Room("Foyer", "현관", -2f, 2f, 4f, 6.4f),
        new Room("Storage", "창고", -9f, -2f, 4f, 8f),
    };

    [MenuItem("OTK/프로토타입 씬 다시 만들기")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("프로토타입 씬 다시 만들기",
                $"{SourceScene}을(를) 복사해서 {TargetScene}을(를) 새로 만듭니다.\n" +
                "Prototype.unity를 직접 수정했다면 그 내용은 사라집니다.", "만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        PrototypeSceneValidator.Validate(true);
    }

    // 명령줄(batchmode)용: Unity.exe -batchmode -executeMethod PrototypeSceneBuilder.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            bool ok = PrototypeSceneValidator.Validate(false);
            EditorApplication.Exit(ok ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
            throw new Exception("그레이박스 씬을 찾을 수 없습니다: " + SourceScene);

        AssetDatabase.DeleteAsset(TargetFolder);
        AssetDatabase.CreateFolder("Assets/Scenes", "Prototype");
        // 씬 파일 내용만 덮어써서 .meta(GUID)는 유지합니다. (지웠다 새로 만들면 다시 만들 때마다 GUID가 바뀜)
        System.IO.File.Copy(SourceScene, TargetScene, true);
        AssetDatabase.ImportAsset(TargetScene, ImportAssetOptions.ForceUpdate);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScene) == null)
            throw new Exception("씬 복사 실패: " + TargetScene);

        var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        Camera mainCamera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.CompareTag("MainCamera"));
        var hud = Object.FindFirstObjectByType<SimpleGameHud>();
        var inspectionCamera = mainCamera.GetComponent<IsometricCameraFollow>();
        var occlusionFader = mainCamera.GetComponent<CameraOcclusionFader>();
        GameObject teammatePlayer = Object.FindFirstObjectByType<PlayerInteractor3D>().gameObject;

        // 1) NavMesh: 캐릭터를 빼고 맵(바닥·벽·가구)만 굽습니다. 문·아이템은 굽고 난 뒤에 만듭니다.
        teammatePlayer.SetActive(false);
        BakeNavMesh();
        teammatePlayer.SetActive(true);

        // 2) 캐릭터 2명 (0번 여동생, 1번 오빠 — 이름은 캐릭터 프로필): 팀원 플레이어(2D 스프라이트)를 내 이동/상호작용 시스템으로 바꾸고 복제
        CharacterProfileValidator.EnsureAssets();
        string sisterName = CharacterRoster.NameOf(CharacterType.Player1);
        string brotherName = CharacterRoster.NameOf(CharacterType.Player2);
        GameObject sister = ConvertPlayer(teammatePlayer, sisterName + " (Player1)", CharacterType.Player1, inspectionCamera, hud);
        GameObject brother = Object.Instantiate(sister);
        brother.name = brotherName + " (Player2)";
        brother.transform.position = sister.transform.position + new Vector3(1.1f, 0f, -0.6f);
        brother.GetComponent<PlayerInteraction>().myType = CharacterType.Player2;
        brother.GetComponentInChildren<SpriteRenderer>().color = new Color(0.65f, 0.82f, 1f);

        // 3) 카메라: 평소엔 구역별 고정 카메라(Cinemachine Cut), 조사할 때만 팀원의 클로즈업 카메라
        var brain = mainCamera.gameObject.AddComponent<CinemachineBrain>();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        inspectionCamera.enabled = false;
        BuildRoomCameras();
        // 첫 구역 카메라가 켜지기 전 한 순간도 탑뷰로 보이도록
        mainCamera.transform.SetPositionAndRotation(Rooms[0].Center - TopDownRotation * Vector3.forward * CameraDistance, TopDownRotation);

        // 4) 페이드 · 대사 패널
        BuildFadeCanvas();
        DialogueManager dialogue = BuildDialogueCanvas();

        // 5) 퍼즐: 열쇠 → 창고 문 → 공구 상자(힘 = 오빠) → 다용도실 문 → 암호 메모(정보 수집 = 여동생)
        Material timber = LoadMaterial("DemoTimber");
        Material paper = LoadMaterial("Clue");

        InspectionItem key = Object.FindObjectsByType<InspectionItem>(FindObjectsSortMode.None).First(i => i.Title == "황동 열쇠");
        key.Collectible = true;
        key.Description = "작은 황동 열쇠. 손잡이에 '창고'라고 새겨져 있다.";
        EditorUtility.SetDirty(key);

        InteractableDoor storageDoor = BuildDoor("창고 문", new Vector3(-2f, 0f, 4.5f), 1.9f, "황동 열쇠",
            "굳게 잠겨 있다. 열쇠가 필요하다.", "철컥 — 창고 문이 열렸다.", timber);
        InteractableDoor pantryDoor = BuildDoor("다용도실 문", new Vector3(-5f, 0f, -1.1f), 1.7f, "공구 상자",
            "문틀이 뒤틀려 꿈쩍도 하지 않는다. 공구가 필요하다.", "공구로 문틀을 비틀어 열었다.", timber);

        BuildItem("공구 상자", new Vector3(-4.6f, 0f, 6.6f), new Vector3(0.8f, 0.5f, 0.55f), CharacterTrait.Strength,
            $"너무 무거워서 들 수 없다... {brotherName}에게 부탁하자.", timber);
        BuildItem("암호 메모", new Vector3(-6.3f, 0f, 0.1f), new Vector3(0.35f, 0.04f, 0.45f), CharacterTrait.Investigation,
            $"알아볼 수 없는 암호다. {sisterName}에게 맡겨 보자.", paper)
            .pickupMessage = "'0900' — 서재의 멈춘 시계와 같은 시각이다. 그날 밤 9시에 무슨 일이 있었던 걸까.";

        BuildNpc(sister.GetComponentInChildren<SpriteCharacterVisual>().gameObject);

        // 6) 게임 시스템 (내 CharacterManager + 연결 스크립트)
        var systems = new GameObject("Game Systems");
        var manager = systems.AddComponent<CharacterManager>();
        manager.players = new[] { sister, brother };

        var integration = systems.AddComponent<PrototypeIntegration>();
        integration.hud = hud;
        integration.inspectionCamera = inspectionCamera;
        integration.occlusionFader = occlusionFader;
        integration.cameraBrain = brain;
        integration.dialogue = dialogue;
        integration.objectives = new[]
        {
            new PrototypeIntegration.Objective { text = "서재 책상을 조사해서 단서를 찾자.", completeWhenItemHeld = "황동 열쇠" },
            new PrototypeIntegration.Objective { text = "황동 열쇠로 현관 옆 창고 문을 열자.", completeWhenDoorOpen = storageDoor },
            new PrototypeIntegration.Objective { text = $"창고의 공구 상자를 옮기자. ({brotherName}만 들 수 있다)", completeWhenItemHeld = "공구 상자" },
            new PrototypeIntegration.Objective { text = "공구로 다용도실 문을 열자.", completeWhenDoorOpen = pantryDoor },
            new PrototypeIntegration.Objective { text = $"다용도실의 메모를 확인하자. ({sisterName}만 읽을 수 있다)", completeWhenItemHeld = "암호 메모" },
        };
        integration.completeMessage = "프로토타입 목표 달성! 메모의 암호를 손에 넣었다.";

        // 7) HUD 초기값
        var hudData = new SerializedObject(hud);
        hudData.FindProperty("location").stringValue = "거실 · 주방";
        hudData.FindProperty("character").stringValue = sisterName;
        hudData.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[OTK] 프로토타입 씬 생성 완료: " + TargetScene);
    }

    static void BakeNavMesh()
    {
        var surface = new GameObject("NavMesh").AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes; // 가구에는 콜라이더가 없어서 메시 기준으로 굽습니다.

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(surface.agentTypeID);
        settings.agentRadius = AgentRadius;

        var bounds = new Bounds(new Vector3(0.5f, 1f, 0.5f), new Vector3(24f, 6f, 20f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.RenderMeshes, 0, new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        data.name = "Prototype NavMesh";
        AssetDatabase.CreateAsset(data, TargetFolder + "/NavMesh.asset");

        surface.navMeshData = data;
        surface.RemoveData();
        surface.AddData();
    }

    static GameObject ConvertPlayer(GameObject player, string name, CharacterType type, IsometricCameraFollow inspectionCamera, SimpleGameHud hud)
    {
        player.name = name;
        player.tag = "Player";

        // 팀원 이동(CharacterController) → 내 이동(NavMeshAgent)
        Object.DestroyImmediate(player.GetComponent<IsometricPlayerController>());
        Object.DestroyImmediate(player.GetComponent<CharacterController>());
        player.GetComponent<PlayerInteractor3D>().Configure(inspectionCamera, null, hud);

        var body = player.AddComponent<CapsuleCollider>();
        body.height = 2f;
        body.radius = 0.35f;
        var rigidbody = player.AddComponent<Rigidbody>(); // 구역·아이템 트리거 감지용
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        var agent = player.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1f;
        agent.height = 2f;
        agent.radius = 0.3f;
        agent.speed = 5f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        player.AddComponent<PlayerMovement>().cameraRelative = true;
        player.AddComponent<PlayerStamina>();
        player.AddComponent<FootstepNoise>();
        player.AddComponent<Inventory>();
        player.AddComponent<PlayerInteraction>().myType = type;
        player.AddComponent<CharacterStatus>();
        return player;
    }

    static void BuildRoomCameras()
    {
        Transform root = new GameObject("Room Cameras").transform;

        foreach (Room room in Rooms)
            BuildRoomZone(root, room.id, room.name, room.minX, room.maxX, room.minZ, room.maxZ, room.view, room.priority);
    }

    // 구역 하나: 트리거(CameraTrigger) + 구역 전체가 보이는 고정 카메라. 추격 테스트 씬 생성기도 같이 씁니다.
    public static CameraTrigger BuildRoomZone(Transform root, string id, string name, float minX, float maxX, float minZ, float maxZ,
        View view = View.TopDown, int priority = 0, string buildingId = "House", int floor = 0)
    {
        Quaternion rotation = view == View.Isometric ? IsometricRotation : TopDownRotation;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        var center = new Vector3((minX + maxX) * 0.5f, 1.5f, (minZ + maxZ) * 0.5f);

        var zone = new GameObject("Zone_" + id);
        zone.transform.SetParent(root);
        zone.transform.position = center;
        var box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(maxX - minX, 3f, maxZ - minZ);

        var camObject = new GameObject($"Cam_{id} ({(view == View.Isometric ? "아이소메트릭" : "탑뷰")})");
        camObject.transform.SetParent(zone.transform);
        var cam = camObject.AddComponent<CinemachineCamera>();
        cam.Priority = priority;

        // 고정 카메라: 구역 전체가 화면에 들어오도록 크기와 위치를 계산
        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        foreach (float x in new[] { minX, maxX })
        foreach (float y in new[] { 0f, 2.2f })
        foreach (float z in new[] { minZ, maxZ })
        {
            var corner = new Vector3(x, y, z);
            float r = Vector3.Dot(corner, right), u = Vector3.Dot(corner, up);
            minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
        }
        Vector3 middle = right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f) + forward * Vector3.Dot(center, forward);
        camObject.transform.SetPositionAndRotation(middle - forward * CameraDistance, rotation);

        LensSettings lens = LensSettings.Default;
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        lens.OrthographicSize = Mathf.Max(MinViewSize, (maxU - minU) * 0.5f + 0.3f, ((maxR - minR) * 0.5f + 0.3f) / ScreenAspect);
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = 100f;
        cam.Lens = lens;
        camObject.SetActive(false);

        var trigger = zone.AddComponent<CameraTrigger>();
        trigger.virtualCamera = camObject;
        trigger.roomName = name;
        trigger.buildingId = buildingId;
        trigger.floor = floor;
        return trigger;
    }

    public static void BuildFadeCanvas()
    {
        var root = new GameObject("Fade Canvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        root.AddComponent<CanvasScaler>();

        var panel = new GameObject("Black", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(root.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = panel.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        var group = panel.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var fade = root.AddComponent<FadeManager>();
        fade.fadePanel = group;
        fade.fadeTime = 0.25f;
    }

    // 화면 하단 가운데(HUD 좌우 패널 사이)에 대사 패널을 만듭니다.
    internal static DialogueManager BuildDialogueCanvas()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var root = new GameObject("Dialogue Canvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // 페이드(1000) 아래
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f); // 팀원 HUD 기준 해상도와 같게
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(root.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.sizeDelta = new Vector2(400f, 126f); // HUD 왼쪽 소지품(~442) · 오른쪽 장착(~944) 패널 사이
        panelRect.anchoredPosition = new Vector2(0f, 24f);
        panel.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.075f, 0.93f);
        var group = panel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var hint = new GameObject("Hint Image", typeof(RectTransform), typeof(Image));
        hint.transform.SetParent(panel.transform, false);
        var hintRect = (RectTransform)hint.transform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 1f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(160f, 160f);
        hintRect.anchoredPosition = new Vector2(0f, 230f); // 화면 가운데 위쪽 (상호작용 안내 위)
        hint.GetComponent<Image>().preserveAspect = true;

        Text speaker = CreateText(panel.transform, "Speaker", font, 15, new Color(1f, 0.8f, 0.25f), new Vector2(16f, -10f), new Vector2(-32f, 24f));
        Text body = CreateText(panel.transform, "Body", font, 17, Color.white, new Vector2(16f, -38f), new Vector2(-32f, 80f));

        var dialogue = root.AddComponent<DialogueManager>();
        dialogue.panel = group;
        dialogue.speakerText = speaker;
        dialogue.bodyText = body;
        dialogue.hintImage = hint.GetComponent<Image>();
        return dialogue;
    }

    static Text CreateText(Transform parent, string name, Font font, int size, Color color, Vector2 topLeft, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = sizeDelta;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    // 기존 회전형 문 구조: 빈 부모(DoorHinge)가 회전축, 넉넉한 BoxCollider 트리거로 접근 감지,
    // 자식 Cube의 NavMeshObstacle(Carve)이 평소에 길을 막습니다.
    // hinge: 경첩 위치, 문짝은 +z 방향으로 width만큼 뻗고 열리면 +x 쪽으로 90도 돕니다.
    static InteractableDoor BuildDoor(string name, Vector3 hinge, float width, string requiredItem, string locked, string opened, Material material)
    {
        var pivot = new GameObject($"DoorHinge ({name})");
        pivot.tag = "Door";
        pivot.transform.position = hinge;
        var reach = pivot.AddComponent<BoxCollider>();
        reach.isTrigger = true;
        reach.center = new Vector3(0f, 1.2f, width * 0.5f);
        reach.size = new Vector3(2.6f, 2.4f, width + 0.6f); // 문 앞뒤로 약 1.3m씩

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Door";
        panel.transform.SetParent(pivot.transform, false);
        panel.transform.localPosition = new Vector3(0f, 1.2f, width * 0.5f);
        panel.transform.localScale = new Vector3(0.12f, 2.4f, width - 0.05f);
        if (material != null) panel.GetComponent<Renderer>().sharedMaterial = material;
        var obstacle = panel.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = Vector3.one;
        obstacle.carving = true;

        var door = pivot.AddComponent<InteractableDoor>();
        door.requiredItemName = requiredItem;
        door.lockedMessage = locked;
        door.openedMessage = opened;
        door.closedMessage = "문을 닫았다.";
        return door;
    }

    static InteractableItem BuildItem(string itemName, Vector3 floorPosition, Vector3 size, CharacterTrait requiredTrait, string failMessage, Material material)
    {
        var root = new GameObject("Item_" + itemName);
        root.tag = "Item";
        root.transform.position = floorPosition + Vector3.up * (size.y * 0.5f);
        var reach = root.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        reach.radius = 1f;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = size;
        if (material != null) visual.GetComponent<Renderer>().sharedMaterial = material;

        var item = root.AddComponent<InteractableItem>();
        item.itemName = itemName;
        item.requiredTrait = requiredTrait;
        item.failMessage = failMessage;
        return item;
    }

    // 팀원 대화 시스템(DummyNpc)으로 첫 단서를 알려주는 NPC
    static void BuildNpc(GameObject spriteVisual)
    {
        var npc = new GameObject("가정부 (NPC)");
        npc.transform.position = new Vector3(1.3f, 1f, 1.2f);
        npc.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        var dummy = npc.AddComponent<DummyNpc>();
        dummy.DisplayName = "가정부";
        dummy.Line = "정전이 나서 일을 할 수가 없네요. 주인어른 서재 책상에 창고 열쇠가 있을 텐데…";
        var body = npc.AddComponent<CapsuleCollider>();
        body.height = 2f;
        body.radius = 0.35f;
        var obstacle = npc.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 0.35f;
        obstacle.height = 2f;
        obstacle.carving = true;

        var visual = Object.Instantiate(spriteVisual, npc.transform, false);
        visual.name = "2D Character Visual";
        visual.GetComponent<SpriteRenderer>().color = new Color(0.8f, 0.85f, 0.8f);
    }

    public static Material LoadMaterial(string name)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
        if (material == null) Debug.LogWarning("[OTK] 머티리얼을 찾지 못했습니다: " + name);
        return material;
    }

    static void AddToBuildSettings()
    {
        // 경로로 다시 만들어서 GUID를 현재 값으로 맞춥니다.
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != TargetScene)
            .Select(s => new EditorBuildSettingsScene(s.path, s.enabled)).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
