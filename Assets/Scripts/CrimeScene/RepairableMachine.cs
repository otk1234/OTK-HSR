using UnityEngine;

// 고칠 수 있는 기계 장치 (인터폰 녹화기·두꺼비집 …): 조건에 맞는 캐릭터(보통 기계 조작 특성 = 오빠)가 E로 고칩니다.
// 고치면 표시등 색이 바뀌고, 고장 나서 볼 수 없던 오브젝트(조사 지점 등)를 켭니다.
public class RepairableMachine : CharacterInteractable
{
    [Header("고치는 데 걸리는 시간 (초)")]
    public float repairTime = 2.5f;

    [Header("표시등 (고장: 빨강 → 고침: 초록)")]
    public Renderer indicator;
    public Material repairedMaterial;

    [Header("고치면 켜지는 오브젝트")]
    public GameObject[] revealWhenRepaired;

    [Header("고친 뒤 메시지")]
    [TextArea] public string repairedMessage = "다시 작동한다.";

    public bool Repaired { get; private set; }

    public override bool Available => !Repaired;
    protected override string ActionLabel => "고치기";
    protected override float WorkTime => repairTime;

    protected override void OnWorkDone(PlayerInteraction user)
    {
        Repaired = true;
        if (indicator != null && repairedMaterial != null) indicator.sharedMaterial = repairedMaterial;
        Reveal(revealWhenRepaired);
        Debug.Log($"[범행 현장] {displayName} 수리 완료 ({user.myType})");
        GameMessage.Show(repairedMessage);
    }
}
