using UnityEngine;

public enum HandoverResult { Ok, NotHeld, SamePerson, NotSamePlace, ReceiverCaught, ReceiverCannotHandle, ReceiverFull }

// 남매끼리 아이템 건네주기 (사용자 결정 2026-10-23):
//  - 같은 건물·장소(구역의 buildingId가 같음, 층은 달라도 됨)에 있을 때만. 어느 구역에도 없으면 안 됨
//  - 그 아이템의 조건(특성)을 받는 쪽도 만족해야 함 (공구 상자는 여동생에게 못 건넴)
//  - 받는 쪽 소지품이 가득 차면 안 됨, 잡힌 캐릭터에게는 안 됨 (숨어 있으면 받을 수 있음)
// 입력은 PrototypeIntegration (R 소지품 선택 중 G). 실제 화면은 UI 담당.
public static class ItemHandover
{
    public static bool SamePlace(GameObject a, GameObject b)
    {
        CameraTrigger roomA = CameraTrigger.FindAt(a.transform.position), roomB = CameraTrigger.FindAt(b.transform.position);
        return roomA != null && roomB != null && roomA.buildingId == roomB.buildingId;
    }

    public static HandoverResult Check(GameObject giver, GameObject receiver, string itemName)
    {
        if (giver == receiver) return HandoverResult.SamePerson;
        Inventory from = giver.GetComponent<Inventory>(), to = receiver.GetComponent<Inventory>();
        if (!from.HasItem(itemName)) return HandoverResult.NotHeld;
        if (!SamePlace(giver, receiver)) return HandoverResult.NotSamePlace;
        CharacterStatus status = receiver.GetComponent<CharacterStatus>();
        if (status != null && status.IsCaught) return HandoverResult.ReceiverCaught;
        if (!Inventory.CanHandle(CharacterRoster.TypeOf(receiver), from.RuleOf(itemName))) return HandoverResult.ReceiverCannotHandle;
        if (to.IsFull) return HandoverResult.ReceiverFull;
        return HandoverResult.Ok;
    }

    // 건네고 결과를 독백으로 띄움
    public static HandoverResult Give(GameObject giver, GameObject receiver, string itemName)
    {
        HandoverResult result = Check(giver, receiver, itemName);
        if (result == HandoverResult.Ok)
        {
            Inventory from = giver.GetComponent<Inventory>();
            Inventory.ItemRule rule = from.RuleOf(itemName);
            from.RemoveItem(itemName);
            receiver.GetComponent<Inventory>().AddItem(itemName, rule);
        }
        string message = MessageFor(result, CharacterRoster.NameOf(receiver), itemName);
        if (!string.IsNullOrEmpty(message)) GameMessage.Show(message);
        return result;
    }

    // 독백 문구 (임시)
    public static string MessageFor(HandoverResult result, string receiverName, string itemName)
    {
        switch (result)
        {
            case HandoverResult.Ok: return $"{itemName}을(를) {receiverName}에게 건넸다.";
            case HandoverResult.NotSamePlace: return $"{receiverName}은(는) 너무 멀리 있다. (같은 건물에 있어야 한다)";
            case HandoverResult.ReceiverCaught: return $"{receiverName}에게 건넬 수 없다.";
            case HandoverResult.ReceiverCannotHandle: return $"{receiverName}은(는) {itemName}을(를) 다룰 수 없다.";
            case HandoverResult.ReceiverFull: return $"{receiverName}의 소지품이 가득 찼다.";
            default: return "";
        }
    }
}
