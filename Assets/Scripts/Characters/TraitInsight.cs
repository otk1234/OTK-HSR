using NarrativeGraybox.Interaction;
using UnityEngine;

// 특성 전용 추가 정보 (여동생의 통찰·화술 등). 조사 항목(InspectionItem)이나 NPC(DummyNpc)에 붙입니다.
//  - 그 특성이 있는 캐릭터가 조작 중이면 원래 문장 뒤에 "[통찰] …" / "[화술] …" 문장이 붙고, 없으면 원래 문장(누구나 보는 사실)만 보임
//  - 근거 ID가 있으면: 그 특성이 있는 캐릭터가 이 조사 항목을 살펴볼 때 사건 파일에 근거로 기록 (특성으로만 얻는 추리 근거)
//  - 팀원 코드는 고치지 않고 글자만 바꿈 (Clue와 같은 방식). 새 분야로 넓힐 때도 이 컴포넌트를 붙이면 됨
public class TraitInsight : MonoBehaviour
{
    [Header("이 특성이 있는 캐릭터에게만")]
    public CharacterTrait requiredTrait = CharacterTrait.Insight;

    [Header("문장 앞 표시 ([통찰] / [화술])")]
    public string label = "통찰";

    [TextArea] public string extraText;

    [Header("(조사 항목만) 살펴보면 기록할 추리 근거 — 비우면 문장만")]
    public string evidenceId;
    public string evidenceTitle;

    private InspectionItem item;
    private DummyNpc npc;
    private string baseText;

    void Awake()
    {
        item = GetComponent<InspectionItem>();
        npc = GetComponent<DummyNpc>();
        baseText = item != null ? item.Description : npc != null ? npc.Line : "";
    }

    public bool AvailableTo(CharacterType who) => CharacterRoster.Has(who, requiredTrait);

    public string TextFor(CharacterType who) =>
        AvailableTo(who) && !string.IsNullOrEmpty(extraText) ? $"{baseText}\n[{label}] {extraText}" : baseText;

    void LateUpdate()
    {
        if (CharacterManager.instance == null) return;
        GameObject active = CharacterManager.instance.GetActivePlayer();
        if (active == null) return;
        CharacterType who = CharacterRoster.TypeOf(active);

        string text = TextFor(who);
        if (item != null) item.Description = text;
        else if (npc != null) npc.Line = text;

        if (item == null || string.IsNullOrEmpty(evidenceId) || !AvailableTo(who)) return;
        var inspector = active.GetComponent<PlayerInteractor3D>();
        if (inspector != null && inspector.Selected == item) CaseFile.Add(evidenceId, evidenceTitle, who);
    }
}
