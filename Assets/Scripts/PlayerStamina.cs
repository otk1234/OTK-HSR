using UnityEngine;

// 달리기 스태미나. 달리는 동안 줄고, 잠시 쉬면 다시 찹니다.
// 바닥나면 회복 기준치까지 찰 때까지 달릴 수 없습니다(숨이 찬 상태).
public class PlayerStamina : MonoBehaviour
{
    public float max = 100f;
    public float drainPerSecond = 22f;
    public float regenPerSecond = 16f;
    [Tooltip("달리기를 멈춘 뒤 회복이 시작되기까지 (초)")]
    public float regenDelay = 0.8f;
    [Tooltip("바닥난 뒤 이만큼 차야 다시 달릴 수 있음")]
    public float recoverThreshold = 30f;

    public float Current { get; private set; }
    public bool Exhausted { get; private set; }
    public bool CanRun => !Exhausted && Current > 0f;
    public float Normalized => max > 0f ? Current / max : 0f;

    private float lastDrainTime = float.NegativeInfinity;

    void Awake()
    {
        max *= CharacterRoster.StaminaMultiplierOf(gameObject); // 체력 차이 (프로필: 여동생 ×0.85, 오빠 ×1.25)
        Current = max;
    }

    public void Drain(float deltaTime)
    {
        Current = Mathf.Max(0f, Current - drainPerSecond * deltaTime);
        lastDrainTime = Time.time;
        if (Current <= 0f) Exhausted = true;
    }

    void Update()
    {
        if (Time.time - lastDrainTime < regenDelay) return;
        Current = Mathf.Min(max, Current + regenPerSecond * Time.deltaTime);
        if (Exhausted && Current >= recoverThreshold) Exhausted = false;
    }
}
