using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

public enum EntryKind { Door, Window, Vent }

[Flags]
public enum ChaserType { None = 0, Police = 1, Father = 2, All = Police | Father }

// 방과 방(또는 집 안과 밖)을 잇는 통로: 문·창문·환풍구.
// 추격자는 이 NavMeshLink를 통해 지나갑니다. 플레이어는 링크를 쓰지 않습니다(문은 직접 열고 걸어서 지나감).
// 잠긴 문이면 링크가 꺼져서 길찾기에서 빠집니다.
public class EntryPoint : MonoBehaviour
{
    public static readonly List<EntryPoint> All = new List<EntryPoint>();

    public const string DoorArea = "Door";
    public const string WindowArea = "Window";
    public const string VentArea = "Vent";

    public EntryKind kind = EntryKind.Door;
    public string displayName;
    public InteractableDoor door;   // kind == Door 일 때
    public NavMeshLink link;

    [Header("이 통로를 쓸 수 있는 추격자")]
    public ChaserType usableBy = ChaserType.All;

    [Header("지나가는 데 걸리는 시간 (문은 연 뒤 통과 시간)")]
    public float traverseTime = 0.4f;

    [Header("지나갈 때 소음 반경")]
    public float traverseNoise = 3f;

    public CameraTrigger RoomA { get; private set; }
    public CameraTrigger RoomB { get; private set; }

    public Vector3 PointA => link.transform.TransformPoint(link.startPoint);
    public Vector3 PointB => link.transform.TransformPoint(link.endPoint);

    public bool IsLocked => door != null && door.IsLocked;
    public bool IsPassable => !IsLocked;
    public bool IsUsableBy(ChaserType chaser) => (usableBy & chaser) != 0;

    void OnEnable()
    {
        All.Add(this);
        if (door != null) door.StateChanged += OnDoorChanged;
        SyncLink();
    }

    void OnDisable()
    {
        All.Remove(this);
        if (door != null) door.StateChanged -= OnDoorChanged;
    }

    void Start()
    {
        RoomA = CameraTrigger.FindAt(PointA);
        RoomB = CameraTrigger.FindAt(PointB);
    }

    void OnDoorChanged(InteractableDoor _) => SyncLink();

    void SyncLink()
    {
        if (link != null) link.activated = IsPassable;
    }

    public CameraTrigger OtherSide(CameraTrigger room) => room == RoomA ? RoomB : RoomA;

    // 링크 위에 올라선 추격자가 어느 통로에 있는지 찾습니다.
    public static EntryPoint FromLink(Object owner, Vector3 position)
    {
        if (owner is Component component)
        {
            EntryPoint entry = component.GetComponentInParent<EntryPoint>();
            if (entry != null) return entry;
        }
        EntryPoint nearest = null;
        float best = float.MaxValue;
        foreach (EntryPoint entry in All)
        {
            float d = Mathf.Min((entry.PointA - position).sqrMagnitude, (entry.PointB - position).sqrMagnitude);
            if (d < best) { best = d; nearest = entry; }
        }
        return nearest;
    }

    // 이 추격자가 쓸 수 있는 NavMesh 구역 (창문·환풍구는 아버지만)
    public static int AreaMaskFor(ChaserType chaser)
    {
        int mask = NavMesh.AllAreas;
        if ((chaser & ChaserType.Father) == 0)
        {
            mask &= ~AreaBit(WindowArea);
            mask &= ~AreaBit(VentArea);
        }
        return mask;
    }

    static int AreaBit(string area)
    {
        int index = NavMesh.GetAreaFromName(area);
        return index < 0 ? 0 : 1 << index;
    }

    // from → to 사이를 막고 있는 잠긴 문을 찾습니다(없으면 null).
    // 잠긴 문 링크를 잠깐 켜서 길을 계산한 뒤, 그 길이 지나가는 잠긴 문을 돌려줍니다.
    public static EntryPoint FindBlockingDoor(Vector3 from, Vector3 to, int areaMask)
    {
        var path = new NavMeshPath();
        NavMesh.CalculatePath(from, to, areaMask, path);
        if (path.status == NavMeshPathStatus.PathComplete) return null;

        var locked = new List<EntryPoint>();
        foreach (EntryPoint entry in All)
            if (entry.IsLocked && entry.link != null) { locked.Add(entry); entry.link.activated = true; }
        try
        {
            NavMesh.CalculatePath(from, to, areaMask, path);
            if (path.status != NavMeshPathStatus.PathComplete) return null;
            Vector3[] corners = path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
                foreach (EntryPoint entry in locked)
                    if (IsLinkSegment(corners[i], corners[i + 1], entry)) return entry;
            return null;
        }
        finally
        {
            foreach (EntryPoint entry in locked) entry.link.activated = false;
        }
    }

    static bool IsLinkSegment(Vector3 a, Vector3 b, EntryPoint entry)
    {
        const float tolerance = 0.6f;
        bool forward = Flat(a - entry.PointA) < tolerance && Flat(b - entry.PointB) < tolerance;
        bool backward = Flat(a - entry.PointB) < tolerance && Flat(b - entry.PointA) < tolerance;
        return forward || backward;
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    void OnDrawGizmos()
    {
        if (link == null) return;
        Gizmos.color = kind == EntryKind.Door ? (IsLocked ? Color.red : new Color(1f, 0.7f, 0.2f))
                     : kind == EntryKind.Window ? Color.cyan : new Color(0.6f, 0.4f, 1f);
        Gizmos.DrawLine(PointA + Vector3.up * 0.1f, PointB + Vector3.up * 0.1f);
        Gizmos.DrawWireSphere(PointA, 0.12f);
        Gizmos.DrawWireSphere(PointB, 0.12f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
