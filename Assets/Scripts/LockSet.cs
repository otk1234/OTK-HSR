using System.Collections.Generic;

// 이동·입력을 막는 이유.
// 여러 시스템(조사, 대화, 숨기, 잡힘 …)이 동시에 잠가도 서로 덮어쓰지 않도록 이유별로 따로 등록합니다.
public enum LockReason
{
    Legacy,           // 예전 방식(bool 하나)으로 잠근 경우
    Inspection,       // 조사·NPC 대화
    InventoryMenu,    // 소지품 선택
    CameraTransition, // 조사 카메라 복귀 등 화면 전환
    Hiding,           // 숨는 곳 안
    Caught,           // 추격자에게 잡힘
    Scripted,         // 연출·이벤트
    Working,          // 무거운 물건 밀기·기계 수리 같은 동작 중
}

public class LockSet
{
    private readonly HashSet<LockReason> reasons = new HashSet<LockReason>();

    public bool IsLocked => reasons.Count > 0;
    public bool Has(LockReason reason) => reasons.Contains(reason);

    public void Set(LockReason reason, bool locked)
    {
        if (locked) reasons.Add(reason);
        else reasons.Remove(reason);
    }

    public void Clear() => reasons.Clear();

    public override string ToString() => reasons.Count == 0 ? "없음" : string.Join(", ", reasons);
}
