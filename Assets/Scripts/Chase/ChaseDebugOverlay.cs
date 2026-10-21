using System.Text;
using UnityEngine;

// 추격 테스트 씬 전용 개발용 표시(게임 UI 아님): 게임 상태, 캐릭터 상태·스태미나·위치, 추격자 상태, 안내 문구.
public class ChaseDebugOverlay : MonoBehaviour
{
    private string prompt = "";
    private string message = "";
    private float messageTime;
    private GUIStyle style;
    private GUIStyle captionStyle;

    // 시연 자막 (에디터 시연 스크립트가 씀). 씬이 다시 시작돼도 남아 있도록 static.
    public static string Caption = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Caption = "";

    void OnEnable()
    {
        GameMessage.PromptChanged += OnPrompt;
        GameMessage.Shown += OnMessage;
    }

    void OnDisable()
    {
        GameMessage.PromptChanged -= OnPrompt;
        GameMessage.Shown -= OnMessage;
    }

    // 안내 문구는 매 프레임 다시 들어오므로, 끊기면(대상에서 멀어지면) 지웁니다.
    private int promptFrame;

    void OnPrompt(string text) { prompt = text; promptFrame = Time.frameCount; }
    void OnMessage(string text, Sprite _) { message = text; messageTime = Time.time; }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            style.normal.textColor = Color.white;
        }

        var text = new StringBuilder();
        bool chase = GameStateManager.IsChase;
        text.AppendLine(chase ? "<color=#ff5555><b>■ 추격 상태</b></color>" : "<color=#88ddff><b>■ 추리 상태</b></color>");

        foreach (CharacterStatus character in CharacterStatus.All)
        {
            var stamina = character.GetComponent<PlayerStamina>();
            CameraTrigger room = character.CurrentRoom;
            string where = room != null ? $"{room.buildingId}/{room.floor}층/{room.roomName}" : "(구역 밖)";
            string staminaText = stamina != null ? $"  스태미나 {stamina.Current:0}{(stamina.Exhausted ? " (숨참)" : "")}" : "";
            string active = character.IsActive ? "▶ " : "   ";
            string targeted = GameStateManager.instance != null && GameStateManager.instance.IsTargeted(character) ? " <color=#ff8888>[추격 대상]</color>" : "";
            text.AppendLine($"{active}{character.name}: {character.State}{staminaText}  @ {where}{targeted}");
        }

        foreach (ChaserSenses senses in ChaserSenses.All)
        {
            var brain = senses.GetComponent<IChaserBrain>();
            float awareness = 0f;
            foreach (CharacterStatus character in CharacterStatus.All) awareness = Mathf.Max(awareness, senses.Awareness(character));
            text.AppendLine($"   {senses.name}: {(brain != null ? brain.StateLabel : "-")}  인지도 {awareness:P0}");
        }

        text.AppendLine();
        text.AppendLine("<color=#bbbbbb>WASD 이동  Shift 달리기  E 상호작용·숨기  F 문 잠금  Tab 캐릭터 전환</color>");
        if (!string.IsNullOrEmpty(prompt) && Time.frameCount - promptFrame <= 2) text.AppendLine("<color=#ffd966>" + prompt + "</color>");
        if (Time.time - messageTime < 3f) text.AppendLine(message);

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(8, 8, 620, 190), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, 610, 186), text.ToString(), style);

        if (!string.IsNullOrEmpty(Caption))
        {
            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter };
                captionStyle.normal.textColor = Color.white;
            }
            var box = new Rect(Screen.width * 0.5f - 420, Screen.height - 130, 840, 110);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 12, box.y + 6, box.width - 24, box.height - 12), Caption, captionStyle);
        }
    }
}
