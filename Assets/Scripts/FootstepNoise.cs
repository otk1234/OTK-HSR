using UnityEngine;

// 걷기·달리기 발소리를 소음 시스템에 보냅니다. 달리면 더 멀리 들립니다.
[RequireComponent(typeof(PlayerMovement))]
public class FootstepNoise : MonoBehaviour
{
    public float walkRadius = 2.5f;
    public float walkInterval = 0.5f;
    public float runRadius = 7f;
    public float runInterval = 0.32f;

    private PlayerMovement movement;
    private float nextStep;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        if (!movement.IsMoving)
        {
            nextStep = 0f; // 멈췄다 다시 걸으면 첫 발부터 소리가 남
            return;
        }
        if (Time.time < nextStep) return;

        bool running = movement.IsRunning;
        float radius = (running ? runRadius : walkRadius) * CharacterRoster.FootstepMultiplierOf(gameObject); // 가벼운 몸이면 작게 (프로필)
        NoiseSystem.Emit(transform.position, radius, running ? NoiseKind.Run : NoiseKind.Footstep, gameObject);
        nextStep = Time.time + (running ? runInterval : walkInterval);
    }
}
