using System.Collections;
using UnityEngine;

// 아지트의 쉬는 자리 하나: 조작하지 않는 캐릭터가 여기 머묾 (자세 없이 위치·방향만).
// 여동생 = 칠판 앞·지도 테이블 옆 (통찰·추리), 오빠 = 작업대 의자·벽에 기댐·해치 옆 망보기 (사용자 예시 2026-10-24).
// 그 캐릭터가 여기 있으면 가까이 가서 E → 남매 대화. 대사는 자리마다, 사건 기록이 생기면 "사건 뒤" 대사를 먼저 (기획이 고치는 데이터).
public class HideoutSpot : CharacterInteractable
{
    [Header("이 자리의 주인")]
    public CharacterType owner = CharacterType.Player1;

    [Header("자리 설명 (개발용)")]
    public string label = "쉬는 중";

    [Header("남매 대화: 평소 / 사건 기록이 생긴 뒤 (차례로 돌아가며)")]
    public Talk[] talks = new Talk[0];
    public Talk[] talksAfterCase = new Talk[0];

    public bool Occupied { get; set; }   // 주인이 지금 여기서 쉬는 중 (HideoutResidents가 정함)

    private int nextTalk;

    public override bool Available => Occupied && !Busy && !HideoutTalk.Playing && !GameSession.Deployed;
    protected override string ActionLabel => "말 걸기";
    protected override float WorkTime => 0.01f;

    void Start()
    {
        displayName = CharacterRoster.NameOf(owner) + "에게";   // 안내: "[E] 오빠에게 말 걸기"
        noiseRadius = 0f;
    }

    // 다음에 할 대화 (점검용으로도 씀)
    public Talk NextTalk
    {
        get
        {
            Talk[] pool = HideoutTalk.AnyCaseRecorded && talksAfterCase.Length > 0 ? talksAfterCase : talks;
            return pool.Length == 0 ? null : pool[nextTalk % pool.Length];
        }
    }

    protected override void OnWorkDone(PlayerInteraction user) => StartCoroutine(TalkRoutine());

    IEnumerator TalkRoutine()
    {
        Talk talk = NextTalk;
        if (talk == null) yield break;
        nextTalk++;
        Busy = true;   // 대화 중엔 이 자리가 E를 맡음 (팀원 조사가 E를 가져가지 않게)
        yield return HideoutTalk.Play(talk.lines);
        Busy = false;
    }
}
