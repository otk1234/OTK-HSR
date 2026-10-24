using System;
using System.Collections.Generic;
using UnityEngine;

// 게임 세션: 씬이 바뀌어도 남아야 하는 진행 상태를 모아 두는 곳 (static).
// 지금은 아지트 상자(남매 공용, 칸 제한 없음)와 임무 배정. 소지품 넘기기·현재 스테이지·체크포인트는 스테이지 전환 때 여기에 추가 (§10).
// 저장·불러오기(파일)는 나중에. 플레이를 새로 시작하면 비웁니다.
public static class GameSession
{
    public struct StoredItem
    {
        public string name;
        public Inventory.ItemRule rule;   // 꺼낼 때 받는 쪽이 다룰 수 있어야 함 (공구 상자는 오빠만 등)
    }

    public static event Action StashChanged;

    static readonly List<StoredItem> stash = new List<StoredItem>();
    public static IReadOnlyList<StoredItem> Stash => stash;

    public static void AddToStash(string itemName, Inventory.ItemRule rule)
    {
        if (string.IsNullOrEmpty(itemName)) return;
        stash.Add(new StoredItem { name = itemName, rule = rule });
        Debug.Log($"[아지트 상자] 넣음: {itemName} — {stash.Count}개");
        StashChanged?.Invoke();
    }

    public static bool RemoveFromStash(int index, out StoredItem item)
    {
        item = default;
        if (index < 0 || index >= stash.Count) return false;
        item = stash[index];
        stash.RemoveAt(index);
        Debug.Log($"[아지트 상자] 꺼냄: {item.name} — {stash.Count}개");
        StashChanged?.Invoke();
        return true;
    }

    public static void ClearStash()
    {
        stash.Clear();
        StashChanged?.Invoke();
    }

    // ---------------- 임무 배정 (지도 테이블) ----------------
    // 한 사람은 한 곳에만. 한 곳에는 1~2명. 출발은 남매 둘 다 배정됐을 때 (사용자 결정 2026-10-24)

    static readonly Dictionary<CharacterType, string> assignments = new Dictionary<CharacterType, string>();

    public static bool Deployed { get; set; }   // 출발해서 임무 장소에 있음 (F6 귀환 전까지)

    public static string AssignmentOf(CharacterType who) => assignments.TryGetValue(who, out string id) ? id : null;

    public static void Assign(CharacterType who, string missionId)
    {
        if (string.IsNullOrEmpty(missionId)) assignments.Remove(who);
        else assignments[who] = missionId;
    }

    public static int CountAssignedTo(string missionId)
    {
        int count = 0;
        foreach (string id in assignments.Values)
            if (id == missionId) count++;
        return count;
    }

    public static void ClearAssignments() => assignments.Clear();

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        stash.Clear();
        StashChanged = null;
        assignments.Clear();
        Deployed = false;
    }
}
