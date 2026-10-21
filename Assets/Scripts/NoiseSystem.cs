using System;
using System.Collections.Generic;
using UnityEngine;

public enum NoiseKind { Footstep, Run, Door, DoorLock, DoorBash, Hiding, Window, Vent, Other }

public struct NoiseEvent
{
    public Vector3 position;
    public float radius;      // 이 거리 안이면 들림 (벽 너머는 듣는 쪽에서 줄여서 판단)
    public NoiseKind kind;
    public GameObject source; // 소리를 낸 대상 (플레이어 캐릭터, 추격자 등)
    public float time;
}

// 발소리·문·숨는 곳 출입 등 모든 소리를 한 곳으로 모읍니다. 추격자의 청각(ChaserSenses)이 구독합니다.
public static class NoiseSystem
{
    public static event Action<NoiseEvent> Emitted;

    // 씬 뷰 디버그 표시용 최근 소음
    public static readonly List<NoiseEvent> Recent = new List<NoiseEvent>();
    const float RecentSeconds = 1.5f;

    public static void Emit(Vector3 position, float radius, NoiseKind kind, GameObject source)
    {
        if (radius <= 0f) return;
        var noise = new NoiseEvent { position = position, radius = radius, kind = kind, source = source, time = Time.time };
        Recent.RemoveAll(n => Time.time - n.time > RecentSeconds);
        Recent.Add(noise);
        Emitted?.Invoke(noise);
    }

    // 도메인 리로드가 꺼져 있어서(Enter Play Mode Options) 플레이할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Emitted = null;
        Recent.Clear();
    }
}
