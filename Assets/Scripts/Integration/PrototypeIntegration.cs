using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using NarrativeGraybox.CameraSystem;
using NarrativeGraybox.Interaction;
using NarrativeGraybox.UI;

// 팀원 그레이박스(HUD·조사·카메라 연출)와 내 게임 시스템(2인 전환·NavMesh 이동·구역 카메라·인벤토리·문)을 잇는 스크립트.
// 두 시스템이 서로를 직접 알 필요가 없도록, 연결은 전부 여기서 합니다.
[DefaultExecutionOrder(50)] // 캐릭터·조사 스크립트가 이번 프레임 입력을 처리한 뒤에 상태를 맞춥니다.
public class PrototypeIntegration : MonoBehaviour
{
    [System.Serializable]
    public class Objective
    {
        [TextArea] public string text;
        [Tooltip("누군가 이 아이템을 가지고 있으면 완료")]
        public string completeWhenItemHeld;
        [Tooltip("이 문이 열려 있으면 완료")]
        public InteractableDoor completeWhenDoorOpen;

        public bool IsComplete(GameObject[] players)
        {
            if (completeWhenDoorOpen != null) return completeWhenDoorOpen.IsOpen;
            if (string.IsNullOrEmpty(completeWhenItemHeld)) return false;
            foreach (GameObject player in players)
                if (player.GetComponent<Inventory>().HasItem(completeWhenItemHeld)) return true;
            return false;
        }
    }

    [Header("캐릭터 표시 이름 (CharacterManager.players 순서)")]
    public string[] characterNames = { "탐정", "조수" };

    [Header("팀원 그레이박스 쪽")]
    public SimpleGameHud hud;
    public IsometricCameraFollow inspectionCamera; // 조사할 때 클로즈업 연출
    public CameraOcclusionFader occlusionFader;    // 캐릭터를 가리는 벽 숨김

    [Header("내 시스템 쪽")]
    public CinemachineBrain cameraBrain;            // 평소 화면은 구역별 고정 카메라(탑뷰/아이소메트릭)가 담당
    public DialogueManager dialogue;                // 독백·단서 문구 (없으면 HUD 알림으로 표시)

    [Header("소지품 칸 수 (HUD 표시와 같게)")]
    public int inventorySize = 5;

    [Header("진행 목표 (위에서부터 순서대로)")]
    public Objective[] objectives;
    public string completeMessage = "프로토타입 목표 달성!";

    private CameraTrigger[] rooms;
    private GameObject currentActive;
    private bool wasInspecting;
    private bool restoringCamera;
    private bool completeShown;
    private GUIStyle labelStyle;
    private GUIStyle centeredStyle;

    private GameObject[] Players => CharacterManager.instance.players;

    void OnEnable()
    {
        GameMessage.Shown += OnMessage;
        GameMessage.PromptChanged += OnPrompt;
    }

    void OnDisable()
    {
        GameMessage.Shown -= OnMessage;
        GameMessage.PromptChanged -= OnPrompt;
    }

    void Start()
    {
        rooms = FindObjectsByType<CameraTrigger>(FindObjectsSortMode.None);
        hud.AddItemHandler = AddToActiveInventory;
        inspectionCamera.enabled = false;

        // 근처에 아이템·문이 있으면 내 상호작용(E)이 조사·대화보다 우선합니다.
        foreach (GameObject player in Players)
        {
            PlayerInteraction interaction = player.GetComponent<PlayerInteraction>();
            player.GetComponent<PlayerInteractor3D>().HasPriorityInteraction = () => interaction.HasTarget;
        }
    }

    void Update()
    {
        GameObject active = CharacterManager.instance.GetActivePlayer();
        if (active != currentActive) OnActivePlayerChanged(active);

        bool fading = FadeManager.instance.isFading;
        PlayerInteractor3D interactor = active.GetComponent<PlayerInteractor3D>();
        interactor.ExternalLock = fading; // 화면 전환 중에는 조사·대화를 시작하지 않음

        bool busy = interactor.IsBusy || hud.Selecting || restoringCamera;
        CharacterManager.instance.inputLocked = busy;
        active.GetComponent<PlayerInteraction>().inputLocked = busy;
        active.GetComponent<PlayerMovement>().SetMovementEnabled(!busy);

        if (interactor.IsInspecting != wasInspecting)
        {
            wasInspecting = interactor.IsInspecting;
            if (wasInspecting) BeginInspectionCamera();
            else StartCoroutine(EndInspectionCamera());
        }

        SyncHud(active);
        KeepRoomCameraInSync(active);

        if (!completeShown && objectives.Length > 0 && CurrentObjective() == null)
        {
            completeShown = true;
            GameMessage.Show(completeMessage);
        }
    }

    void OnActivePlayerChanged(GameObject active)
    {
        currentActive = active;

        // 조사·대화는 조작 중인 캐릭터만 받습니다.
        foreach (GameObject player in Players)
            player.GetComponent<PlayerInteractor3D>().enabled = player == active;

        occlusionFader.Configure(active.transform);
        hud.SetInteractionPrompt("");
    }

    void SyncHud(GameObject active)
    {
        int index = CharacterManager.instance.ActivePlayerIndex;
        hud.SetCharacter(index < characterNames.Length ? characterNames[index] : active.name);
        hud.SetInventory(active.GetComponent<Inventory>().items);

        // 구역이 겹치면(복도 안의 손님방 등) 더 작은 구역 이름을 씁니다.
        CameraTrigger here = null;
        foreach (CameraTrigger room in rooms)
            if (room.Contains(active) && (here == null || Volume(room) < Volume(here))) here = room;
        if (here != null) hud.SetLocation(here.roomName);
    }

    static float Volume(CameraTrigger room)
    {
        Vector3 size = room.GetComponent<Collider>().bounds.size;
        return size.x * size.y * size.z;
    }

    // 페이드 도중 다른 구역에 들어가 전환이 건너뛰어진 경우를 바로잡는 안전장치
    void KeepRoomCameraInSync(GameObject active)
    {
        if (FadeManager.instance.isFading || restoringCamera || wasInspecting) return;
        foreach (CameraTrigger room in rooms)
            if (room.virtualCamera.activeSelf != room.Contains(active)) room.UpdateCameraStateInstant();
    }

    // 조사 시작: 구역 카메라를 잠시 멈추고 팀원의 클로즈업 카메라에 넘깁니다.
    void BeginInspectionCamera()
    {
        cameraBrain.enabled = false;
        inspectionCamera.enabled = true;
    }

    // 조사 끝: 페이드로 가리고 구역 카메라로 돌아옵니다.
    IEnumerator EndInspectionCamera()
    {
        restoringCamera = true;
        FadeManager fade = FadeManager.instance;
        fade.isFading = true;
        yield return StartCoroutine(fade.FadeOut());

        inspectionCamera.enabled = false;
        cameraBrain.enabled = true;
        yield return null;

        yield return StartCoroutine(fade.FadeIn());
        fade.isFading = false;
        restoringCamera = false;
    }

    // 조사로 얻은 아이템(황동 열쇠 등)은 조작 중인 캐릭터의 인벤토리로 들어갑니다.
    bool AddToActiveInventory(string itemName)
    {
        Inventory inventory = CharacterManager.instance.GetActivePlayer().GetComponent<Inventory>();
        if (inventory.items.Count >= inventorySize) return false;
        inventory.AddItem(itemName);
        return true;
    }

    public Objective CurrentObjective()
    {
        foreach (Objective objective in objectives)
            if (!objective.IsComplete(Players)) return objective;
        return null;
    }

    // 게임 메시지는 조작 중인 캐릭터의 독백으로 대사 패널에 띄웁니다.
    void OnMessage(string message, Sprite hint)
    {
        if (dialogue != null)
        {
            int index = CharacterManager.instance.ActivePlayerIndex;
            dialogue.Say(index < characterNames.Length ? characterNames[index] : "", message, hint);
        }
        else if (hud != null) hud.ShowMessage(message);
    }

    void OnPrompt(string message)
    {
        if (hud != null) hud.SetInteractionPrompt(message);
    }

    // 목표·조작법 표시 (팀원 HUD와 같은 1280x720 기준 배율)
    void OnGUI()
    {
        if (hud == null || !hud.enabled) return; // 조사·대화 중에는 HUD처럼 숨김

        float scale = Mathf.Max(0.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            labelStyle.normal.textColor = Color.white;
            centeredStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
        }

        Objective current = CurrentObjective();
        GUI.color = new Color(0.045f, 0.055f, 0.075f, 0.93f);
        GUI.DrawTexture(new Rect(24, 128, 280, 80), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.8f, 0.25f);
        GUI.Label(new Rect(40, 136, 248, 22), "목표", labelStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(40, 158, 256, 48), current != null ? current.text : completeMessage, labelStyle);

        float width = Screen.width / scale;
        GUI.color = new Color(1f, 1f, 1f, 0.8f);
        GUI.Label(new Rect(width * 0.5f - 320, 30, 640, 24),
            "WASD 이동    E 상호작용 · 조사    Tab 캐릭터 전환    R 소지품", centeredStyle);

        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }
}
