using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// 죄책감 시스템 점검
//  - 행동 표 에셋 (없으면 기본값으로 만듦): Assets/Resources/GuiltActions.asset
//  - 계산 점검 (에디터 상태): 시작값 50, ± 반영, 0·100에서 멈춤, 단계 경계(45~55 중립), 이벤트, 챕터당 한 번, 체크포인트
//  - 플레이 점검 (추격 테스트 씬): 씬 시작 = 체크포인트, 잡혀서 재시작되면 체크포인트 값으로 되돌림
public static class GuiltSystemValidator
{
    const string Tag = "[OTK 죄책감 점검]";
    const string TableAsset = "Assets/Resources/" + GuiltSystem.TableResource + ".asset";

    [MenuItem("OTK/죄책감 행동 표 열기")]
    static void SelectTable()
    {
        Selection.activeObject = EnsureTableAsset();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    [MenuItem("OTK/죄책감 시스템 점검")]
    static void ValidateFromMenu()
    {
        failures.Clear();
        RunLogicChecks();
        EditorUtility.DisplayDialog("죄책감 시스템 점검", failures.Count == 0 ? "문제 없음" : string.Join("\n", failures), "확인");
    }

    public static GuiltActionTable EnsureTableAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GuiltActionTable>(TableAsset);
        if (asset != null) return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        asset = ScriptableObject.CreateInstance<GuiltActionTable>();
        asset.FillDefaults();
        AssetDatabase.CreateAsset(asset, TableAsset);
        AssetDatabase.SaveAssets();
        Debug.Log(Tag + " 행동 표를 기본값으로 만듦: " + TableAsset);
        return asset;
    }

    // ---------------- 계산 점검 (에디터 상태) ----------------

    static void RunLogicChecks()
    {
        GuiltActionTable asset = EnsureTableAsset();
        GuiltSystem.Table = null;
        Check(GuiltSystem.Table == asset, "Resources에서 행동 표를 불러옴");
        Check(asset.tiers.Length == 7 && asset.actions.Length == 10, $"행동 표: 단계 {asset.tiers.Length}개, 행동 {asset.actions.Length}개");

        // 계산은 기본값 표로 (기획이 에셋 수치를 바꿔도 점검이 흔들리지 않게)
        var table = ScriptableObject.CreateInstance<GuiltActionTable>();
        table.FillDefaults();
        GuiltSystem.Table = table;

        string Tier(int v) => table.tiers[table.TierIndexOf(v)].name;
        Check(Tier(44) == "약간 낮음" && Tier(45) == "중립" && Tier(55) == "중립" && Tier(56) == "약간 높음",
            $"중립 구간 45~55 (44 {Tier(44)} / 45 {Tier(45)} / 55 {Tier(55)} / 56 {Tier(56)})");
        Check(Tier(0) == "매우 낮음" && Tier(14) == "매우 낮음" && Tier(15) == "낮음" && Tier(85) == "높음" && Tier(86) == "매우 높음" && Tier(100) == "매우 높음",
            "양 끝 단계 경계 (14/15, 85/86)");

        var changes = new List<(int, int)>();
        var tierChanges = new List<(int, int)>();
        Action<int, int> onChanged = (a, b) => changes.Add((a, b));
        Action<int, int> onTier = (a, b) => tierChanges.Add((a, b));
        GuiltSystem.Changed += onChanged;
        GuiltSystem.TierChanged += onTier;
        try
        {
            GuiltSystem.NewGame();
            Check(GuiltSystem.Value == 50 && GuiltSystem.TierName == "중립" && GuiltSystem.TierLevel == 0, $"새 게임 50 중립 ({GuiltSystem.Value} {GuiltSystem.TierName})");

            bool applied = GuiltSystem.Record("steal", CharacterType.Player1);
            Check(applied && GuiltSystem.Value == 58 && GuiltSystem.TierName == "약간 높음" && GuiltSystem.TierLevel == 1,
                $"훔치기 +8 → 58 약간 높음 ({GuiltSystem.Value} {GuiltSystem.TierName})");
            Check(changes.Count == 1 && changes[0] == (50, 58), "Changed 이벤트 (50 → 58)");
            Check(tierChanges.Count == 1 && tierChanges[0] == (3, 4), "TierChanged 이벤트 (중립 → 약간 높음)");
            GuiltSystem.Deed last = GuiltSystem.History.Last();
            Check(last.actionId == "steal" && last.who == CharacterType.Player1 && last.amount == 8 && last.valueAfter == 58, "누가 했는지 기록");

            Check(!GuiltSystem.Record("steal", CharacterType.Player2) && GuiltSystem.Value == 58 && changes.Count == 1, "같은 행동은 챕터당 한 번");
            Check(!GuiltSystem.Record("no_such_action") && GuiltSystem.Value == 58, "행동 표에 없는 행동은 무시");

            GuiltSystem.StartChapter();
            Check(GuiltSystem.Record("steal") && GuiltSystem.Value == 66, "새 챕터에서는 같은 행동 다시 반영 (66)");

            for (int i = 0; i < 5; i++) { GuiltSystem.StartChapter(); GuiltSystem.Record("abandon_partner"); }
            Check(GuiltSystem.Value == 100 && GuiltSystem.TierName == "매우 높음" && GuiltSystem.TierLevel == 3, $"100에서 멈춤 ({GuiltSystem.Value})");

            GuiltSystem.NewGame();
            for (int i = 0; i < 6; i++) { GuiltSystem.StartChapter(); GuiltSystem.Record("rescue_partner"); }
            Check(GuiltSystem.Value == 0 && GuiltSystem.TierName == "매우 낮음" && GuiltSystem.TierLevel == -3, $"0에서 멈춤 ({GuiltSystem.Value})");

            GuiltSystem.NewGame();
            GuiltSystem.Record("steal");
            GuiltSystem.SaveCheckpoint();
            GuiltSystem.Record("tamper_evidence");
            Check(GuiltSystem.Value == 68, "준비: 체크포인트 뒤 증거 훼손 → 68");
            GuiltSystem.RestoreCheckpoint();
            Check(GuiltSystem.Value == 58 && GuiltSystem.History.Count == 1 && GuiltSystem.UsedThisChapter("steal") && !GuiltSystem.UsedThisChapter("tamper_evidence"),
                $"체크포인트로 되돌림: 값·기록·이번 챕터 행동 ({GuiltSystem.Value}, 기록 {GuiltSystem.History.Count}개)");
        }
        finally
        {
            GuiltSystem.Changed -= onChanged;
            GuiltSystem.TierChanged -= onTier;
            GuiltSystem.Table = null;
            GuiltSystem.NewGame();
            Object.DestroyImmediate(table);
        }
    }

    // ---------------- 플레이 모드 자동 진행 (batchmode용) ----------------

    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator<float> steps;
    static double waitUntil;

    // Unity.exe -batchmode -executeMethod GuiltSystemValidator.RunPlaySmokeTest (0=통과, 3=실패)
    public static void RunPlaySmokeTest()
    {
        failures.Clear();
        errors.Clear();
        Application.logMessageReceived += OnLog;
        RunLogicChecks();
        EditorSceneManager.OpenScene(ChaseTestSceneBuilder.TargetScene, OpenSceneMode.Single);
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

    static void StopPolice()
    {
        foreach (PoliceAI police in Object.FindObjectsByType<PoliceAI>()) police.enabled = false;
    }

    static IEnumerator<float> Steps()
    {
        StopPolice();
        Check(GuiltSystem.Value == 50 && GuiltSystem.CheckpointValue == 50, $"플레이 시작: 새 게임 50, 체크포인트 50 ({GuiltSystem.Value}/{GuiltSystem.CheckpointValue})");

        // 다른 씬으로 들어간 것처럼 씬을 다시 불러옴 → 그 시점 값이 체크포인트
        GuiltSystem.Record("steal", CharacterType.Player1);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        yield return 1f;
        StopPolice();
        Check(GuiltSystem.Value == 58 && GuiltSystem.CheckpointValue == 58, $"씬 시작 시점이 체크포인트 (58/{GuiltSystem.CheckpointValue}), 씬을 넘어가도 값 유지");

        // 체크포인트 뒤 행동 → 잡혀서 재시작 → 체크포인트 값으로
        GuiltSystem.Record("tamper_evidence", CharacterType.Player2);
        Check(GuiltSystem.Value == 68, "준비: 증거 훼손 → 68");
        GameStateManager game = GameStateManager.instance;
        game.ReportCaught(CharacterStatus.All[0], null);
        Check(game.RestartPending, "준비: 잡힘 → 씬 재시작 예약");
        yield return game.restartDelay + 2f;
        Check(GameStateManager.instance != null && GameStateManager.instance != game, "씬이 다시 시작됨");
        Check(GuiltSystem.Value == 58 && GuiltSystem.History.Count == 1 && !GuiltSystem.UsedThisChapter("tamper_evidence") && GuiltSystem.UsedThisChapter("steal"),
            $"잡혀서 재시작 → 체크포인트 값으로 되돌림 ({GuiltSystem.Value}, 기록 {GuiltSystem.History.Count}개)");
    }
}
