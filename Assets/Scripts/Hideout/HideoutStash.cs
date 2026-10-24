using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 아지트 소지품 상자: 남매 공용, 칸 제한 없음, 아지트에서만 씀 (사용자 결정 2026-10-24). 내용은 GameSession에 보관.
// 넣기: 조작 중인 캐릭터의 소지품 → 상자 (아이템 조건도 같이). 꺼내기: 그 캐릭터가 다룰 수 있어야 하고(공구 상자 = 힘) 칸이 남아야 함.
// E로 상자를 확대해서 열고, 개발용 창: ←/→ 소지품·상자 고르기, ↑/↓ 물건 고르기, Space/Enter 옮기기, Q 돌아가기.
public class HideoutStash : HideoutStation
{
    public enum Result { Ok, NoItem, CannotHandle, Full }

    protected override string Title => "소지품 상자";
    protected override bool CenteredWindow => true;
    protected override string Controls => "←/→ 소지품·상자   ↑/↓ 고르기   Space 옮기기";

    private bool stashSide;     // false = 내 소지품 쪽, true = 상자 쪽
    private int cursor;
    private string lastMessage = "";

    // ---------------- 규칙 (점검·다른 코드에서도 씀) ----------------

    public static Result Deposit(Inventory inventory, int index)
    {
        if (inventory == null || index < 0 || index >= inventory.items.Count) return Result.NoItem;
        string itemName = inventory.items[index];
        Inventory.ItemRule rule = inventory.RuleOf(itemName);
        inventory.RemoveItem(itemName);
        GameSession.AddToStash(itemName, rule);
        return Result.Ok;
    }

    public static Result Withdraw(Inventory inventory, int index)
    {
        IReadOnlyList<GameSession.StoredItem> stash = GameSession.Stash;
        if (inventory == null || index < 0 || index >= stash.Count) return Result.NoItem;
        CharacterType who = CharacterRoster.TypeOf(inventory.gameObject);
        if (!Inventory.CanHandle(who, stash[index].rule)) return Result.CannotHandle;
        if (inventory.IsFull) return Result.Full;
        GameSession.RemoveFromStash(index, out GameSession.StoredItem item);
        inventory.AddItem(item.name, item.rule);
        return Result.Ok;
    }

    public static string MessageFor(Result result, string itemName, Inventory inventory) => result switch
    {
        Result.Ok => "",
        Result.CannotHandle => $"{itemName}은(는) 내가 다룰 수 없다...",
        Result.Full => $"{Inventory.FullMessage} ({inventory.items.Count} / {inventory.Capacity})",
        _ => "",
    };

    // ---------------- 개발용 창 ----------------

    protected override void OnOpened()
    {
        stashSide = false;
        cursor = 0;
        lastMessage = "";
    }

    Inventory MyInventory => Inspector.GetComponent<Inventory>();
    int SideCount => stashSide ? GameSession.Stash.Count : MyInventory.items.Count;

    protected override void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)) { stashSide = !stashSide; cursor = 0; }
        if (Input.GetKeyDown(KeyCode.UpArrow)) cursor--;
        if (Input.GetKeyDown(KeyCode.DownArrow)) cursor++;
        cursor = Mathf.Clamp(cursor, 0, Mathf.Max(0, SideCount - 1));

        if (!Input.GetKeyDown(KeyCode.Space) && !Input.GetKeyDown(KeyCode.Return)) return;
        if (SideCount == 0) return;
        Inventory inventory = MyInventory;
        if (stashSide)
        {
            string itemName = GameSession.Stash[cursor].name;
            Result result = Withdraw(inventory, cursor);
            lastMessage = result == Result.Ok ? $"{itemName}을(를) 꺼냈다." : MessageFor(result, itemName, inventory);
        }
        else
        {
            string itemName = inventory.items[cursor];
            Deposit(inventory, cursor);
            lastMessage = $"{itemName}을(를) 상자에 넣었다.";
        }
        cursor = Mathf.Clamp(cursor, 0, Mathf.Max(0, SideCount - 1));
    }

    protected override string BuildText()
    {
        Inventory inventory = MyInventory;
        var text = new StringBuilder();
        text.AppendLine($"{Header(!stashSide)}내 소지품 ({inventory.items.Count} / {inventory.Capacity})</b>");
        if (inventory.items.Count == 0) text.AppendLine("   (비어 있음)");
        for (int i = 0; i < inventory.items.Count; i++)
            text.AppendLine($"{Pointer(!stashSide, i)}{inventory.items[i]}{RuleLabel(inventory.RuleOf(inventory.items[i]))}");

        text.AppendLine();
        IReadOnlyList<GameSession.StoredItem> stash = GameSession.Stash;
        text.AppendLine($"{Header(stashSide)}상자 ({stash.Count}개, 남매 공용)</b>");
        if (stash.Count == 0) text.AppendLine("   (비어 있음)");
        CharacterType who = UserType;
        for (int i = 0; i < stash.Count; i++)
        {
            string dim = Inventory.CanHandle(who, stash[i].rule) ? "" : " <color=#888888>(못 다룸)</color>";
            text.AppendLine($"{Pointer(stashSide, i)}{stash[i].name}{RuleLabel(stash[i].rule)}{dim}");
        }
        if (lastMessage.Length > 0) text.Append($"\n<color=#ffd27f>{lastMessage}</color>");
        return text.ToString().TrimEnd();
    }

    static string Header(bool active) => active ? "<color=#7fd0ff>▶ </color><b>" : "   <b>";
    string Pointer(bool sideActive, int i) => sideActive && i == cursor ? "  <color=#7fd0ff>›</color> " : "     ";
}
