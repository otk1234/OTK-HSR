using UnityEngine;

namespace NarrativeGraybox.UI
{
    public sealed class SimpleGameHud : MonoBehaviour
    {
        private string interactionMessage = string.Empty;
        [SerializeField] private string date = "1일차";
        [SerializeField] private string time = "09:00";
        [SerializeField] private string location = "테스트 구역 · 복도";
        [SerializeField] private string character = "현재 캐릭터";
        [SerializeField] private bool twoHanded;
        [SerializeField] private string leftHand = "비어 있음";
        [SerializeField] private string rightHand = "비어 있음";
        [SerializeField] private string bothHands = "양손 물품";
        private readonly string[] inventory = new string[5];
        private GUIStyle labelStyle;
        private bool selecting;
        private int selectedSlot;
        private readonly bool[] equipped = new bool[5];
        public bool FlashlightEquipped => leftHand == "손전등" || rightHand == "손전등";
        public bool Selecting => selecting;
        public int SelectedSlot => selectedSlot; // [OTK 통합] 건네주기(G)가 선택 칸을 읽음
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Q)) selecting = false;
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (!selecting) { selecting = true; selectedSlot = 0; }
                else { EquipSelected(); selecting = false; }
            }
            if (!selecting) return;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) selectedSlot = (selectedSlot + 4) % 5;
            if (Input.GetKeyDown(KeyCode.RightArrow)) selectedSlot = (selectedSlot + 1) % 5;
        }
        public bool EquipSelected()
        {
            if (inventory[selectedSlot] != "손전등" || equipped[selectedSlot] || twoHanded) return false;
            if (leftHand == "비어 있음") leftHand = inventory[selectedSlot];
            else if (rightHand == "비어 있음") rightHand = inventory[selectedSlot];
            else return false;
            equipped[selectedSlot] = true;
            return true;
        }
        private void OnDisable() => selecting = false;

        public void SetInteractionPrompt(string message) => interactionMessage = message;
        public void SetLocation(string value) => location = value;

        // [OTK 통합] 외부 게임 시스템(캐릭터별 인벤토리, 알림)과 연결 --------------------
        public System.Func<string, bool> AddItemHandler; // 설정되면 획득 아이템을 이쪽으로 넘긴다.
        public string InventoryFullMessage = "소지품이 가득 찼습니다. (5 / 5)"; // [OTK 통합] 가득 참 문구 (연결 쪽에서 캐릭터 칸 수로 바꿔 줌)
        private string toastMessage = string.Empty;
        private float toastUntil;
        public void SetCharacter(string value) => character = value;
        public void SetInventory(System.Collections.Generic.IList<string> items)
        {
            for (int i = 0; i < inventory.Length; i++)
                inventory[i] = items != null && i < items.Count ? items[i] : null;
        }
        public void ShowMessage(string message, float seconds = 2.5f)
        {
            toastMessage = message;
            toastUntil = Time.unscaledTime + seconds;
        }
        // ------------------------------------------------------------------------------

        public bool TryAddItem(string itemName)
        {
            if (AddItemHandler != null) // [OTK 통합]
            {
                bool added = AddItemHandler(itemName);
                ShowMessage(added ? itemName + "을(를) 획득했다." : InventoryFullMessage); // [OTK 통합] 가득 참 문구 통일
                return added;
            }
            for (int i = 0; i < inventory.Length; i++)
            {
                if (!string.IsNullOrEmpty(inventory[i])) continue;
                inventory[i] = itemName;
                interactionMessage = itemName + "을(를) 획득했다.";
                return true;
            }
            interactionMessage = "소지품이 가득 찼다.";
            return false;
        }

        private void OnGUI()
        {
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Max(0.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            float width = Screen.width / scale;
            float bottom = Screen.height / scale - 24f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label);
            Panel(new Rect(24, 24, 280, 94));
            Text(new Rect(40, 36, 248, 28), date + "    " + time, 20);
            Text(new Rect(40, 76, 248, 24), location, 16);
            Panel(new Rect(width - 240, 24, 96, 60));
            Panel(new Rect(width - 132, 24, 108, 60));
            Text(new Rect(width - 222, 40, 76, 28), "지도", 18);
            Text(new Rect(width - 112, 40, 88, 28), "메뉴", 18);
            Panel(new Rect(24, bottom - 126, 418, 126));
            Panel(new Rect(38, bottom - 110, 72, 76), true);
            GUI.color = new Color(0.2f, 0.55f, 0.85f);
            GUI.DrawTexture(new Rect(61, bottom - 99, 26, 26), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(50, bottom - 68, 48, 28), Texture2D.whiteTexture);
            Text(new Rect(34, bottom - 30, 88, 24), character, 12);
            int itemCount = 0;
            for (int i = 0; i < inventory.Length; i++)
                if (!string.IsNullOrEmpty(inventory[i])) itemCount++;
            Text(new Rect(128, bottom - 110, 292, 26), "소지품  " + itemCount + " / 5", 16);
            for (int i = 0; i < 5; i++)
            {
                Panel(new Rect(128 + i * 58, bottom - 73, 50, 50), true);
                if (selecting && selectedSlot == i)
                {
                    GUI.color = new Color(1f,.8f,.25f);
                    float x = 128 + i * 58, y = bottom - 73;
                    GUI.DrawTexture(new Rect(x,y,50,3),Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x,y+47,50,3),Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x,y,3,50),Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x+47,y,3,50),Texture2D.whiteTexture);
                }
                Text(new Rect(132 + i * 58, bottom - 72, 20, 20), (i + 1).ToString(), 11);
                if (!string.IsNullOrEmpty(inventory[i]))
                    Text(new Rect(132 + i * 58, bottom - 54, 42, 30), ShortName(inventory[i]) + (equipped[i] ? " ●" : ""), 11);
            }
            float equipmentX = width - 320 - 16;
            Panel(new Rect(equipmentX, bottom - 126, 320, 126));
            Text(new Rect(equipmentX + 14, bottom - 115, 292, 24), "현재 장착", 16);
            if (twoHanded)
                Slot(new Rect(equipmentX + 14, bottom - 78, 292, 64), "왼손 + 오른손 · 양손 사용", bothHands);
            else
            {
                Slot(new Rect(equipmentX + 14, bottom - 78, 140, 64), "왼손", leftHand);
                Slot(new Rect(equipmentX + 166, bottom - 78, 140, 64), "오른손", rightHand);
            }
            // [OTK 통합] 알림(toast)이 떠 있는 동안은 상호작용 안내보다 알림을 먼저 보여준다.
            string shownMessage = Time.unscaledTime < toastUntil ? toastMessage : interactionMessage;
            if (!string.IsNullOrWhiteSpace(shownMessage))
            {
                Rect prompt = new Rect((width - 440) * 0.5f, bottom - 186, 440, 44);
                Panel(prompt);
                Text(new Rect(prompt.x + 16, prompt.y + 8, 408, 28), shownMessage, 16);
            }
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void Panel(Rect rect, bool slot = false)
        {
            GUI.color = slot ? new Color(0.14f, 0.17f, 0.21f, 0.98f) : new Color(0.045f, 0.055f, 0.075f, 0.93f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        private void Text(Rect rect, string value, int size)
        {
            GUI.color = Color.white;
            labelStyle.fontSize = size;
            labelStyle.normal.textColor = Color.white;
            GUI.Label(rect, value, labelStyle);
        }

        private void Slot(Rect rect, string title, string item)
        {
            Panel(rect, true);
            Text(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, 22), title, 13);
            Text(new Rect(rect.x + 8, rect.y + 32, rect.width - 16, 28), item, 15);
        }

        private static string ShortName(string value)
        {
            return value.Length <= 4 ? value : value.Substring(0, 4);
        }

    }
}
