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
            // 멈춘 아버지가 문을 지나는 중이었으면 그 동작이 끝난 뒤 완전히 세움 (안 그러면 문을 다시 열고 지나감)
            for (int i = 0; i < 30 && motor.IsTraversing; i++) yield return 0.1f;
            if (!father.enabled) motor.Stop();
            for (int i = 0; i < 20 && door.door.IsMoving; i++) yield return 0.1f; // 움직이는 중이면 닫기가 무시됨
            if (door.door.IsOpen && !door.door.IsBroken) { door.door.Interact(inventory1); yield return 1f; }
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
        Check(FatherMemory.TotalEscapes == 0, "새로 플레이를 시작하면 학습 기록은 비어 있음");
        Check(p1.CurrentRoom?.roomName == "서재" && p2.CurrentRoom?.roomName == "주방", "두 캐릭터는 떨어진 방(서재·주방)에서 시작");
        Check(motor.chaserType == ChaserType.Father && motor.Agent.areaMask == fatherMask, "아버지는 창문·환풍구를 쓸 수 있는 길찾기");
        Check(defaultChance > 0f && defaultChance <= 0.25f && defaultCooldown >= 30f && father.intuitionStartDelay >= 15f,
            $"직감 기본값은 낮은 확률·긴 대기 ({defaultChance:P0}, {defaultCooldown:0}초마다 최대 1번, 시작 후 {father.intuitionStartDelay:0}초 동안 없음)");
        float playerRun = move1.moveSpeed * move1.runSpeedMultiplier;
        Check(motor.runSpeed < playerRun - 1f, $"아버지 달리기({motor.runSpeed:0.0})는 플레이어 달리기({playerRun:0.0})보다 확실히 느림");
        Check(father.loseSightHesitation >= 1f && father.hidingNoiseCertainty > 0f && father.hidingNoiseCertainty < 1f && father.maxSpotsToSearch == 2,
            $"난이도 기본값: 놓친 뒤 망설임 {father.loseSightHesitation:0.0}초, 쫓는 중 숨는 소리 확신 {father.hidingNoiseCertainty:P0}, 뒤지는 숨는 곳 {father.maxSpotsToSearch}곳");

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
        Place(father, new Vector3(4.8f, 0f, -2f), 11f);    // 동쪽 복도 서쪽 끝, 여동생 쪽을 봄
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
        HidingSpot masterCloset = Spot("안방 옷장");
        Place(p1, masterCloset.transform.position);        // 갑자기 시야에서 사라져 잠긴 안방 옷장에 숨음 (학습 점검용)
        masterCloset.TryEnter(p1);
        yield return 0.15f;
        Vector3 lostAt = father.transform.position;
        Check(father.IsHesitating, $"놓친 직후에는 제자리에서 두리번거림 (상태 {father.StateLabel})");
        yield return father.loseSightHesitation * 0.6f;
        Check(father.IsHesitating && Flat(father.transform.position, lostAt) < 0.3f, $"망설이는 동안({father.loseSightHesitation:0.0}초)은 움직이지 않음");
        for (waited = 0f; waited < 5f && father.Current != FatherAI.Mode.Intercept; waited += 0.1f) yield return 0.1f;
        Check(father.Current == FatherAI.Mode.Intercept && father.HasPrediction && father.PredictedPoint.x > lastSeenX + 1.5f,
            $"놓치면 마지막 위치를 지나 움직이던 방향(동쪽)으로 앞질러 감 (마지막 x {lastSeenX:0.0} → 예측 x {father.PredictedPoint.x:0.0}, 상태 {father.StateLabel})");
        for (waited = 0f; waited < father.interceptTimeout + 4f && father.Current == FatherAI.Mode.Intercept; waited += 0.2f) yield return 0.2f;
        Check(father.Current == FatherAI.Mode.SearchSpots || father.Current == FatherAI.Mode.Search, $"예측한 곳에도 없으면 수색 (상태 {father.StateLabel})");

        // 쫓는 중에 들은 숨는 소리: 확률로만 그곳을 확실히 뒤짐
        for (waited = 0f; waited < 10f && father.Current != FatherAI.Mode.Search; waited += 0.1f) yield return 0.1f;
        HidingSpot laundryCloset = Spot("세탁실 수납장");
        float certainty = father.hidingNoiseCertainty;
        father.hidingNoiseCertainty = 0f;
        NoiseSystem.Emit(laundryCloset.transform.position, 20f, NoiseKind.Hiding, p1.gameObject);
        Check(father.Current == FatherAI.Mode.Search, $"수색 중 숨는 소리, 확률이 빗나가면: 그쪽을 평소처럼 수색 (상태 {father.StateLabel})");
        father.hidingNoiseCertainty = 1f;
        NoiseSystem.Emit(laundryCloset.transform.position, 20f, NoiseKind.Hiding, p1.gameObject);
        Check(father.Current == FatherAI.Mode.SearchSpots && father.SpotBeingChecked == laundryCloset,
            $"확률이 맞으면: 그 숨는 곳(세탁실 수납장)을 바로 뒤지러 감 (상태 {father.StateLabel})");
        father.hidingNoiseCertainty = certainty;
        for (waited = 0f; waited < father.searchTime + 30f && father.Current != FatherAI.Mode.Prowl; waited += 0.2f) yield return 0.2f;
        Check(father.Current == FatherAI.Mode.Prowl, $"수색해도 없으면 포기하고 배회 ({waited:0.0}초)");
        yield return game.chaseEndDelay + 0.3f;
        Check(!GameStateManager.IsChase && !p1.IsCaught, "포기하면 추리 상태로");
        Freeze();
        Check(FatherMemory.HideEscapes(masterCloset) == 1 && FatherMemory.RouteEscapes(Room("동쪽 복도"), Room("안방")) == 1,
            "학습 기록: 포기한 순간 숨어 있던 곳(안방 옷장)과 도망 경로(동쪽 복도 → 안방)를 기억");
        masterCloset.Exit(p1);
        yield return masterCloset.exitTime + 0.2f;

        // ==== 3-3 학습 ====
        // 문 앞에서 포기 (실제 흐름: 잠복하다 시간이 다 됨) → 그 문을 기억
        EntryPoint learnDoor = Entry("아이방 문");
        foreach (float s in SetLock(learnDoor, true)) yield return s;
        float ambushDefault = father.ambushTime;
        father.ambushTime = 2f;
        father.forceDoorTactic = DoorTactic.Ambush;
        Place(father, new Vector3(-11f, 0f, -1.5f), 0f);
        ForgetAll();
        father.enabled = true;
        father.StandStill(1f);
        yield return 0.1f;
        NoiseSystem.Emit(new Vector3(-10f, 0f, 5f), 20f, NoiseKind.Run, p1.gameObject);
        for (waited = 0f; waited < 25f && FatherMemory.DoorEscapes(learnDoor) == 0; waited += 0.2f) yield return 0.2f;
        Check(FatherMemory.DoorEscapes(learnDoor) == 1 && father.Current == FatherAI.Mode.Prowl,
            $"잠복하다 포기하면 '아이방 문으로 버팀'을 기억 ({waited:0.0}초, 상태 {father.StateLabel})");
        father.ambushTime = ambushDefault;
        father.forceDoorTactic = DoorTactic.None;
        Freeze();

        // 기준 횟수(2) 이상이면: 그 문 앞에서 우회·잠복을 더 고르고, 배회할 때 그 문 너머 방을 더 자주 들름
        FatherTactics.DoorWeights weightsBefore = father.WeightsFor(learnDoor);
        float prowlBefore = father.ProwlWeight(Room("아이방"), Room("중앙 홀"));
        Check(father.learnThreshold == 2, $"학습 기준: 같은 수법 {father.learnThreshold}회");
        FatherMemory.RecordDoorEscape(learnDoor);            // 2회째
        FatherTactics.DoorWeights weightsAfter = father.WeightsFor(learnDoor);
        var learnRng = new System.Random(5);
        float FeignShare(FatherTactics.DoorWeights w)
        {
            int feign = 0;
            var situation = new FatherTactics.DoorSituation { detourAvailable = true, detourExtra = 5f, breakable = true };
            for (int i = 0; i < 4000; i++) if (FatherTactics.Choose(situation, w, learnRng) == DoorTactic.FeignRetreat) feign++;
            return feign / 4000f;
        }
        float feignBefore = FeignShare(weightsBefore), feignAfter = FeignShare(weightsAfter);
        Check(weightsAfter.detour > weightsBefore.detour && weightsAfter.ambush > weightsBefore.ambush && weightsAfter.feign == weightsBefore.feign && feignAfter < feignBefore - 0.05f,
            $"2회 버틴 문 앞: 우회·잠복 가중치 ×{weightsAfter.detour / weightsBefore.detour:0.0} (위장 후퇴 {feignBefore:P0} → {feignAfter:P0}, 막는 판단 {1f - feignBefore:P0} → {1f - feignAfter:P0})");
        Check(father.ProwlWeight(Room("아이방"), Room("중앙 홀")) > prowlBefore,
            $"배회: 그 문 너머 방(아이방)을 더 자주 들름 (가중치 {prowlBefore:0.0} → {father.ProwlWeight(Room("아이방"), Room("중앙 홀")):0.0})");
        foreach (float s in SetLock(learnDoor, false)) yield return s;

        // 숨는 곳: 2회 이상 숨어서 넘긴 곳은 평소 후보 반경(6m) 밖이어도(1.5배까지) 먼저 뒤짐
        HidingSpot learnSpot = Spot("장난감 상자");
        Vector3 westCenter = new Vector3(-8f, 0f, 0f);
        float spotDistance = Flat(learnSpot.transform.position, westCenter);
        FatherMemory.RecordHideEscape(learnSpot);
        FatherMemory.RecordHideEscape(learnSpot);
        Place(father, westCenter, 0f);
        father.enabled = true;
        father.StandStill(5f);
        father.SearchSpotsAround(westCenter);
        Check(father.Current == FatherAI.Mode.SearchSpots && father.SpotBeingChecked == learnSpot && spotDistance > father.spotSearchRadius,
            $"자주 숨은 곳(장난감 상자, {spotDistance:0.0}m 떨어짐)을 가장 먼저 뒤지러 감 (평소 반경 {father.spotSearchRadius:0}m 밖)");
        Freeze();

        // 배회 중 자주 숨은 곳이 있는 방에 오면 가끔 열어 봄 (점검은 확률 100%로)
        float checkChance = father.checkLearnedSpotChance;
        father.checkLearnedSpotChance = 1f;
        Place(father, new Vector3(-8f, 0f, 5f), 0f);
        father.enabled = true;
        father.StandStill(0.2f);
        for (waited = 0f; waited < 3f && father.Current != FatherAI.Mode.Investigate; waited += 0.1f) yield return 0.1f;
        Check(father.Current == FatherAI.Mode.Investigate && father.InvestigateSpot == learnSpot,
            $"배회하다 아이방에 오면 자주 숨은 장난감 상자를 열어 봄 (기본 확률 {checkChance:P0})");
        father.checkLearnedSpotChance = checkChance;
        Freeze();

        // 도망 경로: 서쪽 복도에서 주방으로 2회 도망쳤다면, 서쪽 복도에서 놓쳤을 때 주방 길목으로 먼저 감
        FatherMemory.RecordRouteEscape(Room("서쪽 복도"), Room("주방"));
        FatherMemory.RecordRouteEscape(Room("서쪽 복도"), Room("주방"));
        Place(father, new Vector3(-10f, 0f, 1.5f), 160f);
        Place(p1, new Vector3(-9f, 0f, -1.5f));            // 서 있음 (움직이는 방향 없음 → 방향 예측은 불가)
        ForgetAll();
        father.enabled = true;
        father.StandStill(5f);
        for (waited = 0f; waited < 2f && father.Current != FatherAI.Mode.Chase; waited += 0.05f) yield return 0.05f;
        Check(father.Current == FatherAI.Mode.Chase, "준비: 서쪽 복도에서 여동생 발견 → 추격");
        Place(p1, new Vector3(-2f, 0f, 5f));               // 시야에서 사라짐 (잠긴 안방)
        for (waited = 0f; waited < 6f && father.Current != FatherAI.Mode.Intercept; waited += 0.1f) yield return 0.1f;
        Check(father.Current == FatherAI.Mode.Intercept && father.PredictionLearned && CameraTrigger.FindAt(father.PredictedPoint)?.roomName == "주방",
            $"서쪽 복도에서 놓치자, 늘 도망치던 주방 길목으로 먼저 감 (상태 {father.StateLabel})");
        Freeze();
        yield return game.chaseEndDelay + 0.3f;

        // ==== 3-2 잠긴 문 판단 ====
        // 판단 확률 (계산만)
        var rngCheck = new System.Random(99);
        var weights = new FatherTactics.DoorWeights { detour = father.detourWeight, feign = father.feignWeight, ambush = father.ambushWeight };
        Dictionary<DoorTactic, float> Share(FatherTactics.DoorSituation situation)
        {
            var counts = new Dictionary<DoorTactic, float>();
            foreach (DoorTactic t in Enum.GetValues(typeof(DoorTactic))) counts[t] = 0f;
            for (int i = 0; i < 4000; i++) counts[FatherTactics.Choose(situation, weights, rngCheck)] += 1f / 4000f;
            return counts;
        }
        var near = Share(new FatherTactics.DoorSituation { detourAvailable = true, detourExtra = 4f, hasWeapon = true, breakable = true });
        Check(Mathf.Abs(near[DoorTactic.Detour] - 0.45f) < 0.04f && Mathf.Abs(near[DoorTactic.FeignRetreat] - 0.25f) < 0.04f && Mathf.Abs(near[DoorTactic.Ambush] - 0.30f) < 0.04f && near[DoorTactic.Bash] == 0f,
            $"우회로가 가까우면 우회 {near[DoorTactic.Detour]:P0} · 위장 후퇴 {near[DoorTactic.FeignRetreat]:P0} · 잠복 {near[DoorTactic.Ambush]:P0}, 무기가 있어도 파괴 안 함");
        var far = Share(new FatherTactics.DoorSituation { detourAvailable = true, detourExtra = 45f, hasWeapon = false, breakable = true });
        Check(far[DoorTactic.Detour] < near[DoorTactic.Detour] - 0.15f, $"우회로가 멀면(+45m) 우회를 덜 고름 ({far[DoorTactic.Detour]:P0})");
        var none = Share(new FatherTactics.DoorSituation { detourAvailable = false, hasWeapon = false, breakable = true });
        Check(none[DoorTactic.Detour] == 0f && none[DoorTactic.Bash] == 0f && none[DoorTactic.FeignRetreat] > 0.3f && none[DoorTactic.Ambush] > 0.4f,
            $"우회로 없음 + 무기 없음: 위장 후퇴 {none[DoorTactic.FeignRetreat]:P0} · 잠복 {none[DoorTactic.Ambush]:P0} (우회·파괴 없음)");
        var armed = Share(new FatherTactics.DoorSituation { detourAvailable = false, hasWeapon = true, breakable = true });
        Check(armed[DoorTactic.Bash] > 0.999f, "우회로 없음 + 무기 있음: 최후의 수단으로 항상 파괴");

        // 장면 준비: 두 캐릭터는 잠긴 안방, 아이방 문 잠금. 아버지는 서쪽 복도 (아이방 문이 환풍구보다 확실히 가까운 쪽)
        EntryPoint kidDoor = Entry("아이방 문"), masterDoor = Entry("안방 문");
        foreach (float s in SetLock(kidDoor, true)) yield return s;
        Vector3 westHall = new Vector3(-11f, 0f, -1.5f), kidInside = new Vector3(-10f, 0f, 5f);
        Check(FatherTactics.LockedDoorOnRoute(westHall, kidInside, fatherMask, out float viaDoor, out float viaVent) == kidDoor && viaVent > viaDoor + 2f,
            $"준비: 서쪽 복도 → 아이방 안쪽은 잠긴 아이방 문이 지름길 (문 {viaDoor:0.0}m / 환풍구 {viaVent:0.0}m)");
        Vector3 DoorMid(EntryPoint e) => (e.PointA + e.PointB) * 0.5f;
        IEnumerable<float> SendFatherToKidRoom(DoorTactic tactic)
        {
            Place(father, westHall, 0f);
            father.forceDoorTactic = tactic;
            father.enabled = true;
            father.StandStill(1f);
            ForgetAll();
            yield return 0.1f;
            NoiseSystem.Emit(kidInside, 20f, NoiseKind.Run, p1.gameObject); // 아이방 안에서 소리 (여동생은 실제로는 안방에 있음)
            yield return 0.1f;
            Check(kidDoor.door.IsLocked && RoomOf(father) == "서쪽 복도",
                $"준비({tactic}): 아이방 문 잠김, 아버지는 서쪽 복도 (문 {kidDoor.door.State}, 아버지 {RoomOf(father)}, {father.StateLabel})");
        }

        // 문 확인 → 잠복 → 문이 열리면 들이닥침
        foreach (float s in SendFatherToKidRoom(DoorTactic.Ambush)) yield return s;
        bool sawApproach = false;
        for (waited = 0f; waited < 15f && father.Current != FatherAI.Mode.Ambush; waited += 0.1f)
        {
            sawApproach |= father.Current == FatherAI.Mode.DoorApproach && father.DoorTarget == kidDoor;
            yield return 0.1f;
        }
        Check(sawApproach, "가는 길이 잠긴 문을 지나면, 길찾기로 바로 돌아가지 않고 먼저 문 앞에서 손잡이를 확인");
        for (waited = 0f; waited < 6f && Flat(father.transform.position, DoorMid(kidDoor)) > 2.2f; waited += 0.1f) yield return 0.1f;
        yield return 1.5f;
        Check(father.Current == FatherAI.Mode.Ambush && father.IsSneaking && Flat(father.transform.position, DoorMid(kidDoor)) < 2.2f && kidDoor.door.IsLocked,
            $"잠복: 문 옆에서 조용히 기다림 (문까지 {Flat(father.transform.position, DoorMid(kidDoor)):0.0}m, 상태 {father.StateLabel})");
        foreach (float s in SetLock(kidDoor, false)) yield return s;  // 떠난 줄 알고 문을 엶
        for (waited = 0f; waited < 8f && RoomOf(father) != "아이방"; waited += 0.1f) yield return 0.1f;
        Check(RoomOf(father) == "아이방", $"잠금이 풀리자 바로 들이닥침 ({waited:0.0}초)");
        Freeze();
        foreach (float s in SetLock(kidDoor, true)) yield return s;

        // 우회: 환풍구로 조용히
        foreach (float s in SendFatherToKidRoom(DoorTactic.Detour)) yield return s;
        bool sawSneakDetour = false;
        for (waited = 0f; waited < 25f && RoomOf(father) != "아이방"; waited += 0.1f)
        {
            sawSneakDetour |= father.Current == FatherAI.Mode.Detour && father.IsSneaking;
            yield return 0.1f;
        }
        Check(RoomOf(father) == "아이방" && kidDoor.door.IsLocked && sawSneakDetour && father.LastDoorTactic == DoorTactic.Detour && father.LastDetourAvailable,
            $"우회: 문 대신 환풍구로 조용히 들어감 ({waited:0.0}초)");
        Freeze();

        // 위장 후퇴: 보란 듯이 멀어졌다가 → 몰래 우회
        father.feignDetourChance = 1f;
        foreach (float s in SendFatherToKidRoom(DoorTactic.FeignRetreat)) yield return s;
        float retreatedTo = 0f;
        bool loudRetreat = false, sneakAfter = false;
        for (waited = 0f; waited < 45f && RoomOf(father) != "아이방"; waited += 0.1f)
        {
            if (father.Current == FatherAI.Mode.FeignRetreat && !father.IsSneaking)
            {
                loudRetreat = true;
                retreatedTo = Mathf.Max(retreatedTo, Flat(father.transform.position, DoorMid(kidDoor)));
            }
            sneakAfter |= loudRetreat && father.Current == FatherAI.Mode.Detour && father.IsSneaking;
            yield return 0.1f;
        }
        Check(loudRetreat && retreatedTo >= 4.5f, $"위장 후퇴: 포기한 척 문에서 {retreatedTo:0.0}m 떨어진 곳까지 걸어감");
        Check(sneakAfter && RoomOf(father) == "아이방" && kidDoor.door.IsLocked, $"그 뒤 몰래 환풍구로 돌아 들어감 ({waited:0.0}초)");
        father.feignDetourChance = 0.5f;
        Freeze();
        foreach (float s in SetLock(kidDoor, false)) yield return s;

        // 우회로가 없는 안방: 무기 없으면 위장 후퇴·잠복만
        father.forceDoorTactic = DoorTactic.None;
        Place(father, hall, 0f);
        father.enabled = true;
        father.StandStill(1f);
        ForgetAll();
        yield return 0.1f;
        NoiseSystem.Emit(p1.transform.position, 20f, NoiseKind.Run, p1.gameObject);
        for (waited = 0f; waited < 15f && father.Current != FatherAI.Mode.FeignRetreat && father.Current != FatherAI.Mode.Ambush; waited += 0.1f) yield return 0.1f;
        Check((father.Current == FatherAI.Mode.FeignRetreat || father.Current == FatherAI.Mode.Ambush) && !father.LastDetourAvailable && masterDoor.door.IsLocked
              && masterDoor.door.Durability >= masterDoor.door.maxDurability,
            $"안방(우회로 없음) + 무기 없음: 문을 부수지 않고 {father.StateLabel}");
        Freeze();

        // 무기가 있으면: 최후의 수단으로 파괴 (두 캐릭터는 서재로 옮겨 둠)
        Place(p1, new Vector3(6f, 0f, 4.5f));
        Place(p2, new Vector3(7f, 0f, 4f));
        father.hasWeapon = true;
        Place(father, hall, 0f);
        father.enabled = true;
        father.StandStill(1f);
        ForgetAll();
        yield return 0.1f;
        NoiseSystem.Emit(new Vector3(-2f, 0f, 5f), 20f, NoiseKind.Run, p1.gameObject);
        for (waited = 0f; waited < 20f && !masterDoor.door.IsBroken; waited += 0.2f) yield return 0.2f;
        Check(father.LastDoorTactic == DoorTactic.Bash && masterDoor.door.IsBroken, $"안방 + 무기 있음: 문을 부숨 ({waited:0.0}초)");
        for (waited = 0f; waited < 8f && RoomOf(father) != "안방"; waited += 0.2f) yield return 0.2f;
        Check(RoomOf(father) == "안방", "부순 문으로 들어가 확인");
        father.hasWeapon = false;
        Freeze();
        yield return game.chaseEndDelay + 0.3f;

        // ---- 직감: 숨지 않은 캐릭터가 있는 방으로 → 조작하지 않는 캐릭터도 노림 ----
        foreach (float s in SetLock(Entry("안방 문"), false)) yield return s;
        HidingSpot closet = Spot("안방 옷장");
        Place(p1, closet.transform.position);
        closet.TryEnter(p1);                                 // 조작 중인 여동생은 숨음 (직감 대상에서 빠짐)
        yield return closet.enterTime + 0.2f;
        Place(p2, new Vector3(5f, 0f, -8.2f));              // 오빠는 세탁실 구석에 서 있음
        Place(father, new Vector3(-2f, 0f, -1.5f), 180f);
        ForgetAll();
        father.intuitionChance = 1f;
        father.ClearIntuitionCooldown();
        father.enabled = true;
        father.ResetToProwl();
        Check(p1.IsActive && p1.IsHidden && father.IntuitionTarget == p2 && father.ProwlRoom == p2.CurrentRoom,
            $"직감: 숨은 캐릭터는 빼고, 조작하지 않는 오빠가 있는 방(세탁실)으로 감 (고른 방 {father.ProwlRoom?.roomName})");
        father.intuitionChance = 0f;                         // 연달아 작동하지 않도록
        for (waited = 0f; waited < 20f && !game.IsTargeted(p2); waited += 0.1f) yield return 0.1f;
        Check(game.IsTargeted(p2) && !p2.IsActive, $"직감으로 찾아가 조작하지 않는 캐릭터를 발견 → 추격 ({waited:0.0}초)");
        Freeze();
        Check(!p2.IsCaught, "준비: 오빠는 잡히지 않음");
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
        Check(father.Current == FatherAI.Mode.Chase && game.IsTargeted(p1), "준비: 주방에 있는 여동생을 발견 → 추격");
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

        // 잡혀서 씬이 다시 시작돼도 학습은 남음 (오브젝트는 새로 만들어지므로 이름으로 다시 찾음)
        yield return game.restartDelay + 2f;
        var reloadedFather = Object.FindAnyObjectByType<FatherAI>();
        EntryPoint reloadedDoor = EntryPoint.All.FirstOrDefault(e => e.displayName == "아이방 문");
        HidingSpot reloadedSpot = HidingSpot.All.FirstOrDefault(s => s.displayName == "장난감 상자");
        Check(reloadedFather != null && reloadedFather != father && reloadedDoor != null
              && FatherMemory.DoorEscapes(reloadedDoor) >= 2 && FatherMemory.HideEscapes(reloadedSpot) >= 2,
            $"씬이 재시작돼도 학습 유지 (아이방 문 {FatherMemory.DoorEscapes(reloadedDoor)}회, 장난감 상자 {FatherMemory.HideEscapes(reloadedSpot)}회)");
    }
}
