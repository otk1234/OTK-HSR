using System;
using System.Collections.Generic;
using UnityEngine;

// 사건 파일: 두 캐릭터가 같이 쓰는 단서 목록. 조사 지점에서 단서(Clue)를 살펴보면 기록됩니다.
// 추리 단계는 Has(단서 ID)로 필요한 단서를 모았는지 확인합니다.
// 사건별로 따로 기록합니다 (아지트 칠판이 사건 단위로 모아 봄). Has·Add·Entries는 지금 사건(CurrentCaseId) 기준 —
// 추리 창(DeductionBoard)이 씬을 시작할 때 자기 사건으로 정합니다. 사건이 없는 씬(프로토타입 등)은 빈 사건 ID("")에 기록.
// 플레이를 새로 시작하면 비웁니다. (체크포인트·저장 연동은 추격자가 범행 현장에 들어올 때 정함)
public static class CaseFile
{
    public struct Entry
    {
        public string caseId;
        public string clueId;
        public string title;
        public CharacterType foundBy;
        public string note;   // 찾을 때 본 설명 (아지트 칠판 사진의 기억 글, 없으면 빈칸)
    }

    public static event Action<Entry> Added;

    static readonly Dictionary<string, List<Entry>> cases = new Dictionary<string, List<Entry>>();
    static readonly List<string> caseOrder = new List<string>();   // 처음 기록된 순서
    static readonly List<Entry> none = new List<Entry>();

    public static string CurrentCaseId { get; private set; } = "";

    public static void SetCurrentCase(string caseId) => CurrentCaseId = caseId ?? "";

    // 지금 사건의 단서
    public static IReadOnlyList<Entry> Entries => EntriesOf(CurrentCaseId);

    public static IReadOnlyList<Entry> EntriesOf(string caseId) =>
        cases.TryGetValue(caseId ?? "", out List<Entry> list) ? list : none;

    // 단서가 하나라도 기록된 사건들 (처음 기록된 순서)
    public static IReadOnlyList<string> CaseIds => caseOrder;

    public static bool Has(string clueId) => Has(CurrentCaseId, clueId);

    public static bool Has(string caseId, string clueId)
    {
        foreach (Entry entry in EntriesOf(caseId))
            if (entry.clueId == clueId) return true;
        return false;
    }

    // 지금 사건에 처음 찾은 단서면 true
    public static bool Add(string clueId, string title, CharacterType foundBy, string note = null) => Add(CurrentCaseId, clueId, title, foundBy, note);

    public static bool Add(string caseId, string clueId, string title, CharacterType foundBy, string note = null)
    {
        caseId ??= "";
        if (string.IsNullOrEmpty(clueId) || Has(caseId, clueId)) return false;
        if (!cases.TryGetValue(caseId, out List<Entry> list))
        {
            list = new List<Entry>();
            cases[caseId] = list;
            caseOrder.Add(caseId);
        }
        var entry = new Entry { caseId = caseId, clueId = clueId, title = title, foundBy = foundBy, note = note ?? "" };
        list.Add(entry);
        Debug.Log($"[사건 파일] 단서 획득: {title} ({foundBy}) — {(caseId.Length > 0 ? caseId : "사건 없음")} {list.Count}개");
        Added?.Invoke(entry);
        return true;
    }

    public static void Clear()
    {
        cases.Clear();
        caseOrder.Clear();
    }

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Clear();
        CurrentCaseId = "";
        Added = null;
    }
}
