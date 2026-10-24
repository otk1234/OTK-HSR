using UnityEngine;

public enum InterventionKind { Incite, Abet, Prevent } // 종용 · 가담 · 막기

// 범행 현장의 개입 지점. 이번 사건의 방향(InterventionPlanner에서 남매가 정함)과 같은 종류만 쓸 수 있고, 사건마다 한 번.
// 세 방향의 차이 (사용자 결정 2026-10-24):
//  - 종용 = 아버지의 '판단'을 조종 (정보를 흘려 다음 표적을 바꿈). 범행을 없애지 않고 바꿀 뿐 → nextTarget
//  - 가담 = 아버지의 '범행'을 도움 (도구, 현장 정리) / 막기 = 범행의 '조건'을 무너뜨림 (피해자 경고, 수단 제거)
// 누구나 할 수 있지만 걸리는 시간은 캐릭터 특성에 따라 다름 (예: 시체 치우기는 힘 특성(오빠)이 빠르고 여동생은 오래 걸림).
// 끝나면 죄책감 기록 + CaseState에 개입 기록 + 지정한 오브젝트를 숨김(치운 시체 등).
public class Intervention : CharacterInteractable
{
    [Header("개입 종류")]
    public InterventionKind kind = InterventionKind.Prevent;

    [Header("사건 ID (CaseDeduction.caseId와 같게)")]
    public string caseId = "case302";

    [Header("동작 이름 (안내 문구: [E] 표시 이름 + 동작 이름)")]
    public string actionLabel = "하기";

    [Header("무슨 일을 했는지 (기록·로그용)")]
    [TextArea] public string summary;

    [Header("걸리는 시간 (초): 이 특성이 있으면 skilledTime, 없으면 otherTime")]
    public CharacterTrait skilledTrait = CharacterTrait.None;
    public float skilledTime = 3f;
    public float otherTime = 3f;

    [Header("(종용) 아버지의 다음 표적을 이것으로 바꿈 — 비우면 그대로")]
    public string nextTarget;

    [Header("죄책감 행동 ID (GuiltActions)")]
    public string guiltActionId;

    [Header("끝나면 숨길 오브젝트")]
    public GameObject[] hideWhenDone;

    [Header("끝난 뒤 메시지")]
    [TextArea] public string doneMessage;

    public bool Done { get; private set; }

    public static string KindName(InterventionKind kind) =>
        kind == InterventionKind.Incite ? "종용" : kind == InterventionKind.Abet ? "가담" : "막기";

    public override bool Available =>
        !Done && CaseState.TryGetDirection(caseId, out InterventionKind direction) && direction == kind && !CaseState.HasIntervened(caseId);

    protected override string ActionLabel => actionLabel;
    protected override float WorkTime => otherTime;

    public override float WorkTimeFor(CharacterType who) =>
        skilledTrait != CharacterTrait.None && CharacterRoster.Has(who, skilledTrait) ? skilledTime : otherTime;

    protected override void OnWorkDone(PlayerInteraction user)
    {
        if (!CaseState.RecordIntervention(caseId, kind, summary, user.myType, nextTarget)) return;
        Done = true;
        if (!string.IsNullOrEmpty(guiltActionId)) GuiltSystem.Record(guiltActionId, user.myType);
        if (hideWhenDone != null)
            foreach (GameObject go in hideWhenDone)
                if (go != null) go.SetActive(false);
        if (!string.IsNullOrEmpty(doneMessage)) GameMessage.Show(doneMessage);
    }
}
