using System;
using System.Collections.Generic;
using UnityEngine;

public enum CharacterState { Normal, Running, Hidden, Caught }

// 플레이어 캐릭터의 현재 상태(보통/달리는 중/숨음/잡힘)와 위치를 다른 시스템에 알려 줍니다.
// 조작하지 않는 캐릭터도 똑같이 조회할 수 있습니다(추격자가 노릴 수 있도록).
public class CharacterStatus : MonoBehaviour
{
    public static readonly List<CharacterStatus> All = new List<CharacterStatus>();

    public event Action<CharacterStatus> Changed;

    private PlayerMovement movement;
    private PlayerInteraction interaction;
    private CharacterState lastState;

    public HidingSpot HidingIn { get; private set; }
    public bool IsHidden => HidingIn != null;
    public bool IsCaught { get; private set; }
    public bool IsRunning => movement != null && movement.IsRunning;
    public bool IsActive => CharacterManager.instance != null && CharacterManager.instance.GetActivePlayer() == gameObject;

    // 추격자 눈에 보일 수 있는 상태인가 (숨었거나 이미 잡혔으면 아님)
    public bool CanBeSeen => !IsHidden && !IsCaught;

    public CharacterState State =>
        IsCaught ? CharacterState.Caught :
        IsHidden ? CharacterState.Hidden :
        IsRunning ? CharacterState.Running : CharacterState.Normal;

    // 지금 있는 구역 (건물·층·방 이름은 CameraTrigger에)
    public CameraTrigger CurrentRoom => CameraTrigger.FindAt(transform.position);

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        interaction = GetComponent<PlayerInteraction>();
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        CharacterState state = State;
        if (state != lastState)
        {
            lastState = state;
            Changed?.Invoke(this);
        }
    }

    // HidingSpot만 부릅니다.
    public void SetHidden(HidingSpot spot)
    {
        HidingIn = spot;
        Changed?.Invoke(this);
    }

    // 추격자에게 잡힘: 이동·상호작용을 막습니다. (연출은 나중에)
    public void SetCaught()
    {
        if (IsCaught) return;
        IsCaught = true;
        if (movement != null) movement.Locks.Set(LockReason.Caught, true);
        if (interaction != null) interaction.Locks.Set(LockReason.Caught, true);
        Changed?.Invoke(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
