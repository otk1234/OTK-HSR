using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// 추격 시스템 1단계 시연: 메뉴 OTK → 추격 시스템 시연
// 플레이 모드에서 자막과 함께 순서대로 보여 주고, 끝나면 직접 조작할 수 있습니다.
// (자동 점검과 달리 천천히 진행하고 에디터를 닫지 않습니다)
public static class ChaseDemo
{
    static IEnumerator<float> steps;
    static double waitUntil;

    // 시연 없이 테스트 씬만 열기 (직접 플레이용). 명령줄: -executeMethod ChaseDemo.OpenTestScene
    [MenuItem("OTK/추격 테스트 씬 열기")]
    public static void OpenTestScene()
    {
        EditorSceneManager.OpenScene(ChaseTestSceneBuilder.TargetScene, OpenSceneMode.Single);
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 11f);
        }
    }

    [MenuItem("OTK/추격 시스템 시연")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorSceneManager.OpenScene(ChaseTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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
        catch (System.Exception e) { Debug.LogException(e); more = false; }
        if (more) waitUntil = EditorApplication.timeSinceStartup + steps.Current;
        else Stop();
    }

    static void Say(string text)
    {
        ChaseDebugOverlay.Caption = text;
        Debug.Log("[시연] " + text.Replace("\n", " "));
    }

    static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        CharacterStatus p1 = manager.players[0].GetComponent<CharacterStatus>();
        CharacterStatus p2 = manager.players[1].GetComponent<CharacterStatus>();
        PlayerMovement move1 = p1.GetComponent<PlayerMovement>();
        PlayerStamina stamina1 = p1.GetComponent<PlayerStamina>();
        var police = Object.FindAnyObjectByType<PoliceAI>();
        var chaser = police;
        var motor = chaser.GetComponent<ChaserMotor>();
        var senses = chaser.GetComponent<ChaserSenses>();
        EntryPoint Entry(string name) => EntryPoint.All.First(e => e.displayName == name);
        HidingSpot Spot(string name) => HidingSpot.All.First(s => s.displayName == name);
        Inventory inventory = p1.GetComponent<Inventory>();
        string RoomOf(Component c) => CameraTrigger.FindAt(c.transform.position)?.roomName ?? "";

        void Place(Component who, Vector3 floor, float yaw = float.NaN)
        {
            ChaserMotor m = who.GetComponent<ChaserMotor>();
            if (m != null) m.Teleport(floor);
            else who.GetComponent<NavMeshAgent>().Warp(floor);
            if (!float.IsNaN(yaw)) who.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // 씬 뷰: 집 전체를 위에서, 추격자를 선택해 시야 기즈모가 보이게
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 11f);
        }
        Selection.activeGameObject = chaser.gameObject;

        chaser.enabled = false;
        Place(chaser, new Vector3(1.5f, 0f, -1.5f), 0f);
        Place(p1, new Vector3(-9f, 0f, 1.6f));
        Place(p2, new Vector3(-5f, 0f, 6.0f));          // 오빠는 침실에서 대기
        Say("추격 시스템 시연을 시작합니다 (1단계 + 경찰 AI).\n(왼쪽 위: 게임 상태 · 캐릭터 상태 · 스태미나 · 위치 · 추격자 인지도)");
        yield return 4f;

        // ① 걷기 · 달리기 · 스태미나
        Say("① 걷기: 발소리가 작게 납니다 (반경 2.5m).");
        move1.scriptedInput = new Vector2(1f, 0f);
        yield return 1.4f;
        move1.scriptedInput = new Vector2(-1f, 0f);
        yield return 1.4f;
        Say("② Shift 달리기: 1.75배 빠르고, 발소리가 멀리 퍼지며(7m) 스태미나가 줄어듭니다.");
        move1.scriptedRun = true;
        for (int i = 0; i < 12 && !stamina1.Exhausted; i++)
        {
            move1.scriptedInput = new Vector2(i % 2 == 0 ? 1f : -1f, 0f);
            yield return 0.8f;
        }
        Say("③ 스태미나가 바닥나면 '숨참' 상태 → 회복 기준(30)까지 걷기만 가능합니다.");
        yield return 2.5f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;

        // ② 문 잠금 → 추격자 막힘
        EntryPoint livingDoor = Entry("거실 문");
        Place(p1, new Vector3(-4.6f, 0f, 0.9f));
        yield return 0.6f;
        livingDoor.door.ToggleLock(inventory);
        Say("④ 문 앞에서 F: 문을 잠갔습니다. 잠긴 문은 추격자 길찾기에서 빠집니다.");
        yield return 2.5f;
        motor.SetDestination(new Vector3(-6.5f, 0f, -1.6f));
        yield return 1f;
        EntryPoint blocking = EntryPoint.FindBlockingDoor(chaser.transform.position, new Vector3(-6.5f, 0f, -1.6f), motor.Agent.areaMask);
        Say($"⑤ 추격자(경찰 규칙)가 거실로 오려 하지만 막혔습니다. 막고 있는 문: {(blocking != null ? blocking.displayName : "?")}\n경찰은 창문·환풍구를 못 쓰므로 다른 길이 없습니다. (씬 뷰에서 확인)");
        yield return 5f;

        // ③ 잠금 해제 → 추격자가 문을 열고 들어옴
        livingDoor.door.ToggleLock(inventory);
        Place(p1, new Vector3(-8.5f, 0f, 1.6f));
        motor.SetDestination(new Vector3(-6.5f, 0f, -1.6f));
        Say("⑥ 잠금을 풀면: 추격자는 닫힌 문을 직접 열고 지나갑니다 (문 소리 발생).");
        for (float t = 0f; t < 10f && RoomOf(chaser) != "거실"; t += 0.25f) yield return 0.25f;
        yield return 2f;

        // ④ 경찰 AI의 잠긴 문 대응: 노크·경고 → 부수기 → 포기 → 의심 지점 기억
        EntryPoint bathDoor = Entry("욕실 문");
        HidingSpot curtainSpot = Spot("커튼");
        Place(p1, curtainSpot.transform.position);
        yield return 0.3f;
        curtainSpot.TryEnter(p1);
        Say("⑦ 여동생은 커튼 뒤에 숨어서 지켜봅니다. 오빠는 욕실에 들어가 문을 잠갔습니다.");
        Place(p2, new Vector3(-7.5f, 0f, -4.2f));
        bathDoor.door.ToggleLock(p2.GetComponent<Inventory>());
        yield return 2.5f;
        police.giveUpTime = 7f; // 시연용으로 조금 짧게
        police.enabled = true;
        police.ResetToPatrol();
        motor.Stop();
        NoiseSystem.Emit(p2.transform.position, 10f, NoiseKind.Run, p2.gameObject);
        Say("⑧ 욕실 안에서 소리 → 경찰이 확인하러 가지만 문이 잠겨 길이 막힙니다.");
        for (float t = 0f; t < 8f && police.Current != PoliceAI.Mode.DoorKnock; t += 0.2f) yield return 0.2f;
        Say("⑨ (a) 노크와 경고: \"경찰이다! 문 열어!\" — 플레이어가 반응할 시간 2초.");
        for (float t = 0f; t < 5f && police.Current != PoliceAI.Mode.DoorBash; t += 0.2f) yield return 0.2f;
        for (float t = 0f; t < 12f && police.Current == PoliceAI.Mode.DoorBash; t += 0.25f)
        {
            Say($"⑨ 문 부수기: 쾅! 칠 때마다 큰 소음. 남은 내구도 {bathDoor.door.Durability:0} (기본 10초, 시연은 7초 뒤 포기)");
            yield return 0.25f;
        }
        Say($"⑩ 시간이 지나 포기. 문에는 피해가 남습니다(내구도 {bathDoor.door.Durability:0}).\n(b) 포기한 욕실은 '의심 지점'으로 기억해 한동안 순찰 때 들릅니다. (씬 뷰 주황 네모)");
        yield return 5f;
        police.enabled = false;
        motor.Stop();
        curtainSpot.Exit(p1);
        yield return 1f;

        // ⑤ 숨기 (목격 없음)
        HidingSpot closet = Spot("옷장");
        Place(p2, closet.transform.position);
        manager.SwitchInstant(1);
        Place(chaser, new Vector3(0f, 0f, 5.2f), 270f);
        Say("⑪ 오빠로 전환. 침실 옷장에 숨습니다 (E). 숨는 동안 추격자가 보고 있으면 기억됩니다.");
        yield return 1.5f;
        closet.TryEnter(p2);
        yield return 2f;
        motor.SetDestination(new Vector3(-7.5f, 0f, 4.2f));
        Say("⑫ 추격자가 침실에 들어오지만, 숨는 장면을 못 봤으므로 옷장을 모르고 지나칩니다.\n(숨은 캐릭터는 시야에 잡히지 않음 · 옷장은 2인용, 침대 밑은 1인용)");
        yield return 6f;
        motor.SetDestination(new Vector3(0f, 0f, 5.2f));
        yield return 3f;
        closet.Exit(p2);
        yield return 1.5f;

        // ⑥ 추격 상태의 즉시 컷 (이벤트로 추격 시작)
        manager.SwitchInstant(0);
        Place(p1, new Vector3(-6f, 0f, 1.2f));
        yield return 1f;
        GameStateManager.instance.ForceChase(true);
        Say("⑬ 추격 상태 (이벤트로 시작 · 왼쪽 위 빨간 표시): 방 이동·Tab 전환 모두 페이드 없이 즉시 컷됩니다.");
        yield return 2.5f;
        manager.SwitchInstant(1);
        Say("⑬ Tab → 오빠 (침실, 즉시 컷)");
        yield return 1.8f;
        manager.SwitchInstant(0);
        Say("⑬ Tab → 여동생 (거실, 즉시 컷)");
        yield return 1.8f;
        GameStateManager.instance.ForceChase(false);
        Say("⑬ 추격이 끝나면 잠시 뒤 추리 상태로 돌아갑니다.");
        yield return 3f;

        // ⑦ 피날레: 소리 → 확인 → 발견 → 추격 상태 → 숨는 장면 목격 → 수색 → 잡힘
        HidingSpot curtain = Spot("커튼");
        Place(chaser, new Vector3(-2f, 0f, 0.3f), 90f);
        Place(p1, new Vector3(-6f, 0f, -1.6f));
        senses.Forget(p1);
        senses.Forget(p2);
        Say("⑭ 이제 경찰 AI를 켭니다. 여동생이 거실에서 달리면… 추격자가 소리를 듣고 확인하러 옵니다.");
        yield return 2.5f;
        chaser.enabled = true;
        police.ResetToPatrol();
        move1.scriptedRun = true;
        move1.scriptedInput = new Vector2(1f, 0f);
        yield return 0.5f;
        move1.scriptedInput = new Vector2(-1f, 0f);
        yield return 0.4f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        for (float t = 0f; t < 8f && !GameStateManager.IsChase; t += 0.25f) yield return 0.25f;

        Say("⑮ 발견 → 추격 상태! 여동생이 추격자가 보는 앞에서 커튼 뒤로 숨으면… 숨는 장면을 봤으므로 그곳을 뒤집니다.");
        move1.scriptedRun = true;
        move1.scriptedInput = new Vector2(-1f, 0f);
        yield return 0.5f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        curtain.TryEnter(p1);
        for (float t = 0f; t < 8f && !p1.IsCaught && !p2.IsCaught; t += 0.25f) yield return 0.25f;

        Say("⑯ 잡힘 → 씬 재시작 (잡힘 연출은 나중에).\n시연 끝! 이제 직접 조작해 보세요: WASD · Shift · E · F · Tab");
        yield return 12f;
        ChaseDebugOverlay.Caption = "";
    }
}
