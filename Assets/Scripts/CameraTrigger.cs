using System.Collections.Generic;
using UnityEngine;

public class CameraTrigger : MonoBehaviour
{
    [Header("이 구역의 가상 카메라")]
    public GameObject virtualCamera;

    [Header("구역 이름 (HUD 위치 표시용)")]
    public string roomName;

    [Header("위치 정보 (추격 대상 판단용)")]
    public string buildingId = "House";
    public int floor = 0;

    // 씬에 있는 모든 구역 (위치 조회용)
    public static readonly List<CameraTrigger> All = new List<CameraTrigger>();

    private List<GameObject> playersInRoom = new List<GameObject>();
    private Collider area;

    public bool Contains(GameObject player) => playersInRoom.Contains(player);

    void Awake() { area = GetComponent<Collider>(); }
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !playersInRoom.Contains(other.gameObject))
        {
            playersInRoom.Add(other.gameObject);

            if (CharacterManager.instance.GetActivePlayer() == other.gameObject)
            {
                // 추격 중에는 페이드 없이 바로 컷
                if (GameStateManager.InstantCameraCuts) virtualCamera.SetActive(true);
                else FadeManager.instance.DoFadeTransition(virtualCamera);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && playersInRoom.Contains(other.gameObject))
        {
            playersInRoom.Remove(other.gameObject);
            // 조작 중인 캐릭터가 아직 이 구역에 있으면 카메라를 끄지 않습니다.
            UpdateCameraStateInstant();
        }
    }


    public void UpdateCameraStateInstant()
    {
        bool isActivePlayerHere = playersInRoom.Contains(CharacterManager.instance.GetActivePlayer());
        virtualCamera.SetActive(isActivePlayerHere);
    }

    // ---------------- 위치 조회 ----------------

    public float Volume
    {
        get
        {
            Vector3 size = (area != null ? area : GetComponent<Collider>()).bounds.size;
            return size.x * size.y * size.z;
        }
    }

    public bool ContainsPoint(Vector3 position)
    {
        Bounds bounds = (area != null ? area : GetComponent<Collider>()).bounds;
        return position.x >= bounds.min.x && position.x <= bounds.max.x
            && position.z >= bounds.min.z && position.z <= bounds.max.z
            && position.y >= bounds.min.y - 1f && position.y <= bounds.max.y + 1f;
    }

    // 이 위치가 속한 구역. 겹치면(복도 안의 손님방, 집을 감싼 마당 등) 더 작은 구역을 고릅니다. 없으면 null.
    public static CameraTrigger FindAt(Vector3 position)
    {
        CameraTrigger best = null;
        foreach (CameraTrigger room in All)
            if (room.ContainsPoint(position) && (best == null || room.Volume < best.Volume)) best = room;
        return best;
    }

    public static bool SameRoom(Vector3 a, Vector3 b)
    {
        CameraTrigger roomA = FindAt(a);
        return roomA != null && roomA == FindAt(b);
    }

    public static bool SameBuilding(Vector3 a, Vector3 b)
    {
        CameraTrigger roomA = FindAt(a), roomB = FindAt(b);
        string buildingA = roomA != null ? roomA.buildingId : "";
        string buildingB = roomB != null ? roomB.buildingId : "";
        return buildingA == buildingB;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
