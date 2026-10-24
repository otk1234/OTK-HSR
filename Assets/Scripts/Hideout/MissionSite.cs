using UnityEngine;

// 출발했을 때 남매가 도착하는 임무 장소 (지금은 아지트 씬 안의 임시 방, 진짜 스테이지 씬은 스테이지 전환 때).
// spawnPoints[0] = 먼저 도착하는 자리, [1] = 두 번째 (둘이 같이 가면 나란히)
public class MissionSite : MonoBehaviour
{
    public string missionId;
    public Transform[] spawnPoints = new Transform[0];
}
