using UnityEngine;

// 지도 테이블의 장소 하나 (기획이 고치는 데이터). 핀 색은 종류와 기록에서 정함:
//  우리 집 = 노랑 / 임무 = 빨강 (그 사건 기록이 생기면 파랑) / 지난 사건 = 사건 기록이 있을 때만 파랑으로 보임
// 임무 하나 = 장소 하나. 지금은 출발하면 아지트 씬 안의 임시 장소로 이동 (진짜 스테이지 씬은 스테이지 전환 때, §10)
[CreateAssetMenu(menuName = "OTK/임무 장소", fileName = "Mission")]
public class MissionDefinition : ScriptableObject
{
    public enum Kind { Home, Mission, PastCase }

    public string id = "mission";
    public string displayName = "장소";
    public Kind kind = Kind.Mission;

    [Header("이 장소의 사건 ID (기록이 있으면 '이전 조사 지역' = 파랑)")]
    public string caseId = "";

    [TextArea(3, 6)] public string description = "";

    [Header("지도 위 위치 (-1~1, x = 동쪽, y = 북쪽)")]
    public Vector2 mapPosition;
}
