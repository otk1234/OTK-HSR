using System;
using UnityEngine;

// 캐릭터 특성 (여러 개를 함께 가질 수 있음). 상호작용·단서·추리 근거·대사의 "누가 할 수 있나" 조건에 씁니다.
// 여동생 = 통찰력·의사소통으로 정보를 따내는 쪽 / 오빠 = 몸으로 길을 여는 쪽 (사용자 결정 2026-10-23)
[Flags]
public enum CharacterTrait
{
    None = 0,
    Investigation = 1, // 관찰(정보 수집): 남이 못 보는 숨겨진 단서를 찾음
    Strength = 2,      // 힘: 무거운 물건 옮기기·밀기, 잠긴 문 강제로 열기
    Mechanics = 4,     // 기계 조작: 기계 수리·조작
    Insight = 8,       // 통찰: 같은 단서에서 추가 근거를 얻음 (TraitInsight)
    Persuasion = 16,   // 화술: NPC에게서 추가 대사·정보를 얻음 ([화술] 대사)
}

// 캐릭터 한 명의 설정 (기획이 고치는 데이터). 이름·나이가 정해지면 에셋만 고치면 됩니다.
// 번호(CharacterType)는 코드가 캐릭터를 구분하는 값이라 그대로 두고, 화면에 보이는 이름은 여기서 가져옵니다.
[CreateAssetMenu(menuName = "OTK/캐릭터 프로필", fileName = "CharacterProfile")]
public class CharacterProfile : ScriptableObject
{
    [Header("번호 (0번 = Player1, 1번 = Player2)")]
    public CharacterType type = CharacterType.Player1;

    [Header("표시 이름 · 나이 (임시)")]
    public string displayName = "캐릭터";
    public int age = 20;

    [Header("소지품 최대 개수")]
    public int inventoryCapacity = 5;

    [Header("특성")]
    public CharacterTrait traits = CharacterTrait.None;

    [Header("몸 (1 = 기본)")]
    [Tooltip("발소리가 들리는 반경 배율 (가벼운 몸이면 1보다 작게)")]
    public float footstepMultiplier = 1f;
    [Tooltip("스태미나 최대치 배율 (체력이 좋으면 1보다 크게)")]
    public float staminaMultiplier = 1f;

    public bool Has(CharacterTrait trait) => (traits & trait) == trait;
}
