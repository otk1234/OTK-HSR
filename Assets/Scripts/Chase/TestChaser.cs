using System.Collections.Generic;
using UnityEngine;

// 1단계(감지·숨기·문) 시험용 더미 추격자. 2단계에서 경찰 AI로 바꿉니다.
// 순찰 → (소리) 확인하러 감 → (발견) 추격 → 놓치면 마지막 위치 확인 후 포기.
// 숨는 장면을 봤다면 그 숨는 곳을 뒤집니다. 잠긴 문에 막히면 그냥 포기합니다.
[RequireComponent(typeof(ChaserMotor), typeof(ChaserSenses))]
public class TestChaser : MonoBehaviour
{
    public enum Mode { Patrol, Investigate, Chase, SearchSpot, LostTarget }

    public Transform[] patrolPoints;
    public float catchDistance = 1.0f;
    public float lookAroundTime = 2f;
    public float giveUpTime = 4f;

    public Mode Current { get; private set; } = Mode.Patrol;
    public CharacterStatus Target { get; private set; }

    private ChaserMotor motor;
    private ChaserSenses senses;
    private int patrolIndex;
    private float modeTime;
    private Vector3 investigatePoint;
    private HidingSpot spotToSearch;

    void Awake()
    {
        motor = GetComponent<ChaserMotor>();
        senses = GetComponent<ChaserSenses>();
    }

    void OnEnable()
    {
        senses.Spotted += OnSpotted;
        senses.Heard += OnHeard;
        motor.Blocked += OnBlocked;
    }

    void OnDisable()
    {
        senses.Spotted -= OnSpotted;
        senses.Heard -= OnHeard;
        motor.Blocked -= OnBlocked;
        if (GameStateManager.instance != null) GameStateManager.instance.EndChase(this);
    }

    void Start() => SetMode(Mode.Patrol);

    void SetMode(Mode mode)
    {
        Current = mode;
        modeTime = Time.time;
        motor.SetRunning(mode == Mode.Chase || mode == Mode.SearchSpot);
        if (mode == Mode.Patrol) GoToPatrolPoint();
    }

    void OnSpotted(CharacterStatus who)
    {
        if (who.IsCaught || (GameStateManager.instance != null && GameStateManager.instance.RestartPending)) return;
        Target = who;
        List<CharacterStatus> targets = ChaseTargeting.FromSighting(who);
        if (GameStateManager.instance != null) GameStateManager.instance.BeginChase(this, targets);
        SetMode(Mode.Chase);
    }

    void OnHeard(NoiseEvent noise)
    {
        if (Current == Mode.Chase || Current == Mode.SearchSpot) return;
        investigatePoint = noise.position;
        motor.SetDestination(investigatePoint);
        SetMode(Mode.Investigate);
    }

    void OnBlocked(EntryPoint entry)
    {
        Debug.Log($"[더미 추격자] {entry.displayName} 잠겨 있어서 포기");
        GiveUp();
    }

    void GiveUp()
    {
        if (Target != null) senses.Forget(Target);
        Target = null;
        spotToSearch = null;
        if (GameStateManager.instance != null) GameStateManager.instance.EndChase(this);
        SetMode(Mode.Patrol);
    }

    void Update()
    {
        if (GameStateManager.instance != null && GameStateManager.instance.RestartPending) { motor.Stop(); return; }

        switch (Current)
        {
            case Mode.Patrol:
                if (motor.Arrived()) { patrolIndex++; GoToPatrolPoint(); }
                break;

            case Mode.Investigate:
                if (motor.Arrived() && Time.time - modeTime > 0.5f)
                {
                    transform.Rotate(0f, 120f * Time.deltaTime, 0f); // 둘러보기
                    if (Time.time - modeTime > lookAroundTime + 3f) SetMode(Mode.Patrol);
                }
                break;

            case Mode.Chase:
                UpdateChase();
                break;

            case Mode.SearchSpot:
                motor.SetDestination(spotToSearch.transform.position);
                if (Vector3.Distance(transform.position, spotToSearch.transform.position) <= catchDistance + 0.4f)
                {
                    foreach (CharacterStatus found in spotToSearch.Search(gameObject))
                        GameStateManager.instance?.ReportCaught(found, this);
                    GiveUp();
                }
                break;

            case Mode.LostTarget:
                if (Target != null && senses.IsVisible(Target)) { SetMode(Mode.Chase); break; } // 다시 보이면 추격 재개
                if (motor.Arrived()) transform.Rotate(0f, 120f * Time.deltaTime, 0f);
                if (Time.time - modeTime > giveUpTime) GiveUp();
                break;
        }
    }

    void UpdateChase()
    {
        if (Target == null) { GiveUp(); return; }

        if (Target.IsHidden)
        {
            // 숨는 장면을 봤으면 그곳을 뒤짐, 아니면 놓침
            if (Target.HidingIn.WasSeenEnteringBy(gameObject)) { spotToSearch = Target.HidingIn; SetMode(Mode.SearchSpot); }
            else LoseTarget();
            return;
        }

        if (senses.IsVisible(Target))
        {
            motor.SetDestination(Target.transform.position);
            if (Vector3.Distance(transform.position, Target.transform.position) <= catchDistance)
                GameStateManager.instance?.ReportCaught(Target, this);
        }
        else LoseTarget();
    }

    void LoseTarget()
    {
        if (senses.TryGetLastSeen(Target, out Vector3 last, out _)) motor.SetDestination(last);
        SetMode(Mode.LostTarget);
    }

    void GoToPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        motor.SetDestination(patrolPoints[patrolIndex % patrolPoints.Length].position);
    }
}
