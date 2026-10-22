using System.Collections.Generic;
using UnityEngine;
using NarrativeGraybox.Interaction;
using NarrativeGraybox.UI;

// 기획 요구사항을 개발자에게 전달하기 위한 복제 씬 전용 더미 시스템입니다.
// 원본 Prototype 씬과 실제 저장 데이터에는 영향을 주지 않습니다.
[DefaultExecutionOrder(100)]
public sealed class PlannerReferenceDemo : MonoBehaviour
{
    private sealed class CharacterUiState
    {
        public string leftHand = "비어 있음";
        public string rightHand = "비어 있음";
        public int selectedSlot;
        public bool selecting;
    }

    private readonly Dictionary<GameObject, CharacterUiState> states = new Dictionary<GameObject, CharacterUiState>();
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
        // [OTK 통합] 남매 이름·칸 수는 캐릭터 프로필(CharacterRoster)에서 가져오므로 EnableSiblingCharacterRules 호출 삭제
        integration.EnableOverviewCameraFollow();
        PrototypeFloorTravel.EnsureCreated(integration);
        CreateSaveBed();

        for (int i = 0; i < manager.players.Length; i++)
        {
            GameObject player = manager.players[i];
            states[player] = new CharacterUiState();
            Inventory inventory = player.GetComponent<Inventory>();
            if (inventory.items.Count == 0)
            {
                inventory.items.Add(i == 0 ? "손전등" : "공구 상자");
                inventory.items.Add(i == 0 ? "사건 메모" : "응급 키트");
            }
        }
    }

    private void Update()
    {
        if (manager == null || FadeManager.instance == null || FadeManager.instance.isFading) return;
        GameObject active = manager.GetActivePlayer();
        CharacterUiState state = states[active];
        Inventory inventory = active.GetComponent<Inventory>();

        if (Input.GetKeyDown(KeyCode.Q)) state.selecting = false;
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!state.selecting)
            {
                state.selecting = true;
                state.selectedSlot = Mathf.Clamp(state.selectedSlot, 0, Mathf.Max(0, inventory.items.Count - 1));
            }
            else
            {
                EquipSelected(active);
                state.selecting = false;
            }
        }
        if (state.selecting && inventory.items.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow)) state.selectedSlot = (state.selectedSlot + inventory.items.Count - 1) % inventory.items.Count;
            if (Input.GetKeyDown(KeyCode.RightArrow)) state.selectedSlot = (state.selectedSlot + 1) % inventory.items.Count;
        }

        if (IsNearSaveBed(active))
        {
            if (Input.GetKeyDown(KeyCode.E)) PerformDummySave();
        }
    }

    public bool EquipSelected(GameObject player)
    {
        Inventory inventory = player.GetComponent<Inventory>();
        CharacterUiState state = states[player];
        if (inventory.items.Count == 0) return false;
        state.selectedSlot = Mathf.Clamp(state.selectedSlot, 0, inventory.items.Count - 1);
        string item = inventory.items[state.selectedSlot];
        if (state.leftHand == item || state.rightHand == item) return false;
        if (state.leftHand == "비어 있음") state.leftHand = item;
        else if (state.rightHand == "비어 있음") state.rightHand = item;
        else return false;
        ShowNotice(item + " 장착");
        return true;
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
        int index = manager.ActivePlayerIndex;
        CharacterUiState state = states[active];
        Inventory inventory = active.GetComponent<Inventory>();
        string character = CharacterRoster.NameOf(active); // [OTK 통합] 캐릭터 프로필 이름
        int inventoryCapacity = inventory.Capacity; // [OTK 통합] 캐릭터 프로필 칸 수
        string location = active.transform.position.x > 40 ? "저택 · 2층" : "저택 · 1층";

        Panel(new Rect(20, 20, 285, 88));
        Label(new Rect(36, 30, 250, 26), "1일차   21:40", 20);
        Label(new Rect(36, 67, 250, 24), location, 16);

        Panel(new Rect(width - 230, 20, 210, 120));
        Label(new Rect(width - 214, 27, 180, 24), "지도", 18);
        MiniMap(new Rect(width - 210, 58, 170, 66), active.transform.position.x > 40);

        float bottom = height - 20;
        Panel(new Rect(20, bottom - 135, 490, 135));
        Portrait(new Rect(35, bottom - 118, 82, 82), index);
        Label(new Rect(35, bottom - 34, 82, 24), character, 14, true);
        Label(new Rect(132, bottom - 118, 340, 24), "소지품  " + inventory.items.Count + " / " + inventoryCapacity, 16);
        for (int i = 0; i < inventoryCapacity; i++)
        {
            Rect slot = new Rect(132 + i * 56, bottom - 79, 48, 48);
            Slot(slot, i < inventory.items.Count ? inventory.items[i] : "", state.selecting && state.selectedSlot == i);
        }

        Panel(new Rect(width - 330, bottom - 135, 310, 135));
        Label(new Rect(width - 314, bottom - 124, 275, 22), character + " · 손", 16);
        HandSlot(new Rect(width - 314, bottom - 90, 132, 66), "왼손", state.leftHand);
        HandSlot(new Rect(width - 166, bottom - 90, 132, 66), "오른손", state.rightHand);

        string prompt = IsNearSaveBed(active) ? "[E] 침대에서 저장하기 (더미)" : state.selecting ? "← → 선택   [R] 장착   [Q] 취소" : "[Tab] 캐릭터 전환   [R] 소지품 장착";
        if (Time.unscaledTime < noticeUntil) prompt = notice;
        Panel(new Rect(width * .5f - 220, bottom - 57, 440, 38));
        Label(new Rect(width * .5f - 208, bottom - 52, 416, 28), prompt, 14, true);

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    private void MiniMap(Rect rect, bool upstairs)
    {
        GUI.color = new Color(.16f, .19f, .23f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = upstairs ? new Color(.55f, .65f, .78f) : new Color(.38f, .45f, .52f);
        GUI.DrawTexture(new Rect(rect.x + 8, rect.y + 8, 72, 24), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + 88, rect.y + 8, 72, 48), Texture2D.whiteTexture);
        GUI.color = new Color(1f, .75f, .2f); GUI.DrawTexture(new Rect(rect.x + (upstairs ? 120 : 40), rect.y + 25, 8, 8), Texture2D.whiteTexture);
    }

    private void Portrait(Rect rect, int index)
    {
        GUI.color = new Color(.12f, .14f, .18f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = index == 0 ? new Color(.75f, .45f, .58f) : new Color(.25f, .55f, .85f); // [OTK 통합] 0번 여동생 = 분홍
        GUI.DrawTexture(new Rect(rect.x + 26, rect.y + 12, 30, 30), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + 17, rect.y + 46, 48, 28), Texture2D.whiteTexture);
    }

    private void Slot(Rect rect, string item, bool selected)
    {
        GUI.color = selected ? new Color(.8f, .6f, .16f) : new Color(.14f, .17f, .21f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
        if (!string.IsNullOrEmpty(item)) Label(new Rect(rect.x + 4, rect.y + 15, rect.width - 8, 30), item.Length > 5 ? item.Substring(0, 5) : item, 11, true);
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

    private void Label(Rect rect, string value, int size, bool centered = false)
    {
        GUIStyle style = centered ? centeredStyle : textStyle;
        style.fontSize = size;
        GUI.color = Color.white;
        GUI.Label(rect, value, style);
    }
}
