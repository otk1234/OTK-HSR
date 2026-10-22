using System.Collections;
using UnityEngine;

// E로 쓰는 캐릭터 전용 상호작용의 공통 부분 (무거운 물건 밀기, 기계 수리 …).
// 트리거 콜라이더가 있는 오브젝트에 붙이면 PlayerInteraction이 근처 대상으로 잡습니다.
// 조건(특성)에 맞지 않는 캐릭터가 쓰면 failMessage만 띄웁니다 (InteractableItem과 같은 CharacterRoster.CanUse 판정).
public abstract class CharacterInteractable : MonoBehaviour
{
    [Header("표시 이름")]
    public string displayName = "물건";

    [Header("쓸 수 있는 조건: 필요한 특성 (None = 누구나)")]
    public CharacterTrait requiredTrait = CharacterTrait.None;

    [Header("(예전 방식) 이 캐릭터만 — 새로 만들 때는 특성을 쓸 것")]
    public CharacterType requiredCharacter = CharacterType.All;

    [Header("조건 실패 시 출력할 메시지")]
    [TextArea] public string failMessage = "나는 할 수 없다...";

    [Header("동작할 때 나는 소리 반경 (m, 0이면 소리 없음)")]
    public float noiseRadius = 5f;

    // 아직 쓸 수 있는가 (다 밀었거나 다 고쳤으면 false → 안내도 사라짐)
    public abstract bool Available { get; }
    public bool Busy { get; protected set; }
    public float Progress { get; private set; }      // 0~1 (동작 중 진행도, 개발용 표시)
    public string ActionName => ActionLabel;
    protected abstract string ActionLabel { get; }   // "밀기", "고치기"
    protected abstract float WorkTime { get; }
    protected abstract void OnWorkDone(PlayerInteraction user);

    // 캐릭터마다 걸리는 시간이 다르면 덮어씀 (예: 시체 치우기는 여동생이 오래 걸림)
    public virtual float WorkTimeFor(CharacterType who) => WorkTime;

    public bool CanUse(CharacterType type) => CharacterRoster.CanUse(type, requiredCharacter, requiredTrait);

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
        Progress = 0f;
        SetLock(user, true);
        if (noiseRadius > 0f) NoiseSystem.Emit(transform.position, noiseRadius, NoiseKind.Other, user.gameObject);
        float duration = Mathf.Max(0.01f, WorkTimeFor(user.myType));
        IEnumerator routine = WorkRoutine(duration);
        for (float t = 0f; ; t += Time.deltaTime)
        {
            Progress = Mathf.Clamp01(t / duration);
            if (!routine.MoveNext()) break;
            yield return routine.Current;
        }
        Progress = 1f;
        SetLock(user, false);
        Busy = false;
        OnWorkDone(user);
    }

    // 기본: 시간만 기다림. 움직임이 있으면 덮어씀.
    protected virtual IEnumerator WorkRoutine(float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime) yield return null;
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
