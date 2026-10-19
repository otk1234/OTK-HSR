using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("이 캐릭터는 누구인가?")]
    public CharacterType myType = CharacterType.Player1;

    
    private InteractableItem nearbyItem = null;
    private InteractableDoor nearbyDoor = null; 

    private Inventory myInventory;

    void Start()
    {
        myInventory = GetComponent<Inventory>();
    }

    void Update()
    {
        if (FadeManager.instance != null && FadeManager.instance.isFading) return;
        if (CharacterManager.instance.GetActivePlayer() != gameObject) return;

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
                    Debug.Log($"[{gameObject.name}] {nearbyItem.failMessage}");
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
            Debug.Log($"[{nearbyItem.itemName}] E키로 획득.");
        }

        else if (other.CompareTag("Door"))
        {
            nearbyDoor = other.GetComponent<InteractableDoor>();
            Debug.Log("E키로 조사.");
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