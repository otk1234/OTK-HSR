using System.Collections.Generic;
using UnityEngine;

// 아버지 AI의 기억. 잡혀서 씬이 다시 시작돼도 남도록 static으로 보관하고, 플레이를 새로 시작할 때 비웁니다.
// 씬을 다시 불러오면 오브젝트가 새로 만들어지므로 키는 '씬 이름/오브젝트 이름'.
//  - 자주 보인 장소: 방마다 캐릭터를 본 횟수 (배회할 방을 고를 때 가중치)
public static class FatherMemory
{
    static readonly Dictionary<string, int> roomSightings = new Dictionary<string, int>();

    public static string Key(Component component) =>
        component == null ? "" : component.gameObject.scene.name + "/" + component.name;

    // ---------------- 자주 보인 장소 ----------------

    public static void RecordSighting(CameraTrigger room)
    {
        if (room == null) return;
        string key = Key(room);
        roomSightings[key] = Sightings(room) + 1;
    }

    public static int Sightings(CameraTrigger room) =>
        room != null && roomSightings.TryGetValue(Key(room), out int count) ? count : 0;

    // ---------------- 초기화 ----------------

    public static void Clear()
    {
        roomSightings.Clear();
    }

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Clear();
}
