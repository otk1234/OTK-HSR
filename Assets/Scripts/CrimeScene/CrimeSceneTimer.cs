using System;
using UnityEngine;

// 범행 현장 제한 시간. 끝나면 TimeUp 이벤트만 보냅니다. (추격자가 들이닥치는 건 아직 구현하지 않음)
// 추격 중에는 멈춤. 조사 화면에서 멈출지는 체크박스로.
public class CrimeSceneTimer : MonoBehaviour
{
    [Header("제한 시간 (초)")]
    public float timeLimit = 180f;

    [Header("조사 화면을 보는 동안 시간을 멈출지")]
    public bool pauseWhileInspecting = false;

    public event Action TimeUp;

    public float Remaining { get; private set; }
    public bool Expired { get; private set; }

    void Start() => Remaining = timeLimit;

    void Update()
    {
        if (Expired || GameStateManager.IsChase) return;
        if (pauseWhileInspecting && CrimeSceneDirector.ActiveInspector() is { IsInspecting: true }) return;

        Remaining -= Time.deltaTime;
        if (Remaining <= 0f) Finish();
    }

    // 점검·연출용
    public void SetRemaining(float seconds)
    {
        Remaining = seconds;
        if (Remaining <= 0f && !Expired) Finish();
    }

    void Finish()
    {
        Remaining = 0f;
        Expired = true;
        Debug.Log("[범행 현장] 제한 시간 종료 (추격자 도착은 아직 구현하지 않음)");
        TimeUp?.Invoke();
    }
}
