using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public enum HidingKind { Closet, UnderBed, Curtain }

// 숨는 곳: 옷장·수납장 / 침대 밑 / 커튼·문 뒤.
// 숨는 동작(enterTime) 중에 추격자 시야에 들어오면 그 추격자는 '숨는 장면을 봤다'고 기억합니다.
// 추격 AI는 WasSeenEnteringBy로 확인한 뒤 Search로 뒤져서 찾아냅니다.
public class HidingSpot : MonoBehaviour
{
    public static readonly List<HidingSpot> All = new List<HidingSpot>();

    public HidingKind kind = HidingKind.Closet;
    public string displayName = "옷장";
    [Min(1)] public int capacity = 1;

    [Header("숨기 / 나오기 시간 (초)")]
    public float enterTime = 0.6f;
    public float exitTime = 0.4f;

    [Header("출입 소음 반경")]
    public float noiseRadius = 3f;

    [Tooltip("숨어 있어도 추격자가 이 거리 안으로 들어오면 바로 들킴 (0 = 직접 뒤져야만 발견). 커튼처럼 허술한 곳용")]
    public float exposedWithin = 0f;

    [Header("숨을 수 있는 캐릭터 (All = 누구나). 예: 침대 밑은 몸이 큰 오빠는 못 들어감")]
    public CharacterType onlyFor = CharacterType.All;
    [TextArea] public string notForMessage = "몸이 커서 들어갈 수 없다...";

    private readonly List<CharacterStatus> occupants = new List<CharacterStatus>();
    private readonly HashSet<CharacterStatus> moving = new HashSet<CharacterStatus>();
    private readonly HashSet<Object> witnesses = new HashSet<Object>();

    public IReadOnlyList<CharacterStatus> Occupants => occupants;
    public bool IsOccupied => occupants.Count > 0;
    public int TimesUsed { get; private set; } // 아버지 AI 학습용 (반복 사용 기억)

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public bool CanEnter(CharacterStatus who) =>
        who != null && Allows(who) && !who.IsHidden && !who.IsCaught && occupants.Count + moving.Count < capacity;

    // 이 캐릭터가 들어갈 수 있는 곳인가 (자리와 상관없이)
    public bool Allows(CharacterStatus who) =>
        who != null && CharacterRoster.CanUse(CharacterRoster.TypeOf(who.gameObject), onlyFor, CharacterTrait.None);

    // 숨거나 나오는 동작 중인가
    public bool IsBusy(CharacterStatus who) => moving.Contains(who);

    public bool WasSeenEnteringBy(Object chaser) => witnesses.Contains(chaser);

    public bool TryEnter(CharacterStatus who)
    {
        if (!CanEnter(who)) return false;
        StartCoroutine(EnterRoutine(who));
        return true;
    }

    public void Exit(CharacterStatus who)
    {
        if (!occupants.Contains(who) || moving.Contains(who)) return;
        StartCoroutine(ExitRoutine(who));
    }

    // 추격자가 이곳을 뒤짐: 숨어 있던 캐릭터 목록을 돌려줍니다.
    public List<CharacterStatus> Search(Object chaser)
    {
        return new List<CharacterStatus>(occupants);
    }

    IEnumerator EnterRoutine(CharacterStatus who)
    {
        moving.Add(who);
        PlayerMovement movement = who.GetComponent<PlayerMovement>();
        if (movement != null) movement.Locks.Set(LockReason.Hiding, true);
        NoiseSystem.Emit(transform.position, noiseRadius, NoiseKind.Hiding, who.gameObject);

        // 숨는 동작이 끝날 때까지, 이 장면을 본 추격자를 기록
        for (float t = 0f; t < enterTime; t += Time.deltaTime)
        {
            RecordWitnesses(who);
            yield return null;
        }
        RecordWitnesses(who);

        moving.Remove(who);
        occupants.Add(who);
        TimesUsed++;
        SetVisible(who, false);
        who.SetHidden(this);
    }

    IEnumerator ExitRoutine(CharacterStatus who)
    {
        moving.Add(who);
        NoiseSystem.Emit(transform.position, noiseRadius, NoiseKind.Hiding, who.gameObject);
        yield return new WaitForSeconds(exitTime);

        moving.Remove(who);
        occupants.Remove(who);
        if (occupants.Count == 0) witnesses.Clear();
        SetVisible(who, true);
        who.SetHidden(null);
        PlayerMovement movement = who.GetComponent<PlayerMovement>();
        if (movement != null) movement.Locks.Set(LockReason.Hiding, false);
    }

    void RecordWitnesses(CharacterStatus who)
    {
        foreach (ChaserSenses senses in ChaserSenses.All)
            if (senses.HasLineOfSight(who)) witnesses.Add(senses.gameObject);
    }

    static void SetVisible(CharacterStatus who, bool visible)
    {
        foreach (Renderer renderer in who.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
    }

    void Update()
    {
        // 허술한 곳(커튼): 추격자가 너무 가까이 오면 들킴
        if (exposedWithin <= 0f || occupants.Count == 0) return;
        foreach (ChaserSenses senses in ChaserSenses.All)
        {
            Vector3 offset = senses.transform.position - transform.position;
            if (new Vector2(offset.x, offset.z).magnitude <= exposedWithin) // 높이 차이는 무시 (몸 중심 vs 바닥 지점)
                witnesses.Add(senses.gameObject);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 1f, 0.5f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f, new Vector3(0.6f, 0.1f, 0.6f));
        if (exposedWithin > 0f) Gizmos.DrawWireSphere(transform.position, exposedWithin);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
