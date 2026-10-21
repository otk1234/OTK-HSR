using System.Collections.Generic;
using UnityEngine;

// 추격 대상 규칙
//  - 두 캐릭터가 같은 방(공간)에 있으면 둘 다 쫓는다.
//  - 따로 있으면 한 명만: 발견한 캐릭터, 이벤트로 시작했으면 추격자와 같은 건물에 있는 캐릭터.
public static class ChaseTargeting
{
    // 추격자가 spotted를 발견했을 때
    public static List<CharacterStatus> FromSighting(CharacterStatus spotted)
    {
        var targets = new List<CharacterStatus> { spotted };
        foreach (CharacterStatus other in CharacterStatus.All)
            if (other != spotted && other.CanBeSeen && CameraTrigger.SameRoom(other.transform.position, spotted.transform.position))
                targets.Add(other);
        return targets;
    }

    // 발견 없이(이벤트 등) 추격을 시작할 때
    public static List<CharacterStatus> ForChaser(Vector3 chaserPosition)
    {
        CharacterStatus first = null;
        float nearest = float.MaxValue;
        foreach (CharacterStatus character in CharacterStatus.All)
        {
            if (character.IsCaught || !CameraTrigger.SameBuilding(character.transform.position, chaserPosition)) continue;
            float distance = (character.transform.position - chaserPosition).sqrMagnitude;
            if (distance < nearest) { nearest = distance; first = character; }
        }
        return first == null ? new List<CharacterStatus>() : FromSighting(first);
    }
}
