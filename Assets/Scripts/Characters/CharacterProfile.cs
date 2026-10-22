using System;
using UnityEngine;

// 캐릭터 특성 (여러 개를 함께 가질 수 있음). 상호작용·단서·추리 선택지의 "누가 할 수 있나" 조건에 씁니다.
[Flags]
public enum CharacterTrait
{
    None = 0,
    Investigation = 1, // 정보 수집: 숨겨진 단서, 정보가 필요한 추리 선택지
    Strength = 2,      // 힘: 무거운 물건 옮기기·밀기
    Mechanics = 4,     // 기계 조작: 기계 수리·조작
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

    public bool Has(CharacterTrait trait) => (traits & trait) == trait;
}
