using System;
using System.Collections.Generic;
using UnityEngine;

// 다음 사건으로 넘길 결과 (추리 답, 오답에 따른 다음 사건 오류, 개입 방향·개입). 다음 사건이 아직 없으므로 지금은 기록과 로그만.
// 씬이 바뀌거나 재시작돼도 남도록 static, 플레이를 새로 시작하면 비움.
public static class CaseState
{
    public struct Answer
    {
        public string caseId;
        public string questionId;
        public string text;
        public bool correct;
        public CharacterType answeredBy;
    }

    public static event Action<Answer> Answered;

    static readonly List<Answer> answers = new List<Answer>();
    static readonly List<string> nextCaseErrors = new List<string>();

    public static IReadOnlyList<Answer> Answers => answers;
    public static IReadOnlyList<string> NextCaseErrors => nextCaseErrors;

    public static bool HasAnswered(string caseId, string questionId) => TryGet(caseId, questionId, out _);

    public static bool TryGet(string caseId, string questionId, out Answer answer)
    {
        foreach (Answer a in answers)
            if (a.caseId == caseId && a.questionId == questionId) { answer = a; return true; }
        answer = default;
        return false;
    }

    // 한 번 답하면 바꿀 수 없음. 기록됐으면 true
    public static bool Record(string caseId, CaseDeduction.Question question, CaseDeduction.Option option, CharacterType by)
    {
        if (question == null || option == null || HasAnswered(caseId, question.id)) return false;
        var answer = new Answer { caseId = caseId, questionId = question.id, text = option.text, correct = option.correct, answeredBy = by };
        answers.Add(answer);
        Debug.Log($"[추리] {question.text} → {option.text} ({(option.correct ? "정답" : "오답")}, {by})");
        if (!option.correct)
        {
            nextCaseErrors.Add(question.wrongConsequence);
            Debug.Log($"[다음 사건] 큰 오류 예약: {question.wrongConsequence}");
        }
        Answered?.Invoke(answer);
        return true;
    }

    // ---------------- 개입 (사건마다 방향 하나, 개입 한 번) ----------------

    public struct InterventionRecord
    {
        public string caseId;
        public InterventionKind kind;
        public string text;
        public CharacterType doneBy;
        public string nextTarget; // 종용으로 바꾼 아버지의 다음 표적 (없으면 빈칸)
    }

    static readonly Dictionary<string, InterventionKind> directions = new Dictionary<string, InterventionKind>();
    static readonly List<InterventionRecord> interventions = new List<InterventionRecord>();
    public static IReadOnlyList<InterventionRecord> Interventions => interventions;

    public static bool TryGetDirection(string caseId, out InterventionKind kind) => directions.TryGetValue(caseId, out kind);

    // 한 번 정하면 바꿀 수 없음
    public static bool SetDirection(string caseId, InterventionKind kind)
    {
        if (directions.ContainsKey(caseId)) return false;
        directions[caseId] = kind;
        Debug.Log($"[개입] 이번 사건의 방향: {Intervention.KindName(kind)}");
        return true;
    }

    public static bool HasIntervened(string caseId)
    {
        foreach (InterventionRecord r in interventions)
            if (r.caseId == caseId) return true;
        return false;
    }

    public static bool RecordIntervention(string caseId, InterventionKind kind, string text, CharacterType by, string nextTarget = null)
    {
        if (HasIntervened(caseId)) return false;
        interventions.Add(new InterventionRecord { caseId = caseId, kind = kind, text = text, doneBy = by, nextTarget = nextTarget ?? "" });
        Debug.Log($"[개입] {Intervention.KindName(kind)}: {text} ({by}) → 다음 사건에 반영 예정");
        if (!string.IsNullOrEmpty(nextTarget)) Debug.Log($"[다음 사건] 아버지의 표적 변경: {nextTarget}");
        return true;
    }

    // 이 사건의 종용으로 바뀐 다음 표적 (없으면 null → 원래 표적 그대로)
    public static string NextTargetAfter(string caseId)
    {
        foreach (InterventionRecord r in interventions)
            if (r.caseId == caseId && !string.IsNullOrEmpty(r.nextTarget)) return r.nextTarget;
        return null;
    }

    public static void Clear()
    {
        answers.Clear();
        nextCaseErrors.Clear();
        directions.Clear();
        interventions.Clear();
    }

    // 도메인 리로드가 꺼져 있으므로(Enter Play Mode Options) 플레이를 시작할 때마다 직접 비웁니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Clear();
        Answered = null;
    }
}
