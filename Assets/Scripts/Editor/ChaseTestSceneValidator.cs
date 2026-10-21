using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// 추격 테스트 씬 점검
//  - Validate: 빠진 스크립트, 필수 구성, 방마다 바닥 NavMesh (에디터 상태)
//  - 플레이 점검: 달리기·스태미나·소음, 잠금 일원화, 위치, 문/창문/환풍구 길찾기, 추격자 문 통과·막힘·부수기,
//    시야·청각, 추격 상태(즉시 컷), 추격 대상 규칙, 숨기(목격/비목격/정원/허술한 곳), 잡힘 (키 입력 대신 함수 호출)
public static class ChaseTestSceneValidator
{
    [MenuItem("OTK/추격 테스트 씬 점검")]
    static void ValidateFromMenu()
    {
        EditorSceneManager.OpenScene(ChaseTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        if (Object.FindAnyObjectByType<PoliceAI>() == null) problems.Add("경찰 AI 없음");

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
        if (Object.FindObjectsByType<HidingSpot>().Length != 6) problems.Add("숨는 곳 6개가 아님");

        foreach (CameraTrigger room in Object.FindObjectsByType<CameraTrigger>())
        {
            Bounds area = room.GetComponent<Collider>().bounds;
            if (!NavMesh.SamplePosition(new Vector3(area.center.x + 0.9f, 0f, area.center.z + 0.9f), out _, 1.5f, NavMesh.AllAreas))
                problems.Add($"{room.roomName}: 바닥에 NavMesh 없음");
        }

        string result = problems.Count == 0 ? "문제 없음" : string.Join("\n", problems);
        if (problems.Count == 0) Debug.Log("[OTK 추격 점검] 씬 점검 통과");
        else Debug.LogError("[OTK 추격 점검] 씬 점검 실패\n" + result);
        if (showDialog) EditorUtility.DisplayDialog("추격 테스트 씬 점검", result, "확인");
        return problems.Count == 0;
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod ChaseTestSceneValidator.RunPlaySmokeTest (0=통과, 3=실패)
    public static void RunPlaySmokeTest()
    {
        EditorSceneManager.OpenScene(ChaseTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        if (failures.Count == 0) Debug.Log("[OTK 추격 플레이 점검] 전부 통과");
        else Debug.Log("[OTK 추격 플레이 점검] 실패\n" + string.Join("\n", failures));
        EditorApplication.Exit(failures.Count == 0 ? 0 : 3);
    }

    static void Check(bool condition, string what)
    {
        if (condition) Debug.Log("[OTK 추격 플레이 점검] OK  " + what);
        else { Debug.Log("[OTK 추격 플레이 점검] FAIL " + what); failures.Add(what); }
    }

    static string RoomOf(Component c) => CameraTrigger.FindAt(c.transform.position)?.roomName ?? "(없음)";

    static int PlayerMask()
    {
        int mask = NavMesh.AllAreas;
        foreach (string area in new[] { EntryPoint.DoorArea, EntryPoint.WindowArea, EntryPoint.VentArea })
            mask &= ~(1 << NavMesh.GetAreaFromName(area));
        return mask;
    }

    static bool PathComplete(Vector3 from, Vector3 to, int mask)
    {
        var path = new NavMeshPath();
        NavMesh.CalculatePath(from, to, mask, path);
        return path.status == NavMeshPathStatus.PathComplete;
    }

    // 각 단계 뒤 숫자 = 다음 단계까지 기다릴 시간(초)
    static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        var game = GameStateManager.instance;
        CharacterStatus p1 = manager.players[0].GetComponent<CharacterStatus>();
        CharacterStatus p2 = manager.players[1].GetComponent<CharacterStatus>();
        var move1 = p1.GetComponent<PlayerMovement>();
        var stamina1 = p1.GetComponent<PlayerStamina>();
        var chaser = Object.FindAnyObjectByType<PoliceAI>();
        var motor = chaser.GetComponent<ChaserMotor>();
        var senses = chaser.GetComponent<ChaserSenses>();
        EntryPoint Entry(string name) => EntryPoint.All.First(e => e.displayName == name);
        HidingSpot Spot(string name) => HidingSpot.All.First(s => s.displayName == name);
        int police = EntryPoint.AreaMaskFor(ChaserType.Police), father = EntryPoint.AreaMaskFor(ChaserType.Father), player = PlayerMask();

        var noises = new List<NoiseEvent>();
        NoiseSystem.Emitted += noises.Add;

        // 시험 중에는 더미 AI를 끄고 직접 조종
        chaser.enabled = false;
        motor.Stop();
        void Place(Component who, Vector3 floor, float yaw = float.NaN)
        {
            ChaserMotor chaserMotor = who.GetComponent<ChaserMotor>();
            if (chaserMotor != null) chaserMotor.Teleport(floor);
            else who.GetComponent<NavMeshAgent>().Warp(floor);
            if (!float.IsNaN(yaw)) who.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        Place(chaser, new Vector3(8f, 0f, -6f), 0f); // 창고 구석

        for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.2f;
        yield return 0.3f;

        // ---- 시작 상태 · 위치 ----
        Check(GameStateManager.State == GameState.Investigation, "시작은 추리 상태");
        Check(p1.GetComponent<NavMeshAgent>().isOnNavMesh && p2.GetComponent<NavMeshAgent>().isOnNavMesh, "두 캐릭터 NavMesh 위");
        Check(p1.CurrentRoom != null && p1.CurrentRoom.roomName == "거실" && p1.CurrentRoom.buildingId == "House" && p1.CurrentRoom.floor == 0,
            "캐릭터 위치 조회: 집/1층/거실");
        Check(p1.State == CharacterState.Normal && p2.State == CharacterState.Normal, "캐릭터 상태 = 보통 (조작 안 하는 캐릭터 포함)");
        Check(CameraTrigger.All.First(r => r.roomName == "거실").virtualCamera.activeSelf, "거실 카메라 켜짐");

        // ---- 잠금 일원화 ----
        move1.SetMovementEnabled(false);
        move1.Locks.Set(LockReason.Hiding, true);
        move1.SetMovementEnabled(true);
        Check(move1.Locks.IsLocked, "다른 이유의 잠금(숨기)은 예전 방식 잠금 해제에 덮어써지지 않음");
        move1.Locks.Set(LockReason.Hiding, false);
        Check(!move1.Locks.IsLocked, "모든 이유가 풀리면 이동 가능");

        // ---- 달리기 · 스태미나 · 발소리 ----
        Place(p1, new Vector3(-9f, 0f, 1.6f)); // 거실 서쪽 끝 → 동쪽으로 뛸 공간
        yield return 0.2f;
        Vector3 before = p1.transform.position;
        float staminaBefore = stamina1.Current;
        noises.Clear();
        move1.scriptedInput = new Vector2(1f, 0f); // 화면 오른쪽
        move1.scriptedRun = true;
        yield return 0.4f;
        Check(move1.IsRunning && p1.State == CharacterState.Running, "Shift 달리기 → 상태 '달리는 중'");
        Check(stamina1.Current < staminaBefore, "달리면 스태미나 감소");
        Check(noises.Any(n => n.kind == NoiseKind.Run && n.source == p1.gameObject), "달리기 발소리 발생");
        float runDistance = (p1.transform.position - before).magnitude;
        move1.scriptedRun = false;
        noises.Clear();
        before = p1.transform.position;
        move1.scriptedInput = new Vector2(-1f, 0f);
        yield return 0.4f;
        float walkDistance = (p1.transform.position - before).magnitude;
        Check(!move1.IsRunning && noises.Any(n => n.kind == NoiseKind.Footstep), "걷기 발소리는 작은 소리");
        Check(runDistance > walkDistance * 1.3f, $"달리기가 걷기보다 빠름 ({runDistance:0.00} > {walkDistance:0.00})");
        stamina1.Drain(1000f);
        move1.scriptedRun = true;
        yield return 0.2f;
        Check(stamina1.Exhausted && !move1.IsRunning && move1.IsMoving, "스태미나가 바닥나면 달릴 수 없음(걷기만)");
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        Place(p1, new Vector3(-6f, 0f, 1.2f));
        yield return 0.3f;

        // ---- 문: 플레이어는 닫힌 문을 못 지나가고, 추격자는 열고 지나감 ----
        Vector3 living = new Vector3(-5f, 0f, 1f), hall = new Vector3(-1.8f, 0f, 1.2f);
        EntryPoint livingDoor = Entry("거실 문");
        Check(!PathComplete(living, hall, player), "닫힌 문은 플레이어 길을 막음");
        Check(PathComplete(living, hall, police), "잠기지 않은 닫힌 문은 추격자 길찾기에 포함");

        livingDoor.door.ToggleLock(p1.GetComponent<Inventory>());
        yield return 0.1f;
        Check(livingDoor.door.IsLocked && livingDoor.door.State == DoorState.Locked, "플레이어가 문을 잠금(F)");
        Check(!PathComplete(hall, living, police), "잠긴 문: 경찰은 거실로 갈 길이 없음");
        Check(PathComplete(hall, living, father), "잠긴 문: 아버지는 환풍구로 거실에 갈 수 있음");
        EntryPoint blocking = EntryPoint.FindBlockingDoor(hall, living, police);
        Check(blocking == livingDoor, "막고 있는 잠긴 문 찾기 = 거실 문");
        Vector3 yard = new Vector3(-12f, 0f, 1.2f);
        Check(!PathComplete(yard, living, police) && PathComplete(yard, living, father), "창문은 아버지만 (마당 → 거실)");
        livingDoor.door.ToggleLock(p1.GetComponent<Inventory>());
        yield return 0.1f;

        // 추격자가 닫힌 문을 직접 열고 지나감
        Place(chaser, hall, 270f);
        yield return 0.2f;
        motor.SetDestination(new Vector3(-6.5f, 0f, -1.8f));
        float waited = 0f;
        while (waited < 8f && RoomOf(chaser) != "거실") { waited += 0.25f; yield return 0.25f; }
        Check(RoomOf(chaser) == "거실" && livingDoor.door.IsOpen, $"추격자가 닫힌 문을 열고 거실로 들어감 ({waited:0.0}초)");

        // 경로를 정했는데 문이 잠겨 있으면 막힘
        Place(chaser, hall, 270f);
        livingDoor.door.Interact(p1.GetComponent<Inventory>()); // 닫기
        yield return 1f;
        livingDoor.door.ToggleLock(p1.GetComponent<Inventory>());
        motor.SetDestination(new Vector3(-6.5f, 0f, -1.8f));
        yield return 1.5f;
        Check(RoomOf(chaser) == "홀" && !motor.HasCompletePath, "잠긴 문 앞에서 추격자가 막힘");

        // 부수기 (욕실 문)
        EntryPoint bathDoor = Entry("욕실 문");
        bathDoor.door.ToggleLock(p1.GetComponent<Inventory>());
        noises.Clear();
        Vector3 bath = new Vector3(-6f, 0f, -4.5f);
        Check(!PathComplete(living, bath, police), "잠긴 욕실 문이 길을 막음");
        bathDoor.door.Bash(40f, chaser.gameObject);
        Check(!bathDoor.door.IsBroken && bathDoor.door.Durability < bathDoor.door.maxDurability && noises.Any(n => n.kind == NoiseKind.DoorBash),
            "문 내리치기: 내구도 감소 + 큰 소음");
        bathDoor.door.Bash(100f, chaser.gameObject);
        yield return 0.2f;
        Check(bathDoor.door.IsBroken && !bathDoor.door.IsLocked && PathComplete(living, bath, player), "내구도 0 → 문이 부서져 누구나 지나감");
        livingDoor.door.ToggleLock(p1.GetComponent<Inventory>()); // 다시 풀어 둠

        // ---- 감각: 시야 · 청각 ----
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 0f);   // 홀, 북쪽(복도 방향)을 봄
        Place(p2, new Vector3(1.5f, 0f, 1.6f));            // 바로 앞 3m
        yield return 0.8f;
        Check(senses.CanSee(p2) && senses.IsSpotted(p2), "정면 3m: 보이고 발견됨");
        Place(p2, new Vector3(1.5f, 0f, -4.5f));           // 등 뒤 3m (현관)
        senses.Forget(p2);
        yield return 0.5f;
        Check(!senses.CanSee(p2), "등 뒤(시야각 밖)는 안 보임");
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 90f);  // 주방 쪽(동쪽)을 봄
        Place(p2, new Vector3(6f, 0f, -1.2f));             // 주방 (닫힌 문·벽 너머 4.5m)
        yield return 0.3f;
        Check(!senses.CanSee(p2), "닫힌 문·벽 너머는 안 보임");
        senses.Forget(p2);
        // 벽 너머라 반경이 절반으로 줄어듦: 10m → 5m (들림), 6m → 3m (안 들림)
        NoiseSystem.Emit(p2.transform.position, 10f, NoiseKind.Run, p2.gameObject);
        bool heardLoud = senses.LastHeard.HasValue && senses.LastHeard.Value.source == p2.gameObject && senses.LastHeard.Value.radius == 10f;
        NoiseSystem.Emit(p2.transform.position, 6f, NoiseKind.Footstep, p2.gameObject);
        bool heardQuiet = senses.LastHeard.HasValue && senses.LastHeard.Value.radius == 6f;
        Check(heardLoud, "벽 너머 큰 소리는 들림");
        Check(!heardQuiet, "벽 너머 작은 소리는 줄어서 안 들림");
        NoiseSystem.Emit(chaser.transform.position, 12f, NoiseKind.Door, chaser.gameObject);
        Check(senses.LastHeard.Value.radius != 12f, "자기가 낸 소리는 무시");

        // ---- 추격 대상 규칙 ----
        Place(p1, new Vector3(-1.5f, 0f, 1.5f));
        Place(p2, new Vector3(1.5f, 0f, 1.5f));
        yield return 0.3f;
        Check(ChaseTargeting.FromSighting(p1).Count == 2, "같은 방(홀)에 있으면 둘 다 추격 대상");
        Place(p2, new Vector3(6f, 0f, 5f));
        yield return 0.3f;
        var single = ChaseTargeting.FromSighting(p1);
        Check(single.Count == 1 && single[0] == p1, "다른 방이면 발견된 한 명만");
        Place(p2, new Vector3(-12f, 0f, 0f));              // 마당 (다른 건물)
        yield return 0.3f;
        var byEvent = ChaseTargeting.ForChaser(new Vector3(1.5f, 1f, -1.5f));
        Check(byEvent.Count == 1 && byEvent[0] == p1 && p2.CurrentRoom.buildingId == "Outside", "이벤트 추격: 추격자와 같은 건물(집)에 있는 한 명만");

        // ---- 추격 상태: 즉시 컷 ----
        Place(p2, new Vector3(-5f, 0f, 1.8f));
        game.ForceChase(true);
        yield return 0.1f;
        Check(GameStateManager.IsChase, "추격 상태로 전환");
        Place(p1, new Vector3(6f, 0f, -1.2f));             // 활성 캐릭터를 주방으로
        yield return 0.25f;
        Check(!FadeManager.instance.isFading && FadeManager.instance.fadePanel.alpha < 0.01f &&
              CameraTrigger.All.First(r => r.roomName == "주방").virtualCamera.activeSelf, "추격 중 방 이동: 페이드 없이 즉시 컷");
        manager.SwitchInstant(1);
        yield return 0.1f;
        Check(manager.GetActivePlayer() == p2.gameObject && !FadeManager.instance.isFading &&
              CameraTrigger.All.First(r => r.roomName == "거실").virtualCamera.activeSelf, "추격 중 캐릭터 전환: 즉시 컷");
        manager.SwitchInstant(0);
        game.ForceChase(false);
        yield return game.chaseEndDelay + 0.3f;
        Check(GameStateManager.State == GameState.Investigation, "추격이 끝나면 잠시 뒤 추리 상태로");

        // 방을 넘는 페이드 도중에 추격이 시작되면 페이드를 바로 끊음
        Place(p1, new Vector3(1.5f, 0f, 1.5f));            // 주방 → 홀 (추리 상태라 페이드 시작)
        yield return 0.15f;
        bool wasFading = FadeManager.instance.isFading;
        game.ForceChase(true);
        yield return 0.05f;
        Check(wasFading && !FadeManager.instance.isFading && FadeManager.instance.fadePanel.alpha < 0.01f &&
              CameraTrigger.All.First(r => r.roomName == "홀").virtualCamera.activeSelf,
            $"페이드 도중 추격 시작 → 페이드 즉시 중단 + 새 방 카메라 (페이드 중이었음: {wasFading})");
        game.ForceChase(false);
        yield return game.chaseEndDelay + 0.3f;

        // ---- 숨기 ----
        HidingSpot closet = Spot("옷장"), underBed = Spot("침대 밑"), curtain = Spot("커튼");
        Place(chaser, new Vector3(8f, 0f, -6f), 0f);       // 멀리 (창고)
        Place(p1, closet.transform.position);
        yield return 0.3f;
        Check(closet.TryEnter(p1), "옷장에 숨기 시작");
        yield return closet.enterTime + 0.2f;
        Check(p1.IsHidden && p1.State == CharacterState.Hidden && move1.Locks.Has(LockReason.Hiding), "숨음: 상태 '숨음', 이동 잠김");
        Check(!p1.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "숨은 캐릭터는 보이지 않음");
        Check(!closet.WasSeenEnteringBy(chaser.gameObject), "아무도 못 봤으면 목격 기록 없음");
        Place(chaser, closet.transform.position + new Vector3(0f, 0f, -2.5f), 0f);
        yield return 0.4f;
        Check(!senses.CanSee(p1), "숨은 캐릭터는 정면에서도 안 보임");

        Place(p2, closet.transform.position + new Vector3(0.5f, 0f, 0f));
        yield return 0.3f;
        Check(closet.TryEnter(p2), "옷장(2인용)에 두 번째 캐릭터도 숨기 시작");
        yield return closet.enterTime + 0.2f;
        Check(closet.WasSeenEnteringBy(chaser.gameObject), "숨는 장면을 본 추격자는 기억함");
        Check(!underBed.CanEnter(p1), "이미 숨은 캐릭터는 다른 곳에 또 숨을 수 없음");

        closet.Exit(p1);
        yield return closet.exitTime + 0.2f;
        Check(!p1.IsHidden && !move1.Locks.Has(LockReason.Hiding) && p1.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "나오면 다시 보이고 움직일 수 있음");
        Check(underBed.capacity == 1 && underBed.TryEnter(p1) && !underBed.CanEnter(p1), "침대 밑은 1인용");
        yield return underBed.enterTime + 0.2f;
        underBed.Exit(p1);
        yield return underBed.exitTime + 0.2f;

        // 커튼: 가까이 오면 들킴
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 0f);
        Place(p1, curtain.transform.position);
        yield return 0.3f;
        curtain.TryEnter(p1);
        yield return curtain.enterTime + 0.2f;
        Check(p1.IsHidden && !curtain.WasSeenEnteringBy(chaser.gameObject), "커튼 뒤에 숨음(목격 없음)");
        Place(chaser, curtain.transform.position + new Vector3(0.9f, 0f, 0f), 270f);
        yield return 0.2f;
        Check(curtain.WasSeenEnteringBy(chaser.gameObject), "커튼은 추격자가 바로 옆까지 오면 들킴");
        curtain.Exit(p1);
        yield return curtain.exitTime + 0.2f;

        // ---- 경찰 AI ----
        PoliceAI policeAI = chaser;
        policeAI.searchTime = 3f;  // 점검 시간 단축
        policeAI.giveUpTime = 4f;
        float waitedFor;
        void ForgetAll() { senses.Forget(p1); senses.Forget(p2); }
        float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // 순찰
        Place(p1, new Vector3(-8f, 0f, 4f));               // 침실 (닫힌 문 뒤)
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 0f);
        yield return 0.2f;
        ForgetAll();
        policeAI.enabled = true;
        policeAI.ResetToPatrol();
        Vector3 patrolStart = chaser.transform.position;
        yield return 3f;
        Check(policeAI.Current == PoliceAI.Mode.Patrol && Flat(chaser.transform.position, patrolStart) > 1f, "경찰 순찰: 순찰 지점을 따라 걸음");

        // 소리 → 의심 → 확인 → 순찰 복귀
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 180f);
        policeAI.ResetToPatrol();
        yield return 0.1f;
        Vector3 noisePos = new Vector3(-1.5f, 0f, 1.5f);
        NoiseSystem.Emit(noisePos, 8f, NoiseKind.Run, p2.gameObject);
        yield return 0.1f;
        Check(policeAI.Current == PoliceAI.Mode.Suspicious, "소리를 들으면 의심 상태 → 확인하러 감");
        for (waitedFor = 0f; waitedFor < 5f && Flat(chaser.transform.position, noisePos) > 1.2f; waitedFor += 0.2f) yield return 0.2f;
        Check(Flat(chaser.transform.position, noisePos) <= 1.2f, "소리 난 곳에 도착");
        yield return policeAI.lookAroundTime + 1.5f;
        Check(policeAI.Current == PoliceAI.Mode.Patrol && !GameStateManager.IsChase, "둘러본 뒤 순찰로 돌아감 (추격 상태 아님)");

        // 발견 → 추격 → 놓침(몰래 숨음) → 수색 → 포기 → 복귀
        Place(chaser, new Vector3(1.5f, 0f, -2.2f), 0f);
        policeAI.ResetToPatrol();
        ForgetAll();
        Place(p1, new Vector3(1.5f, 0f, 2f));
        for (waitedFor = 0f; waitedFor < 2f && policeAI.Current != PoliceAI.Mode.Chase; waitedFor += 0.1f) yield return 0.1f;
        Check(policeAI.Current == PoliceAI.Mode.Chase && GameStateManager.IsChase && game.IsTargeted(p1), "발견 → 추격 + 게임 추격 상태");
        Place(p1, underBed.transform.position);            // 시야 밖으로 사라져서 몰래 숨음
        underBed.TryEnter(p1);
        for (waitedFor = 0f; waitedFor < 5f && policeAI.Current != PoliceAI.Mode.Search; waitedFor += 0.2f) yield return 0.2f;
        Check(policeAI.Current == PoliceAI.Mode.Search && GameStateManager.IsChase, "놓치면 마지막으로 본 곳 주변 수색 (추격 상태 유지)");
        for (waitedFor = 0f; waitedFor < policeAI.searchTime + 3f && policeAI.Current == PoliceAI.Mode.Search; waitedFor += 0.2f) yield return 0.2f;
        Check(policeAI.Current == PoliceAI.Mode.Return || policeAI.Current == PoliceAI.Mode.Patrol, "수색 시간이 지나면 포기하고 복귀");
        yield return game.chaseEndDelay + 0.3f;
        Check(!GameStateManager.IsChase && p1.IsHidden && !p1.IsCaught, "숨는 장면을 못 봤으면 찾지 못함 → 추리 상태로");
        underBed.Exit(p1);
        yield return underBed.exitTime + 0.2f;

        // 잠긴 문: 노크·경고 → 부수기 → 포기 → 의심 지점 기억
        Inventory inventory1 = p1.GetComponent<Inventory>();
        if (livingDoor.door.IsOpen) { livingDoor.door.Interact(inventory1); yield return 1f; }
        if (!livingDoor.door.IsLocked) livingDoor.door.ToggleLock(inventory1);
        float durabilityBefore = livingDoor.door.Durability;
        Place(p1, new Vector3(-5f, 0f, 1f));                // 잠긴 거실 안
        Place(chaser, new Vector3(-1.5f, 0f, 1.5f), 270f);
        policeAI.ResetToPatrol();
        ForgetAll();
        yield return 0.1f;
        noises.Clear();
        NoiseSystem.Emit(p1.transform.position, 10f, NoiseKind.Run, p1.gameObject);
        for (waitedFor = 0f; waitedFor < 6f && policeAI.Current != PoliceAI.Mode.DoorKnock; waitedFor += 0.1f) yield return 0.1f;
        Check(policeAI.Current == PoliceAI.Mode.DoorKnock && policeAI.DoorTarget == livingDoor, "안에서 소리 → 길이 막힘 → 잠긴 거실 문 앞에서 노크");
        Check(noises.Any(n => n.source == chaser.gameObject && n.kind == NoiseKind.Other), "노크 소리 발생");
        for (waitedFor = 0f; waitedFor < 4f && policeAI.Current != PoliceAI.Mode.DoorBash; waitedFor += 0.1f) yield return 0.1f;
        Check(policeAI.Current == PoliceAI.Mode.DoorBash, "경고 뒤 부수기 시작");
        for (waitedFor = 0f; waitedFor < policeAI.giveUpTime + 3f && policeAI.Current == PoliceAI.Mode.DoorBash; waitedFor += 0.2f) yield return 0.2f;
        Check(policeAI.Current == PoliceAI.Mode.Return || policeAI.Current == PoliceAI.Mode.Patrol, "시간이 지나면 부수기를 포기하고 복귀");
        Check(livingDoor.door.Durability < durabilityBefore && !livingDoor.door.IsBroken, $"부수다 만 문은 피해가 남음 (내구도 {livingDoor.door.Durability:0})");
        Check(policeAI.SuspiciousPoints.Count == 1 && policeAI.SuspiciousPoints[0].door == livingDoor && policeAI.SuspiciousPoints[0].room.roomName == "거실",
            "포기한 방(거실)을 의심 지점으로 기억");

        // 의심 지점 재방문: 잠금이 풀려 있으면 방 안까지 확인
        Place(p1, new Vector3(-8f, 0f, 4f));                // 들키지 않게 침실로
        senses.hearingMultiplier = 0f;                       // 잠금 푸는 소리로 확인하러 가지 않도록 (재방문 자체를 점검)
        livingDoor.door.ToggleLock(inventory1);
        yield return 0.1f;
        senses.hearingMultiplier = 1f;
        policeAI.revisitInterval = 0f;
        for (waitedFor = 0f; waitedFor < 30f && !(policeAI.Current == PoliceAI.Mode.CheckSuspicious && RoomOf(chaser) == "거실"); waitedFor += 0.25f) yield return 0.25f;
        Check(policeAI.Current == PoliceAI.Mode.CheckSuspicious && RoomOf(chaser) == "거실",
            $"순찰 중 의심 지점을 다시 들러, 잠금이 풀려 있으니 방 안까지 확인 ({waitedFor:0.0}초)");
        for (waitedFor = 0f; waitedFor < 10f && policeAI.SuspiciousPoints.Count > 0; waitedFor += 0.25f) yield return 0.25f;
        Check(policeAI.SuspiciousPoints.Count == 0, $"확인이 끝나면 의심 지점 삭제 (상태: {policeAI.StateLabel}, 위치 {RoomOf(chaser)} {chaser.transform.position})");

        // 잠긴 문 바로 앞에 서 있을 때 너머에서 소리 (시연에서 찾은 경우: 끊긴 길의 끝에 이미 서 있음)
        policeAI.enabled = false;                            // 준비하는 동안 경찰이 문을 다시 열지 않도록
        Place(chaser, new Vector3(-2.5f, 0f, -0.2f), 270f);  // 거실 문 홀 쪽 바로 앞
        senses.hearingMultiplier = 0f;
        if (livingDoor.door.IsOpen) { livingDoor.door.Interact(inventory1); yield return 1f; }
        livingDoor.door.ToggleLock(inventory1);
        Place(p1, new Vector3(-5f, 0f, 1f));
        yield return 0.1f;
        senses.hearingMultiplier = 1f;
        Check(livingDoor.door.IsLocked, "준비: 거실 문 잠김");
        policeAI.enabled = true;
        policeAI.ResetToPatrol();
        motor.Stop();
        ForgetAll();
        yield return 0.1f;
        NoiseSystem.Emit(p1.transform.position, 10f, NoiseKind.Run, p1.gameObject);
        for (waitedFor = 0f; waitedFor < 4f && policeAI.Current != PoliceAI.Mode.DoorKnock; waitedFor += 0.1f) yield return 0.1f;
        Check(policeAI.Current == PoliceAI.Mode.DoorKnock, $"잠긴 문 바로 앞에 서 있어도 막힌 걸 알아채고 노크 (상태: {policeAI.StateLabel})");
        policeAI.enabled = false;
        motor.Stop();
        senses.hearingMultiplier = 0f;
        livingDoor.door.ToggleLock(inventory1);
        yield return 0.1f;
        senses.hearingMultiplier = 1f;

        // ---- 경찰 AI: 보는 앞에서 숨으면 발견 → 추격 → 숨는 곳을 뒤져 잡음 (씬 재시작이 걸리므로 마지막) ----
        closet.Exit(p2);
        yield return closet.exitTime + 0.2f;
        Check(!closet.IsOccupied && !closet.WasSeenEnteringBy(chaser.gameObject), "모두 나오면 목격 기록 초기화");
        Place(p1, new Vector3(6f, 0f, 5f));                 // 탐정은 멀리 (서재)
        Place(chaser, closet.transform.position + new Vector3(0f, 0f, -3f), 0f);
        policeAI.ResetToPatrol();
        motor.Stop(); // 옷장 쪽을 보고 서 있도록
        senses.Forget(p1);
        senses.Forget(p2);
        chaser.enabled = true;
        closet.TryEnter(p2);
        yield return 0.6f;
        Check(GameStateManager.IsChase && game.IsTargeted(p2), "보는 앞에서 숨으려던 캐릭터를 발견 → 추격 상태");
        waited = 0f;
        while (waited < 6f && !p2.IsCaught) { waited += 0.25f; yield return 0.25f; }
        Check(p2.IsCaught && p2.State == CharacterState.Caught, $"숨는 곳을 뒤져서 잡음 ({waited:0.0}초)");
        Check(p2.GetComponent<PlayerMovement>().Locks.Has(LockReason.Caught) && game.RestartPending, "잡히면 이동 잠금 + 씬 재시작 예약");

        NoiseSystem.Emitted -= noises.Add;
    }
}
