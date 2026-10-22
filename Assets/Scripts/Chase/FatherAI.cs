using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 아버지 AI (고지능 추격자)
//  배회(자주 보인 방·직감) → 조사(소리·흘끗 봄) → 추격(발견)
//  → 놓침: 잠깐 두리번 → 마지막으로 본 곳 → 움직이던 방향을 예측해 앞질러 감 → 숨는 곳 확률 수색 → 주변 수색 → 포기
//  - 창문·환풍구 사용 (ChaserMotor ChaserType.Father). 잠긴 문 너머에 다른 길이 있으면 길찾기가 그쪽으로 돌아감
//  - 직감: 가끔(낮은 확률 + 대기 시간) 숨지 않은 캐릭터가 있는 방 쪽으로 배회 → 조작하지 않는 캐릭터도 노림.
//    방까지만 향하고 정확한 위치는 모름 (보거나 들어야 알아챔)
//  - 숨는 소리를 들으면 그 숨는 곳을 뒤짐 (쫓는 중에는 확률로만). 숨는 장면을 봤으면 그곳부터 뒤짐
//  - 잠긴 문: 쫓거나 조사하러 가는 가장 짧은 길이 잠긴 문을 지나면, 문 앞에서 손잡이를 돌려 보고 판단
//    (배회 중에는 판단하지 않음). 우회로가 없고 무기가 있으면 파괴(최후의 수단), 그 밖에는 확률로
//    우회(창문·환풍구, 조용히) / 위장 후퇴(보란 듯이 떠났다가 몰래 우회하거나 돌아와 잠복) / 잠복(문 옆에서 조용히 대기)
//  - 학습 (FatherMemory, 잡혀서 씬이 재시작돼도 유지): 추격·문 앞에서 포기하면 그때 쓴 문·숨는 곳·도망 경로를 기록.
//    같은 수법이 learnThreshold번 이상이면 막는 쪽 우선: 그 문 앞에선 우회·잠복↑, 그 숨는 곳은 먼저 뒤짐,
//    그 방에서 놓치면 늘 도망치던 다음 방 길목으로. 배회할 때도 그쪽을 더 자주 들르고 숨는 곳을 가끔 열어 봄
[RequireComponent(typeof(ChaserMotor), typeof(ChaserSenses))]
public class FatherAI : MonoBehaviour, IChaserBrain
{
    public enum Mode { Prowl, Investigate, Chase, Pursue, Intercept, SearchSpots, Search, DoorApproach, Detour, FeignRetreat, Ambush, Bash }

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

    [Header("놓친 뒤: 망설임")]
    [Tooltip("시야에서 놓친 직후 제자리에서 두리번거리는 시간 (그 사이 숨을 틈)")]
    public float loseSightHesitation = 1.2f;

    [Header("쫓는 중 숨는 소리")]
    [Tooltip("추격·수색 중 숨는 소리를 들었을 때 그 숨는 곳을 확실히 뒤질 확률. 아니면 소리 난 쪽으로 가서 평소처럼 수색 (배회 중에 들은 소리는 항상 확인)")]
    [Range(0f, 1f)] public float hidingNoiseCertainty = 0.5f;

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

    [Header("잠긴 문 판단")]
    [Tooltip("문 앞에서 손잡이를 돌려 보며 잠긴 걸 확인하는 시간")]
    public float doorCheckTime = 0.8f;
    [Tooltip("판단 가중치 (우회는 우회로가 멀수록 줄어듦: 10m 더 → 그대로, 40m 이상 더 → 40%)")]
    public float detourWeight = 45f;
    public float feignWeight = 25f;
    public float ambushWeight = 30f;
    [Tooltip("한 번 판단한 문은 이 시간 동안 다시 판단하지 않음")]
    public float doorDecisionMemory = 20f;

    [Header("잠긴 문: 위장 후퇴")]
    [Tooltip("문에서 이만큼 떨어진 곳까지 보란 듯이 걸어감")]
    public float feignDistance = 7f;
    [Tooltip("포기한 척 서 있는 시간")]
    public float feignPause = 2.5f;
    [Tooltip("그 뒤 우회로가 있으면 몰래 우회할 확률 (아니면 몰래 돌아와 잠복)")]
    [Range(0f, 1f)] public float feignDetourChance = 0.5f;

    [Header("잠긴 문: 잠복")]
    public float ambushTime = 12f;
    [Tooltip("문 너머에서 소리가 나면 기다리는 시간이 늘어나지만 이 시간을 넘지 않음")]
    public float ambushMaxTime = 25f;

    [Header("잠긴 문: 파괴 (무기가 있을 때만, 우회로가 없을 때 최후의 수단)")]
    [Tooltip("테스트용 체크박스 (획득 방식은 나중에)")]
    public bool hasWeapon;
    public float bashDamage = 25f;
    public float bashInterval = 1.2f;
    public float bashGiveUpTime = 20f;

    [Header("학습 (자주 쓰는 수법 차단)")]
    [Tooltip("같은 수법(문·숨는 곳·도망 경로)으로 이만큼 빠져나가면 그걸 막는 쪽을 우선")]
    public int learnThreshold = 2;
    [Tooltip("자주 버틴 문 앞: 1회마다 우회·잠복 가중치 +이 비율 (최대 3배). 위장 후퇴는 그대로")]
    public float doorLearnBoost = 0.5f;
    [Tooltip("배회할 방 가중치: 자주 버틴 문 너머 방, 자주 숨은 곳이 있는 방은 1회마다 +이만큼")]
    public float prowlLearnWeight = 1f;
    [Tooltip("배회 중 자주 숨은 곳이 있는 방에 오면 그곳을 열어 볼 확률")]
    [Range(0f, 1f)] public float checkLearnedSpotChance = 0.35f;

    [Header("점검·디버그")]
    [Tooltip("0이 아니면 이 값으로 무작위 판단을 고정 (점검용)")]
    public int randomSeed;
    [Tooltip("None이 아니면 잠긴 문 판단을 이것으로 고정 (점검·연출용). 할 수 없는 판단이면 잠복")]
    public DoorTactic forceDoorTactic = DoorTactic.None;

    public Mode Current { get; private set; } = Mode.Prowl;
    public CharacterStatus Target { get; private set; }
    public CameraTrigger ProwlRoom { get; private set; }
    public CharacterStatus IntuitionTarget { get; private set; }   // 지금 배회가 직감 때문이면 그 캐릭터
    public bool HasPrediction { get; private set; }
    public Vector3 PredictedPoint { get; private set; }
    public IReadOnlyList<HidingSpot> SpotQueue => spotQueue;
    public HidingSpot SpotBeingChecked => currentSpot;
    public HidingSpot InvestigateSpot => investigateSpot;
    public bool IsHesitating => Current == Mode.Pursue && Time.time < hesitateUntil;
    public EntryPoint DoorTarget { get; private set; }
    public DoorTactic LastDoorTactic { get; private set; }
    public bool LastDetourAvailable { get; private set; }
    public bool IsSneaking => motor != null && motor.quiet;
    public bool PredictionLearned { get; private set; }   // 예측이 학습한 도망 경로에서 나왔는가
    public Vector3 Destination => destination;   // 디버그 표시용
    public Vector3 AmbushPoint => ambushPoint;

    public string DisplayName => name;
    public string StateLabel => Current switch
    {
        Mode.Prowl => IntuitionTarget != null ? $"배회 → {ProwlRoom?.roomName} (직감)" : $"배회 → {ProwlRoom?.roomName}",
        Mode.Investigate => "조사",
        Mode.Chase => "추격",
        Mode.Pursue => IsHesitating ? "놓침 (두리번)" : "마지막 위치로",
        Mode.Intercept => PredictionLearned
            ? $"길목 차단 ({CameraTrigger.FindAt(PredictedPoint)?.roomName}, 학습한 도망 경로)"
            : $"앞질러 감 ({CameraTrigger.FindAt(PredictedPoint)?.roomName})",
        Mode.SearchSpots => currentSpot != null ? $"숨는 곳 수색 ({currentSpot.displayName}, 남은 {spotQueue.Count})" : "숨는 곳 수색",
        Mode.Search => "주변 수색",
        Mode.DoorApproach => $"잠긴 문 확인 ({DoorTarget?.displayName})",
        Mode.Detour => $"우회 (조용히, {DoorTarget?.displayName} 대신)",
        Mode.FeignRetreat => feignSneaking ? "위장 후퇴 → 몰래 돌아감" : "위장 후퇴 (포기한 척)",
        Mode.Ambush => $"잠복 ({DoorTarget?.displayName} 옆)",
        Mode.Bash => $"문 부수기 ({DoorTarget?.displayName} {DoorTarget?.door?.Durability:0})",
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
    private float hesitateUntil;
    private HidingSpot investigateSpot;   // 숨는 소리가 난 곳
    private HidingSpot currentSpot;
    private readonly List<HidingSpot> spotQueue = new List<HidingSpot>();
    private readonly List<Vector3> searchPoints = new List<Vector3>();
    private int searchIndex;

    // 잠긴 문
    private Vector3 doorGoal;              // 문 너머로 가려던 곳
    private HidingSpot doorSpot;           // 문 너머에서 뒤지려던 숨는 곳
    private CameraTrigger doorBehindRoom;  // 문 너머 방
    private float doorDetourExtra;
    private float nextDoorCheck;
    private bool feignSneaking;
    private Vector3 ambushPoint;
    private float ambushStart, ambushUntil;
    private float nextBash;
    private readonly Dictionary<EntryPoint, float> doorIgnoredUntil = new Dictionary<EntryPoint, float>();

    // 학습: 이번 추격에서 일어난 일 (포기하면 '빠져나간 방법'으로 기록)
    private readonly HashSet<EntryPoint> episodeDoors = new HashSet<EntryPoint>();
    private readonly Dictionary<CharacterStatus, CameraTrigger> episodeRooms = new Dictionary<CharacterStatus, CameraTrigger>();
    private readonly List<(CameraTrigger from, CameraTrigger to)> episodeMoves = new List<(CameraTrigger, CameraTrigger)>();
    private float nextEpisodeTrack;

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
        DoorTarget = null;
        doorIgnoredUntil.Clear();
        ClearEpisode();
        if (Game != null) Game.EndChase(this);
        BeginProwl();
    }

    // 점검·연출용: 이 지점 근처 숨는 곳 수색을 시작 (학습한 곳 우선)
    public void SearchSpotsAround(Vector3 center)
    {
        HasPrediction = false;
        BeginSpotSearch(center, null);
    }

    // 잠긴 문 앞 판단 가중치 (학습 반영: 자주 버틴 문이면 우회·잠복을 더 고름)
    public FatherTactics.DoorWeights WeightsFor(EntryPoint door)
    {
        int escapes = FatherMemory.DoorEscapes(door);
        float boost = escapes >= learnThreshold ? Mathf.Min(3f, 1f + doorLearnBoost * escapes) : 1f;
        return new FatherTactics.DoorWeights { detour = detourWeight * boost, feign = feignWeight, ambush = ambushWeight * boost };
    }

    // 배회할 방 가중치 (자주 보인 방, 학습: 자주 버틴 문 너머 방·자주 숨은 곳이 있는 방)
    public float ProwlWeight(CameraTrigger room, CameraTrigger here)
    {
        float weight = 1f + FatherMemory.Sightings(room) * sightingWeight;
        foreach (EntryPoint entry in EntryPoint.All)
        {
            int escapes = FatherMemory.DoorEscapes(entry);
            if (escapes >= learnThreshold && (entry.RoomA == room || entry.RoomB == room)) weight += escapes * prowlLearnWeight;
        }
        foreach (HidingSpot spot in HidingSpot.All)
        {
            int escapes = FatherMemory.HideEscapes(spot);
            if (escapes >= learnThreshold && room.ContainsPoint(spot.transform.position)) weight += escapes * prowlLearnWeight;
        }
        if (here != null && room.buildingId != here.buildingId) weight *= 0.25f;
        return weight;
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
        hesitateUntil = 0f;
        motor.quiet = false; // 잠입은 우회·잠복 등에서 따로 켬
        bool hurry = mode == Mode.DoorApproach || mode == Mode.Detour || mode == Mode.Bash;
        motor.SetRunning(mode == Mode.Chase || mode == Mode.Pursue || mode == Mode.Intercept || mode == Mode.SearchSpots || (hurry && Chasing));
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
        if (Current == Mode.Ambush)
        {
            // 잠복 중 문 너머에서 소리: 아직 안에 있음 → 조금 더 기다림
            CharacterStatus who = noise.source != null ? noise.source.GetComponent<CharacterStatus>() : null;
            if (who != null && doorBehindRoom != null && doorBehindRoom.ContainsPoint(noise.position))
                ambushUntil = Mathf.Min(ambushStart + ambushMaxTime, Mathf.Max(ambushUntil, Time.time + 6f));
            return;
        }
        if (IsDoorMode(Current)) return; // 문 앞 판단 중에는 다른 소리에 끌려가지 않음
        if (Current == Mode.Prowl || Current == Mode.Investigate)
        {
            // 평소에 들은 숨는 소리는 항상 확인
            Investigate(noise.position, hidingAt);
            return;
        }
        // 쫓는 중에는 확률로만 '확실'하게 그곳을 뒤짐 (아니면 소리 난 쪽을 평소처럼 수색)
        if (hidingAt != null && rng.NextDouble() >= hidingNoiseCertainty) hidingAt = null;
        switch (Current)
        {
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
        ClearEpisode();
        ResumeChase(who);
    }

    // ---------------- 학습 ----------------

    void ClearEpisode()
    {
        episodeDoors.Clear();
        episodeRooms.Clear();
        episodeMoves.Clear();
    }

    // 추격 중 대상이 지나간 방 순서를 기록 (빠져나가면 '도망 경로'로 학습)
    void TrackEpisode()
    {
        if (Game == null || !Chasing || Time.time < nextEpisodeTrack) return;
        nextEpisodeTrack = Time.time + 0.2f;
        foreach (CharacterStatus target in Game.TargetsOf(this))
        {
            if (target == null) continue;
            CameraTrigger room = target.CurrentRoom;
            if (room == null) continue;
            if (episodeRooms.TryGetValue(target, out CameraTrigger previous) && previous != room) episodeMoves.Add((previous, room));
            episodeRooms[target] = room;
        }
    }

    // 추격을 포기함 = 플레이어가 빠져나감: 이번 추격에서 쓴 문·숨는 곳·도망 경로를 기록
    void LearnFromEscape()
    {
        foreach (EntryPoint door in episodeDoors) FatherMemory.RecordDoorEscape(door);
        if (Game != null)
            foreach (CharacterStatus target in Game.TargetsOf(this))
                if (target != null && target.IsHidden && !target.IsCaught) FatherMemory.RecordHideEscape(target.HidingIn);
        foreach (var (from, to) in episodeMoves) FatherMemory.RecordRouteEscape(from, to);
        ClearEpisode();
    }

    void ResumeChase(CharacterStatus who)
    {
        Target = who;
        tracking = false;
        HasPrediction = false;
        currentSpot = null;
        spotQueue.Clear();
        DoorTarget = null;
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
        LearnFromEscape();
        if (Game != null) Game.EndChase(this);
        foreach (CharacterStatus character in CharacterStatus.All) senses.Forget(character);
        BeginProwl();
    }

    // 놓친 직후: 잠깐 두리번거린 뒤 마지막으로 본 곳으로
    void LoseSight()
    {
        goal = lastSeenPosition;
        SetMode(Mode.Pursue);
        hesitateUntil = Time.time + loseSightHesitation;
        motor.Stop();
        destination = transform.position;
    }

    // 마지막으로 본 곳에 도착: 움직이던 방향으로 앞질러 가거나, 바로 숨는 곳 수색
    void AfterPursue()
    {
        PredictionLearned = TryPredictLearned(out Vector3 predicted);
        HasPrediction = PredictionLearned || TryPredict(out predicted);
        if (HasPrediction)
        {
            PredictedPoint = predicted;
            SetMode(Mode.Intercept);
            MoveTo(predicted);
            return;
        }
        BeginSpotSearch(lastSeenPosition, null);
    }

    // 학습: 이 방에서 자주 도망친 다음 방이 있으면 그 길목(두 방을 잇는 문의 다음 방 쪽)으로 먼저 감
    bool TryPredictLearned(out Vector3 predicted)
    {
        predicted = lastSeenPosition;
        CameraTrigger from = CameraTrigger.FindAt(lastSeenPosition);
        CameraTrigger next = FatherMemory.MostLikelyNext(from, out int count);
        if (next == null || count < learnThreshold) return false;
        EntryPoint between = null;
        foreach (EntryPoint entry in EntryPoint.All)
            if (entry.kind == EntryKind.Door && ((entry.RoomA == from && entry.RoomB == next) || (entry.RoomA == next && entry.RoomB == from))) { between = entry; break; }
        if (between != null) predicted = between.RoomA == next ? between.PointA : between.PointB;
        else predicted = RandomPointIn(next, lastSeenPosition);
        return NavMesh.SamplePosition(predicted, out NavMeshHit onMesh, 1.5f, motor.Agent.areaMask) && Reachable(onMesh.position);
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

        // 학습: 자주 숨어서 넘긴 곳은 조금 더 멀어도(1.5배) 먼저 뒤짐 (뒤질 개수에 포함)
        var candidates = new List<HidingSpot>();
        var learnedSpots = new List<HidingSpot>();
        foreach (HidingSpot spot in HidingSpot.All)
        {
            if (spotQueue.Contains(spot)) continue;
            float distance = Mathf.Min(FlatDistance(spot.transform.position, center),
                HasPrediction ? FlatDistance(spot.transform.position, PredictedPoint) : float.MaxValue);
            bool learned = FatherMemory.HideEscapes(spot) >= learnThreshold;
            if (distance > (learned ? spotSearchRadius * 1.5f : spotSearchRadius) || !Reachable(spot.transform.position)) continue;
            (learned ? learnedSpots : candidates).Add(spot);
        }
        learnedSpots.Sort((a, b) => FatherMemory.HideEscapes(b).CompareTo(FatherMemory.HideEscapes(a)));
        int picks = 0;
        foreach (HidingSpot spot in learnedSpots)
        {
            if (picks >= maxSpotsToSearch) break;
            spotQueue.Add(spot);
            picks++;
        }
        for (; picks < maxSpotsToSearch && candidates.Count > 0; picks++)
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
        TrackEpisode();
        if (CheckDoorOnRoute()) return;

        switch (Current)
        {
            case Mode.Prowl: UpdateProwl(); break;
            case Mode.Investigate: UpdateInvestigate(); break;
            case Mode.Chase: UpdateChase(); break;
            case Mode.Pursue: UpdatePursue(); break;
            case Mode.Intercept: UpdateIntercept(); break;
            case Mode.SearchSpots: UpdateSearchSpots(); break;
            case Mode.Search: UpdateSearch(); break;
            case Mode.DoorApproach: UpdateDoorApproach(); break;
            case Mode.Detour: UpdateDetour(); break;
            case Mode.FeignRetreat: UpdateFeign(); break;
            case Mode.Ambush: UpdateAmbush(); break;
            case Mode.Bash: UpdateBash(); break;
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
        if (pauseUntil <= 0f)
        {
            pauseUntil = Time.time + prowlPause;
            // 학습: 자주 숨어서 넘긴 곳이 이 방에 있으면 가끔 열어 봄
            HidingSpot learned = LearnedSpotHere();
            if (learned != null && rng.NextDouble() < checkLearnedSpotChance) Investigate(learned.transform.position, learned);
            return;
        }
        if (Time.time < pauseUntil) return;
        BeginProwl();
    }

    HidingSpot LearnedSpotHere()
    {
        CameraTrigger here = CameraTrigger.FindAt(transform.position);
        if (here == null) return null;
        HidingSpot best = null;
        int most = learnThreshold - 1;
        foreach (HidingSpot spot in HidingSpot.All)
        {
            int escapes = FatherMemory.HideEscapes(spot);
            if (escapes > most && here.ContainsPoint(spot.transform.position)) { most = escapes; best = spot; }
        }
        return best;
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
        if (hesitateUntil > 0f)
        {
            if (Time.time < hesitateUntil) { LookAround(); return; }
            hesitateUntil = 0f;
            modeTime = Time.time;
            MoveTo(goal);
            return;
        }
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
            float weight = ProwlWeight(room, here);
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

    // 지나가려는 순간 누가 문을 잠금: 쫓거나 조사하던 중이면 그 문 앞에서 판단, 아니면 다시 길찾기
    void OnBlockedOnLink(EntryPoint entry)
    {
        if (IsDecisionMode(Current) && entry != null && entry.door != null && !IsDoorIgnored(entry)) BeginDoorApproach(entry);
        else MoveTo(destination);
    }

    // 목적지까지 어떤 길(창문·환풍구 포함)로도 갈 수 없음: 막은 잠긴 문이 있으면 그 문 앞에서 판단.
    // 배회 중이거나 문 때문이 아니면 쫓던 중엔 근처 수색, 아니면 다른 방 배회.
    bool CheckBlockedPath()
    {
        NavMeshAgent agent = motor.Agent;
        if (agent.pathPending || motor.IsTraversing) return false;
        bool partial = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial;
        bool fellShort = !agent.hasPath && FlatDistance(transform.position, destination) > 1.0f;
        if (!partial && !fellShort) return false;
        if (Current != Mode.Prowl && Current != Mode.Search)
        {
            EntryPoint door = EntryPoint.FindBlockingDoor(transform.position, destination, agent.areaMask);
            if (door != null && !IsDoorIgnored(door)) { BeginDoorApproach(door); return true; }
        }
        if (Chasing) BeginSpotSearch(transform.position, null);
        else BeginProwl();
        return true;
    }

    // ---------------- 잠긴 문 판단 ----------------

    static bool IsDoorMode(Mode mode) =>
        mode == Mode.DoorApproach || mode == Mode.Detour || mode == Mode.FeignRetreat || mode == Mode.Ambush || mode == Mode.Bash;

    // 쫓거나 조사하러 가는 중 (배회·주변 수색은 판단하지 않음)
    static bool IsDecisionMode(Mode mode) =>
        mode == Mode.Investigate || mode == Mode.Pursue || mode == Mode.Intercept || mode == Mode.SearchSpots || mode == Mode.Detour;

    bool IsDoorIgnored(EntryPoint door) => doorIgnoredUntil.TryGetValue(door, out float until) && Time.time < until;
    void IgnoreDoor(EntryPoint door) { if (door != null) doorIgnoredUntil[door] = Time.time + doorDecisionMemory; }

    // 가는 길(가장 짧은 길)이 잠긴 문을 지나는지 주기적으로 확인. 지나면 길찾기가 알아서 돌아가기 전에 문 앞으로 감
    bool CheckDoorOnRoute()
    {
        if (!IsDecisionMode(Current) || Current == Mode.Detour || IsHesitating || Time.time < nextDoorCheck || motor.Agent.pathPending) return false;
        nextDoorCheck = Time.time + 0.4f;
        if (FlatDistance(transform.position, destination) < 1f) return false;
        EntryPoint door = FatherTactics.LockedDoorOnRoute(transform.position, destination, motor.Agent.areaMask, out _, out _);
        if (door == null || IsDoorIgnored(door)) return false;
        BeginDoorApproach(door);
        return true;
    }

    void BeginDoorApproach(EntryPoint door)
    {
        if (!IsDoorMode(Current))
        {
            doorGoal = destination;
            doorSpot = Current == Mode.Investigate ? investigateSpot : Current == Mode.SearchSpots ? currentSpot : null;
        }
        DoorTarget = door;
        Vector3 front = FrontOf(door);
        doorBehindRoom = door.OtherSide(CameraTrigger.FindAt(front));
        SetMode(Mode.DoorApproach);
        MoveTo(front);
    }

    void UpdateDoorApproach()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; }
        if (!motor.Arrived(0.5f))
        {
            if (InMode > 15f) GiveUpDoor();
            return;
        }
        FaceTowards(DoorCenter(DoorTarget));
        if (pauseUntil <= 0f) { pauseUntil = Time.time + doorCheckTime; return; } // 손잡이를 돌려 봄
        if (Time.time < pauseUntil) return;
        DecideAtDoor();
    }

    void DecideAtDoor()
    {
        FatherTactics.LockedDoorOnRoute(transform.position, doorGoal, motor.Agent.areaMask, out float direct, out float actual);
        LastDetourAvailable = actual >= 0f;
        doorDetourExtra = LastDetourAvailable ? Mathf.Max(0f, actual - Mathf.Max(0f, direct)) : 0f;
        var situation = new FatherTactics.DoorSituation
        {
            detourAvailable = LastDetourAvailable,
            detourExtra = doorDetourExtra,
            hasWeapon = hasWeapon,
            breakable = DoorTarget.door.Breakable,
        };
        var weights = WeightsFor(DoorTarget);
        if (Chasing) episodeDoors.Add(DoorTarget);
        DoorTactic tactic = forceDoorTactic != DoorTactic.None ? forceDoorTactic : FatherTactics.Choose(situation, weights, rng);
        if ((tactic == DoorTactic.Detour && !LastDetourAvailable) || (tactic == DoorTactic.Bash && !(hasWeapon && situation.breakable)))
            tactic = DoorTactic.Ambush;
        LastDoorTactic = tactic;
        int learned = FatherMemory.DoorEscapes(DoorTarget);
        Debug.Log($"[아버지] {DoorTarget.displayName} 잠김 → {tactic} (우회로 {(LastDetourAvailable ? $"있음 +{doorDetourExtra:0}m" : "없음")}, 무기 {(hasWeapon ? "있음" : "없음")}" +
                  $"{(learned >= learnThreshold ? $", 학습: 이 문으로 {learned}회 버팀 → 우회·잠복 우선" : "")})");

        switch (tactic)
        {
            case DoorTactic.Detour: BeginDetour(); break;
            case DoorTactic.FeignRetreat: BeginFeign(); break;
            case DoorTactic.Bash: BeginBash(); break;
            default: BeginAmbush(); break;
        }
    }

    // 우회: 창문·환풍구·다른 문으로 조용히 돌아 들어감
    void BeginDetour()
    {
        IgnoreDoor(DoorTarget);
        SetMode(Mode.Detour);
        motor.quiet = true;
        MoveTo(doorGoal);
    }

    void UpdateDetour()
    {
        if (CheckReacquire()) return;
        if ((doorBehindRoom != null && doorBehindRoom.ContainsPoint(transform.position)) || motor.Arrived(0.6f)) { ResumeAfterDoor(); return; }
        if (CheckBlockedPath()) return;
        if (InMode > 45f) GiveUpDoor();
    }

    // 위장 후퇴: 보란 듯이 걸어서 떠나 포기한 척 → 몰래 우회하거나 돌아와 잠복
    void BeginFeign()
    {
        if (!TryFindRetreatPoint(DoorTarget, out Vector3 retreat)) { BeginAmbush(); return; }
        feignSneaking = false;
        SetMode(Mode.FeignRetreat);
        MoveTo(retreat);
    }

    void UpdateFeign()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; } // 떠난 줄 알고 문을 열었음
        if (!motor.Arrived(0.6f) && InMode < 12f) return;
        if (pauseUntil <= 0f) { pauseUntil = Time.time + feignPause; return; }
        if (Time.time < pauseUntil) return;
        feignSneaking = true;
        if (LastDetourAvailable && rng.NextDouble() < feignDetourChance) BeginDetour();
        else BeginAmbush();
    }

    bool TryFindRetreatPoint(EntryPoint door, out Vector3 retreat)
    {
        retreat = transform.position;
        Vector3 center = DoorCenter(door);
        float best = -1f;
        for (int i = 0; i < 12; i++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float distance = feignDistance + ((float)rng.NextDouble() - 0.5f) * 3f;
            Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, motor.Agent.areaMask)) continue;
            if (doorBehindRoom != null && doorBehindRoom.ContainsPoint(hit.position)) continue;
            CameraTrigger room = CameraTrigger.FindAt(hit.position);
            if (room == null || room.buildingId != (doorBehindRoom != null ? doorBehindRoom.buildingId : room.buildingId)) continue; // 집 안에서만
            float length = FatherTactics.PathLength(transform.position, hit.position, motor.Agent.areaMask, out _);
            if (length < 0f || length > feignDistance * 2.5f) continue;
            float away = FlatDistance(hit.position, center);
            if (away > best) { best = away; retreat = hit.position; }
        }
        return best >= feignDistance * 0.6f;
    }

    // 잠복: 문 옆(문짝이 열리는 자리 밖)에서 조용히 기다림. 문이 열리면 바로 들이닥침
    void BeginAmbush()
    {
        ambushPoint = AmbushPointFor(DoorTarget);
        SetMode(Mode.Ambush);
        motor.quiet = true;
        ambushStart = Time.time;
        ambushUntil = Time.time + ambushTime;
        MoveTo(ambushPoint);
    }

    void UpdateAmbush()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; }
        if (motor.Arrived(0.4f)) FaceTowards(DoorCenter(DoorTarget));
        if (Time.time > ambushUntil) GiveUpDoor();
    }

    Vector3 AmbushPointFor(EntryPoint door)
    {
        Vector3 center = DoorCenter(door);
        Vector3 off = FrontOf(door) - center; off.y = 0f;
        Vector3 along = Vector3.ProjectOnPlane(door.transform.forward, Vector3.up).normalized;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 candidate = center + off * 0.8f + along * side * 1.4f;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.6f, motor.Agent.areaMask) && Reachable(hit.position)
                && (doorBehindRoom == null || !doorBehindRoom.ContainsPoint(hit.position)))
                return hit.position;
        }
        return FrontOf(door);
    }

    // 파괴: 무기로 문을 내리침 (우회로가 없을 때 최후의 수단)
    void BeginBash()
    {
        SetMode(Mode.Bash);
        nextBash = Time.time + 0.3f;
        MoveTo(FrontOf(DoorTarget));
    }

    void UpdateBash()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; } // 부서짐(또는 누가 열어 줌)
        if (!motor.Arrived(0.5f)) return;
        FaceTowards(DoorCenter(DoorTarget));
        if (InMode > bashGiveUpTime || !DoorTarget.door.Breakable) { GiveUpDoor(); return; }
        if (Time.time < nextBash) return;
        DoorTarget.door.Bash(bashDamage, gameObject);
        nextBash = Time.time + bashInterval;
    }

    // 문이 열렸거나 부서졌거나 돌아 들어왔음: 하던 일 계속
    void ResumeAfterDoor()
    {
        IgnoreDoor(DoorTarget);
        DoorTarget = null;
        HidingSpot spot = doorSpot;
        doorSpot = null;
        if (spot != null)
        {
            if (Chasing) BeginSpotSearch(spot.transform.position, spot);
            else Investigate(spot.transform.position, spot);
        }
        else if (Chasing)
        {
            goal = doorGoal;
            SetMode(Mode.Pursue);
            MoveTo(doorGoal);
        }
        else Investigate(doorGoal, null);
    }

    void GiveUpDoor()
    {
        EntryPoint door = DoorTarget;
        IgnoreDoor(door);
        DoorTarget = null;
        doorSpot = null;
        if (Chasing) GiveUp(); // 이번 추격에서 막혔던 문은 GiveUp에서 학습
        else
        {
            FatherMemory.RecordDoorEscape(door); // 소리를 쫓아왔다가 문 앞에서 포기
            BeginProwl();
        }
    }

    // 문의 내 쪽 앞 지점
    Vector3 FrontOf(EntryPoint door) =>
        (door.PointA - transform.position).sqrMagnitude <= (door.PointB - transform.position).sqrMagnitude ? door.PointA : door.PointB;

    static Vector3 DoorCenter(EntryPoint door) => (door.PointA + door.PointB) * 0.5f;

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
        if (DoorTarget != null && IsDoorMode(Current))
        {
            Gizmos.color = Current == Mode.Bash ? Color.red : new Color(1f, 0.6f, 0f);
            Gizmos.DrawLine(transform.position, DoorCenter(DoorTarget));
            if (Current == Mode.Ambush) Gizmos.DrawWireCube(ambushPoint + Vector3.up * 0.1f, new Vector3(0.5f, 0.1f, 0.5f));
            if (Current == Mode.Detour || Current == Mode.FeignRetreat) Gizmos.DrawWireSphere(destination, 0.3f);
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
