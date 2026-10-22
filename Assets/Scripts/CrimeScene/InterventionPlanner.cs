using System.Text;
using UnityEngine;

// 개입 방향 정하기 (최소 구현, 개발용 화면): 추리를 마치고 추리 창을 닫으면 잠시 뒤 남매의 대화가 열림.
//   오빠: "그래서, 이번 사건은 어떻게 할 거야?"  →  여동생: 1. 종용  2. 가담  3. 막기
// 정한 방향은 바꿀 수 없고, 그 종류의 개입 지점만 쓸 수 있게 됨 (Intervention.Available).
// 대사 연출·화면은 나중에 (서사가 필요한 사건은 기획이 대사를 씀).
public class InterventionPlanner : MonoBehaviour
{
    public DeductionBoard board;

    [Header("추리 창을 닫고 대화가 열리기까지 (초)")]
    public float openDelay = 1.5f;

    [Header("대사 (임시)")]
    [TextArea] public string brotherLine = "그래서, 이번 사건은 어떻게 할 거야?";
    [TextArea] public string[] sisterLines =
    {
        "아빠가 원하는 방향으로 판을 깔아 주자. (종용)",
        "아빠를 돕자. 현장을 정리해서 흔적을 지우자. (가담)",
        "다음 범행을 막자. 박 선생에게 알려야 해. (막기)",
    };

    public bool IsOpen { get; private set; }

    private float readySince = -1f;
    private GameObject lockedPlayer;
    private GUIStyle style;

    string CaseId => board != null && board.deduction != null ? board.deduction.caseId : "";
    bool Decided => CaseState.TryGetDirection(CaseId, out _);

    void Update()
    {
        if (IsOpen)
        {
            for (int i = 0; i < 3; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Choose((InterventionKind)i);
            return;
        }

        // 추리를 다 마쳤고 추리 창이 닫혀 있으면 잠시 뒤 대화 시작
        bool ready = board != null && !board.IsOpen && board.CurrentQuestion == null && !Decided
                     && (FadeManager.instance == null || !FadeManager.instance.isFading);
        if (!ready) { readySince = -1f; return; }
        if (readySince < 0f) readySince = Time.time;
        if (Time.time - readySince >= openDelay) Open();
    }

    public void Open()
    {
        if (IsOpen || Decided || CharacterManager.instance == null) return;
        IsOpen = true;
        lockedPlayer = CharacterManager.instance.GetActivePlayer();
        SetLock(lockedPlayer, true);
        Debug.Log("[개입] 오빠: " + brotherLine);
    }

    public bool Choose(InterventionKind kind)
    {
        if (!IsOpen || !CaseState.SetDirection(CaseId, kind)) return false;
        Debug.Log("[개입] 여동생: " + sisterLines[(int)kind]);
        IsOpen = false;
        SetLock(lockedPlayer, false);
        lockedPlayer = null;
        return true;
    }

    static void SetLock(GameObject player, bool locked)
    {
        if (player == null) return;
        player.GetComponent<PlayerMovement>().Locks.Set(LockReason.Deduction, locked);
        player.GetComponent<PlayerInteraction>().Locks.Set(LockReason.Deduction, locked);
        var inspector = player.GetComponent<NarrativeGraybox.Interaction.PlayerInteractor3D>();
        if (inspector != null) inspector.enabled = !locked && CharacterManager.instance.GetActivePlayer() == player;
        CharacterManager.instance.SwitchLocks.Set(LockReason.Deduction, locked);
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 17, richText = true, wordWrap = true };
            style.normal.textColor = Color.white;
        }

        var text = new StringBuilder();
        text.AppendLine("<b>[개발용 대화] 개입 방향 정하기</b>   <color=#aaaaaa>(한 번 정하면 바꿀 수 없음)</color>");
        text.AppendLine();
        text.AppendLine($"<color=#8fc8ff><b>오빠</b></color>  {brotherLine}");
        text.AppendLine();
        text.AppendLine("<color=#ffd27a><b>여동생</b></color>");
        for (int i = 0; i < sisterLines.Length; i++) text.AppendLine($"  [{i + 1}] {sisterLines[i]}");

        var box = new Rect(Screen.width * 0.5f - 380, Screen.height * 0.5f - 150, 760, 250);
        GUI.color = new Color(0.03f, 0.03f, 0.05f, 0.94f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 18, box.y + 12, box.width - 36, box.height - 24), text.ToString(), style);
    }
}
