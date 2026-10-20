using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("이 캐릭터는 누구인가?")]
    public CharacterType myType = CharacterType.Player1;

    // 조사·대화·소지품 선택 중에는 외부에서 잠급니다.
    [HideInInspector] public bool inputLocked = false;

    private InteractableItem nearbyItem = null;
    private InteractableDoor nearbyDoor = null;

    private Inventory myInventory;

    // 근처에 줍거나 열 수 있는 대상이 있는가 (있으면 그레이박스 조사보다 먼저 처리)
    public bool HasTarget => nearbyItem != null || nearbyDoor != null;

    void Start()
    {
        myInventory = GetComponent<Inventory>();
    }

    void Update()
    {
        if (FadeManager.instance != null && FadeManager.instance.isFading) return;
        if (CharacterManager.instance.GetActivePlayer() != gameObject) return;
        if (inputLocked) return;

        if (nearbyItem != null) GameMessage.Prompt($"[E] {nearbyItem.itemName} 줍기");
        else if (nearbyDoor != null) GameMessage.Prompt(nearbyDoor.IsOpen ? "[E] 문 닫기" : "[E] 문 열기");

        if (Input.GetKeyDown(KeyCode.E))
        {
            // 1순위: 바닥에 떨어진 아이템 줍기
            if (nearbyItem != null)
            {
                if (nearbyItem.requiredCharacter == CharacterType.All || nearbyItem.requiredCharacter == myType)
                {
                    nearbyItem.PickUp(myInventory);
                    nearbyItem = null;
                }
                else
                {
                    GameMessage.Show(nearbyItem.failMessage);
                }
            }

            else if (nearbyDoor != null)
            {
                nearbyDoor.Interact(myInventory);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Item"))
        {
            nearbyItem = other.GetComponent<InteractableItem>();
        }

        else if (other.CompareTag("Door"))
        {
            nearbyDoor = other.GetComponent<InteractableDoor>();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Item") && nearbyItem != null && other.gameObject == nearbyItem.gameObject)
        {
            nearbyItem = null;
        }

        else if (other.CompareTag("Door") && nearbyDoor != null && other.gameObject == nearbyDoor.gameObject)
        {
            nearbyDoor = null;
        }
    }
}
