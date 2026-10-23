using UnityEngine;

// 잠긴 문을 강제로 열기 (오빠의 힘). 문(InteractableDoor) 옆의 별도 트리거에 붙입니다.
//  - 잠겨 있고, 지금 캐릭터에게 열쇠가 없을 때만 안내가 뜸 (열쇠가 있으면 평소처럼 조용히 엶)
//  - 힘 특성만 가능, 동작하는 동안 이동·전환 잠김, 끝나면 큰 소리와 함께 열림
//  - 죄책감 +2 (조사를 위해 따는 것일 수도 있으므로 작게, 사용자 결정 2026-10-23)
public class ForcedEntry : CharacterInteractable
{
    public InteractableDoor door;
    public float forceTime = 2f;
    public string guiltActionId = "force_entry";
    [TextArea] public string doneMessage = "문을 억지로 열었다.";

    public override bool Available
    {
        get
        {
            if (door == null || door.IsOpen || door.IsBroken || !door.IsLocked) return false;
            GameObject active = CharacterManager.instance != null ? CharacterManager.instance.GetActivePlayer() : null;
            Inventory inventory = active != null ? active.GetComponent<Inventory>() : null;
            return !(door.HasKeyLock && inventory != null && inventory.HasItem(door.requiredItemName));
        }
    }

    protected override string ActionLabel => "강제로 열기";
    protected override float WorkTime => forceTime;

    protected override void OnWorkDone(PlayerInteraction user)
    {
        door.ForceOpen(user.gameObject);
        if (!string.IsNullOrEmpty(guiltActionId)) GuiltSystem.Record(guiltActionId, user.myType);
        if (!string.IsNullOrEmpty(doneMessage)) GameMessage.Show(doneMessage);
    }
}
