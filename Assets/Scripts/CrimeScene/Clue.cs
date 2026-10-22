using System.Collections.Generic;
using NarrativeGraybox.Interaction;
using UnityEngine;

// 팀원 조사 항목(InspectionItem)에 붙이는 단서 표시. 팀원 코드는 고치지 않습니다.
//  - 조사 화면에서 이 항목을 클릭해 살펴보면 사건 파일에 기록 (CrimeSceneDirector가 확인)
//  - visibleTo: 특정 캐릭터에게만 보이는 숨겨진 단서 (예: 여동생만 찾는 단서). 다른 캐릭터에게는 모양·클릭 판정이 꺼짐
[RequireComponent(typeof(InspectionItem))]
public class Clue : MonoBehaviour
{
    [Header("단서 ID (추리 데이터에서 이 ID로 찾음)")]
    public string clueId;

    [Header("이 캐릭터에게만 보임 (All = 모두)")]
    public CharacterType visibleTo = CharacterType.All;

    private InspectionItem item;
    private readonly List<Renderer> shapes = new List<Renderer>();
    private Collider[] colliders;
    private bool visible = true;

    public string Title => item != null ? item.Title : GetComponent<InspectionItem>().Title;

    void Awake()
    {
        item = GetComponent<InspectionItem>();
        // 처음부터 켜져 있는 렌더러만 모양 (꺼져 있는 외곽선은 InspectionItem이 마우스를 올릴 때 켬)
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) shapes.Add(r);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    void LateUpdate()
    {
        if (visibleTo == CharacterType.All || CharacterManager.instance == null) return;
        GameObject active = CharacterManager.instance.GetActivePlayer();
        bool show = active != null && active.GetComponent<PlayerInteraction>().myType == visibleTo;
        if (show == visible) return;
        visible = show;
        foreach (Renderer r in shapes) r.enabled = show;
        foreach (Collider c in colliders) c.enabled = show;
        if (!show) item.SetHover(false);
    }

    public bool IsVisibleTo(CharacterType type) => visibleTo == CharacterType.All || visibleTo == type;
}
