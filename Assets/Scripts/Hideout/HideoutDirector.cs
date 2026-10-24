using UnityEngine;

// 아지트 진행: 개발용 안내 상자(게임 UI 아님) + 예시 데이터 키.
// 아직 범행 현장 → 아지트로 넘어오지 않으므로(스테이지 전환은 나중에), 아지트만 열면 칠판·상자가 비어 있음
// → F5로 "302호를 마치고 돌아온" 예시 기록을 채워 칠판·상자·작업대를 확인할 수 있게 함.
public class HideoutDirector : MonoBehaviour
{
    [Header("예시 데이터에 쓸 사건 (302호)")]
    public CaseDeduction sampleCase;

    [Header("예시 데이터 키 (개발용)")]
    public KeyCode sampleKey = KeyCode.F5;

    [Header("임무 장소에서 아지트로 돌아오는 키 (개발용, 임무 끝 처리는 나중에)")]
    public KeyCode returnKey = KeyCode.F6;
    public MissionTable missionTable;

    private GUIStyle style;

    void Update()
    {
        if (Input.GetKeyDown(sampleKey) && HideoutSampleData.Fill(sampleCase))
            GameMessage.Show("[개발용] 302호를 마치고 돌아온 예시 기록을 채웠다.");
        if (Input.GetKeyDown(returnKey) && missionTable != null) missionTable.ReturnHome();
    }

    void OnGUI()
    {
        GameObject active = CharacterManager.instance != null ? CharacterManager.instance.GetActivePlayer() : null;
        if (active != null && active.GetComponent<NarrativeGraybox.Interaction.PlayerInteractor3D>().IsInspecting) return;   // 확대 중에는 가리지 않게
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { fontSize = 13, richText = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            style.normal.textColor = Color.white;
        }
        string sample = HideoutSampleData.Filled ? "예시 기록 있음" : $"[{sampleKey}] 예시 기록 채우기";
        if (GameSession.Deployed) sample = $"임무 중 — [{returnKey}] 아지트로 귀환";
        GUI.Box(new Rect(Screen.width - 268, 150, 256, 74),
            $"<b>[개발용] 아지트 (다락방)</b>\n칠판·상자·작업대·지도: 가까이 가서 E\n<color=#bbbbbb>{sample}</color>", style);
    }
}

// 예시 기록: 302호에서 단서 5개 → 추리 3개(정답) → 종용(다음 표적 = 옆집 사람) + 상자에 물건 4개
public static class HideoutSampleData
{
    public const string CaseId = "case302";

    public static bool Filled { get; private set; }

    public static bool Fill(CaseDeduction data)
    {
        if (Filled || data == null) return false;
        Filled = true;

        var sister = CharacterType.Player1;
        var brother = CharacterType.Player2;
        // 기억 글 = 범행 현장에서 찾을 때 본 설명 (CrimeSceneTestSceneBuilder 문장을 지난 일로 고쳐 씀)
        CaseFile.Add(data.caseId, "body", "시체", sister, "집주인 한정우(임시). 거실 바닥에 엎드려 쓰러져 있었다. 몸이 아직 조금 따뜻했다.");
        CaseFile.Add(data.caseId, "navy_fiber", "손톱 밑 섬유", sister, "손톱 밑에 남색 털실이 끼어 있었다. ...아빠의 남색 외투와 같은 색이었다.");
        CaseFile.Add(data.caseId, "two_cups", "찻잔 두 개", brother, "찻잔 두 개. 하나는 반쯤 남아 있었다.");
        CaseFile.Add(data.caseId, "victim_diary", "피해자 수첩", brother, "책장과 벽 사이에 떨어진 수첩. 마지막 장: '목요일 밤 9시, 박 선생 댁. 그 일을 털어놓을 것.'");
        CaseFile.Add(data.caseId, "intercom_video", "녹화 영상", brother, "오늘 밤 8시 40분. 모자를 눌러쓴 남색 외투의 남자가 들어온다.");

        foreach (CaseDeduction.Question q in data.questions)
        {
            CaseDeduction.Option chosen = System.Array.Find(q.options, o => o.correct);
            if (chosen != null) CaseState.Record(data.caseId, q, chosen, sister);
        }
        CaseState.SetDirection(data.caseId, InterventionKind.Incite);
        CaseState.RecordIntervention(data.caseId, InterventionKind.Incite,
            "수첩의 마지막 장을 찢고 '옆집 사람이 그날 밤 다 봤다'는 메모를 남겼다", sister, "옆집 사람");
        GuiltSystem.Record("incite_crime", sister);

        GameSession.AddToStash("황동 열쇠", default);
        GameSession.AddToStash("공구 상자", new Inventory.ItemRule { requiredTrait = CharacterTrait.Strength });
        GameSession.AddToStash("암호 메모", new Inventory.ItemRule { requiredTrait = CharacterTrait.Investigation });
        GameSession.AddToStash("손전등", default);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Filled = false;
}
