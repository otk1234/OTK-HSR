using System.Collections;
using UnityEngine;

// 무거운 물건 (책장·냉장고 …): 조건에 맞는 캐릭터(기본: 오빠)가 E로 밀어 옆으로 치웁니다.
// 치우고 나면 뒤에 가려져 있던 오브젝트(조사 지점 등)를 켭니다.
// 길을 막는 부분은 자식의 NavMeshObstacle(Carve)이라, 옮기면 길도 같이 바뀝니다.
public class HeavyObject : CharacterInteractable
{
    [Header("밀면 움직이는 거리·방향 (월드 기준)")]
    public Vector3 pushOffset = new Vector3(1.2f, 0f, 0f);

    [Header("미는 데 걸리는 시간 (초)")]
    public float pushTime = 1.5f;

    [Header("다 밀면 켜지는 오브젝트")]
    public GameObject[] revealWhenMoved;

    public bool Moved { get; private set; }

    public override bool Available => !Moved;
    protected override string ActionLabel => "밀기";
    protected override float WorkTime => pushTime;

    protected override IEnumerator WorkRoutine(float duration)
    {
        Vector3 from = transform.position, to = from + pushOffset;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        transform.position = to;
    }

    protected override void OnWorkDone(PlayerInteraction user)
    {
        Moved = true;
        Reveal(revealWhenMoved);
        Debug.Log($"[범행 현장] {displayName}을(를) 밀어 치움 ({user.myType})");
        GameMessage.Show($"{displayName}을(를) 밀어냈다.");
    }
}
