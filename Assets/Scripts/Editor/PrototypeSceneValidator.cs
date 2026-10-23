using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Cinemachine;
using NarrativeGraybox.CameraSystem;
using NarrativeGraybox.Interaction;
using Object = UnityEngine.Object;

// 프로토타입 씬이 제대로 만들어졌는지 점검합니다.
//  - Validate: 빠진 스크립트, NavMesh로 모든 구역에 갈 수 있는지 (에디터 상태)
//  - 플레이 점검: 실제 플레이 모드에서 퍼즐을 처음부터 끝까지 진행해 봅니다 (키 입력 대신 함수 호출)
public static class PrototypeSceneValidator
{
    [MenuItem("OTK/프로토타입 씬 점검")]
    static void ValidateFromMenu()
    {
        EditorSceneManager.OpenScene(PrototypeSceneBuilder.TargetScene, OpenSceneMode.Single);
        Validate(true);
    }

    public static bool Validate(bool showDialog)
    {
        var problems = new List<string>();

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0) problems.Add($"빠진 스크립트 {missing}개: {go.name}");
        }

        var manager = Object.FindAnyObjectByType<CharacterManager>();
        if (manager == null || manager.players == null || manager.players.Length != 2) problems.Add("CharacterManager 캐릭터 2명 설정 안 됨");
        if (Object.FindAnyObjectByType<PrototypeIntegration>() == null) problems.Add("PrototypeIntegration 없음");
        if (Object.FindAnyObjectByType<FadeManager>() == null) problems.Add("FadeManager 없음");

        if (manager != null && manager.players.Length > 0)
        {
            Vector3 start = manager.players[0].transform.position + Vector3.down;
            if (!NavMesh.SamplePosition(start, out NavMeshHit from, 1f, NavMesh.AllAreas))
                problems.Add("시작 위치가 NavMesh 위에 없음");
            else
            {
                foreach (CameraTrigger room in Object.FindObjectsByType<CameraTrigger>())
                {
                    // 가구 위가 아닌 바닥 지점 중 하나라도 걸어서 갈 수 있으면 통과
                    Bounds area = room.GetComponent<Collider>().bounds;
                    bool hasFloor = false, reachable = false;
                    for (float x = area.min.x + 0.4f; x < area.max.x && !reachable; x += 0.5f)
                    for (float z = area.min.z + 0.4f; z < area.max.z && !reachable; z += 0.5f)
                    {
                        if (!NavMesh.SamplePosition(new Vector3(x, 0f, z), out NavMeshHit to, 0.3f, NavMesh.AllAreas) || to.position.y > 0.2f) continue;
                        hasFloor = true;
                        var path = new NavMeshPath();
                        NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                        reachable = path.status == NavMeshPathStatus.PathComplete;
                    }
                    if (!hasFloor) problems.Add($"{room.roomName}: 바닥에 NavMesh 없음");
                    else if (!reachable) problems.Add($"{room.roomName}: 걸어서 갈 수 없음");
                    else Debug.Log($"[OTK 점검] {room.roomName}: 이동 경로 OK");
                }
            }
        }

        string result = problems.Count == 0 ? "문제 없음" : string.Join("\n", problems);
        if (problems.Count == 0) Debug.Log("[OTK 점검] 씬 점검 통과");
        else Debug.LogError("[OTK 점검] 씬 점검 실패\n" + result);
        if (showDialog) EditorUtility.DisplayDialog("프로토타입 씬 점검", result, "확인");
        return problems.Count == 0;
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod PrototypeSceneValidator.RunPlaySmokeTest
    public static void RunPlaySmokeTest()
    {
        EditorSceneManager.OpenScene(PrototypeSceneBuilder.TargetScene, OpenSceneMode.Single);
        failures.Clear();
        errors.Clear();
        Application.logMessageReceived += OnLog;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stackTrace);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        steps = Steps();
        waitUntil = EditorApplication.timeSinceStartup + 1.5;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        bool more;
        try { more = steps.MoveNext(); }
        catch (Exception e) { failures.Add("점검 중 예외: " + e); more = false; }

        if (more) { waitUntil = EditorApplication.timeSinceStartup + steps.Current; return; }

        EditorApplication.update -= Tick;
        failures.AddRange(errors.Select(e => "콘솔 오류: " + e));
        if (failures.Count == 0) Debug.Log("[OTK 플레이 점검] 전부 통과");
        else Debug.Log("[OTK 플레이 점검] 실패\n" + string.Join("\n", failures));
        EditorApplication.Exit(failures.Count == 0 ? 0 : 3);
    }

    static void Check(bool condition, string what)
    {
        if (condition) Debug.Log("[OTK 플레이 점검] OK  " + what);
        else { Debug.Log("[OTK 플레이 점검] FAIL " + what); failures.Add(what); }
    }

    // 각 단계 뒤 숫자 = 다음 단계까지 기다릴 시간(초)
    static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        GameObject sister = manager.players[0], brother = manager.players[1];
        var integration = Object.FindAnyObjectByType<PrototypeIntegration>();
        var brain = Object.FindAnyObjectByType<CinemachineBrain>();
        var inspectionCamera = Object.FindAnyObjectByType<IsometricCameraFollow>();
        var doors = Object.FindObjectsByType<InteractableDoor>();
        InteractableDoor storageDoor = doors.First(d => d.requiredItemName == "황동 열쇠");
        InteractableDoor pantryDoor = doors.First(d => d.requiredItemName == "공구 상자");
        CameraTrigger Room(string name) => Object.FindObjectsByType<CameraTrigger>().First(r => r.roomName == name);

        Check(manager.GetActivePlayer() == sister, "시작 캐릭터 = 여동생");
        Check(sister.name.StartsWith(CharacterRoster.NameOf(CharacterType.Player1)) && brother.name.StartsWith(CharacterRoster.NameOf(CharacterType.Player2))
              && integration.NameFor(0) == CharacterRoster.NameOf(CharacterType.Player1), "캐릭터 이름은 캐릭터 프로필에서");
        Check(integration.InventoryCapacityFor(0) == CharacterRoster.CapacityOf(CharacterType.Player1)
              && integration.InventoryCapacityFor(1) == CharacterRoster.CapacityOf(CharacterType.Player2), "소지품 칸 수는 캐릭터 프로필에서");
        Check(sister.GetComponent<NavMeshAgent>().isOnNavMesh && brother.GetComponent<NavMeshAgent>().isOnNavMesh, "두 캐릭터 모두 NavMesh 위");
        Check(sister.GetComponent<PlayerInteractor3D>().enabled && !brother.GetComponent<PlayerInteractor3D>().enabled, "조사 입력은 조작 중인 캐릭터만");
        for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.2f; // 첫 화면 페이드가 끝날 때까지
        yield return 0.5f;
        Check(Room("거실 · 주방").Contains(sister), "시작 위치가 거실 구역 안으로 인식됨");
        Check(Room("거실 · 주방").virtualCamera.activeSelf, "거실 구역 카메라 켜짐");
        Check(Camera.main.orthographic && Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, 0f)) < 1f, "거실은 고정 탑뷰 시점");
        Check(brain.enabled && !inspectionCamera.enabled, "평소엔 구역 카메라가 화면 담당");

        // 이동 → 구역 카메라 전환
        Vector3 before = sister.transform.position;
        for (int i = 0; i < 20; i++) sister.GetComponent<NavMeshAgent>().Move(Vector3.right * 0.05f);
        Check((sister.transform.position - before).magnitude > 0.5f, "NavMeshAgent 이동");

        sister.GetComponent<NavMeshAgent>().Warp(new Vector3(8.1f, 0f, 2.2f));
        yield return 1.5f;
        Check(Room("서재").Contains(sister) && Room("서재").virtualCamera.activeSelf, "서재 들어가면 서재 카메라로 전환");
        Check(!FadeManager.instance.isFading, "페이드 끝남");

        // 서재 조사 → 황동 열쇠
        var interactor = sister.GetComponent<PlayerInteractor3D>();
        var area = Object.FindObjectsByType<InspectionArea>().First(a => a.Items.Any(i => i.Title == "황동 열쇠"));
        interactor.Enter(area);
        yield return 0.3f;
        Check(!brain.enabled && inspectionCamera.enabled, "조사 중엔 팀원 클로즈업 카메라로 전환");
        Check(manager.inputLocked, "조사 중 캐릭터 전환 잠금");
        interactor.Select(area.Items.First(i => i.Title == "황동 열쇠"));
        interactor.CollectSelected();
        Check(sister.GetComponent<Inventory>().HasItem("황동 열쇠"), "조사로 얻은 열쇠가 여동생 인벤토리에 들어감");
        yield return 0.8f;
        interactor.Back();
        yield return 1.2f;
        Check(brain.enabled && !inspectionCamera.enabled && !FadeManager.instance.isFading, "조사 끝나면 구역 카메라로 복귀");
        Check(!manager.inputLocked, "조사 끝나면 잠금 해제");
        Check(integration.CurrentObjective().completeWhenDoorOpen == storageDoor, "목표가 '창고 문 열기'로 진행");

        // 특정 구역만 아이소메트릭
        sister.GetComponent<NavMeshAgent>().Warp(new Vector3(6.5f, 0f, -4f));
        yield return 1.5f;
        Check(Room("계단 복도").virtualCamera.activeSelf && Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, 45f)) < 1f,
            "계단 복도는 아이소메트릭 시점");
        sister.GetComponent<NavMeshAgent>().Warp(new Vector3(8.1f, 0f, 2.2f));
        yield return 1.5f;
        Check(Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, 0f)) < 1f, "서재로 돌아오면 다시 탑뷰");

        // 창고 문 (열쇠 필요)
        storageDoor.Interact(brother.GetComponent<Inventory>());
        yield return 0.8f;
        Check(!storageDoor.IsOpen, "열쇠 없는 오빠는 창고 문을 못 엶");
        Check(DialogueManager.instance.IsShowing && DialogueManager.instance.bodyText.text.StartsWith("굳게"), "잠긴 문 메시지가 대사 패널에 타이핑됨");
        storageDoor.Interact(sister.GetComponent<Inventory>());
        yield return 1f;
        Check(storageDoor.IsOpen, "열쇠 가진 여동생은 창고 문을 엶");
        Check(sister.GetComponent<Inventory>().HasItem("황동 열쇠"), "문을 열어도 열쇠는 소지품에 남음 (아이템은 써도 사라지지 않음)");

        // 건네주기: 같은 건물이면 됨 → 열쇠가 오빠에게 넘어감
        Check(ItemHandover.Give(sister, brother, "황동 열쇠") == HandoverResult.Ok
              && brother.GetComponent<Inventory>().HasItem("황동 열쇠") && !sister.GetComponent<Inventory>().HasItem("황동 열쇠"),
            "건네주기: 같은 건물(집)이면 열쇠를 오빠에게 건넴");
        storageDoor.Interact(brother.GetComponent<Inventory>()); // 닫기
        yield return 1f;
        storageDoor.Interact(brother.GetComponent<Inventory>()); // 건네받은 열쇠로 다시 열기 (한 번 연 문은 잠금이 풀려 있음)
        yield return 1f;
        Check(storageDoor.IsOpen && brother.GetComponent<Inventory>().HasItem("황동 열쇠"), "건네받은 쪽도 문을 쓸 수 있고, 열쇠는 그대로 남음");

        // 건네주기: 어느 구역에도 없으면 안 됨
        var sisterAgent = sister.GetComponent<NavMeshAgent>();
        Vector3 sisterAt = sister.transform.position;
        sisterAgent.enabled = false;
        sister.transform.position = new Vector3(500f, 0f, 500f);
        Check(ItemHandover.Check(brother, sister, "황동 열쇠") == HandoverResult.NotSamePlace, "건네주기: 구역 밖(다른 장소)이면 안 됨");
        sister.transform.position = sisterAt;
        sisterAgent.enabled = true;
        sisterAgent.Warp(sisterAt);
        yield return 0.2f;

        // 캐릭터 전환
        typeof(CharacterManager).GetMethod("SwitchPlayer", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, new object[] { 1 });
        yield return 0.3f;
        Check(manager.GetActivePlayer() == brother, "오빠로 전환");
        Check(brother.GetComponent<PlayerInteractor3D>().enabled && !sister.GetComponent<PlayerInteractor3D>().enabled, "전환 후 조사 입력도 오빠에게");

        // 창고 → 공구 상자 (오빠만)
        Vector3 storageInside = new Vector3(-4.6f, 0f, 6.2f), foyer = new Vector3(0f, 0f, 5.2f);
        var storagePath = new NavMeshPath();
        NavMesh.CalculatePath(foyer, storageInside, NavMesh.AllAreas, storagePath);
        Check(storagePath.status == NavMeshPathStatus.PathComplete, "창고 문이 열린 뒤 창고까지 걸어갈 수 있음");
        var box = Object.FindObjectsByType<InteractableItem>().First(i => i.itemName == "공구 상자");
        Check(box.requiredTrait == CharacterTrait.Strength && box.CanPickUp(CharacterType.Player2) && !box.CanPickUp(CharacterType.Player1),
            "공구 상자는 힘 특성(오빠)만 들 수 있음");
        box.PickUp(brother.GetComponent<Inventory>());
        yield return 0.2f;
        Check(ItemHandover.Give(brother, sister, "공구 상자") == HandoverResult.ReceiverCannotHandle
              && brother.GetComponent<Inventory>().HasItem("공구 상자") && !sister.GetComponent<Inventory>().HasItem("공구 상자"),
            "건네주기: 힘 특성이 없는 여동생에게 공구 상자는 못 건넴");

        // 다용도실 문 (공구 필요)
        var pantryPath = new NavMeshPath();
        NavMesh.CalculatePath(new Vector3(-3f, 0f, -0.2f), new Vector3(-6.5f, 0f, -0.2f), NavMesh.AllAreas, pantryPath);
        Check(pantryPath.status != NavMeshPathStatus.PathComplete, "닫힌 다용도실 문은 길을 막음");
        pantryDoor.Interact(brother.GetComponent<Inventory>());
        yield return 1.2f;
        Check(pantryDoor.IsOpen, "공구 가진 오빠가 다용도실 문을 엶");
        NavMesh.CalculatePath(new Vector3(-3f, 0f, -0.2f), new Vector3(-6.5f, 0f, -0.2f), NavMesh.AllAreas, pantryPath);
        Check(pantryPath.status == NavMeshPathStatus.PathComplete, "다용도실 문이 열린 뒤 걸어갈 수 있음");

        // 암호 메모 (여동생만) → 목표 달성
        var memo = Object.FindObjectsByType<InteractableItem>().First(i => i.itemName == "암호 메모");
        Check(memo.requiredTrait == CharacterTrait.Investigation && memo.CanPickUp(CharacterType.Player1) && !memo.CanPickUp(CharacterType.Player2),
            "암호 메모는 정보 수집 특성(여동생)만 읽을 수 있음");
        memo.PickUp(sister.GetComponent<Inventory>());
        yield return 0.3f;
        Check(integration.CurrentObjective() == null, "모든 목표 완료");

        // 소지품이 가득 차면 조사로 얻기도 거절 + 바닥 줍기와 같은 문구 + 그 캐릭터의 칸 수
        foreach (int index in new[] { 1, 0 })
        {
            typeof(CharacterManager).GetMethod("SwitchPlayer", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, new object[] { index });
            yield return 0.2f;
            Inventory inventory = manager.players[index].GetComponent<Inventory>();
            int heldBefore = inventory.items.Count;
            while (inventory.TryAddItem("점검용 물건")) { }
            string expected = $"{Inventory.FullMessage} ({inventory.Capacity} / {inventory.Capacity})";
            Check(!integration.hud.TryAddItem("점검용 추가 물건") && integration.hud.InventoryFullMessage == expected
                  && inventory.items.Count == inventory.Capacity,
                $"{integration.NameFor(index)}: 가득 차면 조사로 얻기 거절, 문구 '{integration.hud.InventoryFullMessage}'");
            GameObject giver = manager.players[1 - index];
            giver.GetComponent<Inventory>().AddItem("점검용 쪽지");
            Check(ItemHandover.Check(giver, manager.players[index], "점검용 쪽지") == HandoverResult.ReceiverFull,
                $"건네주기: {integration.NameFor(index)}의 소지품이 가득 차면 못 건넴");
            giver.GetComponent<Inventory>().RemoveItem("점검용 쪽지");
            inventory.items.RemoveRange(heldBefore, inventory.items.Count - heldBefore);
        }
    }
}
