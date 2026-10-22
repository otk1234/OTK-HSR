using System;
using System.Collections.Generic;
using UnityEngine;

// 사건 파일: 두 캐릭터가 같이 쓰는 단서 목록. 조사 지점에서 단서(Clue)를 살펴보면 기록됩니다.
// 추리 단계(다음 작업)는 Has(단서 ID)로 필요한 단서를 모았는지 확인합니다.
// 플레이를 새로 시작하면 비웁니다. (체크포인트·저장 연동은 추격자가 범행 현장에 들어올 때 정함)
public static class CaseFile
{
    public struct Entry
    {
        public string clueId;
        public string title;
        public CharacterType foundBy;
    }

    public static event Action<Entry> Added;

    static readonly List<Entry> entries = new List<Entry>();
    public static IReadOnlyList<Entry> Entries => entries;

    public static bool Has(string clueId)
    {
        foreach (Entry entry in entries)
            if (entry.clueId == clueId) return true;
        return false;
    }

    // 처음 찾은 단서면 true
    public static bool Add(string clueId, string title, CharacterType foundBy)
    {
        if (string.IsNullOrEmpty(clueId) || Has(clueId)) return false;
        var entry = new Entry { clueId = clueId, title = title, foundBy = foundBy };
        entries.Add(entry);
        Debug.Log($"[사건 파일] 단서 획득: {title} ({foundBy}) — {entries.Count}개");
        Added?.Invoke(entry);
        return true;
    }

    public static void Clear() => entries.Clear();

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        entries.Clear();
        Added = null;
    }
}
