using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using NarrativeGraybox.Interaction;
using Object = UnityEngine.Object;

// 범행 현장 시연 (1단계): 메뉴 OTK → 범행 현장 시연
// 플레이 모드에서 자막과 함께 순서대로 보여 주고(실제 게임 화면 그대로), 끝나면 직접 조작할 수 있습니다.
// 명령줄 확인: -executeMethod CrimeSceneDemo.TestFromCommandLine (끝까지 돌리고 콘솔 오류가 없으면 0)
public static class CrimeSceneDemo
{
    static IEnumerator<float> steps;
    static double waitUntil;
    static bool exitWhenDone;
    static readonly List<string> errors = new List<string>();

    [MenuItem("OTK/범행 현장 시연")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Start();
    }

    public static void TestFromCommandLine()
    {
        exitWhenDone = true;
        errors.Clear();
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        Start();
    }

    static void Start()
    {
        EditorSceneManager.OpenScene(CrimeSceneTestSceneBuilder.TargetScene, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            steps = Steps();
            waitUntil = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Stop();
        }
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        steps = null;
    }

    static void Tick()
    {
        if (steps == null || EditorApplication.timeSinceStartup < waitUntil) return;
        bool more;
        try { more = steps.MoveNext(); }
        catch (Exception e) { Debug.LogException(e); more = false; }
        if (more) { waitUntil = EditorApplication.timeSinceStartup + steps.Current; return; }

        Stop();
        if (exitWhenDone)
        {
            Debug.Log(errors.Count == 0 ? "[시연] 끝까지 진행, 콘솔 오류 없음" : "[시연] 콘솔 오류\n" + string.Join("\n", errors));
            EditorApplication.Exit(errors.Count == 0 ? 0 : 3);
        }
    }

    static void Say(string text)
    {
        ChaseDebugOverlay.Caption = text;
        Debug.Log("[시연] " + text.Replace("\n", " "));
    }

    // ---------------- 도우미 ----------------

    static int PlayerMask()
    {
        int mask = NavMesh.AllAreas;
        foreach (string area in new[] { EntryPoint.DoorArea, EntryPoint.WindowArea, EntryPoint.VentArea })
        {
            int index = NavMesh.GetAreaFromName(area);
            if (index >= 0) mask &= ~(1 << index);
        }
        return mask;
    }

    // 조작 중인 캐릭터를 길찾기 경로를 따라 걸어가게 함 (키 입력 대신 scriptedInput)
    static IEnumerable<float> Walk(GameObject who, Vector3 target, float timeout = 15f)
    {
        var move = who.GetComponent<PlayerMovement>();
        var path = new NavMeshPath();
        NavMesh.SamplePosition(who.transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas);
        NavMesh.SamplePosition(target, out NavMeshHit to, 1f, NavMesh.AllAreas);
        if (!NavMesh.CalculatePath(from.position, to.position, PlayerMask(), path) || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning($"[시연] 길이 없어 순간이동: {who.name} → {target}");
            who.GetComponent<NavMeshAgent>().Warp(target);
            yield break;
        }

        float started = Time.time;
        foreach (Vector3 corner in path.corners.Skip(1))
        {
            while (Time.time - started < timeout)
            {
                Vector3 delta = corner - who.transform.position;
                delta.y = 0f;
                if (delta.magnitude < 0.25f) break;
                move.scriptedInput = new Vector2(delta.x, delta.z).normalized;
                yield return 0.02f;
            }
        }
        move.scriptedInput = null;
        yield return 0.2f;
    }

    static IEnumerable<float> OpenDoor(string name, GameObject who)
    {
        InteractableDoor door = EntryPoint.All.First(e => e.displayName == name).door;
        if (!door.IsOpen) door.Interact(who.GetComponent<Inventory>());
        yield return 0.2f;
        for (int i = 0; i < 30 && door.IsMoving; i++) yield return 0.1f;
        yield return 0.3f;
    }

    static InspectionArea Area(string title) =>
        Object.FindObjectsByType<InspectionArea>(FindObjectsInactive.Include).First(a => a.Title == title);

    static InspectionItem ItemOf(InspectionArea area, string clueId) =>
        area.Items.First(i => i.GetComponent<Clue>().clueId == clueId);

    // 조사 화면을 열고, 항목을 하나씩 살펴보게 한 뒤 닫음
    static IEnumerable<float> Inspect(GameObject who, InspectionArea area, float readTime, params string[] clueIds)
    {
        var inspector = who.GetComponent<PlayerInteractor3D>();
        inspector.Enter(area);
        yield return 1.2f;
        foreach (string id in clueIds)
        {
            inspector.Select(ItemOf(area, id));
            yield return readTime;
        }
        inspector.Back();
        if (inspector.IsInspecting) inspector.Back();
        yield return 0.3f;
        for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.1f;
        yield return 0.3f;
    }

    // ---------------- 시연 순서 ----------------

    static IEnumerator<float> Steps()
    {
        CharacterManager manager = CharacterManager.instance;
        GameObject sister = manager.players[0], brother = manager.players[1];
        PlayerInteraction sisterHands = sister.GetComponent<PlayerInteraction>(), brotherHands = brother.GetComponent<PlayerInteraction>();
        CrimeSceneTimer timer = Object.FindAnyObjectByType<CrimeSceneDirector>().timer;
        var shelf = Object.FindAnyObjectByType<HeavyObject>();
        var recorder = Object.FindAnyObjectByType<RepairableMachine>();
        const float read = 3.2f;

        Say("<b>범행 현장 테스트 씬</b>: 아버지가 이미 살인을 저지르고 떠난 302호 (임시 사건).\n경찰이 오기 전에 제한 시간(180초) 동안 현장을 조사합니다.");
        yield return 5f;
        Say("오른쪽 위는 <b>개발용 표시</b>: 남은 시간 · 죄책감 · 모은 단서.\n(실제 게임 UI가 아님 → UI 담당에게 넘길 부분)");
        yield return 4.5f;

        // ---- 0. 벽 너머 조사 판정 수정 ----
        Say("<b>[수정]</b> 벽 건너 욕조가 2.4m 안에 있어도, 벽에 가려지면 조사 대상이 아닙니다.\n(화면 아래 안내에 [E] 조사가 뜨지 않음)");
        foreach (float s in Walk(sister, new Vector3(3.6f, 0f, 3.8f))) yield return s;
        yield return 4f;

        // ---- 추리 창: 단서가 없으면 정답 선택지가 잠김 ----
        var board = Object.FindAnyObjectByType<DeductionBoard>();
        Say("<b>[J] 추리 창</b> (개발용 최소 화면). 선택지는 정답·오답 모두 근거 단서가 있어야 열리고\n지금은 <b>??? (근거 단서 부족)</b>. 언제나 고를 수 있는 건 '잘 모르겠다'(= 실패)뿐입니다.");
        board.Open();
        yield return 5f;
        board.Close();
        yield return 0.5f;

        // ---- 1. 시체: 여동생이 조사 + 여동생 전용 단서 ----
        Say("① 여동생이 거실로 들어가 시체를 조사합니다.");
        foreach (float s in OpenDoor("현관 중문", sister)) yield return s;
        foreach (float s in Walk(sister, new Vector3(-3.4f, 0f, -2.3f))) yield return s;
        Say("① 조사는 팀원 조사 시스템 그대로 (E → 클로즈업 → 클릭).\n살펴본 항목은 <b>사건 파일</b>에 기록됩니다 (오른쪽 위 목록).");
        foreach (float s in Inspect(sister, Area("시체"), read, "body", "head_wound")) yield return s;

        Say("② <b>여동생 전용 단서</b>: 시체 손톱 밑의 남색 섬유.\n여동생에게만 보이고, 여동생만 찾을 수 있습니다.");
        foreach (float s in Inspect(sister, Area("시체"), read + 0.5f, "navy_fiber")) yield return s;

        Say("② 오빠(Tab)로 같은 곳을 조사하면 섬유가 <b>보이지 않습니다</b>.");
        brother.GetComponent<NavMeshAgent>().Warp(new Vector3(-3.9f, 0f, -2.3f));
        yield return 0.3f;
        manager.SwitchInstant(1);
        yield return 1.2f;
        var brotherInspector = brother.GetComponent<PlayerInteractor3D>();
        brotherInspector.Enter(Area("시체"));
        yield return 3.5f;
        brotherInspector.Back();
        yield return 0.3f;
        for (int i = 0; i < 30 && FadeManager.instance.isFading; i++) yield return 0.1f;

        // ---- 2. 책장: 여동생 실패 → 오빠가 밀어 치움 → 수첩 ----
        manager.SwitchInstant(0);
        Say("③ 침실의 책장. 여동생은 너무 무거워서 밀지 못합니다.");
        foreach (float s in OpenDoor("침실 문", sister)) yield return s;
        foreach (float s in Walk(sister, new Vector3(-6.4f, 0f, 2.6f))) yield return s;
        yield return 0.5f;
        shelf.Use(sisterHands);
        yield return 3.5f;

        Say("③ <b>오빠(물리적 개입)</b>는 책장을 밀어 치울 수 있습니다. (미는 동안 이동·전환 잠김, 소리 남)");
        manager.SwitchInstant(1);
        foreach (float s in Walk(brother, new Vector3(-6.4f, 0f, 3.4f))) yield return s;
        shelf.Use(brotherHands);
        yield return shelf.pushTime + 1.2f;
        Say("③ 책장 뒤에 떨어져 있던 <b>피해자 수첩</b> → 다음 범행의 단서.");
        foreach (float s in Inspect(brother, Area("책장 뒤 바닥"), read + 1.5f, "victim_diary")) yield return s;

        // ---- 3. 인터폰 녹화기: 여동생 실패 → 오빠가 고침 → 녹화 영상 ----
        Say("④ 현관의 고장 난 인터폰 녹화기. 여동생은 고칠 줄 모릅니다.");
        manager.SwitchInstant(0);
        sister.GetComponent<NavMeshAgent>().Warp(new Vector3(6.9f, 0f, 3.9f));
        yield return 1f;
        recorder.Use(sisterHands);
        yield return 3.5f;

        Say("④ 오빠가 고치면 표시등이 초록으로 바뀌고 <b>녹화 영상</b>을 볼 수 있게 됩니다.");
        manager.SwitchInstant(1);
        brother.GetComponent<NavMeshAgent>().Warp(new Vector3(6.9f, 0f, 3.1f));
        yield return 1f;
        recorder.Use(brotherHands);
        yield return recorder.repairTime + 1.5f;
        foreach (float s in Inspect(brother, Area("인터폰 녹화 영상"), read + 1.5f, "intercom_video")) yield return s;

        Say("④ 오빠에게는 영상의 <b>사실</b>만 보입니다. 여동생이 보면 <b>[통찰]</b> 문장이 붙고\n'아빠의 걸음걸이'가 <b>여동생만 얻는 추리 근거</b>로 기록됩니다. (오빠가 고치고 → 여동생이 봄)");
        manager.SwitchInstant(0);
        sister.GetComponent<NavMeshAgent>().Warp(new Vector3(6.9f, 0f, 3.5f));
        yield return 1.5f;
        foreach (float s in Inspect(sister, Area("인터폰 녹화 영상"), read + 2f, "intercom_video")) yield return s;

        // ---- 4. 제한 시간 ----
        // ---- 5. 제한 시간 → 추리 창 자동 ----
        manager.SwitchInstant(0);
        Say("⑤ 제한 시간이 끝나면 <b>추리 창이 자동으로</b> 열립니다.\n(경찰·아버지가 들이닥치는 건 나중 단계)");
        timer.SetRemaining(5f);
        yield return 6.5f;

        Say("⑥ 추리는 질문마다 <b>한 번만</b>. 일부러 '잘 모르겠다'를 골라 봅니다 (= 실패).\n실패여도 진행은 막히지 않고, <b>다음 사건의 큰 오류</b>로 기록됩니다.");
        yield return 4.5f;
        board.Choose(3);
        yield return 4f;

        Say("⑥ 여동생이 찾은 <b>손톱 밑 섬유</b>를 근거로 '아빠'를 고릅니다.\n(누가 찾았든 사건 파일에 근거가 있으면 남매 누구나 고를 수 있음 — 여동생이 오빠에게 알려 주면 되니까)");
        yield return 5f;
        board.Choose(1);
        yield return 3f;

        Say("⑥ 수첩을 찾았으므로 다음 범행 '박 선생 댁'이 열려 있습니다.");
        yield return 3.5f;
        board.Choose(0);
        yield return 1f;
        Say("⑥ 플레이어 화면에는 <b>정답·오답이 보이지 않습니다</b> (고른 답만). 결과는 기록에 남아 다음 사건으로 넘겨집니다.\n다음 단계 = 개입 (종용 · 가담 · 막기)");
        yield return 4.5f;
        board.Close();

        // ---- 7. 개입: 남매 대화로 방향 → 그 종류만 한 번 ----
        var planner = Object.FindAnyObjectByType<InterventionPlanner>();
        Say("⑦ 추리를 마치면 남매가 <b>이번 사건의 방향</b>을 정합니다 (개발용 대화 화면).\n정한 방향의 개입만 할 수 있고, 사건마다 한 번.");
        for (int i = 0; i < 40 && !planner.IsOpen; i++) yield return 0.1f;
        yield return 5f;
        Say("⑦ 이번엔 <b>가담</b>: 시체를 욕실로 옮겨 숨깁니다.\n(종용 = 휴대전화 문자 조작, 막기 = 식탁 위 편지지로 경고 편지 → 지금은 잠김)");
        planner.Choose(InterventionKind.Abet);
        yield return 4.5f;

        var hideBody = Object.FindObjectsByType<Intervention>().First(i => i.kind == InterventionKind.Abet);
        Say($"⑦ 누구나 할 수 있지만 <b>능력에 따라 시간이 다름</b>: 시체 옮기기는\n오빠 {hideBody.WorkTimeFor(CharacterType.Player2)}초 · 여동생 {hideBody.WorkTimeFor(CharacterType.Player1)}초. 오빠가 옮깁니다.");
        brother.GetComponent<NavMeshAgent>().Warp(new Vector3(-3.6f, 0f, -2.3f));
        manager.SwitchInstant(1);
        yield return 2f;
        hideBody.Use(brotherHands);
        yield return hideBody.WorkTimeFor(CharacterType.Player2) + 1.5f;
        Say($"⑦ 가담 → <b>죄책감 +8</b> (지금 {GuiltSystem.Value}, {GuiltSystem.TierName}). 개입 결과도 다음 사건으로 넘겨집니다.\n(변동폭은 챕터가 많으므로 작게: 종용 +5 · 가담 +8 · 막기 −5)");
        yield return 6f;

        timer.SetRemaining(timer.timeLimit);
        manager.SwitchInstant(0);
        Say("시연 끝. 타이머를 180초로 되돌렸습니다. 이제 직접 조작해 보세요. (추리·개입은 이미 끝나 결과만 보임)\n남은 조사 지점: 깨진 재떨이 · 핏자국 · 식탁 찻잔 · 욕조 수건  (WASD 이동 · E 조사/상호작용 · Tab 전환 · J 추리)");
        yield return 12f;
        Say("");
    }
}
