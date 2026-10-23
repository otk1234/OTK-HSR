using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public enum DoorState { Closed, Open, Locked, Broken }

// 회전형 문. 닫혀 있으면 자식의 NavMeshObstacle(Carve)이 길을 막습니다(플레이어는 직접 열어야 지나감).
// 추격자는 EntryPoint의 NavMeshLink로 잠기지 않은 문을 열고 지나가고, 잠긴 문 앞에서는 막힙니다.
public class InteractableDoor : MonoBehaviour
{
    [Header("필요한 아이템 (빈칸이면 그냥 열림)")]
    public string requiredItemName = "";

    [Header("메시지 설정")]
    public string lockedMessage = "굳게 잠겨 있다.";
    public string openedMessage = "철컥 소리와 함께 문이 열렸다.";
    public string closedMessage = "문을 닫았다."; // [추가] 닫혔을 때 메시지
    public string lockMessage = "문을 잠갔다.";
    public string unlockMessage = "잠금을 풀었다.";

    [Header("문 열림 연출 설정")]
    public float openAngle = 90f;
    public float openSpeed = 2f;

    [Header("잠금 (F): 열쇠 없이 플레이어가 잠그고 풀 수 있는 문인가")]
    public bool playerLockable = false;

    [Header("부수기 내구도 (0 = 부술 수 없음)")]
    public float maxDurability = 100f;

    [Header("소음 반경")]
    public float openNoise = 5f;
    public float lockNoise = 3f;
    public float bashNoise = 14f;

    private bool isOpen = false;
    private bool isMoving = false;
    private bool isLocked;
    private bool isBroken;
    private Quaternion closedRotation;
    private NavMeshObstacle obstacle;

    public bool IsOpen => isOpen;
    public bool IsMoving => isMoving;
    public bool IsLocked => isLocked;
    public bool IsBroken => isBroken;
    public bool HasKeyLock => !string.IsNullOrEmpty(requiredItemName);
    public bool Breakable => maxDurability > 0f && !isBroken;
    public float Durability { get; private set; }

    public DoorState State =>
        isBroken ? DoorState.Broken :
        isOpen ? DoorState.Open :
        isLocked ? DoorState.Locked : DoorState.Closed;

    public event Action<InteractableDoor> StateChanged;

    void Awake()
    {
        closedRotation = transform.rotation;
        obstacle = GetComponentInChildren<NavMeshObstacle>();
        Durability = maxDurability;
        // 열쇠가 필요한 문은 잠긴 채로 시작합니다.
        isLocked = HasKeyLock;
    }


    public void Interact(Inventory playerInventory)
    {
        if (isMoving) return;

        if (isBroken)
        {
            GameMessage.Show("문이 부서져 있다.");
            return;
        }

        if (!isOpen)
        {
            if (!isLocked)
            {
                GameMessage.Show(openedMessage);
                StartCoroutine(MoveDoorRoutine(true, playerInventory.gameObject));
            }
            else if (HasKeyLock && playerInventory.HasItem(requiredItemName))
            {
                // 열쇠로 한 번 열면 잠금이 풀린 상태로 남습니다.
                isLocked = false;
                GameMessage.Show(openedMessage);
                StartCoroutine(MoveDoorRoutine(true, playerInventory.gameObject));
            }
            else
            {
                GameMessage.Show(lockedMessage);
            }
        }
        else
        {
            GameMessage.Show(closedMessage);
            StartCoroutine(MoveDoorRoutine(false, playerInventory.gameObject));
        }
    }

    // ---------------- 잠금 (F) ----------------

    public bool CanToggleLock(Inventory playerInventory)
    {
        if (isOpen || isMoving || isBroken) return false;
        return playerLockable || (HasKeyLock && playerInventory != null && playerInventory.HasItem(requiredItemName));
    }

    public void ToggleLock(Inventory playerInventory)
    {
        if (!CanToggleLock(playerInventory)) return;
        isLocked = !isLocked;
        GameMessage.Show(isLocked ? lockMessage : unlockMessage);
        NoiseSystem.Emit(transform.position, lockNoise, NoiseKind.DoorLock, playerInventory != null ? playerInventory.gameObject : gameObject);
        StateChanged?.Invoke(this);
    }

    // ---------------- 추격자용 ----------------

    // 잠기지 않은 닫힌 문을 엽니다. noiseScale: 조용히 열면 1보다 작게.
    public bool OpenByAI(GameObject who, float noiseScale = 1f)
    {
        if (isOpen || isLocked || isBroken || isMoving) return isOpen;
        StartCoroutine(MoveDoorRoutine(true, who, noiseScale));
        return true;
    }

    // 잠긴 문을 내리칩니다. 내구도가 0이 되면 부서져서 영구히 열린 상태가 됩니다.
    public void Bash(float damage, GameObject who)
    {
        if (!Breakable || isOpen) return;
        Durability = Mathf.Max(0f, Durability - damage);
        NoiseSystem.Emit(transform.position, bashNoise, NoiseKind.DoorBash, who);
        if (Durability <= 0f) Break();
        else StateChanged?.Invoke(this);
    }

    void Break()
    {
        isBroken = true;
        isLocked = false;
        StopAllCoroutines();
        isMoving = false;
        isOpen = true;
        if (obstacle != null) obstacle.enabled = false;
        // 경첩에서 떨어져 나간 것처럼 크게 젖혀 둡니다.
        transform.rotation = closedRotation * Quaternion.Euler(0f, openAngle * 1.2f, 0f);
        StateChanged?.Invoke(this);
    }

    private IEnumerator MoveDoorRoutine(bool opening, GameObject who, float noiseScale = 1f)
    {
        isMoving = true;
        isOpen = opening;
        NoiseSystem.Emit(transform.position, openNoise * noiseScale, NoiseKind.Door, who);
        StateChanged?.Invoke(this);

        if (opening && obstacle != null) obstacle.enabled = false;

        Quaternion startRotation = transform.rotation;

        Quaternion endRotation = opening ? closedRotation * Quaternion.Euler(0, openAngle, 0) : closedRotation;

        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.deltaTime * openSpeed;
            transform.rotation = Quaternion.Slerp(startRotation, endRotation, timer);
            yield return null;
        }

        transform.rotation = endRotation;


        if (!opening && obstacle != null) obstacle.enabled = true;

        isMoving = false;
        StateChanged?.Invoke(this);
    }
}
