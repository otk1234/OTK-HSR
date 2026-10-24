using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Cinemachine;
using NarrativeGraybox.Interaction;
using Object = UnityEngine.Object;

// 아지트 씬 점검 (에디터 상태)
//  - 빠진 스크립트, 캐릭터 2명·연결, 추격자 없음, 다락방 구역 하나 = 아이소메트릭 카메라
//  - 칠판·상자·작업대·해치가 있고, 시작 위치에서 걸어서 닿음 (NavMesh)
public static class HideoutSceneValidator
{
    [MenuItem("OTK/아지트 씬 점검")]
    static void ValidateFromMenu()
    {
        EditorSceneManager.OpenScene(HideoutSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        if (Object.FindAnyObjectByType<GameStateManager>() == null) problems.Add("GameStateManager 없음");
        if (Object.FindAnyObjectByType<ChaserSenses>() != null) problems.Add("추격자가 있음 (아지트에는 없음)");

        // 다락방 구역 하나 = 아이소메트릭 카메라 하나 (탑뷰가 아님: 카메라가 대각선 방향에서 내려다봄)
        CameraTrigger[] rooms = Object.FindObjectsByType<CameraTrigger>().Where(r => r.buildingId == "Hideout").ToArray();
        if (rooms.Length != 1) problems.Add($"다락방 구역이 하나가 아님 ({rooms.Length})");
        else
        {
            var cam = rooms[0].virtualCamera != null ? rooms[0].virtualCamera.GetComponent<CinemachineCamera>() : null;
            Vector3 forward = cam != null ? cam.transform.forward : Vector3.zero;
            if (cam == null || Mathf.Abs(forward.x) < 0.3f || Mathf.Abs(forward.z) < 0.3f || forward.y > -0.3f)
                problems.Add("다락방 카메라가 아이소메트릭(대각선)이 아님");
        }

        Vector3 start = manager != null && manager.players != null && manager.players.Length > 0 ? manager.players[0].transform.position : Vector3.zero;
        foreach (string name in new[] { HideoutSceneBuilder.BoardName, HideoutSceneBuilder.StashName, HideoutSceneBuilder.DeskName, HideoutSceneBuilder.HatchName, HideoutSceneBuilder.TableName })
        {
            GameObject spot = GameObject.Find(name);
            if (spot == null) { problems.Add($"{name} 없음"); continue; }
            if (!Reachable(start, spot.transform.position)) problems.Add($"{name}: 시작 위치에서 걸어서 닿지 않음");
        }

        // 칠판·상자·작업대 = 조사 지점(확대). 팀원 조사는 2.4m 안의 가장 가까운 지점을 고르므로 서로 충분히 떨어져 있어야 함
        var stations = Object.FindObjectsByType<HideoutStation>(FindObjectsInactive.Include);
        if (stations.Length != 4 || Object.FindAnyObjectByType<Chalkboard>() == null || Object.FindAnyObjectByType<HideoutStash>() == null || Object.FindAnyObjectByType<StatusDesk>() == null || Object.FindAnyObjectByType<MissionTable>() == null)
            problems.Add($"칠판·상자·작업대·지도 테이블 기능이 하나씩 붙어 있지 않음 ({stations.Length})");
        if (stations.Any(s => s.GetComponent<InspectionArea>() == null || s.GetComponent<InspectionArea>().Items == null))
            problems.Add("조사 지점(InspectionArea)이 아닌 기능이 있음");
        if (Object.FindObjectsByType<InspectionArea>(FindObjectsInactive.Include).Length != 4) problems.Add("아지트의 조사 지점은 칠판·상자·작업대·지도 테이블 4개여야 함");

        // 지도 테이블: 장소 데이터(우리 집 1 · 임무 2 · 지난 사건 1), 빨간 핀 임무마다 임시 장소(도착 자리 2, 자기 구역)
        var table = Object.FindAnyObjectByType<MissionTable>();
        if (table != null)
        {
            if (table.places.Length != 4 || table.places.Any(p => p == null) || table.places.Count(p => p.kind == MissionDefinition.Kind.Home) != 1
                || table.places.Count(p => p.kind == MissionDefinition.Kind.Mission) != 2 || table.places.Select(p => p.id).Distinct().Count() != 4)
                problems.Add("지도 장소 데이터가 우리 집 1·임무 2·지난 사건 1이 아님 (또는 ID 중복)");
            if (table.mapSurface == null || table.missionPinMaterial == null || table.homePinMaterial == null || table.pastPinMaterial == null) problems.Add("지도 면·핀 재질이 연결 안 됨");
            foreach (MissionDefinition mission in table.places.Where(p => p != null && p.kind == MissionDefinition.Kind.Mission))
            {
                MissionSite site = table.SiteFor(mission.id);
                if (site == null || site.spawnPoints.Length < 2 || site.spawnPoints.Any(s => s == null)) { problems.Add($"{mission.displayName}: 임시 장소·도착 자리 없음"); continue; }
                CameraTrigger zone = Object.FindObjectsByType<CameraTrigger>().FirstOrDefault(t => t.ContainsPoint(site.transform.position));   // FindAt은 플레이 중에만 목록이 참
                if (zone == null || zone.buildingId != mission.id) problems.Add($"{mission.displayName}: 장소 구역(카메라)이 없음");
                if (site.spawnPoints.Any(s => !NavMesh.SamplePosition(s.position, out _, 1f, NavMesh.AllAreas))) problems.Add($"{mission.displayName}: 도착 자리에 NavMesh 없음");
            }
            var tableDirector = Object.FindAnyObjectByType<HideoutDirector>();
            if (tableDirector != null && tableDirector.missionTable != table) problems.Add("HideoutDirector에 지도 테이블(F6 귀환)이 연결 안 됨");
        }
        for (int i = 0; i < stations.Length; i++)
            for (int j = i + 1; j < stations.Length; j++)
                if (Vector3.Distance(Flat(stations[i].transform.position), Flat(stations[j].transform.position)) < 2.5f)
                    problems.Add($"{stations[i].name}·{stations[j].name} 조사 지점이 너무 가까움");
        var chalkboard = Object.FindAnyObjectByType<Chalkboard>();
        if (chalkboard != null && (chalkboard.knownCases.Length == 0 || chalkboard.knownCases.Any(c => c == null))) problems.Add("칠판에 사건 데이터(302호)가 연결 안 됨");
        if (chalkboard != null && (chalkboard.photoSurface == null || chalkboard.photoMaterial == null || chalkboard.outlineMaterial == null)) problems.Add("칠판 사진 면·재질이 연결 안 됨");
        var director = Object.FindAnyObjectByType<HideoutDirector>();
        if (director == null || director.sampleCase == null) problems.Add("HideoutDirector·예시 사건 연결 안 됨");

        string result = problems.Count == 0 ? "문제 없음" : string.Join("\n", problems);
        if (problems.Count == 0) Debug.Log("[OTK 아지트 점검] 씬 점검 통과");
        else Debug.LogError("[OTK 아지트 점검] 씬 점검 실패\n" + result);
        if (showDialog) EditorUtility.DisplayDialog("아지트 씬 점검", result, "확인");
        return problems.Count == 0;
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    const string Tag = "[OTK 아지트 플레이 점검]";
    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod HideoutSceneValidator.RunPlaySmokeTest (0=통과, 3=실패)
    public static void RunPlaySmokeTest()
    {
        EditorSceneManager.OpenScene(HideoutSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        catch (System.Exception e) { failures.Add("점검 중 예외: " + e); more = false; }

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

    static IEnumerator<float> Steps()
    {
        CharacterManager manager = CharacterManager.instance;
        GameObject sister = manager.players[0], brother = manager.players[1];
        Inventory sisterBag = sister.GetComponent<Inventory>(), brotherBag = brother.GetComponent<Inventory>();
        PlayerInteractor3D sisterEyes = sister.GetComponent<PlayerInteractor3D>();
        var board = Object.FindAnyObjectByType<Chalkboard>();
        var stash = Object.FindAnyObjectByType<HideoutStash>();
        var desk = Object.FindAnyObjectByType<StatusDesk>();
        var director = Object.FindAnyObjectByType<HideoutDirector>();
        var integration = Object.FindAnyObjectByType<PrototypeIntegration>();

        // ---- 처음: 비어 있음, 추리 상태 고정 ----
        Check(GameStateManager.State == GameState.Investigation, "아지트는 추리 상태 (추격 없음)");
        Check(GameSession.Stash.Count == 0 && board.Summaries().Count == 0 && board.Photos.Length == 0, "처음엔 상자·칠판이 비어 있음 (사진 없음)");
        Check(CharacterManager.instance.GetActivePlayer() == sister, "여동생으로 시작");

        // ---- 확대: 시체 조사처럼 E → 확대 카메라, 이동·전환 잠김, Q로 돌아옴 ----
        sisterEyes.Enter(board.Area);
        yield return 1.0f;
        Check(board.IsOpen && !stash.IsOpen && board.UserType == CharacterType.Player1, "칠판 조사 → 칠판 창이 열림 (다른 기능은 안 열림)");
        Check(integration.inspectionCamera.enabled && integration.inspectionCamera.IsFocusing && !integration.cameraBrain.enabled, "칠판을 확대해서 봄 (조사 카메라)");
        Check(sister.GetComponent<PlayerMovement>().Locks.IsLocked && manager.SwitchLocks.IsLocked, "확대된 동안 이동·캐릭터 전환 잠김");
        Check(Chalkboard.Describe(board.Summaries(), 0).Contains("사건 기록 없음"), "기록이 없으면 칠판에 '사건 기록 없음'");
        board.Close();
        yield return 1.5f;
        Check(!board.IsOpen && !integration.inspectionCamera.enabled && integration.cameraBrain.enabled, "닫으면 구역 카메라로 돌아옴");
        Check(!sister.GetComponent<PlayerMovement>().Locks.IsLocked && !manager.SwitchLocks.IsLocked, "닫으면 잠금이 풀림");

        // ---- 예시 기록 (F5와 같은 함수) → 칠판 데이터·사진 ----
        Check(HideoutSampleData.Fill(director.sampleCase) && !HideoutSampleData.Fill(director.sampleCase), "예시 기록은 한 번만 채워짐");
        yield return 0.2f;
        List<CaseBoard.CaseSummary> cases = board.Summaries();
        CaseBoard.CaseSummary c302 = cases.FirstOrDefault(c => c.caseId == "case302");
        Check(cases.Count == 1 && c302 != null && c302.title == director.sampleCase.caseTitle, $"칠판: 사건 1개, 제목 = 사건 데이터 ({cases.Count})");
        Check(c302 != null && c302.clues.Count == 5 && c302.clues.Count(l => l.foundBy == CharacterType.Player1) == 2, "칠판: 302호 단서 5개와 찾은 사람");
        Check(c302 != null && c302.answers.Count == director.sampleCase.questions.Length && c302.answers.All(a => a.question != "relation" && a.answer.Length > 0),
            "칠판: 고른 답이 질문 문장과 함께");
        Check(c302 != null && c302.hasDirection && c302.direction == InterventionKind.Incite && c302.intervention != null && c302.nextTarget == "옆집 사람",
            "칠판: 개입(종용)과 아버지의 다음 표적 '옆집 사람'");
        string boardText = Chalkboard.Describe(cases, 0);
        Check(!boardText.Contains("정답") && !boardText.Contains("오답") && !boardText.Contains("오류"), "칠판에 정답·오답·다음 사건 오류는 안 보임");
        Check(CaseFile.EntriesOf("").Count == 0 && CaseFile.EntriesOf("case302").Count == 5, "단서는 사건별로 기록됨 (사건 없음 칸은 비어 있음)");

        InspectionItem[] photos = board.Photos;
        Check(photos.Length == 7, $"칠판 사진 = 피해자 1 + 단서 5 + 다음 표적 1 ({photos.Length})");
        Check(photos.All(p => p != null && p.GetComponent<Collider>() != null && !string.IsNullOrEmpty(p.Description)), "사진마다 클릭 판정과 기억 글이 있음");
        InspectionItem victim = photos.FirstOrDefault(p => p.Title.StartsWith("피해자 사진"));
        Check(victim != null && victim.Description.Contains("종용") && !victim.Description.Contains("정답"), $"피해자 사진: 사건 요약 기억 ({victim?.Description})");
        InspectionItem fiber = photos.FirstOrDefault(p => p.Title == "손톱 밑 섬유");
        Check(fiber != null && fiber.Description.Contains("남색 털실") && fiber.Description.Contains(CharacterRoster.NameOf(CharacterType.Player1)),
            "단서 사진: 찾을 때 본 설명 + 찾은 사람");
        Check(photos.Any(p => p.Title == "다음 표적: 옆집 사람"), "다음 표적 메모가 붙음");
        string authored = director.sampleCase.boardMemory;
        director.sampleCase.boardMemory = "점검용 기억 문장";
        Check(board.Summaries()[0].memory == "점검용 기억 문장", "기획이 쓴 기억 문장이 있으면 그것을 씀");
        director.sampleCase.boardMemory = authored;

        // 칠판을 확대한 채 사진 클릭 → 팀원 조사 창에 기억 글 (마우스 대신 Select)
        sisterEyes.Enter(board.Area);
        yield return 1.0f;
        sisterEyes.Select(fiber);
        Check(sisterEyes.Selected == fiber && board.IsOpen, "확대한 칠판에서 사진을 고르면 그 사진의 기억 글이 보임");
        board.Close();
        yield return 1.5f;
        Check(!board.IsOpen && sisterEyes.Selected == null, "사진을 고른 채로 닫아도 한 번에 돌아옴");

        // ---- 상자 ----
        Check(GameSession.Stash.Count == 4, $"예시 기록: 상자에 물건 4개 ({GameSession.Stash.Count})");
        int toolbox = GameSession.Stash.ToList().FindIndex(s => s.name == "공구 상자");
        Check(HideoutStash.Withdraw(sisterBag, toolbox) == HideoutStash.Result.CannotHandle && GameSession.Stash.Count == 4 && !sisterBag.HasItem("공구 상자"),
            "여동생은 공구 상자(힘)를 꺼낼 수 없음");
        Check(HideoutStash.Withdraw(brotherBag, toolbox) == HideoutStash.Result.Ok && brotherBag.HasItem("공구 상자") && brotherBag.RuleOf("공구 상자").requiredTrait == CharacterTrait.Strength && GameSession.Stash.Count == 3,
            "오빠는 공구 상자를 꺼냄 (힘 조건 유지)");
        Check(HideoutStash.Deposit(brotherBag, brotherBag.items.IndexOf("공구 상자")) == HideoutStash.Result.Ok && !brotherBag.HasItem("공구 상자")
              && GameSession.Stash.Any(s => s.name == "공구 상자" && s.rule.requiredTrait == CharacterTrait.Strength),
            "다시 넣으면 상자로 (조건도 같이)");
        int memo = GameSession.Stash.ToList().FindIndex(s => s.name == "암호 메모");
        Check(HideoutStash.Withdraw(sisterBag, memo) == HideoutStash.Result.Ok && sisterBag.HasItem("암호 메모"), "여동생은 암호 메모(관찰)를 꺼냄");
        while (!sisterBag.IsFull) sisterBag.AddItem("점검용 돌멩이");
        int before = GameSession.Stash.Count;
        Check(HideoutStash.Withdraw(sisterBag, 0) == HideoutStash.Result.Full && GameSession.Stash.Count == before, $"소지품이 가득 차면 꺼낼 수 없음 ({sisterBag.items.Count} / {sisterBag.Capacity})");
        Check(HideoutStash.MessageFor(HideoutStash.Result.Full, "황동 열쇠", sisterBag).Contains(Inventory.FullMessage), "가득 참 문구는 공통 문구");
        for (int i = 0; i < 3; i++) HideoutStash.Deposit(sisterBag, sisterBag.items.IndexOf("점검용 돌멩이"));
        Check(GameSession.Stash.Count == before + 3, "상자는 칸 제한 없음 (계속 넣을 수 있음)");

        sisterEyes.Enter(stash.Area);
        yield return 1.0f;
        Check(stash.IsOpen && integration.inspectionCamera.IsFocusing, "상자도 확대해서 엶");
        stash.Close();
        yield return 1.5f;

        // ---- 작업대 ----
        string status = StatusDesk.Describe();
        Check(status.Contains(CharacterRoster.NameOf(CharacterType.Player1)) && status.Contains(CharacterRoster.NameOf(CharacterType.Player2))
              && status.Contains($"/ {sisterBag.Capacity}") && status.Contains($"/ {brotherBag.Capacity}") && status.Contains("암호 메모"),
            "작업대: 남매 이름·칸 수·소지품");
        Check(status.Contains(GuiltSystem.TierName) && GuiltSystem.Value == 55, $"작업대: 죄책감 단계 (예시 종용 +5 → {GuiltSystem.Value})");

        // ---- 오빠로 바꿔도 같은 기능 (조작 캐릭터 기준) ----
        manager.SwitchInstant(1);
        yield return 1.2f;
        Check(manager.GetActivePlayer() == brother, "오빠로 전환");
        brother.GetComponent<PlayerInteractor3D>().Enter(desk.Area);
        yield return 1.0f;
        Check(desk.IsOpen && desk.UserType == CharacterType.Player2 && integration.inspectionCamera.IsFocusing, "오빠도 작업대를 확대해서 봄");
        desk.Close();
        yield return 1.5f;
        Check(!desk.IsOpen, "작업대 닫힘");

        // ---- 지도 테이블: 핀 ----
        var table = Object.FindAnyObjectByType<MissionTable>();
        PlayerInteractor3D brotherEyes = brother.GetComponent<PlayerInteractor3D>();
        var pins = table.Pins.ToList();
        Check(pins.Count(p => table.PinKind(p.Value) == MissionTable.PinColor.Yellow) == 1 && pins.Count(p => table.PinKind(p.Value) == MissionTable.PinColor.Red) == 2
              && pins.Count(p => table.PinKind(p.Value) == MissionTable.PinColor.Blue) == 1 && table.Area.Items.Length == 4,
            $"지도 핀: 노랑(우리 집) 1 · 빨강(임무) 2 · 파랑(302호, 예시 기록 후) 1 ({pins.Count})");
        InspectionItem bluePin = pins.First(p => table.PinKind(p.Value) == MissionTable.PinColor.Blue).Key;
        Check(bluePin.Description.Contains("종용"), "파란 핀: 그 사건의 기억 (칠판 피해자 사진과 같은 글)");

        brotherEyes.Enter(table.Area);
        yield return 1.0f;
        Check(table.IsOpen && integration.inspectionCamera.IsFocusing && Vector3.Dot(integration.inspectionCamera.transform.forward, Vector3.down) > 0.9f,
            "지도 테이블 → 위에서 내려다보게 확대");
        Check(!table.CanDepart && !table.Depart(), "아무도 배정 안 하면 출발 못 함");

        MissionDefinition park = table.places.First(p => p.id == "park_teacher"), neighbor = table.places.First(p => p.id == "case302_neighbor");
        InspectionItem PinOf(MissionDefinition place) => table.Pins.First(p => p.Value == place).Key;

        // 빨간 핀 → 임무 창: 아무도 안 고르면 확정 안 됨 → 여동생 고르고 F
        brotherEyes.Select(PinOf(park));
        yield return 0.2f;
        Check(table.SelectedMission == park, "빨간 핀을 누르면 그 임무 창");
        Check(!table.ConfirmAssignment() && GameSession.AssignmentOf(CharacterType.Player1) == null, "최소 한 명은 골라야 배정 확정");
        table.TogglePick(CharacterType.Player1);
        Check(table.ConfirmAssignment() && GameSession.AssignmentOf(CharacterType.Player1) == "park_teacher", "여동생 → 박 선생 댁 배정 (F)");
        Check(!table.CanDepart, "오빠가 배정 안 됐으면 아직 출발 못 함");
        brotherEyes.Back();   // Q: 지도로
        yield return 0.2f;
        Check(table.IsOpen && table.SelectedMission == null, "Q → 임무 창에서 지도로");

        // 옆집에 둘 다 → 여동생은 박 선생 댁에서 옮겨짐 → 다시 오빠만
        brotherEyes.Select(PinOf(neighbor));
        yield return 0.2f;
        Check(table.Picking.Count == 0, "다른 임무 창은 그 임무의 배정만 보여 줌");
        table.TogglePick(CharacterType.Player1);
        table.TogglePick(CharacterType.Player2);
        Check(table.ConfirmAssignment() && GameSession.CountAssignedTo("case302_neighbor") == 2 && GameSession.CountAssignedTo("park_teacher") == 0,
            "두 명 다 옆집 → 여동생은 박 선생 댁에서 옮겨짐 (한 사람은 한 곳만)");
        table.TogglePick(CharacterType.Player1);
        Check(table.ConfirmAssignment() && GameSession.AssignmentOf(CharacterType.Player1) == null && GameSession.AssignmentOf(CharacterType.Player2) == "case302_neighbor",
            "옆집에서 여동생을 빼면 배정 해제");
        brotherEyes.Back();
        yield return 0.2f;
        brotherEyes.Select(PinOf(park));
        yield return 0.2f;
        table.TogglePick(CharacterType.Player1);
        table.ConfirmAssignment();
        brotherEyes.Back();
        yield return 0.2f;
        Check(table.CanDepart, "여동생 → 박 선생 댁, 오빠 → 옆집: 출발 가능");

        // 창·버튼 위 클릭은 뒤의 핀을 고르지 않음 (팀원 조사 화면의 BlocksPointer)
        Check(brotherEyes.BlocksPointer != null, "지도가 열려 있으면 창·버튼 위치 클릭을 막는 연결이 있음");

        // ---- 출발: 각자 배정된 장소로 ----
        Check(table.Depart(), "출발");
        yield return 4.0f;
        MissionSite parkSite = table.SiteFor("park_teacher"), neighborSite = table.SiteFor("case302_neighbor");
        Check(!table.IsOpen && GameSession.Deployed && !table.Busy, "출발하면 지도가 닫히고 이동이 끝남");
        Check(CameraTrigger.FindAt(sister.transform.position)?.buildingId == "park_teacher", $"여동생은 박 선생 댁 (임시 장소)에 있음 ({sister.transform.position})");
        Check(CameraTrigger.FindAt(brother.transform.position)?.buildingId == "case302_neighbor", $"오빠는 옆집 (임시 장소)에 있음 ({brother.transform.position})");
        CameraTrigger brotherRoom = CameraTrigger.FindAt(brother.transform.position);
        Check(brotherRoom != null && brotherRoom.virtualCamera.activeSelf, "조작 중인 오빠의 장소 카메라가 켜짐");
        Check(!ItemHandover.SamePlace(brother, sister), "서로 다른 장소에 있으면 물건을 건넬 수 없음");

        // ---- F6: 아지트로 귀환 ----
        Check(table.ReturnHome(), "F6 귀환");
        yield return 3.0f;
        Check(!GameSession.Deployed && CameraTrigger.FindAt(sister.transform.position)?.buildingId == "Hideout" && CameraTrigger.FindAt(brother.transform.position)?.buildingId == "Hideout",
            "둘 다 다락방으로 돌아옴");
        Check(GameSession.AssignmentOf(CharacterType.Player1) == null && GameSession.AssignmentOf(CharacterType.Player2) == null, "귀환하면 배정은 비워짐");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------------- 개발용 창 화면 캡처 (확인용, 그래픽이 있는 에디터에서) ----------------
    // Unity.exe -executeMethod HideoutSceneValidator.CaptureWindows -overviewPath <폴더> (게임 화면에 창이 뜬 상태를 PNG로, 끝나면 에디터 종료)

    static string captureFolder;

    public static void CaptureWindows()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int index = System.Array.IndexOf(args, "-overviewPath");
        captureFolder = index >= 0 && index + 1 < args.Length ? args[index + 1] : ".";
        EditorApplication.delayCall += () =>
        {
            EditorSceneManager.OpenScene(HideoutSceneBuilder.TargetScene, OpenSceneMode.Single);
            EditorApplication.playModeStateChanged += OnCapturePlayMode;
            EditorApplication.EnterPlaymode();
        };
    }

    static void OnCapturePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= OnCapturePlayMode;
        Application.runInBackground = true;   // 에디터 창이 뒤에 있어도 게임이 멈추지 않게
        steps = CaptureSteps();
        waitUntil = EditorApplication.timeSinceStartup + 2.0;
        EditorApplication.update += CaptureTick;
    }

    static void CaptureTick()
    {
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        bool more;
        try { more = steps.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); more = false; }
        if (more) { waitUntil = EditorApplication.timeSinceStartup + steps.Current; return; }
        EditorApplication.update -= CaptureTick;
        EditorApplication.Exit(0);
    }

    static IEnumerator<float> CaptureSteps()
    {
        var director = Object.FindAnyObjectByType<HideoutDirector>();
        HideoutSampleData.Fill(director.sampleCase);
        PlayerInteractor3D eyes = CharacterManager.instance.players[0].GetComponent<PlayerInteractor3D>();
        foreach (HideoutStation station in new HideoutStation[] { Object.FindAnyObjectByType<HideoutStash>(), Object.FindAnyObjectByType<StatusDesk>(), Object.FindAnyObjectByType<Chalkboard>() })
        {
            eyes.Enter(station.Area);
            yield return 2.0f;
            string path = System.IO.Path.Combine(captureFolder, $"window_{station.GetType().Name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[아지트 창 캡처] " + path);
            yield return 1.0f;
            station.Close();
            yield return 1.5f;
        }

        // 지도 테이블: 지도 화면 → 빨간 핀 임무 창 (여동생 고른 상태)
        var table = Object.FindAnyObjectByType<MissionTable>();
        eyes.Enter(table.Area);
        yield return 2.0f;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureFolder, "window_MissionTable_map.png"));
        yield return 1.0f;
        eyes.Select(table.Pins.First(p => p.Value.id == "park_teacher").Key);
        yield return 0.3f;   // 임무 창이 열린 뒤에 고름 (창을 열 때 지금 배정으로 초기화)
        table.TogglePick(CharacterType.Player1);
        yield return 0.5f;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureFolder, "window_MissionTable_mission.png"));
        Debug.Log("[아지트 창 캡처] 지도 테이블");
        yield return 1.0f;
    }

    // 가구 둘레(가운데에서 1.4m, 바닥 높이) 중 한 곳까지 걸어갈 수 있는가
    // (가운데에서 찾으면 큰 테이블은 윗면의 작은 NavMesh 조각이 잡혀서 둘레를 봄)
    static bool Reachable(Vector3 from, Vector3 target)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas)) return false;
        foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            Vector3 point = new Vector3(target.x, 0f, target.z) + side * 1.4f;
            if (!NavMesh.SamplePosition(point, out NavMeshHit b, 0.5f, NavMesh.AllAreas) || b.position.y > 0.3f) continue;
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) return true;
        }
        return false;
    }
}
