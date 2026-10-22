using System.Collections.Generic;
using UnityEngine;
using NarrativeGraybox.Interaction;
using NarrativeGraybox.UI;

// 기획 요구사항을 개발자에게 전달하기 위한 복제 씬 전용 더미 시스템입니다.
// 원본 Prototype 씬과 실제 저장 데이터에는 영향을 주지 않습니다.
[DefaultExecutionOrder(100)]
public sealed class PlannerReferenceDemo : MonoBehaviour
{
    private const string EmptyHand = "비어 있음";

    private sealed class CharacterUiState
    {
        public string leftHand = EmptyHand;
        public string rightHand = EmptyHand;
        public bool leftIsNewer; // 양손이 찼을 때 먼저 든 쪽을 바꾸기 위한 순서
        public int selectedSlot;
        public bool selecting;
    }

    [Header("왼쪽 상단 시간 · 장소 (플레이 중 바꾸면 바로 보임)")]
    [SerializeField] private int day = 1;
    [SerializeField, Range(0, 23)] private int hour = 21;
    [SerializeField, Range(0, 59)] private int minute = 40;
    [SerializeField] private Vector2 clockPosition = new Vector2(20, 20);
    [SerializeField] private Vector2 clockSize = new Vector2(240, 92);
    [SerializeField, Range(18, 48)] private int clockFontSize = 32;
    [SerializeField] private Color clockPanelColor = new Color(.045f, .055f, .075f, .82f);
    [SerializeField] private Color clockAccentColor = new Color(1f, .74f, .3f);
    [SerializeField] private Color clockMutedColor = new Color(.66f, .7f, .78f);
    [SerializeField, Range(0, 16)] private int clockCornerRadius = 8;

    [Header("왼쪽 아래 캐릭터 · 소지품")]
    [SerializeField] private Color characterPanelColor = new Color(0f, 0f, 0f, 0f); // 투명 = 배경 없음
    [SerializeField] private bool inventoryStartsOpen;
    [SerializeField, Range(1f, 20f)] private float inventoryFoldSpeed = 7f;
    [SerializeField, Range(.05f, 1f)] private float portraitSwapDuration = .3f;
    private bool inventoryOpen;
    private float inventoryOpenAmount;
    private int shownIndex = -1;
    private float swapProgress = 1f;

    [Header("조사 · 대화 안내 (가까이 가면 머리 위에 뜸)")]
    [SerializeField] private float hintHeight = 1.4f;
    [SerializeField, Range(1f, 20f)] private float hintFadeSpeed = 8f;
    [Header("대화 · 조사 화면")]
    [SerializeField, Range(5f, 80f)] private float dialogueCharsPerSecond = 28f;
    [SerializeField] private Color npcPortraitColor = new Color(.55f, .6f, .5f);
    [System.NonSerialized] private DummyNpc dialogueNpc;
    [System.NonSerialized] private string[] dialogueLines;
    [System.NonSerialized] private int dialogueLine;
    [System.NonSerialized] private float dialogueStartTime;

    // 손 카드 장착 효과용
    [System.NonSerialized] private int handsShownIndex = -1;
    [System.NonSerialized] private string lastLeftHand;
    [System.NonSerialized] private string lastRightHand;
    [System.NonSerialized] private float leftPopAt = -10f;
    [System.NonSerialized] private float rightPopAt = -10f;

    // 지도: 0 = 오른쪽 위 아이콘만, 1 = 미니맵, 2 = 큰 지도 (M으로 순환)
    [System.NonSerialized] private int mapMode;
    [System.NonSerialized] private CameraTrigger[] mapRooms;
    // 남매 중 누구든 한 번 들어간 방 → 지도에 열림 (값 = 처음 열린 시각, 밝아지는 효과용). 플레이마다 초기화
    [System.NonSerialized] private Dictionary<CameraTrigger, float> visitedRooms = new Dictionary<CameraTrigger, float>();

    // 선택창 (ShowChoice로 연다).
    // NonSerialized: 에디터가 재컴파일할 때 null 배열을 빈 배열로 저장해 "열린 빈 선택창"이 되는 것을 막는다
    [System.NonSerialized] private string choiceTitle;
    [System.NonSerialized] private string choiceQuestion;
    [System.NonSerialized] private string[] choiceOptions;
    [System.NonSerialized] private System.Action<int> choiceCallback;
    [System.NonSerialized] private int choiceIndex;
    [System.NonSerialized] private float choiceOpenedAt;
    [System.NonSerialized] private Vector2 lastChoiceMouse;
    public bool ChoiceOpen => choiceOptions != null && choiceOptions.Length > 0;

    private Vector3 hintWorldPos;
    private string hintText;
    private float hintAlpha;

    private readonly Dictionary<GameObject, CharacterUiState> states = new Dictionary<GameObject, CharacterUiState>();
    private GUIStyle roundedStyle;
    private Texture2D roundedTexture;
    private int roundedRadius = -1;
    private CharacterManager manager;
    private SimpleGameHud originalHud;
    private GUIStyle textStyle;
    private GUIStyle centeredStyle;
    private Vector3 bedPosition = new Vector3(53f, 0f, 1.5f);
    private string notice = "";
    private float noticeUntil;
    private int dummySaveCount;

    public int DummySaveCount => dummySaveCount;
    public bool SaveBedExists => GameObject.Find("기획 참고 · 2층 침대 세이브 구간") != null;

    private void Start()
    {
        manager = CharacterManager.instance;
        originalHud = FindFirstObjectByType<SimpleGameHud>();
        if (originalHud != null) originalHud.enabled = false;

        var integration = FindFirstObjectByType<PrototypeIntegration>();
        integration.EnableSiblingCharacterRules();
        integration.EnableOverviewCameraFollow();
        // R로 고르는 동안·선택창이 열린 동안 이동·Tab·조사 잠금
        integration.ExtraBusy = () => ChoiceOpen || mapMode == 2 || (states.TryGetValue(manager.GetActivePlayer(), out CharacterUiState s) && s.selecting);
        PrototypeFloorTravel.EnsureCreated(integration);
        CreateSaveBed();
        choiceOptions = null;
        choiceCallback = null;
        mapMode = 0;
        mapRooms = null;
        handsShownIndex = -1;
        visitedRooms = new Dictionary<CameraTrigger, float>();
        dialogueNpc = null;
        dialogueLines = null;
        inventoryOpen = inventoryStartsOpen;
        inventoryOpenAmount = inventoryOpen ? 1f : 0f;

        for (int i = 0; i < manager.players.Length; i++)
        {
            GameObject player = manager.players[i];
            states[player] = new CharacterUiState();
            Inventory inventory = player.GetComponent<Inventory>();
            inventory.requireInHand = true; // 이 씬에서는 손에 든 아이템만 쓸 수 있다
            PlayerInteractor3D interactor = player.GetComponent<PlayerInteractor3D>();
            if (interactor != null)
            {
                interactor.HideDefaultUI = true; // 대화·조사 화면은 이 스크립트가 그린다
                interactor.TalkAdvance = AdvanceDialogue;
            }
            if (inventory.items.Count == 0)
            {
                inventory.items.Add(i == 0 ? "손전등" : "공구 상자");
                inventory.items.Add(i == 0 ? "사건 메모" : "응급 키트");
            }
        }
    }

    private void Update()
    {
        // 팀원 PlayerInteractor3D가 대화·조사를 끝낼 때 예전 HUD를 다시 켜므로, 이 씬에서는 계속 꺼 둔다
        if (originalHud != null && originalHud.enabled) originalHud.enabled = false;

        if (manager != null)
        {
            // 소지품을 고르는 중(R)에는 접혀 있어도 잠시 펼친다
            float target = inventoryOpen || states[manager.GetActivePlayer()].selecting ? 1f : 0f;
            inventoryOpenAmount = Mathf.MoveTowards(inventoryOpenAmount, target, inventoryFoldSpeed * Time.unscaledDeltaTime);

            // Tab으로 캐릭터가 바뀌면 초상화 카드가 앞뒤로 자리를 바꾼다
            if (manager.ActivePlayerIndex != shownIndex)
            {
                if (shownIndex >= 0) swapProgress = 0f;
                shownIndex = manager.ActivePlayerIndex;
            }
            swapProgress = Mathf.MoveTowards(swapProgress, 1f, Time.unscaledDeltaTime / portraitSwapDuration);
            UpdateInteractionHint();

            // 두 캐릭터 중 누구든 서 있는 방은 지도에 열린다 (조작하지 않는 캐릭터 포함)
            foreach (GameObject player in manager.players)
            {
                CameraTrigger room = RoomOf(player);
                if (room != null && !visitedRooms.ContainsKey(room)) visitedRooms[room] = Time.unscaledTime;
            }

            // 새 대화가 시작되면 대사를 처음부터 한 글자씩. Line에 줄바꿈을 넣으면 여러 줄로 넘긴다
            PlayerInteractor3D talker = manager.GetActivePlayer().GetComponent<PlayerInteractor3D>();
            DummyNpc npc = talker != null ? talker.TalkingTo : null;
            if (npc != dialogueNpc)
            {
                dialogueNpc = npc;
                dialogueLines = npc == null ? null : npc.Line.Split('\n');
                dialogueLine = 0;
                dialogueStartTime = Time.unscaledTime;
            }
        }
        if (manager == null || FadeManager.instance == null || FadeManager.instance.isFading) return;
        if (ChoiceOpen)
        {
            UpdateChoice(); // 선택창이 열려 있으면 다른 입력은 받지 않는다
            return;
        }
        if (Input.GetKeyDown(KeyCode.M)) CycleMap();
        if (mapMode == 2)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)) mapMode = 0;
            return; // 큰 지도가 열려 있는 동안은 다른 입력을 받지 않는다
        }
        GameObject active = manager.GetActivePlayer();
        CharacterUiState state = states[active];
        Inventory inventory = active.GetComponent<Inventory>();

        // 고르기는 Q로 닫거나, 걷기 시작하면(WASD) 자동으로 닫힌다
        if (Input.GetKeyDown(KeyCode.Q)
            || (state.selecting && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))))
            state.selecting = false;
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!state.selecting)
            {
                state.selecting = true;
                state.selectedSlot = Mathf.Clamp(state.selectedSlot, 0, Mathf.Max(0, inventory.items.Count - 1));
            }
            else EquipSelected(active); // 장착해도 열어 둔다: R을 연달아 눌러 양손을 바로 바꿀 수 있게
        }
        if (state.selecting && inventory.items.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow)) state.selectedSlot = (state.selectedSlot + inventory.items.Count - 1) % inventory.items.Count;
            if (Input.GetKeyDown(KeyCode.RightArrow)) state.selectedSlot = (state.selectedSlot + 1) % inventory.items.Count;
        }

        if (IsNearSaveBed(active))
        {
            if (Input.GetKeyDown(KeyCode.E) && !state.selecting)
            {
                ShowChoice("침대", "잠시 누워서 지금까지를 기록해 둘까?", new[] { "저장한다", "그만둔다" }, choice =>
                {
                    if (choice == 0) PerformDummySave();
                });
            }
        }
    }

    /// <summary>
    /// "할까 말까" 같은 선택창을 연다. 고르면 onChosen(고른 번호), Q·Esc로 취소하면 onChosen(-1).
    /// 열려 있는 동안 이동·Tab·조사는 잠긴다.
    /// </summary>
    public void ShowChoice(string title, string question, string[] options, System.Action<int> onChosen)
    {
        if (options == null || options.Length == 0) return;
        choiceTitle = title;
        choiceQuestion = question;
        choiceOptions = options;
        choiceCallback = onChosen;
        choiceIndex = 0;
        choiceOpenedAt = Time.unscaledTime;
        states[manager.GetActivePlayer()].selecting = false;
    }

    private void CloseChoice(int result)
    {
        System.Action<int> callback = choiceCallback;
        choiceOptions = null;
        choiceCallback = null;
        callback?.Invoke(result);
    }

    private void UpdateChoice()
    {
        if (Time.unscaledTime - choiceOpenedAt < .15f) return; // 연 키(E)가 바로 결정으로 이어지지 않게
        int count = choiceOptions.Length;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.W))
            choiceIndex = (choiceIndex + count - 1) % count;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.S))
            choiceIndex = (choiceIndex + 1) % count;
        for (int i = 0; i < count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { CloseChoice(i); return; }
        }
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) CloseChoice(choiceIndex);
        else if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape)) CloseChoice(-1);
    }

    private void ChoicePanel(float width, float height)
    {
        Rect panel = ConversationRect(width, height);
        ConversationBackground(panel);
        NameTab(new Vector2(panel.x + 18, panel.y - 13), choiceTitle);
        BodyText(new Rect(panel.x + 22, panel.y + 24, panel.width - 44, 40), choiceQuestion, 17, new Color(.95f, .93f, .88f));

        string[] options = choiceOptions;
        int count = options.Length;
        const float gap = 12f;
        const float buttonHeight = 38f;
        float buttonWidth = Mathf.Min(220f, (panel.width - 44f - gap * (count - 1)) / count);
        Vector2 mouse = Event.current.mousePosition;
        bool mouseMoved = (mouse - lastChoiceMouse).sqrMagnitude > .5f;
        lastChoiceMouse = mouse;

        for (int i = 0; i < count; i++)
        {
            Rect button = new Rect(panel.x + 22f + i * (buttonWidth + gap), panel.y + 70f, buttonWidth, buttonHeight);
            if (mouseMoved && button.Contains(mouse)) choiceIndex = i; // 마우스를 올리면 그 칸이 골라짐
            bool on = i == choiceIndex;

            GUI.color = on ? clockAccentColor : new Color(1f, 1f, 1f, .08f);
            GUI.Box(button, GUIContent.none, roundedStyle);
            Rect number = new Rect(button.x + 9f, button.y + (buttonHeight - 20f) * .5f, 20f, 20f);
            GUI.color = on ? new Color(.1f, .12f, .15f, .25f) : new Color(1f, 1f, 1f, .1f);
            GUI.Box(number, GUIContent.none, roundedStyle);
            Label(number, (i + 1).ToString(), 11, true, FontStyle.Bold, on ? new Color(.1f, .12f, .15f) : clockAccentColor);
            Label(new Rect(number.xMax + 9f, button.y + (buttonHeight - 22f) * .5f, button.width - 44f, 22f), options[i], 15,
                false, on ? FontStyle.Bold : FontStyle.Normal, on ? new Color(.1f, .12f, .15f) : new Color(.95f, .93f, .88f));

            if (GUI.Button(button, GUIContent.none, GUIStyle.none)) { CloseChoice(i); break; }
        }
        KeyHints(panel.xMax - 22, panel.yMax - 30, "← →", "선택", "E", "결정", "Q", "취소");
    }

    // 대화 중 E: 글자가 다 안 나왔으면 모두 보이고, 다음 줄이 있으면 넘긴다. false면 PlayerInteractor3D가 대화를 닫는다
    private bool AdvanceDialogue()
    {
        if (dialogueLines == null) return false;
        if (VisibleChars() < dialogueLines[dialogueLine].Length)
        {
            dialogueStartTime = -10000f;
            return true;
        }
        if (dialogueLine < dialogueLines.Length - 1)
        {
            dialogueLine++;
            dialogueStartTime = Time.unscaledTime;
            return true;
        }
        return false;
    }

    private int VisibleChars() => Mathf.FloorToInt((Time.unscaledTime - dialogueStartTime) * dialogueCharsPerSecond);

    // 팀원 PlayerInteractor3D.PanelRect와 같은 자리 (조사 중 이 영역 클릭은 무시되므로 맞춰 둔다)
    private static Rect ConversationRect(float width, float height) => new Rect(width * .2f, height - 175f, width * .6f, 155f);

    private void ConversationBackground(Rect panel)
    {
        RoundedPanel(panel, new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .95f));
        GUI.color = clockAccentColor;
        GUI.DrawTexture(new Rect(panel.x, panel.y + 14, 3, panel.height - 28), Texture2D.whiteTexture);
    }

    private void DialoguePanel(float width, float height)
    {
        Rect panel = ConversationRect(width, height);
        ConversationBackground(panel);

        Rect card = new Rect(panel.x + 18, panel.y + 26, 80, 100);
        PortraitCard(card, npcPortraitColor, dialogueNpc.DisplayName, 1f);
        NameTab(new Vector2(panel.x + 114, panel.y - 13), dialogueNpc.DisplayName);

        string line = dialogueLines[dialogueLine];
        int shown = Mathf.Clamp(VisibleChars(), 0, line.Length);
        BodyText(new Rect(panel.x + 116, panel.y + 26, panel.width - 140, 90), line.Substring(0, shown), 17, new Color(.95f, .93f, .88f));

        bool done = shown >= line.Length;
        bool more = dialogueLine < dialogueLines.Length - 1;
        if (done)
        {
            float blink = .35f + .65f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
            DownArrow(new Vector2(panel.xMax - 22, panel.yMax - 21), new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, blink));
        }
        KeyHints(panel.xMax - 40, panel.yMax - 30, "E", done ? (more ? "다음" : "닫기") : "넘기기", "Q", "닫기");
    }

    private void InspectionPanel(PlayerInteractor3D interactor, float width, float height)
    {
        Rect panel = ConversationRect(width, height);
        ConversationBackground(panel);

        InspectionItem selected = interactor.Selected;
        NameTab(new Vector2(panel.x + 18, panel.y - 13), selected == null ? interactor.Area.Title : selected.Title);
        Rect body = new Rect(panel.x + 22, panel.y + 26, panel.width - 44, 80);
        if (selected == null) BodyText(body, "살펴볼 곳을 클릭하세요.", 15, clockMutedColor);
        else BodyText(body, selected.Description, 17, new Color(.95f, .93f, .88f));

        if (!string.IsNullOrEmpty(interactor.Notice))
            Label(new Rect(panel.x + 22, panel.yMax - 31, panel.width * .5f, 20), interactor.Notice, 13, false, FontStyle.Bold, clockAccentColor);

        if (selected == null) KeyHints(panel.xMax - 22, panel.yMax - 30, "클릭", "조사", "Q", "돌아가기");
        else if (selected.Collectible) KeyHints(panel.xMax - 22, panel.yMax - 30, "E", "획득", "Q", "돌아가기");
        else KeyHints(panel.xMax - 22, panel.yMax - 30, "Q", "돌아가기");

        // 마우스를 올린 곳 이름은 커서 옆 작은 꼬리표로
        if (selected == null && interactor.Hovered != null)
        {
            Vector2 mouse = Event.current.mousePosition;
            float w = Measure(interactor.Hovered.Title, 13, FontStyle.Bold) + 20f;
            Rect tag = new Rect(mouse.x + 16f, mouse.y + 14f, w, 24f);
            GUI.color = new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .92f);
            GUI.Box(tag, GUIContent.none, roundedStyle);
            Label(tag, interactor.Hovered.Title, 13, true, FontStyle.Bold, new Color(.95f, .93f, .88f));
        }
    }

    // 패널 위쪽 모서리에 걸친 앰버색 이름 탭
    private void NameTab(Vector2 position, string title)
    {
        float w = Measure(title, 14, FontStyle.Bold) + 28f;
        Rect tab = new Rect(position.x, position.y, w, 26f);
        GUI.color = new Color(0f, 0f, 0f, .35f);
        GUI.Box(new Rect(tab.x + 1, tab.y + 2, tab.width, tab.height), GUIContent.none, roundedStyle);
        GUI.color = clockAccentColor;
        GUI.Box(tab, GUIContent.none, roundedStyle);
        Label(tab, title, 14, true, FontStyle.Bold, new Color(.1f, .12f, .15f));
    }

    private void BodyText(Rect rect, string text, int size, Color color)
    {
        textStyle.wordWrap = true;
        Label(rect, text, size, false, FontStyle.Normal, color);
        textStyle.wordWrap = false;
    }

    // 오른쪽 끝을 기준으로 [키] 설명 [키] 설명 순서로 그린다
    private void KeyHints(float right, float y, params string[] pairs)
    {
        float x = right;
        for (int i = pairs.Length - 2; i >= 0; i -= 2)
        {
            string key = pairs[i];
            string label = pairs[i + 1];
            float labelWidth = Measure(label, 12, FontStyle.Normal);
            x -= labelWidth;
            Label(new Rect(x, y + 1, labelWidth + 4, 20), label, 12, false, FontStyle.Normal, clockMutedColor);
            float keyWidth = Mathf.Max(20f, Measure(key, 11, FontStyle.Bold) + 12f);
            x -= 6f + keyWidth;
            Rect cap = new Rect(x, y, keyWidth, 20f);
            GUI.color = new Color(1f, 1f, 1f, .14f);
            GUI.Box(cap, GUIContent.none, roundedStyle);
            Label(cap, key, 11, true, FontStyle.Bold, new Color(.95f, .93f, .88f));
            x -= 16f;
        }
    }

    // 왼쪽부터 [키] 설명 순서로, 밝은 맵 위에서도 보이게 어두운 알약 배경을 깐다
    // prefix: 맨 앞에 굵게 붙는 짧은 글자 (예: 소지품 개수 2/6)
    private void KeyHintsFrom(float left, float y, string prefix, params string[] pairs)
    {
        float prefixWidth = Measure(prefix, 12, FontStyle.Bold);
        float total = 10f + prefixWidth + 10f + 1f + 10f;
        for (int i = 0; i < pairs.Length; i += 2)
            total += Mathf.Max(20f, Measure(pairs[i], 11, FontStyle.Bold) + 12f) + 6f + Measure(pairs[i + 1], 12, FontStyle.Normal) + (i + 2 < pairs.Length ? 14f : 10f);
        GUI.color = new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .9f);
        GUI.Box(new Rect(left, y - 3f, total, 26f), GUIContent.none, roundedStyle);

        float x = left + 10f;
        Label(new Rect(x, y + 1, prefixWidth + 4, 20), prefix, 12, false, FontStyle.Bold, clockAccentColor);
        x += prefixWidth + 10f;
        GUI.color = new Color(1f, 1f, 1f, .15f);
        GUI.DrawTexture(new Rect(x, y + 3f, 1f, 14f), Texture2D.whiteTexture);
        x += 11f;
        for (int i = 0; i < pairs.Length; i += 2)
        {
            float keyWidth = Mathf.Max(20f, Measure(pairs[i], 11, FontStyle.Bold) + 12f);
            Rect cap = new Rect(x, y, keyWidth, 20f);
            GUI.color = new Color(1f, 1f, 1f, .14f);
            GUI.Box(cap, GUIContent.none, roundedStyle);
            Label(cap, pairs[i], 11, true, FontStyle.Bold, clockAccentColor);
            x += keyWidth + 6f;
            float labelWidth = Measure(pairs[i + 1], 12, FontStyle.Normal);
            Label(new Rect(x, y + 1, labelWidth + 4, 20), pairs[i + 1], 12, false, FontStyle.Normal, new Color(.95f, .93f, .88f));
            x += labelWidth + 14f;
        }
    }

    // 장착·저장 같은 짧은 알림: 캐릭터 카드 위에 잠깐 떴다가 사라진다
    private void NoticeToast(float x, float y)
    {
        float remaining = noticeUntil - Time.unscaledTime;
        if (remaining <= 0f || string.IsNullOrEmpty(notice)) return;
        float alpha = Mathf.Clamp01(remaining / .4f);
        float w = Measure(notice, 13, FontStyle.Bold) + 28f;
        Rect toast = new Rect(x, y, w, 26f);
        GUI.color = new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .92f * alpha);
        GUI.Box(toast, GUIContent.none, roundedStyle);
        GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, alpha);
        GUI.DrawTexture(new Rect(toast.x, toast.y + 6f, 3f, toast.height - 12f), Texture2D.whiteTexture);
        Label(new Rect(toast.x + 14f, toast.y + 4f, w, 20f), notice, 13, false, FontStyle.Bold, new Color(.95f, .93f, .88f, alpha));
    }

    private float Measure(string text, int size, FontStyle fontStyle)
    {
        textStyle.fontSize = size;
        textStyle.fontStyle = fontStyle;
        return textStyle.CalcSize(new GUIContent(text)).x;
    }

    private static void DownArrow(Vector2 center, Color color)
    {
        RotatedBar(new Vector2(center.x - 3f, center.y), 9f, 2.5f, 45f, color);
        RotatedBar(new Vector2(center.x + 3f, center.y), 9f, 2.5f, -45f, color);
    }

    // PlayerInteractor3D와 같은 거리·순서로 E가 무엇을 할지 찾아서 안내를 띄운다 (누르는 버튼이 아니라 표시만)
    private void UpdateInteractionHint()
    {
        GameObject active = manager.GetActivePlayer();
        PlayerInteractor3D interactor = active.GetComponent<PlayerInteractor3D>();
        string text = null;
        Vector3 anchor = hintWorldPos;
        bool blocked = interactor == null || ChoiceOpen || mapMode == 2 || states[active].selecting
            || (interactor.IsBusy && !interactor.ExternalLock) || FadeManager.instance == null || FadeManager.instance.isFading
            || (interactor.HasPriorityInteraction != null && interactor.HasPriorityInteraction());
        if (!blocked)
        {
            Vector3 origin = active.transform.position;
            DummyNpc npc = null;
            float npcDistance = 1.8f * 1.8f;
            foreach (DummyNpc candidate in FindObjectsByType<DummyNpc>(FindObjectsSortMode.None))
            {
                float d = (candidate.transform.position - origin).sqrMagnitude;
                if (d >= npcDistance) continue;
                if (Physics.Linecast(origin, candidate.transform.position, out RaycastHit obstruction, ~0, QueryTriggerInteraction.Ignore)
                    && obstruction.collider.GetComponentInParent<DummyNpc>() != candidate) continue;
                npc = candidate;
                npcDistance = d;
            }
            if (npc != null)
            {
                text = npc.DisplayName + WaGwa(npc.DisplayName) + " 대화하기";
                anchor = npc.transform.position;
            }
            else
            {
                InspectionArea nearest = null;
                float closest = 2.4f * 2.4f;
                foreach (InspectionArea candidate in FindObjectsByType<InspectionArea>(FindObjectsSortMode.None))
                {
                    float d = (candidate.transform.position - origin).sqrMagnitude;
                    if (d < closest) { nearest = candidate; closest = d; }
                }
                if (nearest != null)
                {
                    text = "조사하기";
                    anchor = nearest.transform.position;
                }
            }
            if (IsNearSaveBed(active))
            {
                text = "침대에서 저장하기";
                anchor = bedPosition;
            }
        }

        if (text != null)
        {
            hintText = text;
            hintWorldPos = anchor;
        }
        hintAlpha = Mathf.MoveTowards(hintAlpha, text != null ? 1f : 0f, hintFadeSpeed * Time.unscaledDeltaTime);
    }

    // 받침이 있으면 "과", 없으면 "와"
    private static string WaGwa(string word)
    {
        if (string.IsNullOrEmpty(word)) return "와";
        char last = word[word.Length - 1];
        if (last < '가' || last > '힣') return "와";
        return (last - '가') % 28 != 0 ? "과" : "와";
    }

    private void InteractionHint(float scale)
    {
        if (hintAlpha <= .01f || string.IsNullOrEmpty(hintText) || Camera.main == null) return;
        Vector3 screen = Camera.main.WorldToScreenPoint(hintWorldPos + Vector3.up * hintHeight);
        if (screen.z < 0f) return;
        Vector2 center = new Vector2(screen.x, Screen.height - screen.y) / scale;
        float rise = (1f - hintAlpha) * 6f; // 나타날 때 살짝 떠오른다

        GUIStyle measure = textStyle;
        measure.fontSize = 13;
        measure.fontStyle = FontStyle.Bold;
        float textWidth = measure.CalcSize(new GUIContent(hintText)).x;
        const float keySize = 20f;
        float width = 8f + keySize + 8f + textWidth + 12f;
        Rect pill = new Rect(center.x - width * .5f, center.y - 16f + rise, width, 30f);

        Color pillColor = new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .92f * hintAlpha);
        GUI.color = new Color(0f, 0f, 0f, .3f * hintAlpha);
        GUI.Box(new Rect(pill.x + 1, pill.y + 2, pill.width, pill.height), GUIContent.none, roundedStyle);

        // 아래로 향한 작은 꼬리 (말풍선처럼)
        Matrix4x4 oldMatrix = GUI.matrix;
        Vector2 tail = new Vector2(pill.center.x, pill.yMax);
        GUI.matrix = oldMatrix * Matrix4x4.TRS(tail, Quaternion.Euler(0f, 0f, 45f), Vector3.one) * Matrix4x4.TRS(-tail, Quaternion.identity, Vector3.one);
        GUI.color = pillColor;
        GUI.DrawTexture(new Rect(tail.x - 4f, tail.y - 4f, 8f, 8f), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;

        GUI.color = pillColor;
        GUI.Box(pill, GUIContent.none, roundedStyle);

        Rect key = new Rect(pill.x + 8f, pill.center.y - keySize * .5f, keySize, keySize);
        GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, hintAlpha);
        GUI.Box(key, GUIContent.none, roundedStyle);
        Label(key, "E", 12, true, FontStyle.Bold, new Color(.1f, .12f, .15f, hintAlpha));
        Rect textRect = new Rect(key.xMax + 8f, pill.y, textWidth + 4f, pill.height);
        textStyle.alignment = TextAnchor.MiddleLeft;
        textStyle.fontSize = 13;
        textStyle.fontStyle = FontStyle.Bold;
        textStyle.normal.textColor = Color.white;
        GUI.color = new Color(.95f, .93f, .88f, hintAlpha);
        GUI.Label(textRect, hintText, textStyle);
    }

    public bool EquipSelected(GameObject player)
    {
        Inventory inventory = player.GetComponent<Inventory>();
        CharacterUiState state = states[player];
        if (inventory.items.Count == 0) return false;
        state.selectedSlot = Mathf.Clamp(state.selectedSlot, 0, inventory.items.Count - 1);
        string item = inventory.items[state.selectedSlot];
        string replaced = null;

        // 이미 들고 있는 아이템을 다시 고르면 내려놓는다
        if (state.leftHand == item || state.rightHand == item)
        {
            if (state.leftHand == item) state.leftHand = EmptyHand;
            else state.rightHand = EmptyHand;
            SyncHands(inventory, state);
            ShowNotice(item + " 내려놓음");
            return true;
        }

        if (state.leftHand == EmptyHand) { state.leftHand = item; state.leftIsNewer = true; }
        else if (state.rightHand == EmptyHand) { state.rightHand = item; state.leftIsNewer = false; }
        else if (state.leftIsNewer) { replaced = state.rightHand; state.rightHand = item; state.leftIsNewer = false; } // 양손이 차면 먼저 든 손을 교체
        else { replaced = state.leftHand; state.leftHand = item; state.leftIsNewer = true; }
        SyncHands(inventory, state);
        ShowNotice(replaced == null ? item + " 장착" : replaced + " 대신 " + item + " 장착");
        return true;
    }

    private static void SyncHands(Inventory inventory, CharacterUiState state)
    {
        inventory.leftHand = state.leftHand == EmptyHand ? null : state.leftHand;
        inventory.rightHand = state.rightHand == EmptyHand ? null : state.rightHand;
    }

    private static bool IsEquipped(CharacterUiState state, string item)
    {
        return !string.IsNullOrEmpty(item) && (state.leftHand == item || state.rightHand == item);
    }

    public string[] GetHands(int playerIndex)
    {
        CharacterUiState state = states[manager.players[playerIndex]];
        return new[] { state.leftHand, state.rightHand };
    }

    public void PerformDummySave()
    {
        dummySaveCount++;
        ShowNotice("저장 완료 · 더미 기능 (실제 데이터는 저장하지 않음)");
    }

    private bool IsNearSaveBed(GameObject player)
    {
        return player.transform.position.x > 40f && Vector3.Distance(player.transform.position, bedPosition) < 2.5f;
    }

    private void ShowNotice(string message)
    {
        notice = message;
        noticeUntil = Time.unscaledTime + 2.5f;
    }

    private void CreateSaveBed()
    {
        var root = new GameObject("기획 참고 · 2층 침대 세이브 구간");
        root.transform.position = bedPosition;
        Material bed = new Material(Shader.Find("Standard")) { color = new Color(.38f, .45f, .58f) };
        Material pillow = new Material(Shader.Find("Standard")) { color = new Color(.78f, .8f, .76f) };
        CreateBox(root.transform, "Bed Base", new Vector3(0, .25f, 0), new Vector3(2.2f, .5f, 1.2f), bed);
        CreateBox(root.transform, "Pillow", new Vector3(.65f, .58f, 0), new Vector3(.55f, .18f, .8f), pillow);
        CreateBox(root.transform, "Save Marker", new Vector3(0, 1.1f, 0), new Vector3(.25f, .8f, .25f), pillow).GetComponent<Collider>().enabled = false;
    }

    private static GameObject CreateBox(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private void OnGUI()
    {
        if (manager == null) return;
        float scale = Mathf.Max(.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            centeredStyle = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter };
        }

        GameObject active = manager.GetActivePlayer();
        // 대화·조사 중에는 예전 HUD처럼 화면을 비워 둔다.
        // IsBusy에는 화면 전환(Tab 페이드) 중 잠금(ExternalLock)도 들어 있으므로 그건 빼고 본다
        PlayerInteractor3D interactor = active.GetComponent<PlayerInteractor3D>();
        if (interactor != null && interactor.IsBusy && !interactor.ExternalLock)
        {
            // 대신 새 대화창 · 조사창을 그린다 (팀원 기본 화면은 HideDefaultUI로 꺼 둠)
            if (interactor.TalkingTo != null && dialogueLines != null) DialoguePanel(width, height);
            else if (interactor.IsInspecting) InspectionPanel(interactor, width, height);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            return;
        }
        if (ChoiceOpen)
        {
            ChoicePanel(width, height); // 선택창이 열려 있는 동안은 대화창처럼 다른 HUD를 비운다
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            return;
        }
        int index = manager.ActivePlayerIndex;
        CharacterUiState state = states[active];
        Inventory inventory = active.GetComponent<Inventory>();
        string character = NameOf(index);
        int inventoryCapacity = index == 0 ? 6 : 5;
        string location = active.transform.position.x > 40 ? "저택 · 2층" : "저택 · 1층";

        ClockPanel(new Rect(clockPosition, clockSize), location);

        MapCorner(width, active);

        float bottom = height - 20;
        CharacterPanel(20, bottom, index, character, inventory, inventoryCapacity, state);

        HandCards(width, bottom, index, state);

        // 아래 가운데 안내 막대는 없앴다: Tab은 뒤 카드, R은 화살표 배지, 알림은 카드 위 토스트, 침대 저장은 머리 위 안내

        if (mapMode == 2) BigMap(width, height);
        else InteractionHint(scale);

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    private void ClockPanel(Rect rect, string location)
    {
        RoundedPanel(rect, clockPanelColor);
        GUI.color = clockAccentColor;
        GUI.DrawTexture(new Rect(rect.x, rect.y + 14, 3, rect.height - 28), Texture2D.whiteTexture);

        // 시간은 크게, 오른쪽에 일차와 시간대를 작게
        float timeHeight = clockFontSize * 1.35f;
        float timeWidth = clockFontSize * 3.1f;
        Label(new Rect(rect.x + 18, rect.y + 6, timeWidth, timeHeight), hour.ToString("00") + ":" + minute.ToString("00"), clockFontSize, false, FontStyle.Bold);
        float sideX = rect.x + 22 + timeWidth;
        float sideY = rect.y + 6 + timeHeight * .5f;
        Label(new Rect(sideX, sideY - 20, rect.xMax - sideX - 8, 20), day + "일차", 15, false, FontStyle.Bold, clockAccentColor);
        Label(new Rect(sideX, sideY, rect.xMax - sideX - 8, 20), PeriodOf(hour), 14, false, FontStyle.Normal, clockMutedColor);

        float dividerY = rect.y + 10 + timeHeight;
        GUI.color = new Color(1f, 1f, 1f, .08f);
        GUI.DrawTexture(new Rect(rect.x + 18, dividerY, rect.width - 36, 1), Texture2D.whiteTexture);

        float locationY = dividerY + 5;
        Diamond(new Vector2(rect.x + 23, locationY + 11), 7, clockAccentColor);
        Label(new Rect(rect.x + 34, locationY, rect.width - 50, 22), location, 16, false, FontStyle.Normal, new Color(.9f, .92f, .95f));
    }

    private static string PeriodOf(int h)
    {
        if (h < 5) return "새벽";
        if (h < 12) return "오전";
        if (h < 18) return "오후";
        if (h < 21) return "저녁";
        return "밤";
    }

    private void RoundedPanel(Rect rect, Color color)
    {
        if (roundedStyle == null || roundedRadius != clockCornerRadius)
        {
            if (roundedTexture != null) Destroy(roundedTexture);
            roundedRadius = clockCornerRadius;
            int radius = Mathf.Max(1, roundedRadius);
            int size = radius * 2 + 2;
            roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(radius - (x + .5f), x + .5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - (y + .5f), y + .5f - (size - radius)));
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f);
                roundedTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            roundedTexture.Apply();
            roundedStyle = new GUIStyle { normal = { background = roundedTexture }, border = new RectOffset(radius, radius, radius, radius) };
        }
        GUI.color = new Color(0f, 0f, 0f, .35f * color.a);
        GUI.Box(new Rect(rect.x + 2, rect.y + 3, rect.width, rect.height), GUIContent.none, roundedStyle);
        GUI.color = color;
        GUI.Box(rect, GUIContent.none, roundedStyle);
    }

    private static void Diamond(Vector2 center, float size, Color color)
    {
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = oldMatrix * Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, 45f), Vector3.one) * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - size * .5f, center.y - size * .5f, size, size), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
    }

    private void OnDestroy()
    {
        if (roundedTexture != null) Destroy(roundedTexture);
    }

    // ---------------- 지도: M을 누를 때마다 아이콘 → 미니맵 → 큰 지도 → 아이콘 ----------------

    private static int FloorOf(Vector3 position) => position.x > 40f ? 2 : 1; // 2층은 x 40 너머에 따로 지어져 있다

    private void RefreshMapRooms() => mapRooms = FindObjectsByType<CameraTrigger>(FindObjectsSortMode.None);

    private CameraTrigger RoomOf(GameObject player)
    {
        if (mapRooms == null) RefreshMapRooms();
        foreach (CameraTrigger room in mapRooms)
            if (room != null && room.Contains(player)) return room;
        return null;
    }

    private void CycleMap()
    {
        mapMode = (mapMode + 1) % 3;
        if (mapMode != 0) RefreshMapRooms();
        if (mapMode == 2) states[manager.GetActivePlayer()].selecting = false;
    }

    private void MapCorner(float width, GameObject active)
    {
        if (mapMode == 0)
        {
            // 아이콘만: 접힌 종이 지도 모양 + M 배지
            Rect icon = new Rect(width - 20f - 48f, 20f, 48f, 48f);
            RoundedPanel(icon, clockPanelColor);
            const float glyphWidth = 24f, glyphHeight = 20f;
            float gx = icon.center.x - glyphWidth * .5f, gy = icon.center.y - glyphHeight * .5f;
            for (int k = 0; k < 3; k++)
            {
                GUI.color = k % 2 == 0 ? clockAccentColor : new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .5f);
                GUI.DrawTexture(new Rect(gx + k * glyphWidth / 3f, gy + (k % 2 == 0 ? 0f : 2f), glyphWidth / 3f - 1f, glyphHeight - 2f), Texture2D.whiteTexture);
            }
            KeyBadge(new Vector2(icon.x - 7f, icon.y - 7f), "M");
            if (GUI.Button(icon, GUIContent.none, GUIStyle.none)) CycleMap();
            return;
        }

        // 미니맵 (큰 지도가 열려 있는 동안에도 그대로 둔다)
        Rect panel = new Rect(width - 20f - 220f, 20f, 220f, 160f);
        RoundedPanel(panel, clockPanelColor);
        int floor = FloorOf(active.transform.position);
        CameraTrigger here = RoomOf(active);
        string header = floor + "층" + (here != null && !string.IsNullOrEmpty(here.roomName) && here.roomName != floor + "층" ? " · " + here.roomName : "");
        Label(new Rect(panel.x + 14f, panel.y + 9f, panel.width - 28f, 18f), header, 12, false, FontStyle.Bold, new Color(.95f, .93f, .88f));
        DrawFloorPlan(new Rect(panel.x + 10f, panel.y + 32f, panel.width - 20f, panel.height - 42f), floor, false);
        KeyBadge(new Vector2(panel.x - 7f, panel.y - 7f), "M");
        if (GUI.Button(panel, GUIContent.none, GUIStyle.none)) CycleMap();
    }

    private void BigMap(float width, float height)
    {
        GUI.color = new Color(0f, 0f, 0f, .55f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);

        float w = Mathf.Min(width - 120f, 920f), h = Mathf.Min(height - 120f, 520f);
        Rect panel = new Rect((width - w) * .5f, (height - h) * .5f, w, h);
        RoundedPanel(panel, new Color(clockPanelColor.r, clockPanelColor.g, clockPanelColor.b, .97f));
        NameTab(new Vector2(panel.x + 18f, panel.y - 13f), "지도");

        // 1층과 2층을 나란히
        float columnWidth = (panel.width - 60f) * .5f;
        for (int floor = 1; floor <= 2; floor++)
        {
            Rect column = new Rect(panel.x + 20f + (floor - 1) * (columnWidth + 20f), panel.y + 26f, columnWidth, panel.height - 80f);
            Label(new Rect(column.x + 2f, column.y, column.width, 20f), floor + "층", 14, false, FontStyle.Bold, clockAccentColor);
            DrawFloorPlan(new Rect(column.x, column.y + 26f, column.width, column.height - 26f), floor, true);
        }

        // 범례
        float lx = panel.x + 22f, ly = panel.yMax - 34f;
        for (int i = 0; i < manager.players.Length; i++)
        {
            Diamond(new Vector2(lx + 5f, ly + 10f), 8f, PlayerColor(i));
            Label(new Rect(lx + 14f, ly + 1f, 60f, 20f), NameOf(i), 12, false, FontStyle.Normal, clockMutedColor);
            lx += 70f;
        }
        GUI.color = clockAccentColor;
        GUI.DrawTexture(new Rect(lx, ly + 4f, 12f, 12f), Texture2D.whiteTexture);
        GUI.color = new Color(.22f, .26f, .32f);
        GUI.DrawTexture(new Rect(lx + 1.5f, ly + 5.5f, 9f, 9f), Texture2D.whiteTexture);
        Label(new Rect(lx + 18f, ly + 1f, 120f, 20f), "지금 있는 곳", 12, false, FontStyle.Normal, clockMutedColor);

        KeyHints(panel.xMax - 22f, panel.yMax - 32f, "M", "닫기");
    }

    private static Color PlayerColor(int index) => index == 0 ? new Color(.25f, .55f, .85f) : new Color(.75f, .45f, .58f);

    // CameraTrigger(방 구역) 상자를 위에서 본 평면도로 그린다
    private void DrawFloorPlan(Rect area, int floor, bool showLabels)
    {
        if (mapRooms == null) RefreshMapRooms();
        bool any = false;
        Bounds all = default;
        foreach (CameraTrigger room in mapRooms)
        {
            Collider zone = room != null ? room.GetComponent<Collider>() : null;
            if (zone == null || FloorOf(zone.bounds.center) != floor) continue;
            if (!any) { all = zone.bounds; any = true; }
            else all.Encapsulate(zone.bounds);
        }
        if (!any)
        {
            Label(area, "지도 정보 없음", 12, true, FontStyle.Normal, clockMutedColor);
            return;
        }

        float s = Mathf.Min(area.width / all.size.x, area.height / all.size.z);
        Vector2 origin = new Vector2(area.x + (area.width - all.size.x * s) * .5f, area.y + (area.height - all.size.z * s) * .5f);
        System.Func<Vector3, Vector2> toMap = p => new Vector2(origin.x + (p.x - all.min.x) * s, origin.y + (all.max.z - p.z) * s);

        GameObject active = manager.GetActivePlayer();
        foreach (CameraTrigger room in mapRooms)
        {
            Collider zone = room != null ? room.GetComponent<Collider>() : null;
            if (zone == null || FloorOf(zone.bounds.center) != floor) continue;
            Bounds b = zone.bounds;
            Vector2 corner = toMap(new Vector3(b.min.x, 0f, b.max.z));
            Rect r = new Rect(corner.x + 1f, corner.y + 1f, b.size.x * s - 2f, b.size.z * s - 2f);
            bool here = room.Contains(active);
            if (!visitedRooms.TryGetValue(room, out float revealedAt))
            {
                // 아직 아무도 안 가 본 방: 흐린 윤곽 + ?
                GUI.color = new Color(1f, 1f, 1f, .14f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, .03f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                if (r.width > 14f && r.height > 14f)
                    Label(r, "?", showLabels ? 13 : 10, true, FontStyle.Bold, new Color(clockMutedColor.r, clockMutedColor.g, clockMutedColor.b, .45f));
                continue;
            }
            if (here)
            {
                GUI.color = clockAccentColor;
                GUI.DrawTexture(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), Texture2D.whiteTexture);
            }
            GUI.color = here ? new Color(.22f, .26f, .32f) : new Color(.15f, .18f, .22f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            float glow = 1f - Mathf.Clamp01((Time.unscaledTime - revealedAt) / .8f); // 처음 열린 직후 잠깐 밝게
            if (glow > 0f)
            {
                GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .35f * glow);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            if (showLabels && r.width > 44f && r.height > 18f)
                Label(r, room.roomName, 11, true, FontStyle.Normal, here ? new Color(.95f, .93f, .88f) : clockMutedColor);
        }

        for (int i = 0; i < manager.players.Length; i++)
        {
            GameObject player = manager.players[i];
            if (FloorOf(player.transform.position) != floor) continue;
            Vector2 m = toMap(player.transform.position);
            bool isActive = player == active;
            float size = (showLabels ? 10f : 7f) + (isActive ? 2f : 0f);
            if (isActive) Diamond(m, size + 4f, Color.white);
            Diamond(m, size, PlayerColor(i));
            if (showLabels) Label(new Rect(m.x - 30f, m.y + 7f, 60f, 16f), NameOf(i), 10, true, FontStyle.Bold, PlayerColor(i));
        }
    }

    // ---------------- 오른쪽 아래: 배경 없는 손 카드 두 장 (왼손 L · 오른손 R) ----------------

    private void HandCards(float width, float bottom, int index, CharacterUiState state)
    {
        // 장착·교체 순간 그 카드가 살짝 튀어 오른다. Tab으로 캐릭터가 바뀐 경우는 튀지 않게 그냥 맞춘다
        if (index != handsShownIndex)
        {
            handsShownIndex = index;
            lastLeftHand = state.leftHand;
            lastRightHand = state.rightHand;
        }
        if (state.leftHand != lastLeftHand) { lastLeftHand = state.leftHand; leftPopAt = Time.unscaledTime; }
        if (state.rightHand != lastRightHand) { lastRightHand = state.rightHand; rightPopAt = Time.unscaledTime; }

        const float size = 72f; // 소지품 칸과 같은 크기
        const float gap = 10f;
        float y = bottom - 9f - size; // 왼쪽 앞 카드와 아랫변을 맞춘다
        HandCard(new Rect(width - 20f - size * 2f - gap, y, size, size), "L", state.leftHand, leftPopAt);
        HandCard(new Rect(width - 20f - size, y, size, size), "R", state.rightHand, rightPopAt);
    }

    private void HandCard(Rect rect, string hand, string item, float popAt)
    {
        float t = (Time.unscaledTime - popAt) / .25f;
        if (t >= 0f && t < 1f)
        {
            float grow = Mathf.Sin(t * Mathf.PI) * .14f * rect.width;
            rect = new Rect(rect.x - grow * .5f, rect.y - grow, rect.width + grow, rect.height + grow);
        }

        bool holding = item != EmptyHand && !string.IsNullOrEmpty(item);
        if (holding)
        {
            GUI.color = new Color(0f, 0f, 0f, .4f);
            GUI.Box(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), GUIContent.none, roundedStyle);
            GUI.color = new Color(.14f, .17f, .21f);
            GUI.Box(rect, GUIContent.none, roundedStyle);
            GUI.color = clockAccentColor;
            GUI.DrawTexture(new Rect(rect.x + 12f, rect.yMax - 7f, rect.width - 24f, 2f), Texture2D.whiteTexture);
            textStyle.wordWrap = true;
            Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, rect.height - 8f), item.Length > 6 ? item.Substring(0, 6) : item, 12, true, FontStyle.Bold, new Color(.95f, .93f, .88f));
            textStyle.wordWrap = false;
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, .1f);
            GUI.Box(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), GUIContent.none, roundedStyle);
            GUI.color = new Color(.1f, .12f, .15f, .6f);
            GUI.Box(rect, GUIContent.none, roundedStyle);
            Label(rect, "빈손", 11, true, FontStyle.Normal, new Color(clockMutedColor.r, clockMutedColor.g, clockMutedColor.b, .75f));
        }
        KeyBadge(new Vector2(rect.x - 7f, rect.y - 7f), hand);
    }

    // 버튼 모서리에 거는 작은 키 배지 (화살표의 R 배지와 같은 모양)
    private void KeyBadge(Vector2 position, string key)
    {
        Rect badge = new Rect(position.x, position.y, 20f, 20f);
        GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .8f);
        GUI.Box(badge, GUIContent.none, roundedStyle);
        GUI.color = new Color(.1f, .12f, .15f);
        GUI.Box(new Rect(badge.x + 1.5f, badge.y + 1.5f, badge.width - 3f, badge.height - 3f), GUIContent.none, roundedStyle);
        Label(badge, key, 11, true, FontStyle.Bold, clockAccentColor);
    }

    private void CharacterPanel(float x, float bottom, int index, string character, Inventory inventory, int capacity, CharacterUiState state)
    {
        const float height = 135f;
        const float foldedWidth = 186f;
        const float slotSize = 72f; // 화살표 버튼 높이와 같게 (카드 높이의 약 3/4)
        const float slotGap = 8f;
        float itemsWidth = 10f + capacity * (slotSize + slotGap);
        float open = Mathf.SmoothStep(0f, 1f, inventoryOpenAmount);
        float top = bottom - height;
        RoundedPanel(new Rect(x, top, foldedWidth + itemsWidth * open, height), characterPanelColor);
        NoticeToast(x + 12f, top - 24f);

        // 조작하지 않는 캐릭터는 뒤에 작게, 조작 중인 캐릭터는 앞에 겹쳐서 크게
        int other = (index + 1) % manager.players.Length;
        // 카드 = 위쪽 정사각형 초상화 + 아래쪽 이름 칸
        Rect back = new Rect(x + 12, top + 8, 60, 75);
        Rect front = new Rect(x + 36, top + 26, 80, 100);
        float s = Mathf.SmoothStep(0f, 1f, swapProgress);
        float arc = Mathf.Sin(swapProgress * Mathf.PI);
        Rect incoming = LerpRect(back, front, s); // 새로 조작하는 캐릭터: 뒤에서 앞으로
        incoming.x += arc * 22f;
        incoming.y -= arc * 4f;
        Rect outgoing = LerpRect(front, back, s); // 방금까지 조작하던 캐릭터: 앞에서 뒤로
        outgoing.x -= arc * 10f;
        if (swapProgress >= .5f)
        {
            Portrait(outgoing, other, 1f - s);
            Label(new Rect(outgoing.x + 4, outgoing.y + 2, 30, 16), "Tab", 10, false, FontStyle.Bold, clockMutedColor);
            Portrait(incoming, index, s);
        }
        else
        {
            Portrait(incoming, index, s);
            Portrait(outgoing, other, 1f - s);
        }

        // 화살표를 누르면 소지품이 펼쳐지고 접힌다
        Rect toggle = new Rect(x + 126, front.yMax - slotSize, 50, slotSize); // 앞 초상화 아래쪽 끝에 맞춤
        bool hover = toggle.Contains(Event.current.mousePosition);
        GUI.color = hover ? new Color(.2f, .23f, .28f, .95f) : new Color(.1f, .12f, .15f, .9f);
        GUI.Box(toggle, GUIContent.none, roundedStyle);
        Chevron(new Vector2(toggle.center.x, toggle.center.y - 5f), 7f, open > .5f, clockAccentColor);
        // 아래쪽 작은 마름모 점: 찬 칸은 앰버, 빈 칸은 흐리게 (접혀 있어도 소지품이 얼마나 찼는지 보임)
        float dotGap = Mathf.Min(7f, (toggle.width - 10f) / Mathf.Max(1, capacity));
        float dotsLeft = toggle.center.x - dotGap * (capacity - 1) * .5f;
        for (int i = 0; i < capacity; i++)
        {
            Color dot = i < inventory.items.Count ? clockAccentColor : new Color(clockMutedColor.r, clockMutedColor.g, clockMutedColor.b, .3f);
            Diamond(new Vector2(dotsLeft + i * dotGap, toggle.yMax - 11f), 4f, dot);
        }
        // 오른쪽 위 모서리에 걸친 작은 R 배지: R로 소지품을 고르고 장착한다
        Rect badge = new Rect(toggle.xMax - 13f, toggle.y - 9f, 20f, 20f);
        GUI.color = state.selecting ? clockAccentColor : new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .8f);
        GUI.Box(badge, GUIContent.none, roundedStyle);
        Rect badgeInner = new Rect(badge.x + 1.5f, badge.y + 1.5f, badge.width - 3f, badge.height - 3f);
        GUI.color = state.selecting ? clockAccentColor : new Color(.1f, .12f, .15f);
        GUI.Box(badgeInner, GUIContent.none, roundedStyle);
        Label(badge, "R", 11, true, FontStyle.Bold, state.selecting ? new Color(.1f, .12f, .15f) : clockAccentColor);
        if (GUI.Button(toggle, GUIContent.none, GUIStyle.none))
        {
            inventoryOpen = !inventoryOpen;
            if (!inventoryOpen) state.selecting = false;
        }

        if (open <= .001f) return;
        GUI.BeginGroup(new Rect(x + foldedWidth, top, itemsWidth * open, height));
        if (state.selecting)
        {
            // 고르는 중에는 제목 자리에 조작 안내를 띄운다
            string selectedItem = state.selectedSlot < inventory.items.Count ? inventory.items[state.selectedSlot] : null;
            KeyHintsFrom(0f, toggle.y - top - 27f, inventory.items.Count + "/" + capacity, "← →", "선택", "R", IsEquipped(state, selectedItem) ? "내려놓기" : "장착", "Q", "취소");
        }
        // 화살표로 펼쳐 둔 상태에서만 제목을 보인다 (고르기를 끝내고 접히는 동안 흰 글자가 번쩍이지 않게)
        else if (inventoryOpen) ShadowLabel(new Rect(0, toggle.y - top - 24, 340, 20), "소지품  " + inventory.items.Count + " / " + capacity, 14, false, FontStyle.Bold);
        for (int i = 0; i < capacity; i++)
        {
            Rect slot = new Rect(i * (slotSize + slotGap), toggle.y - top, slotSize, slotSize);
            string item = i < inventory.items.Count ? inventory.items[i] : "";
            string hand = item == "" ? null : state.leftHand == item ? "L" : state.rightHand == item ? "R" : null;
            Slot(slot, item, state.selecting && state.selectedSlot == i, hand);
        }
        GUI.EndGroup();
    }

    private static Rect LerpRect(Rect a, Rect b, float t)
    {
        return new Rect(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
    }

    // frontness: 1 = 앞 카드(조작 중), 0 = 뒤 카드. 바뀌는 동안 그 사이 값
    private void Portrait(Rect rect, int index, float frontness)
    {
        PortraitCard(rect, PlayerColor(index), NameOf(index), frontness);
    }

    private void PortraitCard(Rect rect, Color body, string displayName, float frontness)
    {
        if (frontness > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, .45f * frontness);
            GUI.Box(new Rect(rect.x + 3, rect.y + 4, rect.width, rect.height), GUIContent.none, roundedStyle);
        }
        Color backBg = new Color(.09f, .1f, .13f);
        GUI.color = Color.Lerp(backBg, new Color(.12f, .14f, .18f), frontness);
        GUI.Box(rect, GUIContent.none, roundedStyle);
        GUI.color = Color.Lerp(Color.Lerp(backBg, body, .45f), body, frontness);
        float w = rect.width, h = rect.width; // 초상화 그림은 위쪽 정사각형 안에
        GUI.DrawTexture(new Rect(rect.x + w * .32f, rect.y + h * .15f, w * .36f, h * .36f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + w * .21f, rect.y + h * .56f, w * .58f, h * .34f), Texture2D.whiteTexture);

        // 아래쪽 이름 칸: 얇은 구분선 + 이름. 앞 카드일수록 또렷하다
        Rect nameRect = new Rect(rect.x, rect.y + h, w, rect.height - h);
        GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .15f + .3f * frontness);
        GUI.DrawTexture(new Rect(rect.x + 6, nameRect.y, w - 12, 1), Texture2D.whiteTexture);
        int nameSize = Mathf.RoundToInt(Mathf.Lerp(9f, 12f, frontness));
        Label(nameRect, displayName, nameSize, true, FontStyle.Bold, Color.Lerp(clockMutedColor * new Color(1f, 1f, 1f, .6f), new Color(.95f, .93f, .88f), frontness));
    }

    private static string NameOf(int playerIndex) => playerIndex == 0 ? "오빠" : "동생";

    // 열려 있으면 ◀, 접혀 있으면 ▶ 모양의 꺾쇠
    private static void Chevron(Vector2 center, float size, bool pointLeft, Color color)
    {
        float thickness = 3f;
        RotatedBar(new Vector2(center.x, center.y - size * .5f), size * 1.45f, thickness, pointLeft ? -45f : 45f, color);
        RotatedBar(new Vector2(center.x, center.y + size * .5f), size * 1.45f, thickness, pointLeft ? 45f : -45f, color);
    }

    private static void RotatedBar(Vector2 center, float length, float thickness, float angle, Color color)
    {
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = oldMatrix * Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - length * .5f, center.y - thickness * .5f, length, thickness), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
    }

    private void Slot(Rect rect, string item, bool selected, string hand = null)
    {
        GUI.color = selected ? new Color(.8f, .6f, .16f) : new Color(.14f, .17f, .21f); GUI.Box(rect, GUIContent.none, roundedStyle);
        if (hand != null)
        {
            // 손에 든 아이템: 칸 테두리 + 오른쪽 위 L/R 표시
            if (!selected)
            {
                GUI.color = new Color(clockAccentColor.r, clockAccentColor.g, clockAccentColor.b, .55f);
                GUI.Box(new Rect(rect.x - 1.5f, rect.y - 1.5f, rect.width + 3, rect.height + 3), GUIContent.none, roundedStyle);
                GUI.color = new Color(.14f, .17f, .21f);
                GUI.Box(rect, GUIContent.none, roundedStyle);
            }
            Rect tag = new Rect(rect.xMax - 17, rect.y + 3, 14, 14);
            GUI.color = selected ? new Color(.1f, .12f, .15f) : clockAccentColor;
            GUI.Box(tag, GUIContent.none, roundedStyle);
            Label(tag, hand, 10, true, FontStyle.Bold, selected ? clockAccentColor : new Color(.1f, .12f, .15f));
        }
        if (!string.IsNullOrEmpty(item)) Label(new Rect(rect.x + 2, rect.y, rect.width - 4, rect.height), item.Length > 5 ? item.Substring(0, 5) : item, 12, true);
    }

    private void HandSlot(Rect rect, string title, string item)
    {
        GUI.color = new Color(.14f, .17f, .21f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
        Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 16, 20), title, 12);
        Label(new Rect(rect.x + 8, rect.y + 31, rect.width - 16, 25), item, 14);
    }

    private static void Panel(Rect rect)
    {
        GUI.color = new Color(.045f, .055f, .075f, .94f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    // 배경 없이 밝은 맵 위에 올라가는 글자용: 검은 그림자를 먼저 깐다
    private void ShadowLabel(Rect rect, string value, int size, bool centered = false, FontStyle fontStyle = FontStyle.Normal)
    {
        Color shadow = new Color(0f, 0f, 0f, .85f);
        Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), value, size, centered, fontStyle, shadow);
        Label(new Rect(rect.x - 1, rect.y + 1, rect.width, rect.height), value, size, centered, fontStyle, shadow);
        Label(rect, value, size, centered, fontStyle);
    }

    private void Label(Rect rect, string value, int size, bool centered = false, FontStyle fontStyle = FontStyle.Normal, Color? color = null)
    {
        GUIStyle style = centered ? centeredStyle : textStyle;
        style.fontSize = size;
        style.fontStyle = fontStyle;
        style.normal.textColor = Color.white;
        style.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
        GUI.color = color ?? Color.white;
        GUI.Label(rect, value, style);
    }
}
