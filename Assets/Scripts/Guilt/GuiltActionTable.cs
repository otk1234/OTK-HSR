using System;
using UnityEngine;

// 죄책감 행동 표 (기획이 고치는 데이터). Assets/Resources/GuiltActions.asset 하나를 GuiltSystem이 불러 씁니다.
//  - 행동: ID → 변화량(+ 부도덕 / − 정의), 설명, 챕터당 한 번만 반영할지
//  - 단계: 위에서부터 순서대로 '이 값 이하'면 그 단계 (마지막 단계의 max는 100)
public class GuiltActionTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string description;
        public int amount;
        public bool oncePerChapter = true;
    }

    [Serializable]
    public class Tier
    {
        public string name;
        public int max;
    }

    [Header("새 게임 시작값 (0~100, 50 = 중립)")]
    public int startValue = 50;

    [Header("단계 (낮은 쪽부터, '이 값 이하'면 그 단계)")]
    public Tier[] tiers;

    [Header("행동 목록 (임시값)")]
    public Entry[] actions;

    void Reset() => FillDefaults();

    public void FillDefaults()
    {
        startValue = 50;
        tiers = new[]
        {
            new Tier { name = "매우 낮음", max = 14 },
            new Tier { name = "낮음", max = 29 },
            new Tier { name = "약간 낮음", max = 44 },
            new Tier { name = "중립", max = 55 },
            new Tier { name = "약간 높음", max = 70 },
            new Tier { name = "높음", max = 85 },
            new Tier { name = "매우 높음", max = 100 },
        };
        actions = new[]
        {
            new Entry { id = "steal", description = "남의 물건 훔치기", amount = 8 },
            new Entry { id = "tamper_evidence", description = "증거 훼손·숨기기", amount = 10 },
            new Entry { id = "abandon_partner", description = "동료를 두고 혼자 숨기", amount = 12 },
            new Entry { id = "deceive_npc", description = "NPC 속이기·거짓말", amount = 5 },
            new Entry { id = "break_object", description = "물건 부수기", amount = 4 },
            new Entry { id = "preserve_evidence", description = "증거 보존·제출", amount = -8 },
            new Entry { id = "rescue_partner", description = "동료 구하기", amount = -12 },
            new Entry { id = "help_npc", description = "NPC 돕기", amount = -5 },
            new Entry { id = "return_stolen", description = "훔친 물건 돌려놓기", amount = -6 },
            new Entry { id = "confess", description = "진실을 말하기 (자백)", amount = -10 },
            // 범행 현장 개입 (챕터가 많으므로 변동폭을 작게)
            new Entry { id = "incite_crime", description = "범죄를 종용하기", amount = 5 },
            new Entry { id = "abet_crime", description = "범죄에 가담하기", amount = 8 },
            new Entry { id = "prevent_crime", description = "이어질 범죄를 막기", amount = -5 },
            // 오빠의 강제로 열기 (조사를 위해 따는 것일 수도 있으므로 작게, 사용자 결정 2026-10-23)
            new Entry { id = "force_entry", description = "잠긴 문을 강제로 열기", amount = 2 },
        };
    }

    // 기본 목록에는 있는데 에셋에 없는 행동만 추가 (기획이 고친 값은 그대로 둠). 추가한 개수
    public int AddMissingDefaults()
    {
        var defaults = CreateInstance<GuiltActionTable>();
        defaults.FillDefaults();
        var list = new System.Collections.Generic.List<Entry>(actions ?? new Entry[0]);
        int added = 0;
        foreach (Entry entry in defaults.actions)
            if (Find(entry.id) == null) { list.Add(entry); added++; }
        actions = list.ToArray();
        DestroyImmediate(defaults);
        return added;
    }

    public Entry Find(string id)
    {
        if (actions == null) return null;
        foreach (Entry entry in actions)
            if (entry.id == id) return entry;
        return null;
    }

    public int TierIndexOf(int value)
    {
        if (tiers == null || tiers.Length == 0) return 0;
        for (int i = 0; i < tiers.Length; i++)
            if (value <= tiers[i].max) return i;
        return tiers.Length - 1;
    }
}
