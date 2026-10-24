using System.Collections;
using UnityEngine;

// 아지트 매트리스 = 휴식. 임무 → 아지트 → 휴식 → 임무 선택 (임무에서 막 돌아오면 휴식해야 지도 테이블에서 출발, 사용자 결정 2026-10-24).
// E → "휴식할까?" (E 예 / Q 아니오) → 페이드 → 남매가 새 자리로 흩어짐 → 짧은 기본 독백.
// 휴식 때마다 스토리 진행도에 맞는 대화가 나올 예정 (스테이지가 생기면). 날짜 넘어가기도 나중에.
public class RestSpot : CharacterInteractable
{
    [Header("휴식 뒤 독백 (조작 중인 캐릭터가 말함) — 임무에서 돌아와 쉰 뒤 / 그냥 쉰 뒤")]
    [TextArea] public string afterMissionLine = "잠깐이라도 눈을 붙였다. 머리가 조금 맑아졌다.";
    [TextArea] public string idleRestLine = "피곤하진 않았지만... 잠깐 누워 있었다.";

    public bool Confirming { get; private set; }

    public override bool Available => !Busy && !HideoutTalk.Playing && !GameSession.Deployed;
    protected override string ActionLabel => "휴식하기";   // 안내: "[E] 매트리스에서 휴식하기" (displayName = "매트리스에서")
    protected override float WorkTime => 0.01f;

    private GUIStyle style;

    void Start() => noiseRadius = 0f;

    protected override void OnWorkDone(PlayerInteraction user)
    {
        Busy = true;   // 확인·휴식 동안 E를 이 자리가 맡음
        Confirming = true;
        HideoutTalk.Lock(true);
    }

    void Update()
    {
        if (!Confirming) return;
        if (Input.GetKeyDown(KeyCode.E)) Confirm(true);
        else if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape)) Confirm(false);
    }

    // 점검에서도 씀
    public void Confirm(bool rest)
    {
        if (!Confirming) return;
        Confirming = false;
        if (rest) StartCoroutine(Rest());
        else
        {
            HideoutTalk.Lock(false);
            Busy = false;
        }
    }

    IEnumerator Rest()
    {
        bool afterMission = GameSession.NeedsRest;
        FadeManager fade = FadeManager.instance;
        if (fade != null) { fade.isFading = true; yield return fade.FadeOut(); }

        GameSession.NeedsRest = false;
        if (HideoutResidents.instance != null)
        {
            HideoutResidents.instance.Shuffle();
            HideoutResidents.instance.PlaceIdle();
        }
        Debug.Log("[휴식] 쉬었다" + (afterMission ? " — 다시 출발할 수 있음" : ""));
        yield return new WaitForSeconds(0.4f);

        if (fade != null) { yield return fade.FadeIn(); fade.isFading = false; }
        HideoutTalk.Lock(false);   // 독백은 HideoutTalk.Play가 다시 잠금
        CharacterType who = CharacterRoster.TypeOf(CharacterManager.instance.GetActivePlayer());
        yield return HideoutTalk.Play(new[] { new TalkLine { speaker = who, text = afterMission ? afterMissionLine : idleRestLine } });
        Busy = false;
    }

    void OnGUI()
    {
        if (!Confirming) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
        }
        var rect = new Rect((Screen.width - 380f) * 0.5f, Screen.height * 0.38f, 380f, 110f);
        Color previous = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
        string hint = GameSession.NeedsRest ? "\n<size=13><color=#bbbbbb>임무에서 막 돌아왔다</color></size>" : "";
        GUI.Label(rect, $"<b>[개발용] 휴식할까?</b>{hint}\n\n<color=#bbbbbb>E 예   Q 아니오</color>", style);
    }
}
