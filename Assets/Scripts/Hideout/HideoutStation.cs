using NarrativeGraybox.Interaction;
using UnityEngine;

// 아지트의 칠판·상자·작업대 공통 부분. 가구 = 팀원 조사 지점(InspectionArea): 가까이 가서 E → 시체 조사처럼 확대, Q로 돌아옴.
// 확대·이동 잠금·조사 창은 팀원 조사 시스템과 PrototypeIntegration이 맡고, 여기선 확대된 동안 개발용 창(게임 UI 아님)과 키 입력만.
// 실제 화면은 UI 담당.
[RequireComponent(typeof(InspectionArea))]
public abstract class HideoutStation : MonoBehaviour
{
    public InspectionArea Area => area != null ? area : area = GetComponent<InspectionArea>();

    // 이 가구를 조사(확대) 중인 조작 캐릭터 (없으면 null)
    public PlayerInteractor3D Inspector
    {
        get
        {
            GameObject active = CharacterManager.instance != null ? CharacterManager.instance.GetActivePlayer() : null;
            PlayerInteractor3D inspector = active != null ? active.GetComponent<PlayerInteractor3D>() : null;
            return inspector != null && inspector.CurrentArea == Area ? inspector : null;
        }
    }

    public bool IsOpen => Inspector != null;
    public CharacterType UserType => IsOpen ? CharacterRoster.TypeOf(Inspector.gameObject) : CharacterType.All;

    protected abstract string Title { get; }
    protected abstract string BuildText();           // 개발용 창 내용 (리치 텍스트)
    protected virtual void HandleInput() { }         // 확대된 동안의 키 입력
    protected virtual string Controls => "";
    protected virtual void OnOpened() { }

    private InspectionArea area;
    private bool wasOpen;
    private GUIStyle style;

    protected virtual void Update()
    {
        bool open = IsOpen;
        if (open && !wasOpen) OnOpened();
        wasOpen = open;
        if (open) HandleInput();
    }

    // 조사 화면 닫기 (선택한 사진이 있으면 그것부터)
    public void Close()
    {
        PlayerInteractor3D inspector = Inspector;
        if (inspector == null) return;
        if (inspector.Selected != null) inspector.Back();
        inspector.Back();
    }

    // true = 바이오하자드 아이템 상자처럼 화면 가운데에 반투명 창 (뒤로 확대된 가구가 비쳐 보임). 칠판은 사진을 봐야 하므로 오른쪽 위
    protected virtual bool CenteredWindow => false;

    // 가운데 창 배경: 확대된 가구가 비칠 정도로만 투명하게 (글자는 불투명)
    protected static readonly Color CenteredBackground = new Color(0.05f, 0.06f, 0.08f, 0.82f);

    void OnGUI()
    {
        if (IsOpen) DrawGui();
    }

    // 기본: 개발용 창 하나 (지도 테이블은 핀·버튼 화면으로 덮어씀)
    protected virtual void DrawGui()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 14, 12, 12) };
            style.normal.textColor = Color.white;
        }
        string text = $"<b>[개발용] {Title}</b>   <color=#999999>{CharacterRoster.NameOf(UserType)}</color>\n\n{BuildText()}" +
                      (Controls.Length > 0 ? $"\n\n<color=#bbbbbb>{Controls}</color>" : "");

        Rect rect;
        if (CenteredWindow)
        {
            style.fontSize = 16;
            float width = Mathf.Min(620f, Screen.width * 0.6f);
            // 아래쪽 팀원 조사 창(높이 175 × 화면 배율)과 겹치지 않게 그 위 공간의 가운데
            float above = Screen.height - 175f * Mathf.Max(.35f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f)) - 12f;
            float height = Mathf.Min(Mathf.Max(style.CalcHeight(new GUIContent(text), width), above * 0.55f), above - 24f);
            rect = new Rect((Screen.width - width) * 0.5f, Mathf.Max(12f, (above - height) * 0.5f), width, height);
        }
        else
        {
            // 확대된 칠판(사진)을 가리지 않게 오른쪽 위에 (아래쪽은 팀원 조사 창)
            style.fontSize = 14;
            float width = Mathf.Min(430f, Screen.width * 0.4f);
            float height = Mathf.Min(style.CalcHeight(new GUIContent(text), width), Screen.height * 0.7f);
            rect = new Rect(Screen.width - width - 16f, 16f, width, height);
        }
        Color previous = GUI.color;
        GUI.color = CenteredWindow ? CenteredBackground : new Color(0.05f, 0.06f, 0.08f, 0.9f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(rect, text, style);
    }

    // 개발용 표시용 특성 이름
    public static string TraitNames(CharacterTrait traits)
    {
        if (traits == CharacterTrait.None) return "없음";
        var names = new System.Collections.Generic.List<string>();
        if ((traits & CharacterTrait.Investigation) != 0) names.Add("관찰");
        if ((traits & CharacterTrait.Insight) != 0) names.Add("통찰");
        if ((traits & CharacterTrait.Persuasion) != 0) names.Add("화술");
        if ((traits & CharacterTrait.Strength) != 0) names.Add("힘");
        if ((traits & CharacterTrait.Mechanics) != 0) names.Add("기계 조작");
        return string.Join("·", names);
    }

    public static string RuleLabel(Inventory.ItemRule rule)
    {
        if (rule.requiredTrait != CharacterTrait.None) return $" <color=#e0c070>[{TraitNames(rule.requiredTrait)}]</color>";
        if (rule.requiredCharacter != CharacterType.All) return $" <color=#e0c070>[{CharacterRoster.NameOf(rule.requiredCharacter)} 전용]</color>";
        return "";
    }
}
