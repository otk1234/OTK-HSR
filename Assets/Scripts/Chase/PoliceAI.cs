using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 경찰 AI (비교적 단순한 추격자)
//  순찰 → 의심(소리·흘끗 봄) → 추격(발견) → 수색(놓침) → 복귀
//  - 숨는 장면을 봤을 때만 그 숨는 곳을 뒤짐
//  - 잠긴 문: 노크·경고 → 부수기 → 일정 시간 뒤 포기, 포기한 방은 '의심 지점'으로 기억해 한동안 순찰 때 들름
//  - 창문·환풍구는 못 씀 (ChaserMotor의 ChaserType.Police)
[RequireComponent(typeof(ChaserMotor), typeof(ChaserSenses))]
public class PoliceAI : MonoBehaviour, IChaserBrain
{
    public enum Mode { Patrol, CheckSuspicious, Suspicious, Chase, SearchSpot, Search, Return, DoorApproach, DoorKnock, DoorBash }

    public class SuspiciousPoint
    {
        public EntryPoint door;
        public CameraTrigger room;   // 문 너머 방
        public float until;          // 이때까지 기억
        public float lastVisit = float.NegativeInfinity;
    }

    [Header("순찰")]
    public Transform[] patrolPoints;
    public float patrolPause = 0.8f;

    [Header("의심 (소리·흘끗 봄)")]
    [Tooltip("발견까지는 아니어도 인지도가 이만큼 차면 확인하러 감")]
    [Range(0f, 1f)] public float suspicionAwareness = 0.5f;
    public float lookAroundTime = 2.5f;

    [Header("추격")]
    public float catchDistance = 1.0f;

    [Header("수색 (놓친 뒤)")]
    public float searchTime = 10f;
    public float searchRadius = 4f;
    public float searchPointPause = 1f;

    [Header("잠긴 문")]
    public float knockTime = 2f;
    public float knockNoise = 8f;
    public string knockMessage = "경찰: 경찰이다! 문 열어!";
    public float bashDamage = 12f;
    public float bashInterval = 1.5f;
    [Tooltip("노크 시작부터 이 시간이 지나면 포기")]
    public float giveUpTime = 10f;

    [Header("의심 지점 기억 (포기한 방)")]
    public float suspiciousMemory = 90f;
    [Tooltip("같은 의심 지점을 다시 들르기까지 최소 간격")]
    public float revisitInterval = 20f;

    public Mode Current { get; private set; } = Mode.Patrol;
    public CharacterStatus Target { get; private set; }
    public EntryPoint DoorTarget { get; private set; }
    public IReadOnlyList<SuspiciousPoint> SuspiciousPoints => suspicious;

    public string DisplayName => name;
    public string StateLabel => Current switch
    {
        Mode.Patrol => "순찰",
        Mode.CheckSuspicious => "의심 지점 확인",
        Mode.Suspicious => "의심",
        Mode.Chase => "추격",
        Mode.SearchSpot => "숨는 곳 수색",
        Mode.Search => "주변 수색",
        Mode.Return => "복귀",
        Mode.DoorApproach => $"잠긴 문으로 ({DoorTarget?.displayName})",
        Mode.DoorKnock => $"노크 ({DoorTarget?.displayName})",
        Mode.DoorBash => $"문 부수기 ({DoorTarget?.displayName} {DoorTarget?.door?.Durability:0})",
        _ => Current.ToString(),
    };

    private ChaserMotor motor;
    private ChaserSenses senses;
    private GameStateManager Game => GameStateManager.instance;

    private int patrolIndex;
    private float modeTime;
    private float pauseUntil;
    private Vector3 goal;                 // 지금 가려는 곳 (의심·수색·추격의 마지막 위치)
    private Vector3 destination;          // 실제로 NavMesh에 넘긴 목적지
    private HidingSpot spotToSearch;
    private SuspiciousPoint visiting;
    private bool visitingInside;
    private readonly List<SuspiciousPoint> suspicious = new List<SuspiciousPoint>();
    private readonly List<Vector3> searchPoints = new List<Vector3>();
    private int searchIndex;
    private float nextBash;
    private float doorStart;
    private Mode modeBeforeDoor;
    private float nextRepath;

    void Awake()
    {
        motor = GetComponent<ChaserMotor>();
        senses = GetComponent<ChaserSenses>();
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

    void Start() => ResetToPatrol();

    // 처음 상태로 (연출·점검용)
    public void ResetToPatrol()
    {
        Target = null;
        DoorTarget = null;
        spotToSearch = null;
        visiting = null;
        if (Game != null) Game.EndChase(this);
        patrolIndex = NearestPatrolIndex();
        SetMode(Mode.Patrol);
        GoToPatrolPoint();
    }

    void SetMode(Mode mode)
    {
        Current = mode;
        modeTime = Time.time;
        pauseUntil = 0f;
        motor.SetRunning(mode == Mode.Chase || mode == Mode.SearchSpot ||
                         ((mode == Mode.DoorApproach) && Target != null));
    }

    float InMode => Time.time - modeTime;

    // ---------------- 감각 이벤트 ----------------

    void OnSpotted(CharacterStatus who)
    {
        if (who.IsCaught || (Game != null && Game.RestartPending)) return;
        if (Current == Mode.Chase || Current == Mode.SearchSpot)
        {
            // 추격 중 다른 캐릭터도 발견: 대상에 추가
            if (Game != null && !Game.IsTargeted(who))
            {
                var targets = new List<CharacterStatus>(Game.TargetsOf(this));
                targets.AddRange(ChaseTargeting.FromSighting(who).FindAll(c => !targets.Contains(c)));
                Game.BeginChase(this, targets);
            }
            return;
        }
        StartChase(who);
    }

    void OnHeard(NoiseEvent noise)
    {
        switch (Current)
        {
            case Mode.Patrol:
            case Mode.CheckSuspicious:
            case Mode.Suspicious:
            case Mode.Return:
                BecomeSuspicious(noise.position);
                break;
            case Mode.Search:
                // 수색 중에 들린 소리는 그쪽부터 다시 수색
                BeginSearch(noise.position, keepTimer: true);
                break;
        }
    }

    // ---------------- 상태 전환 ----------------

    void StartChase(CharacterStatus who)
    {
        Target = who;
        spotToSearch = null;
        if (Game != null) Game.BeginChase(this, ChaseTargeting.FromSighting(who));
        SetMode(Mode.Chase);
    }

    void BecomeSuspicious(Vector3 position)
    {
        goal = position;
        SetMode(Mode.Suspicious);
        MoveTo(goal);
    }

    void BeginSearch(Vector3 center, bool keepTimer = false)
    {
        float started = modeTime;
        goal = center;
        searchPoints.Clear();
        searchPoints.Add(center);
        for (int i = 0; i < 8 && searchPoints.Count < 4; i++)
        {
            Vector2 offset = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, motor.Agent.areaMask)) continue;
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, hit.position, motor.Agent.areaMask, path) && path.status == NavMeshPathStatus.PathComplete)
                searchPoints.Add(hit.position);
        }
        searchIndex = 0;
        SetMode(Mode.Search);
        if (keepTimer) modeTime = started;
        pauseUntil = 0f;
        MoveTo(searchPoints[0]);
        // 다시 보이면 곧바로 '발견'이 다시 일어나도록
        foreach (CharacterStatus character in CharacterStatus.All) senses.Forget(character);
    }

    void ReturnToPatrol()
    {
        Target = null;
        spotToSearch = null;
        DoorTarget = null;
        if (Game != null) Game.EndChase(this);
        foreach (CharacterStatus character in CharacterStatus.All) senses.Forget(character);
        patrolIndex = NearestPatrolIndex();
        SetMode(Mode.Return);
        GoToPatrolPoint();
    }

    // ---------------- 매 프레임 ----------------

    void Update()
    {
        if (Game != null && Game.RestartPending) { motor.Stop(); return; }
        if (motor.IsTraversing) return;

        ForgetExpiredSuspicion();

        switch (Current)
        {
            case Mode.Patrol: UpdatePatrol(); break;
            case Mode.CheckSuspicious: UpdateCheckSuspicious(); break;
            case Mode.Suspicious: UpdateSuspicious(); break;
            case Mode.Chase: UpdateChase(); break;
            case Mode.SearchSpot: UpdateSearchSpot(); break;
            case Mode.Search: UpdateSearch(); break;
            case Mode.Return: UpdateReturn(); break;
            case Mode.DoorApproach: UpdateDoorApproach(); break;
            case Mode.DoorKnock: UpdateDoorKnock(); break;
            case Mode.DoorBash: UpdateDoorBash(); break;
        }
    }

    void UpdatePatrol()
    {
        if (CheckGlimpse()) return;
        if (!motor.Arrived()) return;

        if (pauseUntil <= 0f) { pauseUntil = Time.time + patrolPause; return; }
        if (Time.time < pauseUntil) return;
        pauseUntil = 0f;

        // 순찰 지점에 도착할 때마다, 들를 때가 된 의심 지점이 있으면 먼저 확인
        SuspiciousPoint due = suspicious.Find(p => Time.time - p.lastVisit >= revisitInterval);
        if (due != null) { StartVisit(due); return; }

        patrolIndex = (patrolIndex + 1) % Mathf.Max(1, patrolPoints.Length);
        GoToPatrolPoint();
    }

    void StartVisit(SuspiciousPoint point)
    {
        visiting = point;
        visiting.lastVisit = Time.time;
        visitingInside = false;
        SetMode(Mode.CheckSuspicious);
        MoveTo(OutsideOf(point));
    }

    // 의심 지점 문의 바깥쪽(의심 방이 아닌 쪽) 앞
    static Vector3 OutsideOf(SuspiciousPoint point) =>
        point.door.RoomA == point.room ? point.door.PointB : point.door.PointA;

    void UpdateCheckSuspicious()
    {
        if (CheckGlimpse()) return;
        if (visiting == null || !suspicious.Contains(visiting)) { SetMode(Mode.Patrol); GoToPatrolPoint(); return; }

        if (!visitingInside)
        {
            if (!motor.Arrived(0.5f)) return;
            if (!visiting.door.IsLocked)
            {
                // 열려 있으면 방 안까지 확인
                visitingInside = true;
                MoveTo(RoomCenter(visiting.room));
                modeTime = Time.time;
            }
            else
            {
                // 아직 잠겨 있음: 2초 귀 기울이고 순찰 계속 (안에서 소리가 나면 의심 → 문 대응)
                if (pauseUntil <= 0f) pauseUntil = Time.time + 2f;
                else if (Time.time > pauseUntil) { SetMode(Mode.Patrol); GoToPatrolPoint(); }
            }
            return;
        }

        // 방 안에 도착(또는 너무 오래 걸림) → 둘러본 뒤 확인 끝
        if (!motor.Arrived(0.6f) && InMode < 12f) return;
        if (pauseUntil <= 0f) { pauseUntil = Time.time + lookAroundTime; return; }
        LookAround();
        if (Time.time < pauseUntil) return;
        suspicious.Remove(visiting);
        visiting = null;
        SetMode(Mode.Patrol);
        GoToPatrolPoint();
    }

    void UpdateSuspicious()
    {
        if (CheckBlockedPath()) return;
        if (!motor.Arrived(0.6f))
        {
            if (InMode > 15f) ReturnToPatrol();
            return;
        }
        LookAround();
        if (pauseUntil <= 0f) pauseUntil = Time.time + lookAroundTime;
        else if (Time.time > pauseUntil)
        {
            // 의심 지점으로 기억하던 방 안을 이미 둘러봤으면 확인한 것으로 침
            CameraTrigger here = CameraTrigger.FindAt(transform.position);
            suspicious.RemoveAll(p => p.room != null && p.room == here);
            pauseUntil = 0f;
            SetMode(Mode.Patrol);
            GoToPatrolPoint();
        }
    }

    void UpdateChase()
    {
        CharacterStatus target = PickTarget();
        if (target == null)
        {
            // 보이는 대상이 없음
            if (Target != null && Target.IsHidden && Target.HidingIn.WasSeenEnteringBy(gameObject))
            {
                spotToSearch = Target.HidingIn;
                SetMode(Mode.SearchSpot);
                MoveTo(spotToSearch.transform.position);
                return;
            }
            if (CheckBlockedPath()) return;
            if (motor.Arrived(0.6f)) BeginSearch(goal);
            return;
        }

        Target = target;
        goal = target.transform.position;
        if (Time.time >= nextRepath) { MoveTo(goal); nextRepath = Time.time + 0.2f; }
        if (FlatDistance(transform.position, target.transform.position) <= catchDistance && Game != null)
            Game.ReportCaught(target, this);
    }

    // 추격 대상 중 지금 보이는 가장 가까운 캐릭터
    CharacterStatus PickTarget()
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

    void UpdateSearchSpot()
    {
        if (spotToSearch == null) { BeginSearch(goal); return; }
        if (FlatDistance(transform.position, spotToSearch.transform.position) > catchDistance + 0.4f)
        {
            if (!motor.Agent.pathPending && !motor.Agent.hasPath) MoveTo(spotToSearch.transform.position);
            return;
        }
        List<CharacterStatus> found = spotToSearch.Search(gameObject);
        if (found.Count > 0 && Game != null)
        {
            foreach (CharacterStatus character in found) Game.ReportCaught(character, this);
            return;
        }
        BeginSearch(spotToSearch.transform.position); // 이미 빠져나갔음
    }

    void UpdateSearch()
    {
        if (CheckBlockedPath()) return;
        if (InMode > searchTime) { ReturnToPatrol(); return; }
        if (!motor.Arrived(0.6f)) return;

        LookAround();
        if (pauseUntil <= 0f) { pauseUntil = Time.time + searchPointPause; return; }
        if (Time.time < pauseUntil) return;
        pauseUntil = 0f;
        searchIndex = (searchIndex + 1) % searchPoints.Count;
        MoveTo(searchPoints[searchIndex]);
    }

    void UpdateReturn()
    {
        if (CheckGlimpse()) return;
        if (motor.Arrived()) SetMode(Mode.Patrol);
    }

    // ---------------- 잠긴 문 ----------------

    void OnBlockedOnLink(EntryPoint entry) => ApproachDoor(entry);

    // 목적지까지 길이 끊겼으면 막고 있는 잠긴 문을 찾아 그 문으로 갑니다.
    bool CheckBlockedPath()
    {
        NavMeshAgent agent = motor.Agent;
        if (agent.pathPending || motor.IsTraversing) return false;
        bool partial = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial;
        // 막힌 곳 바로 앞에 서 있으면 '끊긴 길'의 끝에 이미 도착해서 경로가 바로 사라짐 → 목적지와의 거리로 판단
        bool fellShort = !agent.hasPath && FlatDistance(transform.position, destination) > 1.0f;
        if (!partial && !fellShort) return false;
        EntryPoint door = EntryPoint.FindBlockingDoor(transform.position, goal, agent.areaMask);
        if (door != null) { ApproachDoor(door); return true; }
        // 문 때문도 아닌데 갈 수 없음(창문 너머 등): 포기
        if (Target != null) BeginSearch(transform.position);
        else ReturnToPatrol();
        return true;
    }

    void ApproachDoor(EntryPoint door)
    {
        if (door == null || door.door == null) return;
        if (Current != Mode.DoorApproach && Current != Mode.DoorKnock && Current != Mode.DoorBash) modeBeforeDoor = Current;
        DoorTarget = door;
        SetMode(Mode.DoorApproach);
        MoveTo(FrontOf(door));
    }

    void UpdateDoorApproach()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; }
        if (!motor.Arrived(0.5f)) return;
        FaceTowards(DoorTarget.transform.position + DoorTarget.transform.rotation * Vector3.forward * 0.6f);
        doorStart = Time.time;
        NoiseSystem.Emit(DoorTarget.transform.position, knockNoise, NoiseKind.Other, gameObject);
        GameMessage.Show(knockMessage);
        SetMode(Mode.DoorKnock);
    }

    void UpdateDoorKnock()
    {
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; }
        if (InMode < knockTime) return;
        nextBash = Time.time;
        SetMode(Mode.DoorBash);
    }

    void UpdateDoorBash()
    {
        InteractableDoor door = DoorTarget.door;
        if (!DoorTarget.IsLocked) { ResumeAfterDoor(); return; }
        if (Time.time - doorStart >= giveUpTime || !door.Breakable)
        {
            GiveUpAtDoor();
            return;
        }
        if (Time.time >= nextBash)
        {
            door.Bash(bashDamage, gameObject);
            nextBash = Time.time + bashInterval;
        }
    }

    void GiveUpAtDoor()
    {
        EntryPoint door = DoorTarget;
        CameraTrigger here = CameraTrigger.FindAt(transform.position);
        CameraTrigger behind = door.RoomA == here ? door.RoomB : door.RoomA;
        SuspiciousPoint existing = suspicious.Find(p => p.door == door);
        if (existing != null) existing.until = Time.time + suspiciousMemory;
        else suspicious.Add(new SuspiciousPoint { door = door, room = behind, until = Time.time + suspiciousMemory, lastVisit = Time.time });
        Debug.Log($"[경찰] {door.displayName} 포기 → 의심 지점으로 기억 ({(behind != null ? behind.roomName : "?")})");
        ReturnToPatrol();
    }

    // 문이 열렸거나 부서짐: 하던 일 계속
    void ResumeAfterDoor()
    {
        DoorTarget = null;
        switch (modeBeforeDoor)
        {
            case Mode.Chase:
            case Mode.Search:
            case Mode.SearchSpot:
                SetMode(Mode.Chase); // 마지막으로 본 곳(goal)으로 다시 뛰어감
                MoveTo(goal);
                break;
            default:
                BecomeSuspicious(goal);
                break;
        }
    }

    // ---------------- 도우미 ----------------

    bool CheckGlimpse()
    {
        foreach (CharacterStatus character in CharacterStatus.All)
        {
            if (senses.Awareness(character) < suspicionAwareness || senses.IsSpotted(character)) continue;
            if (senses.TryGetLastSeen(character, out Vector3 seen, out _)) { BecomeSuspicious(seen); return true; }
        }
        return false;
    }

    void MoveTo(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 1.5f, motor.Agent.areaMask)) position = hit.position;
        destination = position;
        motor.SetDestination(position);
    }

    void GoToPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        MoveTo(patrolPoints[patrolIndex % patrolPoints.Length].position);
    }

    int NearestPatrolIndex()
    {
        int best = 0;
        float nearest = float.MaxValue;
        for (int i = 0; patrolPoints != null && i < patrolPoints.Length; i++)
        {
            float d = (patrolPoints[i].position - transform.position).sqrMagnitude;
            if (d < nearest) { nearest = d; best = i; }
        }
        return best;
    }

    // 문의 내 쪽 앞 지점
    Vector3 FrontOf(EntryPoint door)
    {
        return (door.PointA - transform.position).sqrMagnitude <= (door.PointB - transform.position).sqrMagnitude ? door.PointA : door.PointB;
    }

    static Vector3 RoomCenter(CameraTrigger room)
    {
        if (room == null) return Vector3.zero;
        Vector3 center = room.GetComponent<Collider>().bounds.center;
        center.y = 0f;
        return center;
    }

    void LookAround() => transform.Rotate(0f, 120f * Time.deltaTime, 0f);

    void FaceTowards(Vector3 point)
    {
        Vector3 flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat);
    }

    static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    void ForgetExpiredSuspicion()
    {
        suspicious.RemoveAll(p => Time.time > p.until);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (DoorTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, DoorTarget.transform.position);
        }
        Gizmos.color = new Color(1f, 0.6f, 0f);
        foreach (SuspiciousPoint point in suspicious)
            if (point.room != null) Gizmos.DrawWireCube(RoomCenter(point.room) + Vector3.up * 0.1f, new Vector3(1f, 0.1f, 1f));
        if (Current == Mode.Search)
        {
            Gizmos.color = Color.cyan;
            foreach (Vector3 p in searchPoints) Gizmos.DrawWireSphere(p, 0.25f);
        }
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.4f, "경찰: " + StateLabel);
#endif
    }
}
