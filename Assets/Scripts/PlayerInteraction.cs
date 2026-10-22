using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("이 캐릭터는 누구인가?")]
    public CharacterType myType = CharacterType.Player1;

    [Header("문 잠그기 / 풀기 키")]
    public KeyCode lockKey = KeyCode.F;

    // 조사·대화·소지품 선택·잡힘 중에는 이유별로 잠급니다.
    public readonly LockSet Locks = new LockSet();

    // 예전 방식 호환용
    public bool inputLocked
    {
        get => Locks.IsLocked;
        set => Locks.Set(LockReason.Legacy, value);
    }

    private InteractableItem nearbyItem = null;
    private InteractableDoor nearbyDoor = null;
    private HidingSpot nearbyHidingSpot = null;
    private CharacterInteractable nearbyInteractable = null; // 무거운 물건·기계 등

    private Inventory myInventory;
    private CharacterStatus status;

    bool InteractableReady => nearbyInteractable != null && (nearbyInteractable.Available || nearbyInteractable.Busy);

    // 근처에 줍거나 열 수 있는 대상이 있는가 (있으면 그레이박스 조사보다 먼저 처리)
    public bool HasTarget => nearbyItem != null || InteractableReady || nearbyDoor != null || nearbyHidingSpot != null || (status != null && status.IsHidden);

    void Start()
    {
        myInventory = GetComponent<Inventory>();
        status = GetComponent<CharacterStatus>();
    }

    void Update()
    {
        if (FadeManager.instance != null && FadeManager.instance.isFading) return;
        if (CharacterManager.instance.GetActivePlayer() != gameObject) return;
        if (Locks.IsLocked) return;

        // 숨어 있는 동안에는 나오기만 할 수 있습니다.
        if (status != null && status.IsHidden)
        {
            HidingSpot spot = status.HidingIn;
            GameMessage.Prompt(spot.IsBusy(status) ? "" : $"[E] {spot.displayName}에서 나오기");
            if (Input.GetKeyDown(KeyCode.E)) spot.Exit(status);
            return;
        }

        // 문과 숨는 곳이 둘 다 가까우면 더 가까운 쪽
        bool hidingFirst = nearbyHidingSpot != null &&
            (nearbyDoor == null || Distance(nearbyHidingSpot.transform) < Distance(nearbyDoor.transform));

        if (nearbyItem != null) GameMessage.Prompt($"[E] {nearbyItem.itemName} 줍기");
        else if (InteractableReady) GameMessage.Prompt(nearbyInteractable.Prompt);
        else if (hidingFirst) GameMessage.Prompt(nearbyHidingSpot.CanEnter(status) ? $"[E] {nearbyHidingSpot.displayName}에 숨기" : $"{nearbyHidingSpot.displayName}: 자리가 없다");
        else if (nearbyDoor != null) GameMessage.Prompt(DoorPrompt());

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

            // 2순위: 캐릭터 전용 상호작용 (밀기·고치기)
            else if (InteractableReady)
            {
                nearbyInteractable.Use(this);
            }

            else if (hidingFirst)
            {
                if (status != null) nearbyHidingSpot.TryEnter(status);
            }

            else if (nearbyDoor != null)
            {
                nearbyDoor.Interact(myInventory);
            }
        }
        else if (Input.GetKeyDown(lockKey) && nearbyItem == null && !hidingFirst && nearbyDoor != null)
        {
            nearbyDoor.ToggleLock(myInventory);
        }
    }

    string DoorPrompt()
    {
        string prompt = nearbyDoor.IsBroken ? "부서진 문" : nearbyDoor.IsOpen ? "[E] 문 닫기" : "[E] 문 열기";
        if (nearbyDoor.CanToggleLock(myInventory)) prompt += nearbyDoor.IsLocked ? $"   [{lockKey}] 잠금 풀기" : $"   [{lockKey}] 잠그기";
        return prompt;
    }

    float Distance(Transform target) => (target.position - transform.position).sqrMagnitude;

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

        else if (other.TryGetComponent(out CharacterInteractable interactable))
        {
            nearbyInteractable = interactable;
        }

        else
        {
            HidingSpot spot = other.GetComponent<HidingSpot>();
            if (spot != null) nearbyHidingSpot = spot;
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

        else if (nearbyInteractable != null && other.gameObject == nearbyInteractable.gameObject)
        {
            nearbyInteractable = null;
        }

        else if (nearbyHidingSpot != null && other.gameObject == nearbyHidingSpot.gameObject)
        {
            nearbyHidingSpot = null;
        }
    }
}
