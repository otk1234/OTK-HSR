using System.Collections.Generic;

// 칠판에 올릴 데이터: 아버지의 살인을 사건별로 모아 줌 (화면은 UI 담당, 여기선 데이터만).
// 사건 파일(단서) + CaseState(고른 답·개입 방향·개입·다음 표적). 정답·오답과 "다음 사건 오류"는 넣지 않음 (§6.5, 플레이어에게 안 보임).
// 나중에 임무 고르기도 칠판에서 하기로 함 (사용자 2026-10-24) → Missions 자리만 있음, 스테이지 전환 때 채움.
public static class CaseBoard
{
    public struct ClueLine { public string clueId; public string title; public CharacterType foundBy; public string memory; }
    public struct AnswerLine { public string question; public string answer; public CharacterType answeredBy; }

    public class CaseSummary
    {
        public string caseId;
        public string title;
        public readonly List<ClueLine> clues = new List<ClueLine>();
        public readonly List<AnswerLine> answers = new List<AnswerLine>();
        public bool hasDirection;
        public InterventionKind direction;
        public string intervention;            // 한 개입 (없으면 null)
        public CharacterType intervenedBy;
        public string nextTarget;              // 종용으로 바뀐 아버지의 다음 표적 (없으면 null)
        public string memory;                  // 피해자 사진의 기억 글 (기획 문장 또는 자동 요약)
    }

    // 사건 요약 기억 (기획 문장이 없을 때): 우리가 내린 추리 + 우리가 한 일. 팀원 조사 창이 2~3줄이라 짧게
    static string AutoMemory(CaseSummary c)
    {
        var text = new System.Text.StringBuilder();
        text.Append(c.answers.Count == 0 ? "아직 아무것도 알아내지 못했다." : "우리는 이렇게 봤다: " + string.Join(" / ", c.answers.ConvertAll(a => a.answer.Split('.')[0])) + ".");
        if (c.intervention != null) text.Append($" 우리가 고른 길: {Intervention.KindName(c.direction)}.");
        return text.ToString();
    }

    // 칠판에서 고를 임무 (스테이지 전환 때 채움)
    public static readonly IReadOnlyList<string> Missions = new string[0];

    // knownCases: 아지트가 아는 사건 데이터 (제목·질문 문장, 개입 방향 조회용). 없는 사건도 기록이 있으면 ID로 표시
    public static List<CaseSummary> Build(IEnumerable<CaseDeduction> knownCases)
    {
        var known = new Dictionary<string, CaseDeduction>();
        var order = new List<string>();
        void Note(string id)
        {
            if (!string.IsNullOrEmpty(id) && !order.Contains(id)) order.Add(id);
        }
        if (knownCases != null)
            foreach (CaseDeduction c in knownCases)
                if (c != null) known[c.caseId] = c;

        foreach (string id in CaseFile.CaseIds) Note(id);
        foreach (CaseState.Answer a in CaseState.Answers) Note(a.caseId);
        foreach (CaseState.InterventionRecord r in CaseState.Interventions) Note(r.caseId);
        foreach (string id in known.Keys)
            if (CaseState.TryGetDirection(id, out _)) Note(id);

        var result = new List<CaseSummary>();
        foreach (string id in order)
        {
            known.TryGetValue(id, out CaseDeduction data);
            var summary = new CaseSummary { caseId = id, title = data != null ? data.caseTitle : id };
            foreach (CaseFile.Entry e in CaseFile.EntriesOf(id))
            {
                // 기억 글: 기획이 쓴 문장 > 찾을 때 본 설명
                string memory = data != null ? data.MemoryOf(e.clueId) : null;
                summary.clues.Add(new ClueLine { clueId = e.clueId, title = e.title, foundBy = e.foundBy, memory = memory ?? e.note ?? "" });
            }
            foreach (CaseState.Answer a in CaseState.Answers)
            {
                if (a.caseId != id) continue;
                CaseDeduction.Question q = data != null ? data.Find(a.questionId) : null;
                summary.answers.Add(new AnswerLine { question = q != null ? q.text : a.questionId, answer = a.text, answeredBy = a.answeredBy });
            }
            summary.hasDirection = CaseState.TryGetDirection(id, out summary.direction);
            foreach (CaseState.InterventionRecord r in CaseState.Interventions)
                if (r.caseId == id) { summary.intervention = r.text; summary.intervenedBy = r.doneBy; }
            summary.nextTarget = CaseState.NextTargetAfter(id);
            summary.memory = data != null && !string.IsNullOrEmpty(data.boardMemory) ? data.boardMemory : AutoMemory(summary);
            result.Add(summary);
        }
        return result;
    }
}
