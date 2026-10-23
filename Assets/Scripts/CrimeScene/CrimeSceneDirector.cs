using System.Text;
using NarrativeGraybox.Interaction;
using UnityEngine;

// 범행 현장 진행: 조사 화면에서 살펴본 단서를 사건 파일에 기록하고, 개발용 표시(게임 UI 아님)를 그립니다.
// 다음 단계에서 추리·개입이 여기에 붙습니다.
public class CrimeSceneDirector : MonoBehaviour
{
    public CrimeSceneTimer timer;
    public DeductionBoard board;

    [Header("사건 이름 (임시)")]
    public string caseTitle = "302호 살인 사건 (임시)";

    private GUIStyle style;

    public static PlayerInteractor3D ActiveInspector()
    {
        if (CharacterManager.instance == null) return null;
        GameObject active = CharacterManager.instance.GetActivePlayer();
        return active != null ? active.GetComponent<PlayerInteractor3D>() : null;
    }

    void Update()
    {
        PlayerInteractor3D inspector = ActiveInspector();
        if (inspector == null || inspector.Selected == null) return;
        if (!inspector.Selected.TryGetComponent(out Clue clue)) return;
        CaseFile.Add(clue.clueId, clue.Title, inspector.GetComponent<PlayerInteraction>().myType);
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            style.normal.textColor = Color.white;
        }

        var text = new StringBuilder();
        text.AppendLine($"<b>[개발용] {caseTitle}</b>");
        if (timer != null)
        {
            int seconds = Mathf.CeilToInt(timer.Remaining);
            text.AppendLine(timer.Expired ? "<color=#ff6666>제한 시간 종료</color>" : $"남은 시간 {seconds / 60}:{seconds % 60:00}");
        }
        text.AppendLine($"죄책감 {GuiltSystem.Value} ({GuiltSystem.TierName})");
        text.AppendLine($"단서 {CaseFile.Entries.Count}개   <color=#bbbbbb>[J] 추리 창</color>");
        foreach (CaseFile.Entry entry in CaseFile.Entries) text.AppendLine($" · {entry.title}");

        // 개입: 정한 방향 → 한 일
        string caseId = board != null && board.deduction != null ? board.deduction.caseId : "";
        int extra = 0;
        if (CaseState.TryGetDirection(caseId, out InterventionKind direction))
        {
            string done = "";
            foreach (CaseState.InterventionRecord r in CaseState.Interventions)
                if (r.caseId == caseId) done = " (완료)";
            text.AppendLine($"개입 방향: {Intervention.KindName(direction)}{done}");
            extra++;
        }
        foreach (CharacterInteractable work in FindObjectsByType<CharacterInteractable>())
            if (work.Busy) { text.AppendLine($"<color=#ffd966>{work.displayName} {work.ActionName}... {work.Progress:P0}</color>"); extra++; }

        var box = new Rect(Screen.width - 268, 150, 256, 96 + (CaseFile.Entries.Count + extra) * 17);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 8, box.y + 4, box.width - 16, box.height - 8), text.ToString(), style);

        // 시연 자막 (에디터 시연 스크립트가 씀). 화면 위쪽 가운데, 조작 안내 아래
        if (!string.IsNullOrEmpty(ChaseDebugOverlay.Caption))
        {
            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter };
                captionStyle.normal.textColor = Color.white;
            }
            var caption = new Rect(Screen.width * 0.5f - 400, 62, 800, 92);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(caption, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(caption.x + 12, caption.y + 6, caption.width - 24, caption.height - 12), ChaseDebugOverlay.Caption, captionStyle);
        }
    }

    private GUIStyle captionStyle;
}
