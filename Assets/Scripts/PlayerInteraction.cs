using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("이 캐릭터는 누구인가?")]
    public CharacterType myType = CharacterType.Player1; // Inspector에서 설정할 내 신분증

    private InteractableItem nearbyItem = null;
    private Inventory myInventory;

    void Start()
    {
        myInventory = GetComponent<Inventory>();
    }

    void Update()
    {
        if (FadeManager.instance != null && FadeManager.instance.isFading) return;
        if (CharacterManager.instance.GetActivePlayer() != gameObject) return;

        if (Input.GetKeyDown(KeyCode.E) && nearbyItem != null)
        {

            if (nearbyItem.requiredCharacter == CharacterType.All || nearbyItem.requiredCharacter == myType)
            {
                nearbyItem.PickUp(myInventory);
                nearbyItem = null;
            }
            else
            {

                Debug.Log($"[{gameObject.name}의 독백] {nearbyItem.failMessage}");
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
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Item"))
        {
            if (nearbyItem != null && other.gameObject == nearbyItem.gameObject)
            {
                nearbyItem = null;
            }
        }
    }
}