using System;
using UnityEngine;

// 사건 하나의 추리 문제 (기획이 고치는 데이터). 질문마다 선택지를 고르고, 한 번 답하면 바꿀 수 없음.
//  - 선택지는 필요한 단서(needAnyClue 중 하나라도)를 사건 파일에 모아야 고를 수 있음. 비워 두면 언제나 고를 수 있음
//    → 오답에도 그럴듯한 근거를 걸어 "잠김 = 정답" 힌트가 되지 않게 하고, 질문마다 언제나 고를 수 있는 "잘 모르겠다"(오답)를 둠
//      (시간 안에 단서를 못 모으면 실패로 처리, 사용자 결정 2026-10-23)
//  - 근거는 3종류: 누구나 찾는 공통 근거 / 여동생 근거(관찰·통찰) / 오빠 근거(힘·기계로 길을 열어야 닿음).
//    정답에는 남매 각자의 능력이 필요하게, 공통 근거만으로는 그럴듯한 오답까지만 가게 배치
//  - 남매가 같이 조사하므로 선택지는 누가 추리하든 똑같이 보임. 누가 찾았든 근거가 사건 파일에 있으면 고를 수 있음 (사용자 결정 2026-10-23)
//    → 같은 결론에 서로 다른 근거로 도달하는 선택지(섬유 / 녹화 영상)는 그대로 둘 다 보임
//  - 틀려도 진행은 막지 않음. 정답·오답은 플레이어에게 보이지 않고 CaseState에만 남아 다음 사건에 영향 (오답이면 큰 패널티)
[CreateAssetMenu(menuName = "OTK/사건 추리", fileName = "CaseDeduction")]
public class CaseDeduction : ScriptableObject
{
    [Serializable]
    public class Option
    {
        [TextArea] public string text;
        public bool correct;
        public string[] needAnyClue = new string[0];

        public bool Unlocked
        {
            get
            {
                if (needAnyClue == null || needAnyClue.Length == 0) return true;
                foreach (string clue in needAnyClue)
                    if (CaseFile.Has(clue)) return true;
                return false;
            }
        }
    }

    [Serializable]
    public class Question
    {
        public string id;
        [TextArea] public string text;
        public Option[] options;
        [Header("틀렸을 때 다음 사건에 남는 오류 (임시 문구)")]
        [TextArea] public string wrongConsequence;
    }

    public const string UnknownOptionText = "잘 모르겠다. (단서가 부족하다)";

    public string caseId = "case302";
    public string caseTitle = "302호 살인 사건 (임시)";
    public Question[] questions;

    public Question Find(string questionId)
    {
        if (questions == null) return null;
        foreach (Question q in questions)
            if (q.id == questionId) return q;
        return null;
    }

    // 임시 사건 (범행 현장 테스트 씬)
    public void FillCase302()
    {
        caseId = "case302";
        caseTitle = "302호 살인 사건 (임시)";
        questions = new[]
        {
            new Question
            {
                id = "relation", text = "피해자와 범인은 어떤 사이였을까?",
                wrongConsequence = "범인을 낯선 사람으로 보고 엉뚱한 방향을 쫓는다.",
                options = new[]
                {
                    new Option { text = "모르는 사람. 강도가 들었다.", needAnyClue = new[] { "blood_trail" } },
                    new Option { text = "아는 사이. 차를 대접했고, 등을 보일 만큼 믿었다.", correct = true, needAnyClue = new[] { "insight_acquaintance" } }, // 여동생 통찰 (찻잔·상처)
                    new Option { text = "사고였다. 넘어지며 재떨이에 부딪쳤다.", needAnyClue = new[] { "weapon_ashtray" } },
                    new Option { text = UnknownOptionText },
                },
            },
            new Question
            {
                id = "culprit", text = "범인은 누구인가?",
                wrongConsequence = "엉뚱한 사람을 의심하는 사이, 아버지는 자유롭게 움직인다.",
                options = new[]
                {
                    new Option { text = "아파트 경비원.", needAnyClue = new[] { "bloody_towel" } },
                    new Option { text = "아빠다. 손톱 밑 섬유가 아빠의 남색 외투와 같다.", correct = true, needAnyClue = new[] { "navy_fiber" } },
                    new Option { text = "아빠다. 녹화 영상 속 걸음걸이가 아빠다.", correct = true, needAnyClue = new[] { "insight_gait" } }, // 오빠가 녹화기를 고치고 → 여동생이 통찰
                    new Option { text = "피해자의 다른 지인.", needAnyClue = new[] { "two_cups" } },
                    new Option { text = UnknownOptionText },
                },
            },
            new Question
            {
                id = "next_target", text = "아버지의 다음 범행은?",
                wrongConsequence = "다음 피해자를 잘못 짚어, 다음 사건에서 손쓸 수 없는 상태로 시작한다.",
                options = new[]
                {
                    new Option { text = "목요일 밤 9시, 박 선생 댁.", correct = true, needAnyClue = new[] { "victim_diary" } },
                    new Option { text = "옆집 사람. 범행을 봤을지도 모른다.", needAnyClue = new[] { "intercom_video" } },
                    new Option { text = "더는 없다. 이번이 마지막이다.", needAnyClue = new[] { "bloody_towel" } },
                    new Option { text = UnknownOptionText },
                },
            },
        };
    }
}
