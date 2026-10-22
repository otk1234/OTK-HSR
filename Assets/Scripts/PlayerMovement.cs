using UnityEngine;
using UnityEngine.AI;
using NarrativeGraybox.Player;

public class PlayerMovement : MonoBehaviour
{
    [Header("이동 속도")]
    public float moveSpeed = 5f;

    [Header("카메라 방향 기준으로 이동 (쿼터뷰 맵용)")]
    public bool cameraRelative = false;

    private NavMeshAgent agent;
    private SpriteCharacterVisual spriteVisual; // 그레이박스의 4방향 스프라이트 (없으면 무시)
    private bool movementEnabled = true;

    // 키를 누르고 있는 동안 쓰는 방향 기준 (카메라 시점이 바뀌어도 손을 뗄 때까지 유지)
    private Vector3 inputForward = Vector3.forward;
    private Vector3 inputRight = Vector3.right;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        spriteVisual = GetComponentInChildren<SpriteCharacterVisual>();
    }

    // 조사·대화·소지품 선택 중에는 외부에서 이동을 잠급니다.
    public void SetMovementEnabled(bool enabled)
    {
        movementEnabled = enabled;
    }

    void OnDisable()
    {
        // 조작이 다른 캐릭터로 넘어가면 걷는 모션을 멈춥니다.
        if (spriteVisual != null) spriteVisual.SetMovementInput(Vector2.zero);
    }

    void Update()
    {

        if ((FadeManager.instance != null && FadeManager.instance.isFading) || !movementEnabled)
        {
            if (spriteVisual != null) spriteVisual.SetMovementInput(Vector2.zero);
            return;
        }


        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (spriteVisual != null) spriteVisual.SetMovementInput(Vector2.ClampMagnitude(new Vector2(h, v), 1f));

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

        if (moveDir.magnitude >= 0.1f)
        {
            agent.Move(moveDir * moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * Time.deltaTime);
        }
    }
}
