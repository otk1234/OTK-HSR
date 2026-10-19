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

    public void PickUp(Inventory playerInventory)
    {
        playerInventory.AddItem(itemName);
        Destroy(gameObject);
    }
}