using System.Collections.Generic;
using UnityEngine;

// 아버지 AI의 기억. 잡혀서 씬이 다시 시작돼도 남도록 static으로 보관하고, 플레이를 새로 시작할 때 비웁니다.
// 씬을 다시 불러오면 오브젝트가 새로 만들어지므로 키는 '씬 이름/오브젝트 이름'.
//  - 자주 보인 장소: 방마다 캐릭터를 본 횟수 (배회할 방을 고를 때 가중치)
//  - 학습 (플레이어가 빠져나간 방법, 아버지가 추격·문 앞에서 포기할 때 기록):
//      잠긴 문으로 버팀 / 숨는 곳에 숨어 넘김 / 방에서 방으로 도망친 경로
public static class FatherMemory
{
    static readonly Dictionary<string, int> roomSightings = new Dictionary<string, int>();
    static readonly Dictionary<string, int> doorEscapes = new Dictionary<string, int>();
    static readonly Dictionary<string, int> hideEscapes = new Dictionary<string, int>();
    static readonly Dictionary<string, Dictionary<string, int>> routeEscapes = new Dictionary<string, Dictionary<string, int>>();

    public static string Key(Component component) =>
        component == null ? "" : component.gameObject.scene.name + "/" + component.name;

    static int Get(Dictionary<string, int> table, Component component) =>
        component != null && table.TryGetValue(Key(component), out int count) ? count : 0;

    static void Add(Dictionary<string, int> table, Component component)
    {
        if (component == null) return;
        string key = Key(component);
        table[key] = (table.TryGetValue(key, out int count) ? count : 0) + 1;
    }

    // ---------------- 자주 보인 장소 ----------------

    public static void RecordSighting(CameraTrigger room) => Add(roomSightings, room);
    public static int Sightings(CameraTrigger room) => Get(roomSightings, room);

    // ---------------- 학습: 빠져나간 방법 ----------------

    public static void RecordDoorEscape(EntryPoint door)
    {
        Add(doorEscapes, door);
        if (door != null) Debug.Log($"[아버지 학습] 문으로 버팀: {door.displayName} ({DoorEscapes(door)}회)");
    }

    public static int DoorEscapes(EntryPoint door) => Get(doorEscapes, door);

    public static void RecordHideEscape(HidingSpot spot)
    {
        Add(hideEscapes, spot);
        if (spot != null) Debug.Log($"[아버지 학습] 숨어서 넘김: {spot.displayName} ({HideEscapes(spot)}회)");
    }

    public static int HideEscapes(HidingSpot spot) => Get(hideEscapes, spot);

    public static void RecordRouteEscape(CameraTrigger from, CameraTrigger to)
    {
        if (from == null || to == null || from == to) return;
        string key = Key(from);
        if (!routeEscapes.TryGetValue(key, out Dictionary<string, int> next)) routeEscapes[key] = next = new Dictionary<string, int>();
        string toKey = Key(to);
        next[toKey] = (next.TryGetValue(toKey, out int count) ? count : 0) + 1;
        Debug.Log($"[아버지 학습] 도망 경로: {from.roomName} → {to.roomName} ({next[toKey]}회)");
    }

    public static int RouteEscapes(CameraTrigger from, CameraTrigger to) =>
        from != null && to != null && routeEscapes.TryGetValue(Key(from), out Dictionary<string, int> next) && next.TryGetValue(Key(to), out int count) ? count : 0;

    // 이 방에서 가장 자주 도망친 다음 방 (없으면 null)
    public static CameraTrigger MostLikelyNext(CameraTrigger from, out int count)
    {
        count = 0;
        CameraTrigger best = null;
        if (from == null || !routeEscapes.ContainsKey(Key(from))) return null;
        foreach (CameraTrigger room in CameraTrigger.All)
        {
            int c = RouteEscapes(from, room);
            if (c > count) { count = c; best = room; }
        }
        return best;
    }

    public static int TotalEscapes
    {
        get
        {
            int total = 0;
            foreach (int c in doorEscapes.Values) total += c;
            foreach (int c in hideEscapes.Values) total += c;
            foreach (Dictionary<string, int> next in routeEscapes.Values) foreach (int c in next.Values) total += c;
            return total;
        }
    }

    // ---------------- 초기화 ----------------

    public static void Clear()
    {
        roomSightings.Clear();
        doorEscapes.Clear();
        hideEscapes.Clear();
        routeEscapes.Clear();
    }

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Clear();
}
