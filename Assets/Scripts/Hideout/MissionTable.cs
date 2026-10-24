using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NarrativeGraybox.Interaction;
using UnityEngine;
using UnityEngine.AI;

// 아지트 지도 테이블 = 임무 고르기 (사용자 요청 2026-10-24). E → 테이블 위 도시 지도를 위에서 내려다보게 확대.
// 핀 = 팀원 조사 항목: 노랑 우리 집 / 빨강 다음 임무 / 파랑 이전 조사 지역. 노랑·파랑은 클릭하면 아래 조사 창에 짧은 설명.
// 빨강을 클릭하면 가운데 임무 창: 장소 설명 + 누가 갈지(이름 클릭 또는 1/2 키, 1~2명) → F 배정 확정, Q 지도로.
// 지도 화면 아래 [출발](Enter) / [돌아가기](Q). 출발 = 남매 둘 다 배정됐을 때, 각자 배정된 장소로 이동 (지금은 씬 안의 임시 장소).
// 창·버튼은 개발용 (실제 화면은 UI 담당).
public class MissionTable : HideoutStation
{
    [Header("지도에 꽂을 장소 (우리 집·임무·지난 사건)")]
    public MissionDefinition[] places = new MissionDefinition[0];

    [Header("칠판과 같은 사건 데이터 (파란 핀 설명)")]
    public CaseDeduction[] knownCases = new CaseDeduction[0];

    [Header("핀을 꽂을 지도 면 (가운데, 로컬 x = 동쪽, z = 북쪽)")]
    public Transform mapSurface;
    public Vector2 mapHalfSize = new Vector2(1f, 0.65f);

    [Header("임무 장소 (출발하면 여기로)")]
    public MissionSite[] sites = new MissionSite[0];

    [Header("핀 재질")]
    public Material homePinMaterial, missionPinMaterial, pastPinMaterial, needleMaterial, outlineMaterial;

    protected override string Title => "지도 — 임무 고르기";
    protected override string BuildText() => "";

    public bool Busy { get; private set; }   // 출발·귀환 이동 중
    public string Message { get; private set; } = "";

    private readonly Dictionary<InspectionItem, MissionDefinition> pinPlaces = new Dictionary<InspectionItem, MissionDefinition>();
    private readonly HashSet<CharacterType> picking = new HashSet<CharacterType>();
    private MissionDefinition pickingFor;
    private string pinnedSignature;
    private Transform pinRoot;
    private readonly List<Rect> blocked = new List<Rect>();
    private PlayerInteractor3D hooked;
    private readonly Dictionary<GameObject, Vector3> homePositions = new Dictionary<GameObject, Vector3>();
    private GUIStyle style, buttonStyle;

    static readonly CharacterType[] Siblings = { CharacterType.Player1, CharacterType.Player2 };

    void Start()
    {
        if (CharacterManager.instance != null)
            foreach (GameObject p in CharacterManager.instance.players) homePositions[p] = p.transform.position;
        RefreshPins();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsOpen)
        {
            Unhook();
            RefreshPins();   // 확대 중에는 핀을 바꾸지 않음 (팀원 조사 화면이 항목을 들고 있음)
        }
    }

    protected override void OnOpened()
    {
        Message = "";
        pickingFor = null;
        hooked = Inspector;
        hooked.BlocksPointer = p => blocked.Any(r => r.Contains(p));
    }

    void Unhook()
    {
        if (hooked == null) return;
        hooked.BlocksPointer = null;
        hooked = null;
        blocked.Clear();
    }

    // 고른 핀 (빨강이면 임무 창)
    public MissionDefinition SelectedPlace
    {
        get
        {
            PlayerInteractor3D inspector = Inspector;
            return inspector != null && inspector.Selected != null && pinPlaces.TryGetValue(inspector.Selected, out MissionDefinition place) ? place : null;
        }
    }

    public MissionDefinition SelectedMission { get { MissionDefinition p = SelectedPlace; return p != null && PinKind(p) == PinColor.Red ? p : null; } }

    protected override void HandleInput()
    {
        MissionDefinition mission = SelectedMission;
        if (mission != pickingFor)
        {
            // 임무 창을 새로 열 때: 지금 배정을 그대로 보여 줌
            pickingFor = mission;
            picking.Clear();
            if (mission != null)
                foreach (CharacterType who in Siblings)
                    if (GameSession.AssignmentOf(who) == mission.id) picking.Add(who);
        }
        if (mission != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) TogglePick(CharacterType.Player1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) TogglePick(CharacterType.Player2);
            if (Input.GetKeyDown(KeyCode.F)) ConfirmAssignment();
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Depart();
    }

    // ---------------- 배정 ----------------

    public void TogglePick(CharacterType who)
    {
        if (!picking.Remove(who)) picking.Add(who);
    }

    public IReadOnlyCollection<CharacterType> Picking => picking;

    // 임무 창에서 고른 사람을 이 임무에 배정 (1~2명). 다른 곳에 배정돼 있던 사람은 이쪽으로 옮김
    public bool ConfirmAssignment()
    {
        MissionDefinition mission = SelectedMission;
        if (mission == null) return false;
        if (picking.Count == 0) { Message = "최소 한 명은 가야 한다."; return false; }
        foreach (CharacterType who in Siblings)
        {
            if (picking.Contains(who)) GameSession.Assign(who, mission.id);
            else if (GameSession.AssignmentOf(who) == mission.id) GameSession.Assign(who, null);
        }
        Message = $"{mission.displayName}: {string.Join(", ", picking.Select(CharacterRoster.NameOf))} 배정";
        Debug.Log($"[임무] 배정 확정 — {Message}");
        return true;
    }

    public bool AllAssigned => Siblings.All(who => SiteFor(GameSession.AssignmentOf(who)) != null);
    public bool CanDepart => AllAssigned && !GameSession.NeedsRest;

    // ---------------- 출발 · 귀환 ----------------

    public bool Depart()
    {
        if (Busy) return false;
        if (GameSession.NeedsRest)
        {
            Message = "임무에서 막 돌아왔다. 먼저 좀 쉬자. (매트리스)";
            return false;
        }
        if (!AllAssigned)
        {
            Message = "남매 둘 다 갈 곳을 정해야 출발할 수 있다.";
            return false;
        }
        StartCoroutine(Travel(true));
        return true;
    }

    // 개발용 (F6): 둘 다 아지트로
    public bool ReturnHome()
    {
        if (Busy || !GameSession.Deployed) return false;
        StartCoroutine(Travel(false));
        return true;
    }

    IEnumerator Travel(bool depart)
    {
        Busy = true;
        if (IsOpen) Close();
        // 확대가 풀리는(팀원 조사 카메라 복귀 페이드) 동안 기다림
        yield return new WaitUntil(() => FadeManager.instance == null || !FadeManager.instance.isFading);
        yield return new WaitWhile(() => CharacterManager.instance.players.Any(p => p.GetComponent<PlayerInteractor3D>().IsInspecting));

        FadeManager fade = FadeManager.instance;
        if (fade != null) { fade.isFading = true; yield return fade.FadeOut(); }

        var used = new Dictionary<MissionSite, int>();
        foreach (GameObject player in CharacterManager.instance.players)
        {
            CharacterType who = CharacterRoster.TypeOf(player);
            Vector3 to = homePositions.TryGetValue(player, out Vector3 home) ? home : player.transform.position;
            if (depart)
            {
                MissionSite site = SiteFor(GameSession.AssignmentOf(who));
                used.TryGetValue(site, out int slot);
                used[site] = slot + 1;
                to = site.spawnPoints[Mathf.Min(slot, site.spawnPoints.Length - 1)].position;
            }
            Warp(player, to);
        }
        GameSession.Deployed = depart;
        if (depart)
            Debug.Log("[임무] 출발 — " + string.Join(", ", Siblings.Select(w => $"{CharacterRoster.NameOf(w)} → {PlaceName(GameSession.AssignmentOf(w))}")));
        else
        {
            Debug.Log("[임무] 아지트로 귀환 — 휴식해야 다시 출발할 수 있음");
            GameSession.ClearAssignments();
            GameSession.NeedsRest = true;
        }
        // 아지트에 있는 동안만 쉬는 자리를 씀: 출발하면 비우고, 돌아오면 새 자리로 (조작하지 않는 캐릭터)
        if (HideoutResidents.instance != null)
        {
            if (!depart) HideoutResidents.instance.Shuffle();
            HideoutResidents.instance.PlaceIdle();
        }

        // 화면이 밝아지기 전에 구역 카메라를 새 장소로 (조작 중인 캐릭터 기준)
        yield return new WaitForFixedUpdate();
        yield return null;   // 구역 트리거(OnTriggerEnter)가 처리된 뒤
        foreach (CameraTrigger room in CameraTrigger.All) room.UpdateCameraStateInstant();

        if (fade != null) { yield return fade.FadeIn(); fade.isFading = false; }
        GameMessage.Show(depart ? "출발했다." : "[개발용] 아지트로 돌아왔다.");
        Busy = false;
    }

    static void Warp(GameObject player, Vector3 position)
    {
        var agent = player.GetComponent<NavMeshAgent>();
        if (agent != null && NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
        else player.transform.position = position;
    }

    public MissionSite SiteFor(string missionId) => string.IsNullOrEmpty(missionId) ? null : sites.FirstOrDefault(s => s != null && s.missionId == missionId);

    string PlaceName(string missionId) => places.FirstOrDefault(p => p != null && p.id == missionId)?.displayName ?? "(없음)";

    // ---------------- 핀 ----------------

    public enum PinColor { None, Yellow, Red, Blue }

    bool Recorded(string caseId) => !string.IsNullOrEmpty(caseId) && CaseBoard.Build(knownCases).Any(c => c.caseId == caseId);

    public PinColor PinKind(MissionDefinition place)
    {
        if (place == null) return PinColor.None;
        switch (place.kind)
        {
            case MissionDefinition.Kind.Home: return PinColor.Yellow;
            case MissionDefinition.Kind.PastCase: return Recorded(place.caseId) ? PinColor.Blue : PinColor.None;
            default: return Recorded(place.caseId) ? PinColor.Blue : PinColor.Red;
        }
    }

    public IEnumerable<KeyValuePair<InspectionItem, MissionDefinition>> Pins => pinPlaces;

    public void RefreshPins()
    {
        string signature = string.Join("|", places.Where(p => p != null).Select(p => $"{p.id}:{PinKind(p)}"));
        if (signature == pinnedSignature) return;
        pinnedSignature = signature;

        if (pinRoot != null) Remove(pinRoot.gameObject);
        pinPlaces.Clear();
        if (mapSurface != null)
        {
            pinRoot = new GameObject("핀들").transform;
            pinRoot.SetParent(mapSurface, false);
            foreach (MissionDefinition place in places)
            {
                PinColor color = PinKind(place);
                if (color == PinColor.None) continue;
                pinPlaces[Pin(place, color)] = place;
            }
        }
        Area.Items = pinPlaces.Keys.ToArray();
    }

    InspectionItem Pin(MissionDefinition place, PinColor color)
    {
        var at = new Vector3(place.mapPosition.x * mapHalfSize.x, 0f, place.mapPosition.y * mapHalfSize.y);
        var go = new GameObject($"핀: {place.displayName}");
        go.transform.SetParent(pinRoot, false);
        go.transform.localPosition = at;

        Material head = color == PinColor.Yellow ? homePinMaterial : color == PinColor.Red ? missionPinMaterial : pastPinMaterial;
        Part(go.transform, PrimitiveType.Cylinder, "바늘", new Vector3(0f, 0.06f, 0f), new Vector3(0.012f, 0.06f, 0.012f), needleMaterial);
        GameObject ball = Part(go.transform, PrimitiveType.Sphere, "머리", new Vector3(0f, 0.13f, 0f), Vector3.one * 0.07f, head);
        GameObject stroke = Part(ball.transform, PrimitiveType.Sphere, "Outline", Vector3.zero, Vector3.one * 1.35f, outlineMaterial);

        // 클릭 판정은 넉넉하게 (위에서 내려다보면 핀 머리가 작음)
        var hit = go.AddComponent<SphereCollider>();
        hit.isTrigger = true;
        hit.center = new Vector3(0f, 0.1f, 0f);
        hit.radius = 0.09f;

        string description = place.description;
        if (color == PinColor.Blue)
        {
            CaseBoard.CaseSummary summary = CaseBoard.Build(knownCases).FirstOrDefault(c => c.caseId == place.caseId);
            if (summary != null) description = summary.memory;
        }
        var item = go.AddComponent<InspectionItem>();
        item.Configure(color == PinColor.Red ? $"[임무] {place.displayName}" : place.displayName, description, false, new[] { stroke.GetComponent<Renderer>() });
        return item;
    }

    static GameObject Part(Transform parent, PrimitiveType shape, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        Remove(part.GetComponent<Collider>());
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    static void Remove(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    // ---------------- 개발용 화면 ----------------

    float UiScale => Mathf.Max(.35f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));   // 팀원 조사 창과 같은 배율

    protected override void DrawGui()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true, padding = new RectOffset(14, 14, 12, 12) };
            style.normal.textColor = Color.white;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true };
        }
        blocked.Clear();
        float above = Screen.height - 175f * UiScale - 12f;   // 아래쪽 팀원 조사 창 위

        MissionDefinition mission = SelectedMission;
        if (mission != null) DrawMissionWindow(mission, above);
        else DrawMapBar(above);
    }

    // 지도 화면 아래: 배정 현황 + [출발] [돌아가기]
    void DrawMapBar(float above)
    {
        float width = Mathf.Min(640f, Screen.width * 0.7f), height = 92f;
        var bar = new Rect((Screen.width - width) * 0.5f, above - height, width, height);
        Fill(bar, CenteredBackground);
        blocked.Add(bar);
        string plan = string.Join("   ", Siblings.Select(w => $"{CharacterRoster.NameOf(w)} → {PlaceName(GameSession.AssignmentOf(w))}"));
        string line = Message.Length > 0 ? $"<color=#ffd27f>{Message}</color>"
            : GameSession.NeedsRest ? "<color=#ffd27f>임무에서 막 돌아왔다. 먼저 좀 쉬자. (매트리스)</color>"
            : "<color=#bbbbbb>빨간 핀을 눌러 갈 사람을 정한다</color>";
        GUI.Label(new Rect(bar.x, bar.y, bar.width - 230f, bar.height), $"<b>[개발용] 배정</b>  {plan}\n{line}", style);

        GUI.enabled = CanDepart && !Busy;
        if (GUI.Button(new Rect(bar.xMax - 220f, bar.y + 22f, 100f, 48f), "출발 <size=12>(Enter)</size>", buttonStyle)) Depart();
        GUI.enabled = true;
        if (GUI.Button(new Rect(bar.xMax - 112f, bar.y + 22f, 100f, 48f), "돌아가기 <size=12>(Q)</size>", buttonStyle)) Close();
    }

    // 빨간 핀: 장소 설명 + 누가 갈지
    void DrawMissionWindow(MissionDefinition mission, float above)
    {
        float width = Mathf.Min(620f, Screen.width * 0.6f), height = Mathf.Min(360f, above - 24f);
        var window = new Rect((Screen.width - width) * 0.5f, Mathf.Max(12f, (above - height) * 0.5f), width, height);
        Fill(window, CenteredBackground);
        blocked.Add(window);
        GUI.Label(new Rect(window.x, window.y, window.width, 140f),
            $"<b>[개발용] 임무 — {mission.displayName}</b>\n\n{mission.description}", style);

        GUI.Label(new Rect(window.x, window.y + 140f, window.width, 40f), "<b>누가 갈까?</b> <color=#999999>(1~2명, 이름을 누르거나 1·2 키)</color>", style);
        for (int i = 0; i < Siblings.Length; i++)
        {
            CharacterType who = Siblings[i];
            string elsewhere = GameSession.AssignmentOf(who) is string id && id != mission.id ? $" <size=12>({PlaceName(id)}에서 옮김)</size>" : "";
            string label = $"{(picking.Contains(who) ? "■" : "□")} {i + 1}. {CharacterRoster.NameOf(who)}{(picking.Contains(who) ? elsewhere : "")}";
            if (GUI.Button(new Rect(window.x + 14f + i * (width - 28f) / 2f, window.y + 196f, (width - 40f) / 2f, 50f), label, buttonStyle)) TogglePick(who);
        }
        string line = Message.Length > 0 ? $"<color=#ffd27f>{Message}</color>   " : "";
        GUI.Label(new Rect(window.x, window.yMax - 52f, window.width, 40f), $"{line}<color=#bbbbbb>F 배정 확정   Q 지도로</color>", style);
    }

    static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    void OnDisable() => Unhook();
}
