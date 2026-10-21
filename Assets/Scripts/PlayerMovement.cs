using UnityEngine;
using UnityEngine.AI;
using NarrativeGraybox.Player;

public class PlayerMovement : MonoBehaviour
{
    [Header("이동 속도")]
    public float moveSpeed = 5f;

    [Header("달리기 (스태미나는 PlayerStamina가 있을 때만 적용)")]
    public bool canRun = true;
    public float runSpeedMultiplier = 1.6f;
    public KeyCode runKey = KeyCode.LeftShift;

    [Header("카메라 방향 기준으로 이동 (쿼터뷰 맵용)")]
    public bool cameraRelative = false;

    // 이동을 막는 이유들 (조사·대화·숨기·잡힘 …). 하나라도 있으면 움직이지 않습니다.
    public readonly LockSet Locks = new LockSet();

    public bool IsMoving { get; private set; }
    public bool IsRunning { get; private set; }

    // 자동 점검·연출용: 값이 있으면 키보드 대신 이 입력을 씁니다.
    [System.NonSerialized] public Vector2? scriptedInput;
    [System.NonSerialized] public bool scriptedRun;

    private NavMeshAgent agent;
    private SpriteCharacterVisual spriteVisual; // 그레이박스의 4방향 스프라이트 (없으면 무시)
    private PlayerStamina stamina;               // 없으면 무제한으로 달림

    // 키를 누르고 있는 동안 쓰는 방향 기준 (카메라 시점이 바뀌어도 손을 뗄 때까지 유지)
    private Vector3 inputForward = Vector3.forward;
    private Vector3 inputRight = Vector3.right;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        spriteVisual = GetComponentInChildren<SpriteCharacterVisual>();
        stamina = GetComponent<PlayerStamina>();
    }

    // 예전 방식 호환용. 새 코드는 Locks.Set(이유, 잠금)을 씁니다.
    public void SetMovementEnabled(bool enabled)
    {
        Locks.Set(LockReason.Legacy, !enabled);
    }

    void OnDisable()
    {
        // 조작이 다른 캐릭터로 넘어가면 걷는 모션을 멈춥니다.
        StopMoving();
    }

    void StopMoving()
    {
        IsMoving = false;
        IsRunning = false;
        if (spriteVisual != null) spriteVisual.SetMovementInput(Vector2.zero);
    }

    void Update()
    {
        if ((FadeManager.instance != null && FadeManager.instance.isFading) || Locks.IsLocked)
        {
            StopMoving();
            return;
        }

        Vector2 input = scriptedInput ?? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool wantsRun = canRun && (scriptedInput.HasValue ? scriptedRun : Input.GetKey(runKey));
        float h = input.x, v = input.y;

        Vector3 moveDir = new Vector3(h, 0f, v).normalized;

        // W가 화면 위쪽이 되도록 카메라 방향에 맞춥니다.
        // 탑뷰 ↔ 아이소메트릭 경계에서 방향이 갑자기 바뀌지 않도록, 키를 뗐을 때만 기준을 갱신합니다.
        if (cameraRelative && Camera.main != null)
        {
            if (moveDir.sqrMagnitude < 0.01f)
            {
                Transform cam = Camera.main.transform;
                inputForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                inputRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            }
            moveDir = (inputForward * v + inputRight * h).normalized;
        }

        IsMoving = moveDir.magnitude >= 0.1f;
        IsRunning = IsMoving && wantsRun && (stamina == null || stamina.CanRun);
        if (IsRunning && stamina != null) stamina.Drain(Time.deltaTime);

        if (spriteVisual != null)
        {
            spriteVisual.SetMovementInput(Vector2.ClampMagnitude(new Vector2(h, v), 1f));
            spriteVisual.SetAnimationSpeed(IsRunning ? runSpeedMultiplier : 1f);
        }

        if (IsMoving)
        {
            float speed = moveSpeed * (IsRunning ? runSpeedMultiplier : 1f);
            agent.Move(moveDir * speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * Time.deltaTime);
        }
    }
}
