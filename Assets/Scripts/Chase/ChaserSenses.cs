using System;
using System.Collections.Generic;
using UnityEngine;

// 추격자의 감각: 시야(시야각·거리·벽 가림)와 청각(NoiseSystem).
// 시야 안에 계속 보이면 '인지도'가 차오르고, 다 차면 Spotted. 가까울수록 빨리 찹니다.
// 씬 뷰에서 추격자를 선택하면 시야 범위·인지도·들은 소리를 볼 수 있습니다.
public class ChaserSenses : MonoBehaviour
{
    public static readonly List<ChaserSenses> All = new List<ChaserSenses>();

    [Header("시야")]
    public float viewDistance = 9f;
    [Range(10f, 360f)] public float viewAngle = 110f;
    public float eyeHeight = 0.6f; // 몸 중심(루트)에서 눈까지
    [Tooltip("등 뒤라도 이 거리 안이면 알아챔")]
    public float closeAwareness = 1.3f;
    [Tooltip("시야에 들어온 뒤 발견까지 걸리는 시간: 가까울 때 / 시야 끝에 있을 때")]
    public float timeToSpotNear = 0.15f;
    public float timeToSpotFar = 0.9f;
    [Tooltip("시야에서 벗어나면 인지도가 초당 이만큼 줄어듦")]
    public float awarenessDecay = 0.6f;
    [Tooltip("달리는 대상은 이 배수만큼 빨리 발견")]
    public float runningSpotMultiplier = 1.5f;
    public LayerMask obstacleMask = ~0;

    [Header("청각")]
    public float hearingMultiplier = 1f;
    [Tooltip("벽 너머 소리는 반경이 이 비율로 줄어듦")]
    [Range(0f, 1f)] public float wallMuffle = 0.5f;

    public event Action<CharacterStatus> Spotted;
    public event Action<NoiseEvent> Heard;

    private readonly Dictionary<CharacterStatus, float> awareness = new Dictionary<CharacterStatus, float>();
    private readonly Dictionary<CharacterStatus, Vector3> lastSeenPosition = new Dictionary<CharacterStatus, Vector3>();
    private readonly Dictionary<CharacterStatus, float> lastSeenTime = new Dictionary<CharacterStatus, float>();
    private readonly HashSet<CharacterStatus> visibleNow = new HashSet<CharacterStatus>();
    private readonly HashSet<CharacterStatus> spotted = new HashSet<CharacterStatus>();

    public NoiseEvent? LastHeard { get; private set; }
    public Vector3 Eye => transform.position + Vector3.up * eyeHeight;
    public IEnumerable<CharacterStatus> VisibleNow => visibleNow;

    void OnEnable()
    {
        All.Add(this);
        NoiseSystem.Emitted += OnNoise;
    }

    void OnDisable()
    {
        All.Remove(this);
        NoiseSystem.Emitted -= OnNoise;
    }

    public float Awareness(CharacterStatus target) => awareness.TryGetValue(target, out float value) ? value : 0f;
    public bool IsSpotted(CharacterStatus target) => spotted.Contains(target);
    public bool IsVisible(CharacterStatus target) => visibleNow.Contains(target);

    public bool TryGetLastSeen(CharacterStatus target, out Vector3 position, out float time)
    {
        time = lastSeenTime.TryGetValue(target, out float t) ? t : float.NegativeInfinity;
        return lastSeenPosition.TryGetValue(target, out position);
    }

    // 다음 발견을 새로 시작 (추격을 포기한 뒤 등)
    public void Forget(CharacterStatus target)
    {
        spotted.Remove(target);
        awareness.Remove(target);
    }

    // 지금 이 대상이 눈에 보이는가 (숨었거나 잡혔으면 false)
    public bool CanSee(CharacterStatus target)
    {
        if (target == null || !target.CanBeSeen) return false;
        return CanSeePoint(target.transform, target.transform.position);
    }

    // 숨는 장면 목격 판정용: 숨는 동작 중인 대상도 보이는지 (상태와 상관없이 기하학적으로만)
    public bool HasLineOfSight(CharacterStatus target) => target != null && CanSeePoint(target.transform, target.transform.position);

    bool CanSeePoint(Transform target, Vector3 point)
    {
        Vector3 toTarget = point - Eye;
        float distance = toTarget.magnitude;
        if (distance > viewDistance) return false;

        Vector3 flat = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        bool close = distance <= closeAwareness;
        if (!close && Vector3.Angle(transform.forward, flat) > viewAngle * 0.5f) return false;

        // 몸 중심 또는 머리 중 하나라도 가리지 않으면 보임
        return Unobstructed(target, point) || Unobstructed(target, point + Vector3.up * 0.6f);
    }

    bool Unobstructed(Transform target, Vector3 point)
    {
        if (!Physics.Linecast(Eye, point, out RaycastHit hit, obstacleMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform.IsChildOf(target) || hit.collider.transform.IsChildOf(transform);
    }

    void Update()
    {
        visibleNow.Clear();
        foreach (CharacterStatus target in CharacterStatus.All)
        {
            float value = Awareness(target);
            if (CanSee(target))
            {
                visibleNow.Add(target);
                lastSeenPosition[target] = target.transform.position;
                lastSeenTime[target] = Time.time;

                float distance = Vector3.Distance(Eye, target.transform.position);
                float spotTime = Mathf.Lerp(timeToSpotNear, timeToSpotFar, Mathf.InverseLerp(closeAwareness, viewDistance, distance));
                if (target.IsRunning) spotTime /= runningSpotMultiplier;
                value = Mathf.Min(1f, value + Time.deltaTime / Mathf.Max(0.01f, spotTime));
            }
            else
            {
                value = Mathf.Max(0f, value - awarenessDecay * Time.deltaTime);
            }
            awareness[target] = value;

            if (value >= 1f && spotted.Add(target)) Spotted?.Invoke(target);
        }
    }

    void OnNoise(NoiseEvent noise)
    {
        // 플레이어 캐릭터가 낸 소리만 듣습니다 (자기·다른 추격자 소리는 무시)
        if (noise.source == null || noise.source.GetComponent<CharacterStatus>() == null) return;

        float radius = noise.radius * hearingMultiplier;
        if (Physics.Linecast(Eye, noise.position + Vector3.up * 0.3f, out RaycastHit hit, obstacleMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(noise.source.transform))
            radius *= wallMuffle;

        if (Vector3.Distance(transform.position, noise.position) > radius) return;
        LastHeard = noise;
        Heard?.Invoke(noise);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 eye = Eye;
        Gizmos.color = new Color(1f, 1f, 0.3f, 0.8f);
        float half = viewAngle * 0.5f;
        Vector3 previous = eye;
        for (float a = -half; a <= half + 0.01f; a += Mathf.Max(2f, viewAngle / 24f))
        {
            Vector3 point = eye + Quaternion.Euler(0f, a, 0f) * transform.forward * viewDistance;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        Gizmos.DrawLine(previous, eye);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, closeAwareness);

        foreach (CharacterStatus target in CharacterStatus.All)
        {
            float value = Awareness(target);
            if (value <= 0f) continue;
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, value);
            Gizmos.DrawLine(eye, target.transform.position);
        }

        if (LastHeard.HasValue && Time.time - LastHeard.Value.time < 3f)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawWireSphere(LastHeard.Value.position, 0.4f);
            Gizmos.DrawLine(transform.position, LastHeard.Value.position);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
