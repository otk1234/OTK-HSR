using System.Collections.Generic;
using System.Text;
using NarrativeGraybox.Interaction;
using UnityEngine;

// 아지트 칠판: 아버지의 살인 추적. E로 확대하면 오른쪽 위에 사건 요약(개발용 창), 칠판에는 사진·메모가 붙음.
// 사진 = 팀원 조사 항목(InspectionItem): 마우스를 올리면 외곽선·제목, 클릭하면 아래 조사 창에 기억 글 (사용자 결정 2026-10-24).
//   피해자 사진 1장(사건 요약 기억) + 찾은 단서마다 사진 1장 + 다음 표적 메모(종용으로 바뀌었으면, 붉은 실로 이음).
// 칠판에는 가장 최근 사건만 붙임 (요약 창은 ←/→로 사건을 넘김). 나중에 임무 고르기도 여기서.
public class Chalkboard : HideoutStation
{
    [Header("아지트가 아는 사건 데이터 (제목·질문 문장·기억 글)")]
    public CaseDeduction[] knownCases = new CaseDeduction[0];

    [Header("사진을 붙일 칠판 면 (가운데, 칠판 앞쪽 = -forward)")]
    public Transform photoSurface;
    public Vector2 surfaceSize = new Vector2(2.3f, 1.3f);

    [Header("사진 재질")]
    public Material photoMaterial, victimMaterial, noteMaterial, outlineMaterial, stringMaterial;

    protected override string Title => "칠판 — 아버지의 살인 추적";
    protected override string Controls => Cases.Count > 1 ? "←/→ 사건 넘기기   사진 클릭: 기억" : "사진 클릭: 기억";

    private int page;
    private List<CaseBoard.CaseSummary> cases;
    private string pinnedSignature;
    private Transform photoRoot;

    List<CaseBoard.CaseSummary> Cases => cases ??= CaseBoard.Build(knownCases);

    public List<CaseBoard.CaseSummary> Summaries() => CaseBoard.Build(knownCases);
    public InspectionItem[] Photos => Area.Items;

    void Start() => RefreshPhotos();

    protected override void Update()
    {
        base.Update();
        if (!IsOpen) RefreshPhotos();   // 확대 중에는 사진을 바꾸지 않음 (팀원 조사 화면이 항목을 들고 있음)
    }

    protected override void OnOpened()
    {
        cases = CaseBoard.Build(knownCases);
        page = Mathf.Max(0, cases.Count - 1);   // 가장 최근 사건부터
    }

    protected override void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow)) page--;
        if (Input.GetKeyDown(KeyCode.RightArrow)) page++;
        page = Mathf.Clamp(page, 0, Mathf.Max(0, Cases.Count - 1));
    }

    protected override string BuildText() => Describe(Cases, page);

    // ---------------- 사진 ----------------

    // 기록이 바뀌었을 때만 다시 붙임
    public void RefreshPhotos()
    {
        List<CaseBoard.CaseSummary> all = CaseBoard.Build(knownCases);
        CaseBoard.CaseSummary latest = all.Count > 0 ? all[all.Count - 1] : null;
        string signature = latest == null ? "" : $"{latest.caseId}|{latest.clues.Count}|{latest.answers.Count}|{latest.intervention}|{latest.nextTarget}";
        if (signature == pinnedSignature) return;
        pinnedSignature = signature;

        if (photoRoot != null) Remove(photoRoot.gameObject);
        var items = new List<InspectionItem>();
        if (latest != null && photoSurface != null)
        {
            photoRoot = new GameObject("사진들").transform;
            photoRoot.SetParent(photoSurface, false);

            // 왼쪽 위: 피해자 사진 (크게)
            InspectionItem victim = Pin($"피해자 사진 — {latest.title}", latest.memory, new Vector2(-0.88f, 0.32f), new Vector2(0.42f, 0.52f), victimMaterial);
            items.Add(victim);

            // 가운데: 단서 사진 (4열)
            for (int i = 0; i < latest.clues.Count; i++)
            {
                CaseBoard.ClueLine clue = latest.clues[i];
                string memory = (string.IsNullOrEmpty(clue.memory) ? "기억이 흐릿하다." : clue.memory) + $"  ({CharacterRoster.NameOf(clue.foundBy)}가 찾았다)";
                var at = new Vector2(-0.38f + (i % 4) * 0.36f, 0.38f - (i / 4) * 0.36f);
                items.Add(Pin(clue.title, memory, at, new Vector2(0.28f, 0.22f), photoMaterial));
            }

            // 피해자 사진 아래: 다음 표적 메모 + 피해자 사진에서 붉은 실 (단서 사진을 가로지르지 않게)
            if (latest.nextTarget != null)
            {
                var at = new Vector2(-0.88f, -0.4f);
                items.Add(Pin($"다음 표적: {latest.nextTarget}", $"우리가 바꿔 놓은 아버지의 다음 표적. 원래 표적 대신 {latest.nextTarget}에게 간다.", at, new Vector2(0.34f, 0.24f), noteMaterial));
                RedString(new Vector2(-0.88f, 0.32f), at);
            }
        }
        Area.Items = items.ToArray();
    }

    // 칠판 면 위의 한 장 (로컬 x·y = 칠판 면, 앞쪽으로 조금 띄움)
    InspectionItem Pin(string title, string memory, Vector2 at, Vector2 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = title;
        go.transform.SetParent(photoRoot, false);
        go.transform.localPosition = new Vector3(at.x, at.y, -0.03f);
        go.transform.localScale = new Vector3(size.x, size.y, 0.015f);
        go.GetComponent<Collider>().isTrigger = true;   // 팀원 조사 화면은 트리거도 클릭함, 몸은 막지 않음
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;

        var pin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Remove(pin.GetComponent<Collider>());
        pin.name = "압정";
        pin.transform.SetParent(go.transform, false);
        pin.transform.localPosition = new Vector3(0f, 0.4f, -0.8f);
        pin.transform.localScale = new Vector3(0.12f / size.x, 0.12f / size.y, 2f) * 0.35f;
        if (stringMaterial != null) pin.GetComponent<Renderer>().sharedMaterial = stringMaterial;

        var stroke = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Remove(stroke.GetComponent<Collider>());
        stroke.name = "Outline";
        stroke.transform.SetParent(go.transform, false);
        stroke.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        stroke.transform.localScale = new Vector3(1.12f, 1.12f, 0.5f);
        if (outlineMaterial != null) stroke.GetComponent<Renderer>().sharedMaterial = outlineMaterial;

        var item = go.AddComponent<InspectionItem>();
        item.Configure(title, memory, false, new[] { stroke.GetComponent<Renderer>() });
        return item;
    }

    // 에디터 캡처(플레이 전)에서도 사진을 붙일 수 있게
    static void Remove(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    void RedString(Vector2 from, Vector2 to)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Remove(line.GetComponent<Collider>());
        line.name = "붉은 실";
        line.transform.SetParent(photoRoot, false);
        Vector2 mid = (from + to) * 0.5f, d = to - from;
        line.transform.localPosition = new Vector3(mid.x, mid.y, -0.045f);
        line.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        line.transform.localScale = new Vector3(d.magnitude, 0.012f, 0.005f);
        if (stringMaterial != null) line.GetComponent<Renderer>().sharedMaterial = stringMaterial;
    }

    // ---------------- 개발용 요약 ----------------

    public static string Describe(List<CaseBoard.CaseSummary> cases, int page)
    {
        var text = new StringBuilder();
        if (cases.Count == 0) text.AppendLine("아직 붙여 둔 게 없다. (사건 기록 없음)");
        else
        {
            CaseBoard.CaseSummary c = cases[Mathf.Clamp(page, 0, cases.Count - 1)];
            text.AppendLine($"<b>■ {c.title}</b>   <color=#999999>({page + 1} / {cases.Count})</color>");
            text.AppendLine($"<color=#a0d0a0>단서 {c.clues.Count}개</color>");
            foreach (CaseBoard.ClueLine clue in c.clues)
                text.AppendLine($"  · {clue.title} <color=#888888>({CharacterRoster.NameOf(clue.foundBy)})</color>");
            text.AppendLine("<color=#a0d0a0>우리의 추리</color>");
            if (c.answers.Count == 0) text.AppendLine("  (아직 없음)");
            foreach (CaseBoard.AnswerLine a in c.answers)
                text.AppendLine($"  · {a.question}\n     → {a.answer}");
            text.AppendLine("<color=#a0d0a0>개입</color>");
            if (!c.hasDirection) text.AppendLine("  (정하지 않음)");
            else text.AppendLine($"  · {Intervention.KindName(c.direction)}{(c.intervention != null ? $": {c.intervention} <color=#888888>({CharacterRoster.NameOf(c.intervenedBy)})</color>" : " (하지 못함)")}");
            if (c.nextTarget != null) text.AppendLine($"<color=#ff9a7f>아버지의 다음 표적: {c.nextTarget}</color>");
        }
        text.AppendLine();
        text.Append(CaseBoard.Missions.Count == 0 ? "<color=#888888>임무: (아직 없음 — 스테이지 전환 때 칠판에서 고름)</color>" : "");
        return text.ToString().TrimEnd();
    }
}
