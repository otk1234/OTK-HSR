using UnityEngine;

// 누가 주울 수 있는지
public enum CharacterType { All, Player1, Player2 }

public class InteractableItem : MonoBehaviour
{
    [Header("아이템 이름")]
    public string itemName = "HeavyBox";

    [Header("주울 수 있는 조건: 필요한 특성 (None = 누구나)")]
    public CharacterTrait requiredTrait = CharacterTrait.None;

    [Header("(예전 방식) 이 캐릭터만 — 새로 만들 때는 특성을 쓸 것")]
    public CharacterType requiredCharacter = CharacterType.All;

    [Header("조건 실패 시 출력할 메시지")]
    public string failMessage = "너무 무거워서 나는 들 수 없다...";

    [Header("획득 시 대사 패널에 띄울 문구·힌트 이미지 (비우면 기본 문구)")]
    [TextArea] public string pickupMessage;
    public Sprite hintImage;

    public bool CanPickUp(CharacterType who) => CharacterRoster.CanUse(who, requiredCharacter, requiredTrait);

    public void PickUp(Inventory playerInventory)
    {
        playerInventory.AddItem(itemName);
        GameMessage.Show(string.IsNullOrEmpty(pickupMessage) ? $"{itemName}을(를) 획득했다." : pickupMessage, hintImage);
        Destroy(gameObject);
    }
}
