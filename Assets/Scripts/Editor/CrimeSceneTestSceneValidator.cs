using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using NarrativeGraybox.Interaction;
using Object = UnityEngine.Object;

// 범행 현장 테스트 씬 점검
//  - Validate: 빠진 스크립트, 필수 구성, 조사 지점·단서 수, 처음엔 숨겨진 조사 지점, 방마다 바닥 NavMesh (에디터 상태)
//  - 플레이 점검: 타이머, 여동생 전용 단서(오빠에겐 안 보임), 조사 화면에서 단서 기록, 책장 밀기·녹화기 수리(여동생은 실패, 오빠는 성공)
//    → 가려져 있던 단서 조사, 시간 종료 (키 입력 대신 함수 호출)
public static class CrimeSceneTestSceneValidator
{
    const string Tag = "[OTK 범행 현장 플레이 점검]";

    [MenuItem("OTK/범행 현장 테스트 씬 점검")]
    static void ValidateFromMenu()
    {
        EditorSceneManager.OpenScene(CrimeSceneTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        else
        {
            if (manager.players[0].GetComponent<PlayerInteraction>().myType != CharacterType.Player1) problems.Add("0번이 Player1(여동생)이 아님");
            if (manager.players.Any(p => p.GetComponent<PlayerInteractor3D>() == null)) problems.Add("캐릭터에 PlayerInteractor3D 없음");
        }
        var integration = Object.FindAnyObjectByType<PrototypeIntegration>();
        if (integration == null || integration.hud == null || integration.inspectionCamera == null || integration.occlusionFader == null || integration.cameraBrain == null)
            problems.Add("PrototypeIntegration 연결 안 됨");
        if (Object.FindAnyObjectByType<FadeManager>() == null) problems.Add("FadeManager 없음");
        var director = Object.FindAnyObjectByType<CrimeSceneDirector>();
        if (director == null || director.timer == null) problems.Add("CrimeSceneDirector·타이머 없음");
        if (Object.FindAnyObjectByType<ChaserSenses>() != null) problems.Add("추격자가 있음 (아직 넣지 않기로 함)");

        var areas = Object.FindObjectsByType<InspectionArea>(FindObjectsInactive.Include);
        var clues = Object.FindObjectsByType<Clue>(FindObjectsInactive.Include);
        if (areas.Length != 7) problems.Add($"조사 지점 7개가 아님 ({areas.Length})");
        if (clues.Length != 9) problems.Add($"단서 9개가 아님 ({clues.Length})");
        if (clues.Select(c => c.clueId).Distinct().Count() != clues.Length) problems.Add("단서 ID 중복");
        if (clues.Count(c => c.visibleToTrait == CharacterTrait.Investigation && c.IsVisibleTo(CharacterType.Player1) && !c.IsVisibleTo(CharacterType.Player2)) != 1) problems.Add("정보 수집(여동생) 전용 단서 1개가 아님");
        foreach (InspectionArea area in areas)
            if (area.Items == null || area.Items.Length == 0 || area.Items.Any(i => i == null || i.GetComponent<Clue>() == null))
                problems.Add($"{area.Title}: 조사 항목 없음 또는 단서 표시 없음");
        foreach (string hidden in new[] { "책장 뒤 바닥", "인터폰 녹화 영상" })
        {
            InspectionArea area = areas.FirstOrDefault(a => a.Title == hidden);
            if (area == null || area.gameObject.activeSelf) problems.Add($"{hidden}: 처음에 숨겨져 있지 않음");
        }
        var heavy = Object.FindAnyObjectByType<HeavyObject>();
        if (heavy == null || heavy.requiredTrait != CharacterTrait.Strength || !heavy.CanUse(CharacterType.Player2) || heavy.CanUse(CharacterType.Player1) || heavy.revealWhenMoved.Length == 0) problems.Add("책장(힘 특성 = 오빠 전용 무거운 물건) 설정 안 됨");
        var board = Object.FindAnyObjectByType<DeductionBoard>();
        if (board == null || board.deduction == null || board.timer == null) problems.Add("추리 창(DeductionBoard) 연결 안 됨");
        else
        {
            // 근거 = 단서(Clue) + 여동생 통찰로만 얻는 근거(TraitInsight)
            var insights = Object.FindObjectsByType<TraitInsight>(FindObjectsInactive.Include).Where(t => !string.IsNullOrEmpty(t.evidenceId)).ToArray();
            if (insights.Select(t => t.evidenceId).Distinct().Count() != 2 || insights.Any(t => t.requiredTrait != CharacterTrait.Insight || t.GetComponent<InspectionItem>() == null))
                problems.Add("통찰 근거(아는 사이·걸음걸이) 2종이 조사 항목에 통찰 조건으로 붙어 있지 않음");
            var clueIds = new HashSet<string>(clues.Select(c => c.clueId).Concat(insights.Select(t => t.evidenceId)));
            // 여동생 능력으로만 얻는 근거: 관찰(숨은 단서) + 통찰
            var sisterOnly = new HashSet<string>(clues.Where(c => !c.IsVisibleTo(CharacterType.Player2)).Select(c => c.clueId).Concat(insights.Select(t => t.evidenceId)));
            foreach (string qid in new[] { "relation", "culprit" })
            {
                CaseDeduction.Question q = board.deduction.Find(qid);
                if (q != null && q.options.Where(o => o.correct).Any(o => o.needAnyClue.Any(id => !sisterOnly.Contains(id))))
                    problems.Add($"추리 '{qid}': 정답이 여동생 능력 없이도 열림 (공통 근거만으로 정답 불가여야 함)");
            }
            foreach (CaseDeduction.Question q in board.deduction.questions)
            {
                if (!q.options.Any(o => o.correct)) problems.Add($"추리 '{q.id}': 정답 없음");
                if (q.options.Count(o => o.needAnyClue == null || o.needAnyClue.Length == 0) != 1 || q.options.Any(o => o.correct && (o.needAnyClue == null || o.needAnyClue.Length == 0)))
                    problems.Add($"추리 '{q.id}': 언제나 고를 수 있는 선택지는 '잘 모르겠다'(오답) 하나여야 함");
                foreach (CaseDeduction.Option o in q.options)
                    foreach (string id in o.needAnyClue)
                        if (!clueIds.Contains(id)) problems.Add($"추리 '{q.id}': 씬에 없는 단서 {id}");
            }
        }
        var interventions = Object.FindObjectsByType<Intervention>();
        foreach (InterventionKind kind in Enum.GetValues(typeof(InterventionKind)))
            if (interventions.Count(i => i.kind == kind) != 1) problems.Add($"개입 지점 '{Intervention.KindName(kind)}'이(가) 1개가 아님");
        var planner = Object.FindAnyObjectByType<InterventionPlanner>();
        if (planner == null || planner.board == null) problems.Add("개입 방향 대화(InterventionPlanner) 연결 안 됨");
        var machine = Object.FindAnyObjectByType<RepairableMachine>();
        if (machine == null || machine.requiredTrait != CharacterTrait.Mechanics || !machine.CanUse(CharacterType.Player2) || machine.CanUse(CharacterType.Player1) || machine.revealWhenRepaired.Length == 0) problems.Add("녹화기(기계 조작 특성 = 오빠 전용 기계) 설정 안 됨");

        foreach (CameraTrigger room in Object.FindObjectsByType<CameraTrigger>())
        {
            if (room.buildingId != "House") continue;
            Bounds zone = room.GetComponent<Collider>().bounds;
            if (!NavMesh.SamplePosition(new Vector3(zone.center.x, 0f, zone.center.z - 1f), out _, 1.5f, NavMesh.AllAreas))
                problems.Add($"{room.roomName}: 바닥에 NavMesh 없음");
        }

        string result = problems.Count == 0 ? "문제 없음" : string.Join("\n", problems);
        if (problems.Count == 0) Debug.Log("[OTK 범행 현장 점검] 씬 점검 통과");
        else Debug.LogError("[OTK 범행 현장 점검] 씬 점검 실패\n" + result);
        if (showDialog) EditorUtility.DisplayDialog("범행 현장 테스트 씬 점검", result, "확인");
        return problems.Count == 0;
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod CrimeSceneTestSceneValidator.RunPlaySmokeTest (0=통과, 3=실패)
    public static void RunPlaySmokeTest()
    {
        EditorSceneManager.OpenScene(CrimeSceneTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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

    static InspectionArea Area(string title) =>
        Object.FindObjectsByType<InspectionArea>(FindObjectsInactive.Include).First(a => a.Title == title);

    static InspectionItem ItemOf(InspectionArea area, string clueId) =>
        area.Items.First(i => i.GetComponent<Clue>().clueId == clueId);

    static void Place(GameObject who, Vector3 floor) => who.GetComponent<NavMeshAgent>().Warp(floor);

    static IEnumerator<float> Steps()
    {
        CharacterManager manager = CharacterManager.instance;
        GameObject sister = manager.players[0], brother = manager.players[1];
        PlayerInteraction sisterHands = sister.GetComponent<PlayerInteraction>(), brotherHands = brother.GetComponent<PlayerInteraction>();
        var director = Object.FindAnyObjectByType<CrimeSceneDirector>();
        CrimeSceneTimer timer = director.timer;

        // 조사 화면을 열고 항목을 살펴본 뒤 닫음 (페이드가 끝날 때까지 기다림)
        IEnumerable<float> Inspect(GameObject who, InspectionArea area, params string[] clueIds)
        {
            var inspector = who.GetComponent<PlayerInteractor3D>();
            inspector.Enter(area);
            yield return 0.8f;
            foreach (string id in clueIds)
            {
                inspector.Select(ItemOf(area, id));
                yield return 0.2f;
            }
            inspector.Back();
            if (inspector.IsInspecting) inspector.Back();
            yield return 0.3f;
            for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.1f;
            yield return 0.2f;
        }

        // ---- 타이머 ----
        Check(manager.GetActivePlayer() == sister, "시작 캐릭터는 여동생 (0번)");
        float before = timer.Remaining;
        yield return 1f;
        Check(!timer.Expired && before - timer.Remaining > 0.5f, $"제한 시간이 흐름 ({before:0.0} → {timer.Remaining:0.0}초)");

        // ---- 여동생 전용 단서: 오빠에게는 안 보임 ----
        InspectionArea body = Area("시체");
        InspectionItem fiber = ItemOf(body, "navy_fiber");
        Check(fiber.GetComponent<Renderer>().enabled && fiber.GetComponent<Collider>().enabled, "여동생일 때 손톱 밑 섬유가 보임");
        manager.SwitchInstant(1);
        yield return 0.2f;
        Check(!fiber.GetComponent<Renderer>().enabled && !fiber.GetComponent<Collider>().enabled, "오빠일 때 손톱 밑 섬유가 안 보이고 클릭되지 않음");
        manager.SwitchInstant(0);
        yield return 0.2f;

        // ---- 벽 너머 조사 지점은 잡히지 않음 (현관에서 벽 건너 욕조) ----
        var hud = Object.FindAnyObjectByType<NarrativeGraybox.UI.SimpleGameHud>();
        var promptField = typeof(NarrativeGraybox.UI.SimpleGameHud).GetField("interactionMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        string Prompt() => (string)promptField.GetValue(hud);
        Place(sister, new Vector3(3.6f, 0f, 3.8f));
        yield return 0.4f;
        Check(Vector3.Distance(sister.transform.position, Area("욕조").transform.position) < 2.4f && !Prompt().Contains("조사"),
            $"현관에서 벽 건너 욕조는 조사 대상이 아님 (안내: {Prompt()})");
        Place(sister, new Vector3(1.4f, 0f, 3.4f));
        yield return 0.4f;
        Check(Prompt().Contains("조사"), $"욕실 안에서는 욕조 조사 가능 (안내: {Prompt()})");

        // ---- 조사 화면에서 살펴본 단서가 사건 파일에 기록 ----
        Place(sister, new Vector3(-3.6f, 0f, -2.2f));
        yield return 0.3f;
        foreach (float s in Inspect(sister, body, "body", "navy_fiber")) yield return s;
        Check(CaseFile.Has("body") && CaseFile.Has("navy_fiber") && CaseFile.Entries.All(e => e.foundBy == CharacterType.Player1),
            $"시체·섬유 단서 기록, 찾은 사람 = 여동생 ({CaseFile.Entries.Count}개)");
        Check(!CaseFile.Has("head_wound"), "살펴보지 않은 항목은 기록 안 됨");
        Check(!sister.GetComponent<PlayerInteractor3D>().IsInspecting && !sister.GetComponent<PlayerMovement>().Locks.IsLocked, "조사를 닫으면 다시 움직일 수 있음");

        // ---- 책장: 여동생은 못 밀고, 오빠는 밀어서 치움 → 수첩 ----
        var shelf = Object.FindAnyObjectByType<HeavyObject>();
        Vector3 shelfStart = shelf.transform.position;
        Vector3 diarySpot = new Vector3(-7.45f, 0f, 3.2f);
        Check(!NavMesh.SamplePosition(diarySpot, out _, 0.1f, NavMesh.AllAreas), "책장 자리는 길이 막혀 있음 (NavMeshObstacle)");
        Place(sister, new Vector3(-6.4f, 0f, 3.2f));
        yield return 0.3f;
        Check(sisterHands.HasTarget, "책장 앞에 서면 상호작용 대상으로 잡힘");
        shelf.Use(sisterHands);
        yield return 0.2f;
        Check(!shelf.Busy && !shelf.Moved, "여동생은 책장을 밀지 못함");

        manager.SwitchInstant(1);
        Place(brother, new Vector3(-6.4f, 0f, 3.2f));
        yield return 0.3f;
        shelf.Use(brotherHands);
        yield return 0.2f;
        Check(shelf.Busy && brother.GetComponent<PlayerMovement>().Locks.Has(LockReason.Working) && manager.SwitchLocks.Has(LockReason.Working),
            "오빠가 미는 동안 이동·캐릭터 전환 잠김");
        yield return shelf.pushTime + 0.3f;
        InspectionArea behindShelf = Area("책장 뒤 바닥");
        Check(shelf.Moved && Vector3.Distance(shelf.transform.position, shelfStart + shelf.pushOffset) < 0.01f && behindShelf.gameObject.activeSelf,
            "오빠가 책장을 밀어 치움 → 책장 뒤 조사 지점이 나타남");
        Check(!brother.GetComponent<PlayerMovement>().Locks.IsLocked && !manager.SwitchLocks.IsLocked, "다 밀면 잠금 풀림");
        yield return 0.3f;
        Check(NavMesh.SamplePosition(diarySpot, out _, 0.3f, NavMesh.AllAreas), "책장을 치운 자리로 걸어갈 수 있음");
        foreach (float s in Inspect(brother, behindShelf, "victim_diary")) yield return s;
        Check(CaseFile.Has("victim_diary") && CaseFile.Entries.Last().foundBy == CharacterType.Player2, "피해자 수첩 기록, 찾은 사람 = 오빠");

        // ---- 녹화기: 여동생은 못 고치고, 오빠는 고침 → 녹화 영상 ----
        var recorder = Object.FindAnyObjectByType<RepairableMachine>();
        manager.SwitchInstant(0);
        Place(sister, new Vector3(6.9f, 0f, 3.5f));
        yield return 0.3f;
        Check(sisterHands.HasTarget, "녹화기 앞에 서면 상호작용 대상으로 잡힘");
        recorder.Use(sisterHands);
        yield return 0.2f;
        Check(!recorder.Busy && !recorder.Repaired, "여동생은 녹화기를 고치지 못함");

        manager.SwitchInstant(1);
        Place(brother, new Vector3(6.9f, 0f, 3.2f));
        yield return 0.3f;
        recorder.Use(brotherHands);
        yield return recorder.repairTime + 0.3f;
        InspectionArea video = Area("인터폰 녹화 영상");
        Check(recorder.Repaired && video.gameObject.activeSelf && recorder.indicator.sharedMaterial == recorder.repairedMaterial,
            "오빠가 녹화기를 고침 → 표시등 초록, 녹화 영상 조사 지점이 나타남");
        Check(!brotherHands.HasTarget || !recorder.Available, "고친 뒤엔 다시 고치기 안내가 없음");
        InspectionItem videoItem = ItemOf(video, "intercom_video");
        foreach (float s in Inspect(brother, video, "intercom_video")) yield return s;
        Check(CaseFile.Has("intercom_video") && CaseFile.Entries.Count == 4, $"녹화 영상 기록 (단서 {CaseFile.Entries.Count}개)");
        Check(!CaseFile.Has(CrimeSceneTestSceneBuilder.GaitEvidence) && !videoItem.Description.Contains("[통찰]"),
            "오빠가 영상을 보면 사실만 보이고 통찰 근거(걸음걸이)는 없음");

        // ---- 여동생 통찰: 오빠가 고친 녹화기의 영상을 여동생이 보면 추가 근거 ----
        manager.SwitchInstant(0);
        Place(sister, new Vector3(6.9f, 0f, 3.5f));
        yield return 0.3f;
        Check(videoItem.Description.Contains("[통찰]"), "여동생이 조작하면 영상 설명에 [통찰] 문장이 붙음");
        foreach (float s in Inspect(sister, video, "intercom_video")) yield return s;
        Check(CaseFile.Has(CrimeSceneTestSceneBuilder.GaitEvidence) && CaseFile.Entries.Last().foundBy == CharacterType.Player1 && CaseFile.Entries.Count == 5,
            "여동생이 영상을 보면 통찰 근거 '아빠의 걸음걸이' 기록 (오빠가 고치고 → 여동생이 봄)");
        Check(!CaseFile.Has(CrimeSceneTestSceneBuilder.AcquaintanceEvidence), "찻잔·상처를 여동생이 안 봤으면 '아는 사이' 근거 없음");
        manager.SwitchInstant(1);
        yield return 0.2f;

        // ---- 추리 (지금 근거: 시체·섬유·수첩·녹화 영상·걸음걸이, 찻잔·상처·아는 사이는 없음 / 조작 중 = 오빠) ----
        var board = Object.FindAnyObjectByType<DeductionBoard>();
        CaseDeduction data = board.deduction;
        board.Open();
        yield return 0.2f;
        Check(board.IsOpen && brother.GetComponent<PlayerMovement>().Locks.Has(LockReason.Deduction) && manager.SwitchLocks.Has(LockReason.Deduction)
              && !brother.GetComponent<PlayerInteractor3D>().enabled, "추리 창이 열리면 이동·전환·조사 잠김");
        Check(board.CurrentQuestion.id == "relation" && !board.Choose(1) && !CaseState.HasAnswered(data.caseId, "relation"),
            "여동생 통찰 근거(아는 사이)가 없으면 '아는 사이' 선택지를 고를 수 없음");
        Check(!board.Choose(0) && !board.Choose(2) && !CaseState.HasAnswered(data.caseId, "relation"),
            "오답 선택지도 근거(핏자국·재떨이)가 없으면 잠김 → 잠김 표시가 정답 힌트가 아님");
        Check(board.VisibleOptions(data.Find("relation"))[3].text == CaseDeduction.UnknownOptionText
              && board.Choose(3) && CaseState.TryGet(data.caseId, "relation", out CaseState.Answer wrong) && !wrong.correct && CaseState.NextCaseErrors.Count == 1,
            "단서가 부족하면 '잘 모르겠다'만 고를 수 있음 → 오답(실패)으로 진행, 다음 사건 큰 오류로 기록");
        CaseDeduction.Question relation = data.Find("relation");
        Check(!CaseState.Record(data.caseId, relation, relation.options[2], CharacterType.Player2) && board.CurrentQuestion.id == "culprit",
            "한 번 답한 질문은 바꿀 수 없음 → 다음 질문");
        CaseDeduction.Question culprit = data.Find("culprit");
        List<CaseDeduction.Option> options = board.VisibleOptions(culprit);
        Check(options.Count == 5 && options[1].needAnyClue.Contains("navy_fiber") && options[1].Unlocked && options[2].Unlocked
              && !options[0].Unlocked && !options[3].Unlocked && options[4].Unlocked,
            "선택지는 누가 추리하든 같음: 섬유·녹화 영상 근거 선택지 둘 다 열림 (근거 없는 오답 2개는 잠김, '잘 모르겠다'는 열림)");
        Check(board.Choose(1) && CaseState.TryGet(data.caseId, "culprit", out CaseState.Answer culpritAnswer) && culpritAnswer.correct && culpritAnswer.answeredBy == CharacterType.Player2,
            "여동생이 찾은 섬유를 근거로 오빠가 '아빠' 정답 (답한 사람 = 오빠로 기록)");
        Check(board.Choose(0) && board.CurrentQuestion == null && CaseState.Answers.Count == 3, "수첩 근거로 다음 범행 정답 → 추리 완료");
        string screen = board.ScreenText();
        Check(screen.Contains("추리 완료") && screen.Contains(CaseDeduction.UnknownOptionText) && !screen.Contains("정답") && !screen.Contains("오답"),
            "추리 창에는 고른 답만 보이고 정답·오답은 보이지 않음");
        board.Close();
        yield return 0.2f;
        Check(!board.IsOpen && !brother.GetComponent<PlayerMovement>().Locks.IsLocked && !manager.SwitchLocks.IsLocked && brother.GetComponent<PlayerInteractor3D>().enabled,
            "추리 창을 닫으면 잠금 풀림");

        // ---- 개입: 남매 대화로 방향을 정하고, 그 종류만 한 번 ----
        var planner = Object.FindAnyObjectByType<InterventionPlanner>();
        Intervention[] spots = Object.FindObjectsByType<Intervention>();
        Intervention Spot(InterventionKind kind) => spots.First(i => i.kind == kind);
        Check(!planner.IsOpen && spots.All(i => !i.Available), "방향을 정하기 전에는 어떤 개입도 할 수 없음");
        yield return planner.openDelay + 0.4f;
        Check(planner.IsOpen && brother.GetComponent<PlayerMovement>().Locks.Has(LockReason.Deduction), "추리를 마치고 창을 닫으면 잠시 뒤 남매 대화(방향 정하기)가 열림");
        Check(planner.Choose(InterventionKind.Abet) && !planner.IsOpen && CaseState.TryGetDirection(data.caseId, out InterventionKind chosen) && chosen == InterventionKind.Abet,
            "여동생이 '가담'을 고름 → 방향 확정");
        Check(!planner.Choose(InterventionKind.Prevent) && !brother.GetComponent<PlayerMovement>().Locks.IsLocked, "방향은 바꿀 수 없음, 대화가 닫히면 잠금 풀림");
        Check(Spot(InterventionKind.Abet).Available && !Spot(InterventionKind.Incite).Available && !Spot(InterventionKind.Prevent).Available,
            "정한 방향(가담)의 개입 지점만 쓸 수 있음");
        Intervention hideBody = Spot(InterventionKind.Abet);
        Check(hideBody.WorkTimeFor(CharacterType.Player1) > hideBody.WorkTimeFor(CharacterType.Player2)
              && Spot(InterventionKind.Prevent).WorkTimeFor(CharacterType.Player1) < Spot(InterventionKind.Prevent).WorkTimeFor(CharacterType.Player2),
            $"능력에 따라 시간이 다름: 시체 치우기 여동생 {hideBody.WorkTimeFor(CharacterType.Player1)}초 / 오빠 {hideBody.WorkTimeFor(CharacterType.Player2)}초, 경고 편지는 여동생이 빠름");

        manager.SwitchInstant(0);
        Place(sister, new Vector3(-3.6f, 0f, -2.3f));
        yield return 0.3f;
        Check(sisterHands.HasTarget, "시체 앞에 서면 개입 대상으로 잡힘");
        float workStarted = Time.time;
        hideBody.Use(sisterHands);
        yield return 2f;
        Check(hideBody.Busy && hideBody.Progress > 0.1f && hideBody.Progress < 0.5f && sister.GetComponent<PlayerMovement>().Locks.Has(LockReason.Working),
            $"여동생이 시체를 옮기는 중 (진행 {hideBody.Progress:P0}, 이동 잠김)");
        for (float waited = 0f; waited < 10f && !hideBody.Done; waited += 0.25f) yield return 0.25f;
        float took = Time.time - workStarted;
        Check(hideBody.Done && took >= hideBody.WorkTimeFor(CharacterType.Player1) - 0.5f, $"여동생은 오래 걸림 ({took:0.0}초)");
        Check(CaseState.HasIntervened(data.caseId) && CaseState.Interventions[0].kind == InterventionKind.Abet && CaseState.Interventions[0].doneBy == CharacterType.Player1,
            "개입 기록 (가담, 여동생)");
        Check(GuiltSystem.Value == 58 && !Area("시체").gameObject.activeInHierarchy, $"가담 → 죄책감 +8 ({GuiltSystem.Value}), 시체가 치워짐");
        Check(spots.All(i => !i.Available), "사건마다 개입은 한 번");

        // ---- 시간 종료 → 추리 창 자동으로 열림 ----
        bool timeUp = false;
        timer.TimeUp += () => timeUp = true;
        timer.SetRemaining(0.3f);
        yield return 0.6f;
        Check(timer.Expired && timeUp && timer.Remaining == 0f, "제한 시간이 끝나면 TimeUp 이벤트 (추격자는 아직 없음)");
        Check(board.IsOpen, "시간이 끝나면 추리 창이 자동으로 열림");
        board.Close();
        yield return planner.openDelay + 0.4f;
        Check(!planner.IsOpen, "방향을 이미 정했으면 대화가 다시 열리지 않음");
        Check(GuiltSystem.History.Count == 1 && GuiltSystem.History[0].actionId == "abet_crime", "죄책감은 개입으로만 바뀜 (조사·추리로는 안 바뀜)");
    }
}
