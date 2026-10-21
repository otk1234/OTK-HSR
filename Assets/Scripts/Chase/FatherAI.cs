using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 아버지 AI (고지능 추격자)
//  배회(자주 보인 방·직감) → 조사(소리·흘끗 봄) → 추격(발견)
//  → 놓침: 마지막으로 본 곳 → 움직이던 방향을 예측해 앞질러 감 → 숨는 곳 확률 수색 → 주변 수색 → 포기
//  - 창문·환풍구 사용 (ChaserMotor ChaserType.Father). 잠긴 문 너머에 다른 길이 있으면 길찾기가 그쪽으로 돌아감
//  - 직감: 가끔(낮은 확률 + 대기 시간) 숨지 않은 캐릭터가 있는 방 쪽으로 배회 → 조작하지 않는 캐릭터도 노림.
//    방까지만 향하고 정확한 위치는 모름 (보거나 들어야 알아챔)
//  - 숨는 소리를 들으면 그 숨는 곳을 뒤짐. 숨는 장면을 봤으면 그곳부터 뒤짐
[RequireComponent(typeof(ChaserMotor), typeof(ChaserSenses))]
public class FatherAI : MonoBehaviour, IChaserBrain
{
    public enum Mode { Prowl, Investigate, Chase, Pursue, Intercept, SearchSpots, Search }

    [Header("배회")]
    public float prowlPause = 1.5f;
    [Tooltip("방을 고를 때, 그 방에서 캐릭터를 본 횟수 1번마다 더하는 가중치 (기본 가중치 1)")]
    public float sightingWeight = 0.5f;

    [Header("직감 (캐릭터가 있는 방 쪽으로)")]
    [Tooltip("배회할 방을 고를 때마다 직감이 작동할 확률")]
    [Range(0f, 1f)] public float intuitionChance = 0.2f;
    [Tooltip("직감이 한 번 작동한 뒤 다시 작동하기까지 최소 시간")]
    public float intuitionCooldown = 35f;
    [Tooltip("씬 시작 후 이 시간 동안은 직감이 작동하지 않음")]
    public float intuitionStartDelay = 20f;

    [Header("의심 (소리·흘끗 봄)")]
    [Range(0f, 1f)] public float suspicionAwareness = 0.5f;
    public float lookAroundTime = 2f;

    [Header("추격")]
    public float catchDistance = 1.0f;
    [Tooltip("추격 중 이 간격마다 '캐릭터를 본 장소'로 기억")]
    public float sightingInterval = 3f;

    [Header("놓친 뒤: 예측")]
    [Tooltip("마지막으로 본 움직임 방향으로 이만큼 앞을 예측 (벽에 막히면 그 앞까지)")]
    public float predictDistance = 7f;
    [Tooltip("예측이 마지막 위치에서 이만큼 이상 떨어져야 앞질러 감")]
    public float minPredictLead = 2f;
    public float interceptTimeout = 10f;

    [Header("놓친 뒤: 숨는 곳 수색")]
    [Tooltip("놓친 곳(또는 예측한 곳)에서 이 거리 안의 숨는 곳이 후보")]
    public float spotSearchRadius = 6f;
    [Tooltip("후보 중 무작위로 뒤져 볼 개수 (숨는 장면을 본 곳은 따로 항상 뒤짐)")]
    public int maxSpotsToSearch = 2;
    public float spotCheckTime = 0.8f;

    [Header("놓친 뒤: 주변 수색")]
    public float searchTime = 8f;
    public float searchRadius = 4f;
    public float searchPointPause = 0.8f;

    [Header("점검·디버그")]
    [Tooltip("0이 아니면 이 값으로 무작위 판단을 고정 (점검용)")]
    public int randomSeed;

    public Mode Current { get; private set; } = Mode.Prowl;
    public CharacterStatus Target { get; private set; }
    public CameraTrigger ProwlRoom { get; private set; }
    public CharacterStatus IntuitionTarget { get; private set; }   // 지금 배회가 직감 때문이면 그 캐릭터
    public bool HasPrediction { get; private set; }
    public Vector3 PredictedPoint { get; private set; }
    public IReadOnlyList<HidingSpot> SpotQueue => spotQueue;
    public HidingSpot SpotBeingChecked => currentSpot;
    public HidingSpot InvestigateSpot => investigateSpot;

    public string DisplayName => name;
    public string StateLabel => Current switch
    {
        Mode.Prowl => IntuitionTarget != null ? $"배회 → {ProwlRoom?.roomName} (직감)" : $"배회 → {ProwlRoom?.roomName}",
        Mode.Investigate => "조사",
        Mode.Chase => "추격",
        Mode.Pursue => "마지막 위치로",
        Mode.Intercept => $"앞질러 감 ({CameraTrigger.FindAt(PredictedPoint)?.roomName})",
        Mode.SearchSpots => currentSpot != null ? $"숨는 곳 수색 ({currentSpot.displayName}, 남은 {spotQueue.Count})" : "숨는 곳 수색",
        Mode.Search => "주변 수색",
        _ => Current.ToString(),
    };

    private ChaserMotor motor;
    private ChaserSenses senses;
    private System.Random rng;
    private GameStateManager Game => GameStateManager.instance;

    private float modeTime;
    private float pauseUntil;
    private Vector3 goal;          // 지금 가려는 곳 (조사·추격의 마지막 위치)
    private Vector3 destination;   // 실제로 NavMesh에 넘긴 목적지
    private Vector3 lastSeenPosition;
    private Vector3 lastSeenVelocity;
    private Vector3 trackedPosition;
    private bool tracking;
    private float nextSightingRecord;
    private float nextIntuition;
    private float nextRepath;
    private float holdUntil;
    private HidingSpot investigateSpot;   // 숨는 소리가 난 곳
    private HidingSpot currentSpot;
    private readonly List<HidingSpot> spotQueue = new List<HidingSpot>();
    private readonly List<Vector3> searchPoints = new List<Vector3>();
    private int searchIndex;

    void Awake()
    {
        motor = GetComponent<ChaserMotor>();
        senses = GetComponent<ChaserSenses>();
        rng = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
    }

    void OnEnable()
    {
        senses.Spotted += OnSpotted;
        senses.Heard += OnHeard;
        motor.Blocked += OnBlockedOnLink;
    }

    void OnDisable()
    {
        senses.Spotted -= OnSpotted;
        senses.Heard -= OnHeard;
        motor.Blocked -= OnBlockedOnLink;
        if (Game != null) Game.EndChase(this);
    }

    void Start()
    {
        nextIntuition = Time.time + intuitionStartDelay;
        ResetToProwl();
    }

    // 처음 상태로 (연출·점검용)
    public void ResetToProwl()
    {
        holdUntil = 0f;
        Target = null;
        tracking = false;
        HasPrediction = false;
        currentSpot = null;
        spotQueue.Clear();
        investigateSpot = null;
        if (Game != null) Game.EndChase(this);
        BeginProwl();
    }

    // 연출·점검용: 배회 상태로 제자리에 서서 지켜봄 (seconds 동안 다른 방으로 가지 않음, 보거나 들으면 평소처럼 반응)
    public void StandStill(float seconds)
    {
        ResetToProwl();
        motor.Stop();
        destination = transform.position;
        holdUntil = Time.time + seconds;
    }

    // 점검용: 무작위 판단을 다시 고정
    public void Reseed(int seed) => rng = new System.Random(seed);

    // 점검용: 직감 대기 시간을 바로 풀기
    public void ClearIntuitionCooldown() => nextIntuition = 0f;

    void SetMode(Mode mode)
    {
        Current = mode;
        modeTime = Time.time;
        pauseUntil = 0f;
        motor.SetRunning(mode == Mode.Chase || mode == Mode.Pursue || mode == Mode.Intercept || mode == Mode.SearchSpots);
    }

    float InMode => Time.time - modeTime;
    bool Chasing => Game != null ? Game.IsChasing(this) : Target != null;

    // ---------------- 감각 이벤트 ----------------

    void OnSpotted(CharacterStatus who)
    {
        if (who.IsCaught || (Game != null && Game.RestartPending)) return;
        FatherMemory.RecordSighting(who.CurrentRoom);
        if (Chasing)
        {
            // 추격 중 다른 캐릭터도 발견: 대상에 추가
            if (Game != null && !Game.IsTargeted(who))
            {
                var targets = new List<CharacterStatus>(Game.TargetsOf(this));
                targets.AddRange(ChaseTargeting.FromSighting(who).FindAll(c => !targets.Contains(c)));
                Game.BeginChase(this, targets);
            }
            if (Current != Mode.Chase) ResumeChase(who);
            return;
        }
        StartChase(who);
    }

    void OnHeard(NoiseEvent noise)
    {
        HidingSpot hidingAt = noise.kind == NoiseKind.Hiding ? NearestSpot(noise.position, 1.0f) : null;
        switch (Current)
        {
            case Mode.Prowl:
            case Mode.Investigate:
                Investigate(noise.position, hidingAt);
                break;
            case Mode.Pursue:
            case Mode.Intercept:
                // 쫓던 대상이 낸 소리면 그쪽으로
                CharacterStatus source = noise.source != null ? noise.source.GetComponent<CharacterStatus>() : null;
                if (source != null && Game != null && Game.IsTargeted(source))
                {
                    if (hidingAt != null) { BeginSpotSearch(noise.position, hidingAt); return; }
                    lastSeenPosition = noise.position;
                    goal = noise.position;
                    SetMode(Mode.Pursue);
                    MoveTo(goal);
                }
                break;
            case Mode.SearchSpots:
            case Mode.Search:
                if (hidingAt != null) { BeginSpotSearch(noise.position, hidingAt); return; }
                if (Current == Mode.Search) BeginSearch(noise.position, keepTimer: true);
                break;
        }
    }

    // ---------------- 상태 전환 ----------------

    void StartChase(CharacterStatus who)
    {
        if (Game != null) Game.BeginChase(this, ChaseTargeting.FromSighting(who));
        ResumeChase(who);
    }

    void ResumeChase(CharacterStatus who)
    {
        Target = who;
        tracking = false;
        HasPrediction = false;
        currentSpot = null;
        spotQueue.Clear();
        if (senses.TryGetLastSeen(who, out Vector3 seen, out _)) lastSeenPosition = seen;
        lastSeenVelocity = Vector3.zero;
        nextSightingRecord = Time.time + sightingInterval;
        SetMode(Mode.Chase);
    }

    void Investigate(Vector3 position, HidingSpot hidingAt)
    {
        goal = position;
        investigateSpot = hidingAt;
        SetMode(Mode.Investigate);
        MoveTo(hidingAt != null ? hidingAt.transform.position : goal);
    }

    void BeginProwl()
    {
        PickProwlRoom();
        SetMode(Mode.Prowl);
        MoveTo(RandomPointIn(ProwlRoom, transform.position));
    }

    void GiveUp()
    {
        Target = null;
        tracking = false;
        HasPrediction = false;
        currentSpot = null;
        spotQueue.Clear();
        if (Game != null) Game.EndChase(this);
        foreach (CharacterStatus character in CharacterStatus.All) senses.Forget(character);
        BeginProwl();
    }

    // 놓친 직후: 마지막으로 본 곳으로
    void LoseSight()
    {
        goal = lastSeenPosition;
        SetMode(Mode.Pursue);
        MoveTo(goal);
    }

    // 마지막으로 본 곳에 도착: 움직이던 방향으로 앞질러 가거나, 바로 숨는 곳 수색
    void AfterPursue()
    {
        HasPrediction = TryPredict(out Vector3 predicted);
        if (HasPrediction)
        {
            PredictedPoint = predicted;
            SetMode(Mode.Intercept);
            MoveTo(predicted);
            return;
        }
        BeginSpotSearch(lastSeenPosition, null);
    }

    bool TryPredict(out Vector3 predicted)
    {
        predicted = lastSeenPosition;
        Vector3 direction = Vector3.ProjectOnPlane(lastSeenVelocity, Vector3.up);
        if (direction.sqrMagnitude < 0.25f) return false; // 거의 서 있었음
        Vector3 ahead = lastSeenPosition + direction.normalized * predictDistance;
        // 캐릭터가 걸어갈 수 있는 길로만 예측 (벽·닫힌 문에 막히면 그 앞까지)
        if (!NavMesh.SamplePosition(lastSeenPosition, out NavMeshHit from, 1.5f, PlayerAreaMask)) return false;
        predicted = NavMesh.Raycast(from.position, ahead, out NavMeshHit hit, PlayerAreaMask) ? hit.position : ahead;
        if (FlatDistance(predicted, lastSeenPosition) < minPredictLead) return false;
        return NavMesh.SamplePosition(predicted, out NavMeshHit onMesh, 1.5f, motor.Agent.areaMask) && Reachable(onMesh.position);
    }

    // 숨는 곳 수색 목록: 숨는 장면을 봤거나 숨는 소리가 난 곳은 항상 먼저, 나머지 후보 중 일부를 무작위로
    void BeginSpotSearch(Vector3 center, HidingSpot certain)
    {
        spotQueue.Clear();
        currentSpot = null;
        if (certain != null) spotQueue.Add(certain);
        if (Target != null && Target.IsHidden && Target.HidingIn.WasSeenEnteringBy(gameObject) && !spotQueue.Contains(Target.HidingIn))
            spotQueue.Add(Target.HidingIn);

        var candidates = new List<HidingSpot>();
        foreach (HidingSpot spot in HidingSpot.All)
        {
            if (spotQueue.Contains(spot)) continue;
            bool near = FlatDistance(spot.transform.position, center) <= spotSearchRadius
                     || (HasPrediction && FlatDistance(spot.transform.position, PredictedPoint) <= spotSearchRadius);
            if (near && Reachable(spot.transform.position)) candidates.Add(spot);
        }
        for (int i = 0; i < maxSpotsToSearch && candidates.Count > 0; i++)
        {
            HidingSpot pick = candidates[rng.Next(candidates.Count)];
            candidates.Remove(pick);
            spotQueue.Add(pick);
        }

        goal = center;
        SetMode(Mode.SearchSpots);
        NextSpot();
    }

    void NextSpot()
    {
        if (spotQueue.Count == 0) { currentSpot = null; BeginSearch(goal); return; }
        currentSpot = spotQueue[0];
        spotQueue.RemoveAt(0);
        pauseUntil = 0f;
        MoveTo(currentSpot.transform.position);
    }

    void BeginSearch(Vector3 center, bool keepTimer = false)
    {
        float started = modeTime;
        goal = center;
        searchPoints.Clear();
        searchPoints.Add(center);
        for (int i = 0; i < 8 && searchPoints.Count < 4; i++)
        {
            Vector2 offset = new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f) * searchRadius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, motor.Agent.areaMask) && Reachable(hit.position))
                searchPoints.Add(hit.position);
        }
        searchIndex = 0;
        SetMode(Mode.Search);
        if (keepTimer) modeTime = started;
        MoveTo(searchPoints[0]);
    }

    // ---------------- 매 프레임 ----------------

    void Update()
    {
        if (Game != null && Game.RestartPending) { motor.Stop(); return; }
        if (motor.IsTraversing) return;

        switch (Current)
        {
            case Mode.Prowl: UpdateProwl(); break;
            case Mode.Investigate: UpdateInvestigate(); break;
            case Mode.Chase: UpdateChase(); break;
            case Mode.Pursue: UpdatePursue(); break;
            case Mode.Intercept: UpdateIntercept(); break;
            case Mode.SearchSpots: UpdateSearchSpots(); break;
            case Mode.Search: UpdateSearch(); break;
        }
    }

    void UpdateProwl()
    {
        if (CheckGlimpse()) return;
        if (Time.time < holdUntil) return;
        if (CheckBlockedPath()) return;
        if (!motor.Arrived(0.6f))
        {
            if (InMode > 25f) BeginProwl(); // 너무 오래 걸리면 다른 방
            return;
        }
        LookAround();
        if (pauseUntil <= 0f) { pauseUntil = Time.time + prowlPause; return; }
        if (Time.time < pauseUntil) return;
        BeginProwl();
    }

    void UpdateInvestigate()
    {
        if (CheckGlimpse()) return;
        if (CheckBlockedPath()) return;
        if (investigateSpot != null)
        {
            // 숨는 소리가 난 곳: 가서 뒤져 봄
            if (FlatDistance(transform.position, investigateSpot.transform.position) > catchDistance + 0.4f)
            {
                if (InMode > 15f) BeginProwl();
                return;
            }
            if (CatchIn(investigateSpot)) return;
            investigateSpot = null;
            pauseUntil = Time.time + lookAroundTime;
            return;
        }
        if (!motor.Arrived(0.6f))
        {
            if (InMode > 15f) BeginProwl();
            return;
        }
        LookAround();
        if (pauseUntil <= 0f) pauseUntil = Time.time + lookAroundTime;
        else if (Time.time > pauseUntil) BeginProwl();
    }

    void UpdateChase()
    {
        CharacterStatus target = PickVisibleTarget();
        if (target == null)
        {
            tracking = false;
            if (Target != null && Target.IsHidden && Target.HidingIn.WasSeenEnteringBy(gameObject))
            {
                BeginSpotSearch(Target.HidingIn.transform.position, Target.HidingIn);
                return;
            }
            LoseSight();
            return;
        }

        if (target != Target) { Target = target; tracking = false; }
        TrackTarget(target);
        if (Time.time >= nextSightingRecord)
        {
            FatherMemory.RecordSighting(target.CurrentRoom);
            nextSightingRecord = Time.time + sightingInterval;
        }

        goal = target.transform.position;
        if (Time.time >= nextRepath) { MoveTo(goal); nextRepath = Time.time + 0.2f; }
        if (FlatDistance(transform.position, target.transform.position) <= catchDistance && Game != null)
            Game.ReportCaught(target, this);
    }

    void UpdatePursue()
    {
        if (CheckReacquire()) return;
        if (CheckBlockedPath()) return;
        if (motor.Arrived(0.6f) || InMode > 8f) AfterPursue();
    }

    void UpdateIntercept()
    {
        if (CheckReacquire()) return;
        if (CheckBlockedPath()) return;
        if (!motor.Arrived(0.6f) && InMode < interceptTimeout) return;
        LookAround();
        if (pauseUntil <= 0f) { pauseUntil = Time.time + 0.6f; return; }
        if (Time.time < pauseUntil) return;
        BeginSpotSearch(lastSeenPosition, null);
    }

    void UpdateSearchSpots()
    {
        if (CheckReacquire()) return;
        if (currentSpot == null) { NextSpot(); return; }
        if (FlatDistance(transform.position, currentSpot.transform.position) > catchDistance + 0.4f)
        {
            if (!motor.Agent.pathPending && !motor.Agent.hasPath) MoveTo(currentSpot.transform.position);
            if (InMode > 30f) BeginSearch(goal);
            return;
        }
        // 도착: 잠깐 열어 보는 시간
        if (pauseUntil <= 0f) { pauseUntil = Time.time + spotCheckTime; FaceTowards(currentSpot.transform.position); return; }
        if (Time.time < pauseUntil) return;
        if (CatchIn(currentSpot)) return;
        NextSpot();
    }

    void UpdateSearch()
    {
        if (CheckReacquire()) return;
        if (CheckBlockedPath()) return;
        if (InMode > searchTime) { GiveUp(); return; }
        if (!motor.Arrived(0.6f)) return;

        LookAround();
        if (pauseUntil <= 0f) { pauseUntil = Time.time + searchPointPause; return; }
        if (Time.time < pauseUntil) return;
        pauseUntil = 0f;
        searchIndex = (searchIndex + 1) % searchPoints.Count;
        MoveTo(searchPoints[searchIndex]);
    }

    // ---------------- 배회할 방 고르기 ----------------

    void PickProwlRoom()
    {
        IntuitionTarget = null;
        CameraTrigger here = CameraTrigger.FindAt(transform.position);

        // 직감: 낮은 확률 + 대기 시간. 숨지 않은 캐릭터가 있는 방 쪽으로 (정확한 위치는 모름)
        if (Time.time >= nextIntuition && rng.NextDouble() < intuitionChance)
        {
            var exposed = new List<CharacterStatus>();
            foreach (CharacterStatus character in CharacterStatus.All)
                if (character.CanBeSeen && character.CurrentRoom != null && character.CurrentRoom != here && Reachable(character.transform.position))
                    exposed.Add(character);
            if (exposed.Count > 0)
            {
                IntuitionTarget = exposed[rng.Next(exposed.Count)];
                ProwlRoom = IntuitionTarget.CurrentRoom;
                nextIntuition = Time.time + intuitionCooldown;
                return;
            }
        }

        // 보통: 자주 보인 방일수록 자주 감. 다른 건물(마당 등)은 덜 감
        var rooms = new List<CameraTrigger>();
        var weights = new List<float>();
        float total = 0f;
        foreach (CameraTrigger room in CameraTrigger.All)
        {
            if (room == here) continue;
            float weight = 1f + FatherMemory.Sightings(room) * sightingWeight;
            if (here != null && room.buildingId != here.buildingId) weight *= 0.25f;
            rooms.Add(room);
            weights.Add(weight);
            total += weight;
        }
        if (rooms.Count == 0) { ProwlRoom = here; return; }
        double roll = rng.NextDouble() * total;
        for (int i = 0; i < rooms.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) { ProwlRoom = rooms[i]; return; }
        }
        ProwlRoom = rooms[rooms.Count - 1];
    }

    // 방 안에서 갈 수 있는 무작위 지점 (가구 위 등은 피함)
    Vector3 RandomPointIn(CameraTrigger room, Vector3 fallback)
    {
        if (room == null) return fallback;
        Bounds bounds = room.GetComponent<Collider>().bounds;
        for (int i = 0; i < 10; i++)
        {
            var candidate = new Vector3(
                Mathf.Lerp(bounds.min.x + 1f, bounds.max.x - 1f, (float)rng.NextDouble()), 0f,
                Mathf.Lerp(bounds.min.z + 1f, bounds.max.z - 1f, (float)rng.NextDouble()));
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, motor.Agent.areaMask) && room.ContainsPoint(hit.position) && Reachable(hit.position))
                return hit.position;
        }
        return new Vector3(bounds.center.x, 0f, bounds.center.z);
    }

    // ---------------- 막힌 길 ----------------

    // 지나가려는 순간 누가 문을 잠금: 다른 길(창문·환풍구)이 있으면 길찾기가 알아서 돌아감
    void OnBlockedOnLink(EntryPoint entry) => MoveTo(destination);

    // 목적지까지 어떤 길(창문·환풍구 포함)로도 갈 수 없음.
    // TODO(3-2단계): 잠긴 문 판단(우회·위장 후퇴·잠복·파괴)으로 바꿈. 지금은 쫓던 중이면 근처 수색, 아니면 다른 방 배회.
    bool CheckBlockedPath()
    {
        NavMeshAgent agent = motor.Agent;
        if (agent.pathPending || motor.IsTraversing) return false;
        bool partial = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial;
        bool fellShort = !agent.hasPath && FlatDistance(transform.position, destination) > 1.0f;
        if (!partial && !fellShort) return false;
        if (Chasing) BeginSpotSearch(transform.position, null);
        else BeginProwl();
        return true;
    }

    // ---------------- 도우미 ----------------

    // 놓친 뒤 수색 중에 다시 보이면 추격 재개
    bool CheckReacquire()
    {
        CharacterStatus target = PickVisibleTarget();
        if (target == null) return false;
        ResumeChase(target);
        return true;
    }

    // 추격 대상 중 지금 보이는 가장 가까운 캐릭터
    CharacterStatus PickVisibleTarget()
    {
        CharacterStatus best = null;
        float nearest = float.MaxValue;
        IEnumerable<CharacterStatus> targets = Game != null ? Game.TargetsOf(this) : (IEnumerable<CharacterStatus>)new[] { Target };
        foreach (CharacterStatus candidate in targets)
        {
            if (candidate == null || candidate.IsCaught || !senses.IsVisible(candidate)) continue;
            float d = (candidate.transform.position - transform.position).sqrMagnitude;
            if (d < nearest) { nearest = d; best = candidate; }
        }
        return best;
    }

    // 보이는 동안 위치와 움직이는 방향을 기록 (놓친 뒤 예측용). 실제 위치가 아니라 '마지막으로 본 위치'를 씀
    void TrackTarget(CharacterStatus target)
    {
        if (!senses.TryGetLastSeen(target, out Vector3 position, out _)) position = target.transform.position;
        if (tracking && Time.deltaTime > 0f)
        {
            Vector3 velocity = Vector3.ClampMagnitude((position - trackedPosition) / Time.deltaTime, 8f);
            lastSeenVelocity = Vector3.Lerp(lastSeenVelocity, velocity, 0.25f);
        }
        else lastSeenVelocity = Vector3.zero;
        trackedPosition = position;
        tracking = true;
        lastSeenPosition = position;
    }

    // 숨는 곳을 뒤져서 숨어 있던 캐릭터를 잡음
    bool CatchIn(HidingSpot spot)
    {
        List<CharacterStatus> found = spot.Search(gameObject);
        if (found.Count == 0 || Game == null) return false;
        foreach (CharacterStatus character in found) Game.ReportCaught(character, this);
        return true;
    }

    bool CheckGlimpse()
    {
        foreach (CharacterStatus character in CharacterStatus.All)
        {
            if (senses.Awareness(character) < suspicionAwareness || senses.IsSpotted(character)) continue;
            if (senses.TryGetLastSeen(character, out Vector3 seen, out _)) { Investigate(seen, null); return true; }
        }
        return false;
    }

    static HidingSpot NearestSpot(Vector3 position, float within)
    {
        HidingSpot best = null;
        float nearest = within;
        foreach (HidingSpot spot in HidingSpot.All)
        {
            float d = FlatDistance(spot.transform.position, position);
            if (d <= nearest) { nearest = d; best = spot; }
        }
        return best;
    }

    bool Reachable(Vector3 position)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(transform.position, position, motor.Agent.areaMask, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    // 플레이어 캐릭터가 걸을 수 있는 구역 (문·창문·환풍구 링크 제외)
    static int PlayerAreaMask
    {
        get
        {
            int mask = NavMesh.AllAreas;
            foreach (string area in new[] { EntryPoint.DoorArea, EntryPoint.WindowArea, EntryPoint.VentArea })
            {
                int index = NavMesh.GetAreaFromName(area);
                if (index >= 0) mask &= ~(1 << index);
            }
            return mask;
        }
    }

    void MoveTo(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 1.5f, motor.Agent.areaMask)) position = hit.position;
        destination = position;
        motor.SetDestination(position);
    }

    void LookAround() => transform.Rotate(0f, 120f * Time.deltaTime, 0f);

    void FaceTowards(Vector3 point)
    {
        Vector3 flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat);
    }

    static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (HasPrediction && (Current == Mode.Intercept || Current == Mode.SearchSpots))
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(lastSeenPosition + Vector3.up * 0.1f, PredictedPoint + Vector3.up * 0.1f);
            Gizmos.DrawWireSphere(PredictedPoint, 0.35f);
        }
        if (Current == Mode.SearchSpots)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f);
            Vector3 previous = transform.position;
            if (currentSpot != null) { Gizmos.DrawLine(previous, currentSpot.transform.position); previous = currentSpot.transform.position; }
            foreach (HidingSpot spot in spotQueue) { Gizmos.DrawLine(previous, spot.transform.position); previous = spot.transform.position; }
        }
        if (Current == Mode.Search)
        {
            Gizmos.color = Color.cyan;
            foreach (Vector3 p in searchPoints) Gizmos.DrawWireSphere(p, 0.25f);
        }
        if (Current == Mode.Prowl && IntuitionTarget != null)
        {
            Gizmos.color = new Color(0.8f, 0.4f, 1f);
            Gizmos.DrawLine(transform.position, IntuitionTarget.transform.position);
        }
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.4f, "아버지: " + StateLabel);
#endif
    }
}
