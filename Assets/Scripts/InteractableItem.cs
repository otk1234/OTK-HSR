using UnityEngine;

// 누가 주울 수 있는지
public enum CharacterType { All, Player1, Player2 }

public class InteractableItem : MonoBehaviour
{
    [Header("아이템 이름")]
    public string itemName = "HeavyBox";

    [Header("주울 수 있는 캐릭터 조건")]
    public CharacterType requiredCharacter = CharacterType.All;

    [Header("조건 실패 시 출력할 메시지")]
    public string failMessage = "너무 무거워서 나는 들 수 없다...";

    [Header("획득 시 대사 패널에 띄울 문구·힌트 이미지 (비우면 기본 문구)")]
    [TextArea] public string pickupMessage;
    public Sprite hintImage;

    public void PickUp(Inventory playerInventory)
    {
        playerInventory.AddItem(itemName);
        GameMessage.Show(string.IsNullOrEmpty(pickupMessage) ? $"{itemName}을(를) 획득했다." : pickupMessage, hintImage);
        Destroy(gameObject);
    }
}
