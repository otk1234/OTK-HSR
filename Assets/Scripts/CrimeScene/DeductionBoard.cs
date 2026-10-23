using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 추리 창 (최소 구현, 개발용 화면). J로 열고 닫음, 제한 시간이 끝나면 자동으로 열림.
// 답하지 않은 첫 질문을 보여 주고 숫자 키로 고름. 근거 단서가 없는 선택지는 고를 수 없음.
// 선택지는 누가 추리하든 같음 (남매 공용 사건 파일 기준). 답한 사람은 기록만 함. 정답·오답은 화면에 보이지 않음.
// 실제 추리 화면은 UI 담당에게 넘길 부분 → 여기선 데이터·규칙만 확인할 수 있으면 됨.
public class DeductionBoard : MonoBehaviour
{
    public CaseDeduction deduction;
    public CrimeSceneTimer timer;

    [Header("열기 / 닫기 키")]
    public KeyCode toggleKey = KeyCode.J;

    public bool IsOpen { get; private set; }

    private GameObject lockedPlayer;
    private GUIStyle style;

    void OnEnable()
    {
        if (timer != null) timer.TimeUp += OnTimeUp;
    }

    void OnDisable()
    {
        if (timer != null) timer.TimeUp -= OnTimeUp;
    }

    void OnTimeUp() => Open();

    static CharacterType ActiveType()
    {
        GameObject active = CharacterManager.instance != null ? CharacterManager.instance.GetActivePlayer() : null;
        return active != null ? active.GetComponent<PlayerInteraction>().myType : CharacterType.All;
    }

    public CaseDeduction.Question CurrentQuestion
    {
        get
        {
            if (deduction == null || deduction.questions == null) return null;
            foreach (CaseDeduction.Question q in deduction.questions)
                if (!CaseState.HasAnswered(deduction.caseId, q.id)) return q;
            return null;
        }
    }

    // 선택지는 누가 추리하든 같음 (남매 공용 사건 파일 기준으로 잠금만 다름)
    public List<CaseDeduction.Option> VisibleOptions(CaseDeduction.Question question)
    {
        var list = new List<CaseDeduction.Option>();
        if (question != null) list.AddRange(question.options);
        return list;
    }

    // 지금 질문의 index번(0부터) 선택지로 답함. 잠긴 선택지면 false
    public bool Choose(int index)
    {
        CaseDeduction.Question question = CurrentQuestion;
        CharacterType who = ActiveType();
        List<CaseDeduction.Option> options = VisibleOptions(question);
        if (question == null || index < 0 || index >= options.Count || !options[index].Unlocked) return false;
        return CaseState.Record(deduction.caseId, question, options[index], who);
    }

    public void Open()
    {
        if (IsOpen || CharacterManager.instance == null) return;
        PlayerInteractor3DGuard();
        IsOpen = true;
        lockedPlayer = CharacterManager.instance.GetActivePlayer();
        SetLock(lockedPlayer, true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SetLock(lockedPlayer, false);
        lockedPlayer = null;
    }

    // 조사 중이면 조사부터 닫음 (조사 화면과 추리 창이 겹치지 않게)
    void PlayerInteractor3DGuard()
    {
        var inspector = CrimeSceneDirector.ActiveInspector();
        if (inspector == null || !inspector.IsInspecting) return;
        inspector.Back();
        if (inspector.IsInspecting) inspector.Back();
    }

    // 추리 창이 열려 있는 동안 이동·상호작용·조사·캐릭터 전환을 막음
    static void SetLock(GameObject player, bool locked)
    {
        if (player == null) return;
        player.GetComponent<PlayerMovement>().Locks.Set(LockReason.Deduction, locked);
        player.GetComponent<PlayerInteraction>().Locks.Set(LockReason.Deduction, locked);
        var inspector = player.GetComponent<NarrativeGraybox.Interaction.PlayerInteractor3D>();
        if (inspector != null) inspector.enabled = !locked && CharacterManager.instance.GetActivePlayer() == player;
        CharacterManager.instance.SwitchLocks.Set(LockReason.Deduction, locked);
    }

    void Update()
    {
        if (!IsOpen)
        {
            if (Input.GetKeyDown(toggleKey) && (FadeManager.instance == null || !FadeManager.instance.isFading)) Open();
            return;
        }
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }
        for (int i = 0; i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Choose(i);
    }

    void OnGUI()
    {
        if (!IsOpen || deduction == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
            style.normal.textColor = Color.white;
        }

        var box = new Rect(Screen.width * 0.5f - 380, Screen.height * 0.5f - 170, 760, 300);
        GUI.color = new Color(0.03f, 0.03f, 0.05f, 0.94f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 18, box.y + 12, box.width - 36, box.height - 24), ScreenText(), style);
    }

    // 추리 창에 보이는 글자 (점검에서도 읽음)
    public string ScreenText()
    {
        var text = new StringBuilder();
        text.AppendLine($"<b>[개발용 추리 창] {deduction.caseTitle}</b>");
        text.AppendLine();
        CaseDeduction.Question question = CurrentQuestion;
        if (question != null)
        {
            text.AppendLine($"<b>{question.text}</b>   <color=#aaaaaa>(한 번 답하면 바꿀 수 없음)</color>");
            List<CaseDeduction.Option> options = VisibleOptions(question);
            for (int i = 0; i < options.Count; i++)
                text.AppendLine(options[i].Unlocked ? $"  [{i + 1}] {options[i].text}" : $"  <color=#777777>[{i + 1}] ??? (근거 단서 부족)</color>");
        }
        else
        {
            // 정답·오답은 보여 주지 않음 (스토리 몰입, 사용자 결정 2026-10-23) — 고른 답만. 결과는 CaseState·콘솔 로그에
            text.AppendLine("<b>추리 완료</b>");
            foreach (CaseState.Answer a in CaseState.Answers)
                if (a.caseId == deduction.caseId) text.AppendLine($"  · {a.text}");
        }
        text.AppendLine();
        text.AppendLine("<color=#bbbbbb>숫자 키 선택   J / Q 닫기</color>");
        return text.ToString();
    }
}
