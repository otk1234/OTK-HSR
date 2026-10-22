using System.Collections;
using UnityEngine;

// E로 쓰는 캐릭터 전용 상호작용의 공통 부분 (무거운 물건 밀기, 기계 수리 …).
// 트리거 콜라이더가 있는 오브젝트에 붙이면 PlayerInteraction이 근처 대상으로 잡습니다.
// 조건에 맞지 않는 캐릭터가 쓰면 failMessage만 띄웁니다 (InteractableItem의 requiredCharacter와 같은 방식).
public abstract class CharacterInteractable : MonoBehaviour
{
    [Header("표시 이름")]
    public string displayName = "물건";

    [Header("쓸 수 있는 캐릭터")]
    public CharacterType requiredCharacter = CharacterType.All;

    [Header("조건 실패 시 출력할 메시지")]
    [TextArea] public string failMessage = "나는 할 수 없다...";

    [Header("동작할 때 나는 소리 반경 (m, 0이면 소리 없음)")]
    public float noiseRadius = 5f;

    // 아직 쓸 수 있는가 (다 밀었거나 다 고쳤으면 false → 안내도 사라짐)
    public abstract bool Available { get; }
    public bool Busy { get; protected set; }
    protected abstract string ActionLabel { get; }   // "밀기", "고치기"
    protected abstract float WorkTime { get; }
    protected abstract void OnWorkDone(PlayerInteraction user);

    public bool CanUse(CharacterType type) => requiredCharacter == CharacterType.All || requiredCharacter == type;

    public string Prompt => Busy ? $"{displayName} {ActionLabel}..." : Available ? $"[E] {displayName} {ActionLabel}" : "";

    public void Use(PlayerInteraction user)
    {
        if (!Available || Busy) return;
        if (!CanUse(user.myType))
        {
            GameMessage.Show(failMessage);
            return;
        }
        StartCoroutine(Work(user));
    }

    // 동작하는 동안 그 캐릭터의 이동·입력·캐릭터 전환을 잠급니다.
    IEnumerator Work(PlayerInteraction user)
    {
        Busy = true;
        SetLock(user, true);
        if (noiseRadius > 0f) NoiseSystem.Emit(transform.position, noiseRadius, NoiseKind.Other, user.gameObject);
        yield return WorkRoutine();
        SetLock(user, false);
        Busy = false;
        OnWorkDone(user);
    }

    // 기본: 시간만 기다림. 움직임이 있으면 덮어씀.
    protected virtual IEnumerator WorkRoutine()
    {
        yield return new WaitForSeconds(WorkTime);
    }

    static void SetLock(PlayerInteraction user, bool locked)
    {
        user.Locks.Set(LockReason.Working, locked);
        user.GetComponent<PlayerMovement>().Locks.Set(LockReason.Working, locked);
        if (CharacterManager.instance != null) CharacterManager.instance.SwitchLocks.Set(LockReason.Working, locked);
    }

    protected static void Reveal(GameObject[] objects)
    {
        if (objects == null) return;
        foreach (GameObject go in objects)
            if (go != null) go.SetActive(true);
    }
}
