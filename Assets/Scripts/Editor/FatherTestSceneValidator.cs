using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// 아버지 테스트 씬 점검
//  - Validate: 빠진 스크립트, 필수 구성, 방마다 바닥 NavMesh, 링크 끝 (에디터 상태)
//  - 플레이 점검: 맵 동선(우회로 있음/먼 우회/우회 불가), 배회, 숨는 소리 → 환풍구로 들어가 뒤짐,
//    추격 → 놓침 → 예측해 앞질러 감 → 수색 → 포기, 자주 보인 장소 기억, 직감(조작하지 않는 캐릭터),
//    숨는 장면을 못 봤어도 놓친 곳 근처 숨는 곳을 뒤져 잡음 (키 입력 대신 함수 호출)
public static class FatherTestSceneValidator
{
    const string Tag = "[OTK 아버지 플레이 점검]";

    [MenuItem("OTK/아버지 테스트 씬 점검")]
    static void ValidateFromMenu()
    {
        EditorSceneManager.OpenScene(FatherTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        if (Object.FindAnyObjectByType<GameStateManager>() == null) problems.Add("GameStateManager 없음");
        if (Object.FindAnyObjectByType<FadeManager>() == null) problems.Add("FadeManager 없음");
        var father = Object.FindAnyObjectByType<FatherAI>();
        if (father == null) problems.Add("아버지 AI 없음");
        else if (father.GetComponent<ChaserMotor>().chaserType != ChaserType.Father) problems.Add("아버지 ChaserMotor 종류가 Father가 아님");
        if (Object.FindAnyObjectByType<PoliceAI>() != null) problems.Add("아버지 테스트 씬에 경찰이 있음");

        var entries = Object.FindObjectsByType<EntryPoint>();
        if (entries.Count(e => e.kind == EntryKind.Door) != 9) problems.Add("문 9개가 아님");
        if (entries.Count(e => e.kind == EntryKind.Window) != 4) problems.Add("창문 4개가 아님");
        if (entries.Count(e => e.kind == EntryKind.Vent) != 2) problems.Add("환풍구 2개가 아님");
        foreach (EntryPoint entry in entries)
        {
            if (entry.link == null) { problems.Add($"{entry.displayName}: 링크 없음"); continue; }
            foreach (Vector3 end in new[] { entry.PointA, entry.PointB })
                if (!NavMesh.SamplePosition(end, out _, 0.3f, NavMesh.AllAreas)) problems.Add($"{entry.displayName}: 링크 끝이 NavMesh 위에 없음 {end}");
        }
        foreach (HidingSpot spot in Object.FindObjectsByType<HidingSpot>())
            if (!NavMesh.SamplePosition(spot.transform.position, out _, 0.6f, NavMesh.AllAreas)) problems.Add($"{spot.displayName}: 숨는 곳 앞이 NavMesh 위에 없음");
        if (Object.FindObjectsByType<HidingSpot>().Length != 9) problems.Add("숨는 곳 9개가 아님");

        foreach (CameraTrigger room in Object.FindObjectsByType<CameraTrigger>())
        {
            Bounds area = room.GetComponent<Collider>().bounds;
            if (!NavMesh.SamplePosition(new Vector3(area.center.x + 1.5f, 0f, area.center.z - 1.5f), out _, 1.5f, NavMesh.AllAreas))
                problems.Add($"{room.roomName}: 바닥에 NavMesh 없음");
        }

        string result = problems.Count == 0 ? "문제 없음" : string.Join("\n", problems);
        if (problems.Count == 0) Debug.Log("[OTK 아버지 점검] 씬 점검 통과");
        else Debug.LogError("[OTK 아버지 점검] 씬 점검 실패\n" + result);
        if (showDialog) EditorUtility.DisplayDialog("아버지 테스트 씬 점검", result, "확인");
        return problems.Count == 0;
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod FatherTestSceneValidator.RunPlaySmokeTest (0=통과, 3=실패)
    public static void RunPlaySmokeTest()
    {
        EditorSceneManager.OpenScene(FatherTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        if (failures.Count == 0) Debug.Log(Tag + " 전부 통과");
        else Debug.Log(Tag + " 실패\n" + string.Join("\n", failures));
        EditorApplication.Exit(failures.Count == 0 ? 0 : 3);
    }

    static void Check(bool condition, string what)
    {
        if (condition) Debug.Log(Tag + " OK  " + what);
        else { Debug.Log(Tag + " FAIL " + what); failures.Add(what); }
    }

    static string RoomOf(Component c) => CameraTrigger.FindAt(c.transform.position)?.roomName ?? "(없음)";

    static int PlayerMask()
    {
        int mask = NavMesh.AllAreas;
        foreach (string area in new[] { EntryPoint.DoorArea, EntryPoint.WindowArea, EntryPoint.VentArea })
            mask &= ~(1 << NavMesh.GetAreaFromName(area));
        return mask;
    }

    // 길이 없으면 -1
    static float PathLength(Vector3 from, Vector3 to, int mask)
    {
        var path = new NavMeshPath();
        NavMesh.CalculatePath(from, to, mask, path);
        if (path.status != NavMeshPathStatus.PathComplete) return -1f;
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return length;
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // 각 단계 뒤 숫자 = 다음 단계까지 기다릴 시간(초)
    static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        var game = GameStateManager.instance;
        CharacterStatus p1 = manager.players[0].GetComponent<CharacterStatus>();
        CharacterStatus p2 = manager.players[1].GetComponent<CharacterStatus>();
        var move1 = p1.GetComponent<PlayerMovement>();
        Inventory inventory1 = p1.GetComponent<Inventory>();
        var father = Object.FindAnyObjectByType<FatherAI>();
        var motor = father.GetComponent<ChaserMotor>();
        var senses = father.GetComponent<ChaserSenses>();
        EntryPoint Entry(string name) => EntryPoint.All.First(e => e.displayName == name);
        HidingSpot Spot(string name) => HidingSpot.All.First(s => s.displayName == name);
        CameraTrigger Room(string name) => CameraTrigger.All.First(r => r.roomName == name);
        int fatherMask = EntryPoint.AreaMaskFor(ChaserType.Father), policeMask = EntryPoint.AreaMaskFor(ChaserType.Police), player = PlayerMask();
        float waited;

        void Place(Component who, Vector3 floor, float yaw = float.NaN)
        {
            ChaserMotor chaserMotor = who.GetComponent<ChaserMotor>();
            if (chaserMotor != null) chaserMotor.Teleport(floor);
            else who.GetComponent<NavMeshAgent>().Warp(floor);
            if (!float.IsNaN(yaw)) who.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        void Freeze()
        {
            father.ResetToProwl();
            father.enabled = false;
            motor.Stop();
        }
        void ForgetAll() { senses.Forget(p1); senses.Forget(p2); }
        IEnumerable<float> SetLock(EntryPoint door, bool locked)
        {
            if (door.door.IsOpen) { door.door.Interact(inventory1); yield return 1f; }
            if (door.door.IsLocked != locked) door.door.ToggleLock(inventory1);
            yield return 0.1f;
        }

        // 준비: 아버지는 끄고 시작, 직감은 직감 점검 때만
        Freeze();
        float defaultChance = father.intuitionChance, defaultCooldown = father.intuitionCooldown;
        father.intuitionChance = 0f;
        father.searchTime = 3f;     // 점검 시간 단축
        father.Reseed(12345);
        Place(father, new Vector3(0f, 0f, -1.5f), 180f);
        for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.2f;
        yield return 0.3f;

        // ---- 시작 상태 ----
        Check(GameStateManager.State == GameState.Investigation, "시작은 추리 상태");
        Check(p1.CurrentRoom?.roomName == "서재" && p2.CurrentRoom?.roomName == "주방", "두 캐릭터는 떨어진 방(서재·주방)에서 시작");
        Check(motor.chaserType == ChaserType.Father && motor.Agent.areaMask == fatherMask, "아버지는 창문·환풍구를 쓸 수 있는 길찾기");
        Check(defaultChance > 0f && defaultChance <= 0.25f && defaultCooldown >= 30f && father.intuitionStartDelay >= 15f,
            $"직감 기본값은 낮은 확률·긴 대기 ({defaultChance:P0}, {defaultCooldown:0}초마다 최대 1번, 시작 후 {father.intuitionStartDelay:0}초 동안 없음)");

        // ---- 맵 동선: 북쪽 방 3개의 잠긴 문 대처가 서로 다르게 ----
        Vector3 hall = new Vector3(0f, 0f, -1.5f);
        Vector3 inKid = new Vector3(-8f, 0f, 5f), inMaster = new Vector3(-2f, 0f, 5f), inStudy = new Vector3(6f, 0f, 4.5f);
        Check(PathLength(new Vector3(-8f, 0f, 0f), inKid, player) < 0f, "닫힌 문은 플레이어 길을 막음");
        foreach (float s in SetLock(Entry("안방 문"), true)) yield return s;
        foreach (float s in SetLock(Entry("아이방 문"), true)) yield return s;
        foreach (float s in SetLock(Entry("서재 문"), true)) yield return s;
        float toKid = PathLength(hall, inKid, fatherMask), toStudy = PathLength(hall, inStudy, fatherMask);
        Check(PathLength(hall, inMaster, fatherMask) < 0f, "안방: 문을 잠그면 아버지도 들어갈 길이 없음 (우회 불가 → 다음 단계 '파괴' 대상)");
        Check(toKid > 0f && toKid < 20f, $"아이방: 문을 잠가도 환풍구로 짧게 우회 ({toKid:0.0}m)");
        Check(toStudy > 25f, $"서재: 문을 잠그면 현관 → 마당 → 창문으로 멀리 우회 ({toStudy:0.0}m)");
        Check(PathLength(hall, inKid, policeMask) < 0f && PathLength(hall, inStudy, policeMask) < 0f, "경찰이었다면 두 방 모두 못 들어감 (창문·환풍구는 아버지 전용)");
        foreach (float s in SetLock(Entry("안방 문"), false)) yield return s;
        foreach (float s in SetLock(Entry("서재 문"), false)) yield return s;
        foreach (float s in SetLock(Entry("주방 문"), true)) yield return s;
        Check(PathLength(new Vector3(-8f, 0f, 0f), new Vector3(-8f, 0f, -6f), policeMask) > 0f, "주방 문을 잠가도 식당 쪽 문으로 돌아가는 순환 동선이 있음");
        foreach (float s in SetLock(Entry("주방 문"), false)) yield return s;

        // ---- 배회 ----
        // 두 캐릭터는 잠긴 안방에 넣어 둠 (아버지가 들어갈 길이 없음 → 배회·소리 점검 중에 잡히지 않음)
        Place(p1, new Vector3(-2f, 0f, 5f));
        Place(p2, new Vector3(-1f, 0f, 4f));
        foreach (float s in SetLock(Entry("안방 문"), true)) yield return s;
        Place(father, hall, 180f);
        ForgetAll();
        father.enabled = true;
        father.ResetToProwl();
        CameraTrigger firstRoom = father.ProwlRoom;
        yield return 4f;
        Check(father.Current == FatherAI.Mode.Prowl && Flat(father.transform.position, hall) > 1f && firstRoom != null,
            $"배회: 방을 골라 돌아다님 (처음 고른 방: {firstRoom?.roomName}, 지금: {father.StateLabel})");

        // ---- 숨는 소리 → 그 숨는 곳을 뒤짐. 문이 잠겨 있으면 환풍구로 (아이방 문은 위에서 잠가 둠) ----
        HidingSpot toyBox = Spot("장난감 상자");
        Place(father, hall, 0f);
        father.ResetToProwl();
        ForgetAll();
        yield return 0.1f;
        NoiseSystem.Emit(toyBox.transform.position, 20f, NoiseKind.Hiding, p2.gameObject);
        yield return 0.1f;
        Check(father.Current == FatherAI.Mode.Investigate && father.InvestigateSpot == toyBox, "숨는 소리를 들으면 그 숨는 곳(장난감 상자)을 뒤지러 감");
        for (waited = 0f; waited < 20f && Flat(father.transform.position, toyBox.transform.position) > 1.6f; waited += 0.25f) yield return 0.25f;
        Check(RoomOf(father) == "아이방" && Entry("아이방 문").door.IsLocked && !Entry("아이방 문").door.IsOpen,
            $"잠긴 아이방 문 대신 환풍구로 들어가 숨는 곳 앞까지 감 ({waited:0.0}초, 위치 {RoomOf(father)})");
        yield return father.spotCheckTime + father.lookAroundTime + 1f;
        Check(father.Current == FatherAI.Mode.Prowl || father.Current == FatherAI.Mode.Investigate && father.InvestigateSpot == null,
            $"비어 있으면 둘러본 뒤 다시 배회 (상태: {father.StateLabel})");
        Freeze();
        foreach (float s in SetLock(Entry("아이방 문"), false)) yield return s;

        // ---- 추격 → 놓침 → 움직이던 방향을 예측해 앞질러 감 → 수색 → 포기 ----
        Place(father, new Vector3(4.8f, 0f, -2f), 11f);    // 동쪽 복도 서쪽 끝, 탐정 쪽을 봄
        Place(p1, new Vector3(5.5f, 0f, 1.5f));
        ForgetAll();
        int sightingsBefore = FatherMemory.Sightings(Room("동쪽 복도"));
        father.enabled = true;
        father.StandStill(5f);
        for (waited = 0f; waited < 2f && father.Current != FatherAI.Mode.Chase; waited += 0.05f) yield return 0.05f;
        Check(father.Current == FatherAI.Mode.Chase && GameStateManager.IsChase && game.IsTargeted(p1), "발견 → 추격 + 게임 추격 상태");
        Check(FatherMemory.Sightings(Room("동쪽 복도")) > sightingsBefore, "본 장소(동쪽 복도)를 기억");
        move1.scriptedInput = new Vector2(1f, 0f);         // 동쪽으로 뜀
        move1.scriptedRun = true;
        yield return 0.3f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        float lastSeenX = p1.transform.position.x;
        Place(p1, new Vector3(-2f, 0f, 5f));               // 갑자기 시야에서 사라짐 (잠긴 안방 안)
        for (waited = 0f; waited < 4f && father.Current != FatherAI.Mode.Intercept; waited += 0.1f) yield return 0.1f;
        Check(father.Current == FatherAI.Mode.Intercept && father.HasPrediction && father.PredictedPoint.x > lastSeenX + 1.5f,
            $"놓치면 마지막 위치를 지나 움직이던 방향(동쪽)으로 앞질러 감 (마지막 x {lastSeenX:0.0} → 예측 x {father.PredictedPoint.x:0.0}, 상태 {father.StateLabel})");
        for (waited = 0f; waited < father.interceptTimeout + 4f && father.Current == FatherAI.Mode.Intercept; waited += 0.2f) yield return 0.2f;
        Check(father.Current == FatherAI.Mode.SearchSpots || father.Current == FatherAI.Mode.Search, $"예측한 곳에도 없으면 수색 (상태 {father.StateLabel})");
        for (waited = 0f; waited < father.searchTime + 12f && father.Current != FatherAI.Mode.Prowl; waited += 0.2f) yield return 0.2f;
        Check(father.Current == FatherAI.Mode.Prowl, $"수색해도 없으면 포기하고 배회 ({waited:0.0}초)");
        yield return game.chaseEndDelay + 0.3f;
        Check(!GameStateManager.IsChase && !p1.IsCaught, "포기하면 추리 상태로");
        Freeze();

        // ---- 직감: 숨지 않은 캐릭터가 있는 방으로 → 조작하지 않는 캐릭터도 노림 ----
        foreach (float s in SetLock(Entry("안방 문"), false)) yield return s;
        HidingSpot closet = Spot("안방 옷장");
        Place(p1, closet.transform.position);
        closet.TryEnter(p1);                                 // 조작 중인 탐정은 숨음 (직감 대상에서 빠짐)
        yield return closet.enterTime + 0.2f;
        Place(p2, new Vector3(5f, 0f, -8.2f));              // 조수는 세탁실 구석에 서 있음
        Place(father, new Vector3(-2f, 0f, -1.5f), 180f);
        ForgetAll();
        father.intuitionChance = 1f;
        father.ClearIntuitionCooldown();
        father.enabled = true;
        father.ResetToProwl();
        Check(p1.IsActive && p1.IsHidden && father.IntuitionTarget == p2 && father.ProwlRoom == p2.CurrentRoom,
            $"직감: 숨은 캐릭터는 빼고, 조작하지 않는 조수가 있는 방(세탁실)으로 감 (고른 방 {father.ProwlRoom?.roomName})");
        father.intuitionChance = 0f;                         // 연달아 작동하지 않도록
        for (waited = 0f; waited < 20f && !game.IsTargeted(p2); waited += 0.1f) yield return 0.1f;
        Check(game.IsTargeted(p2) && !p2.IsActive, $"직감으로 찾아가 조작하지 않는 캐릭터를 발견 → 추격 ({waited:0.0}초)");
        Freeze();
        Check(!p2.IsCaught, "준비: 조수는 잡히지 않음");
        closet.Exit(p1);
        yield return closet.exitTime + 0.2f;
        father.intuitionCooldown = defaultCooldown;
        Place(p2, new Vector3(-2f, 0f, 5f));                 // 안방으로 치움
        yield return game.chaseEndDelay + 0.3f;

        // ---- 숨는 장면을 못 봤어도, 놓친 곳 근처 숨는 곳을 뒤져 잡음 (씬 재시작이 걸리므로 마지막) ----
        HidingSpot pantry = Spot("팬트리");
        foreach (float s in SetLock(Entry("주방 문"), false)) yield return s;
        Entry("주방 문").door.Interact(inventory1);         // 열어 둠
        yield return 1f;
        Place(p1, new Vector3(-8f, 0f, -5.5f));             // 주방
        Place(father, new Vector3(-8f, 0f, -0.5f), 180f);   // 서쪽 복도에서 열린 주방 문 너머를 봄
        ForgetAll();
        father.enabled = true;
        father.StandStill(5f);
        for (waited = 0f; waited < 2f && father.Current != FatherAI.Mode.Chase; waited += 0.05f) yield return 0.05f;
        Check(father.Current == FatherAI.Mode.Chase && game.IsTargeted(p1), "준비: 주방에 있는 탐정을 발견 → 추격");
        father.enabled = false;                              // 숨는 동안 잠깐 멈춤
        motor.Stop();
        Place(father, new Vector3(-11f, 0f, 1.5f), 0f);     // 숨는 장면이 보이지 않는 곳
        Place(p1, pantry.transform.position);
        pantry.TryEnter(p1);
        yield return pantry.enterTime + 0.2f;
        Check(p1.IsHidden && !pantry.WasSeenEnteringBy(father.gameObject), "준비: 아무도 못 본 채 팬트리에 숨음");
        father.enabled = true;
        for (waited = 0f; waited < 25f && !p1.IsCaught; waited += 0.25f) yield return 0.25f;
        Check(p1.IsCaught && game.RestartPending, $"놓친 곳 근처 숨는 곳을 뒤져서 잡음 → 씬 재시작 예약 ({waited:0.0}초)");
    }
}
