using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum DoorTactic { None, Detour, FeignRetreat, Ambush, Bash }

// 아버지 AI의 잠긴 문 판단 계산 (상태 없음 → 씬 없이도 점검 가능)
public static class FatherTactics
{
    public struct DoorSituation
    {
        public bool detourAvailable;   // 창문·환풍구·다른 문으로 돌아가는 길이 있음
        public float detourExtra;      // 문으로 가는 길보다 더 걸어야 하는 거리 (m)
        public bool hasWeapon;
        public bool breakable;
    }

    public struct DoorWeights
    {
        public float detour, feign, ambush;
    }

    // 판단: 우회로가 없고 무기가 있으면 파괴(최후의 수단).
    // 그 밖에는 우회·위장 후퇴·잠복을 가중치 확률로. 우회로가 멀수록 우회를 덜 고름 (10m 더 → 그대로, 40m 이상 더 → 40%).
    public static DoorTactic Choose(DoorSituation situation, DoorWeights weights, System.Random rng)
    {
        if (!situation.detourAvailable && situation.hasWeapon && situation.breakable) return DoorTactic.Bash;

        float detour = situation.detourAvailable
            ? weights.detour * Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(10f, 40f, situation.detourExtra))
            : 0f;
        float feign = Mathf.Max(0f, weights.feign), ambush = Mathf.Max(0f, weights.ambush);
        float total = detour + feign + ambush;
        if (total <= 0f) return situation.detourAvailable ? DoorTactic.Detour : DoorTactic.Ambush;

        double roll = rng.NextDouble() * total;
        if (roll < detour) return DoorTactic.Detour;
        if (roll < detour + feign) return DoorTactic.FeignRetreat;
        return DoorTactic.Ambush;
    }

    // from → to 의 가장 짧은 길(잠긴 문을 열었다고 쳤을 때)이 잠긴 문을 지나면 그 문을 돌려줍니다.
    // directLength: 그 문을 지나는 길이, actualLength: 실제로 갈 수 있는 길이(우회로, 없으면 -1).
    // 실제 길이 문으로 가는 길과 같으면(문을 안 지나도 되면) null.
    public static EntryPoint LockedDoorOnRoute(Vector3 from, Vector3 to, int areaMask, out float directLength, out float actualLength)
    {
        actualLength = PathLength(from, to, areaMask, out _);
        directLength = actualLength;

        var locked = new List<EntryPoint>();
        foreach (EntryPoint entry in EntryPoint.All)
            if (entry.kind == EntryKind.Door && entry.IsLocked && entry.link != null) locked.Add(entry);
        if (locked.Count == 0) return null;

        foreach (EntryPoint entry in locked) entry.link.activated = true;
        try
        {
            directLength = PathLength(from, to, areaMask, out Vector3[] corners);
            if (directLength < 0f) return null;
            if (actualLength >= 0f && actualLength <= directLength + 0.5f) return null;
            for (int i = 0; corners != null && i < corners.Length - 1; i++)
                foreach (EntryPoint entry in locked)
                    if (EntryPoint.IsLinkSegment(corners[i], corners[i + 1], entry)) return entry;
            return null;
        }
        finally
        {
            foreach (EntryPoint entry in locked) entry.link.activated = false;
        }
    }

    // 길이 없으면 -1
    public static float PathLength(Vector3 from, Vector3 to, int areaMask, out Vector3[] corners)
    {
        var path = new NavMeshPath();
        NavMesh.CalculatePath(from, to, areaMask, path);
        corners = null;
        if (path.status != NavMeshPathStatus.PathComplete) return -1f;
        corners = path.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }
}
