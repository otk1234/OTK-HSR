using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 죄책감: 두 캐릭터가 같이 쓰는 수치 하나 (0~100, 50 = 중립). 부도덕한 행동은 올리고, 정의로운 행동은 내립니다.
// 다른 시스템은 Value·TierIndex·TierLevel을 조회하거나 Changed·TierChanged 이벤트만 받습니다. (게이지 UI는 UI 담당)
//  - 기록: GuiltSystem.Record("steal", CharacterType.Player1) — 변화량은 행동 표(GuiltActionTable)에서
//  - 같은 행동은 챕터당 한 번만 반영. 챕터가 바뀔 때 StartChapter() (챕터 구분은 아직 없음 → 지금은 플레이 전체가 한 챕터)
//  - 체크포인트: 씬이 시작될 때 자동 저장, 잡혀서 씬이 다시 시작되면 그 값으로 되돌림 (GameStateManager)
//  - 씬이 재시작돼도 남도록 static. 플레이를 새로 시작하면 새 게임 값으로 초기화. 저장·불러오기 연동은 나중에.
public static class GuiltSystem
{
    public const int Min = 0;
    public const int Max = 100;
    public const string TableResource = "GuiltActions";

    // 기록 한 줄. who는 기록만 해 둠 (수치는 공용, 나중에 대사 분기 등에 사용)
    public struct Deed
    {
        public string actionId;
        public CharacterType who;
        public int amount;
        public int valueAfter;
    }

    public static event Action<int, int> Changed;      // (이전 값, 현재 값)
    public static event Action<int, int> TierChanged;  // (이전 단계, 현재 단계)

    static GuiltActionTable table;
    static bool started;
    static int value;
    static readonly HashSet<string> usedThisChapter = new HashSet<string>();
    static readonly List<Deed> history = new List<Deed>();

    static int checkpointValue;
    static readonly HashSet<string> checkpointUsed = new HashSet<string>();
    static int checkpointHistoryCount;

    public static GuiltActionTable Table
    {
        get
        {
            if (table == null) table = Resources.Load<GuiltActionTable>(TableResource);
            if (table == null)
            {
                Debug.LogWarning($"[죄책감] Resources/{TableResource} 행동 표가 없어 기본값을 씁니다.");
                table = ScriptableObject.CreateInstance<GuiltActionTable>();
                table.FillDefaults();
            }
            return table;
        }
        set => table = value;
    }

    public static int Value { get { EnsureStarted(); return value; } }
    public static int TierIndex => Table.TierIndexOf(Value);
    public static string TierName => Table.tiers != null && Table.tiers.Length > 0 ? Table.tiers[TierIndex].name : "";
    // 중립 단계 기준 몇 단계 위(+)/아래(−)인지
    public static int TierLevel => TierIndex - Table.TierIndexOf(Table.startValue);
    public static IReadOnlyList<Deed> History => history;
    public static int CheckpointValue { get { EnsureStarted(); return checkpointValue; } }

    public static bool UsedThisChapter(string actionId) => usedThisChapter.Contains(actionId);

    static void EnsureStarted()
    {
        if (!started) NewGame();
    }

    public static void NewGame()
    {
        started = true;
        value = Mathf.Clamp(Table.startValue, Min, Max);
        usedThisChapter.Clear();
        history.Clear();
        SaveCheckpoint();
    }

    // 행동 기록. 반영됐으면 true (표에 없는 행동이거나 이번 챕터에 이미 한 행동이면 false)
    public static bool Record(string actionId, CharacterType who = CharacterType.All)
    {
        EnsureStarted();
        GuiltActionTable.Entry entry = Table.Find(actionId);
        if (entry == null)
        {
            Debug.LogWarning($"[죄책감] 행동 표에 없는 행동: {actionId}");
            return false;
        }
        if (entry.oncePerChapter && usedThisChapter.Contains(actionId))
        {
            Debug.Log($"[죄책감] 이번 챕터에 이미 반영됨: {entry.description}");
            return false;
        }

        usedThisChapter.Add(actionId);
        SetValue(value + entry.amount);
        history.Add(new Deed { actionId = actionId, who = who, amount = entry.amount, valueAfter = value });
        Debug.Log($"[죄책감] {entry.description} {entry.amount:+0;-0} → {value} ({TierName}) / {who}");
        return true;
    }

    public static void StartChapter()
    {
        EnsureStarted();
        usedThisChapter.Clear();
    }

    public static void SaveCheckpoint()
    {
        EnsureStarted();
        checkpointValue = value;
        checkpointUsed.Clear();
        checkpointUsed.UnionWith(usedThisChapter);
        checkpointHistoryCount = history.Count;
    }

    public static void RestoreCheckpoint()
    {
        EnsureStarted();
        usedThisChapter.Clear();
        usedThisChapter.UnionWith(checkpointUsed);
        if (history.Count > checkpointHistoryCount) history.RemoveRange(checkpointHistoryCount, history.Count - checkpointHistoryCount);
        SetValue(checkpointValue);
        Debug.Log($"[죄책감] 체크포인트로 되돌림 → {value} ({TierName})");
    }

    static void SetValue(int next)
    {
        next = Mathf.Clamp(next, Min, Max);
        if (next == value) return;
        int before = value;
        int beforeTier = Table.TierIndexOf(before);
        value = next;
        Changed?.Invoke(before, value);
        int tier = Table.TierIndexOf(value);
        if (tier != beforeTier) TierChanged?.Invoke(beforeTier, tier);
    }

    // 체크포인트가 아직 없으므로 씬이 시작되는 시점을 체크포인트로 봅니다.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) SaveCheckpoint();
    }

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 초기화합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        started = false;
        table = null;
        Changed = null;
        TierChanged = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
}
