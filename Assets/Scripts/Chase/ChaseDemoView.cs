using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 시연·개발용 표시 (게임 UI 아님). 시연 메뉴가 켭니다.
//  - 집 전체를 게임과 같은 비스듬한 탑뷰로 비추는 카메라 (방 카메라 위에 덮어 그림)
//  - 아버지: 시야 범위, 지금 가는 길(몰래 움직이면 하늘색), 상태 글자
//  - 캐릭터가 낸 소리의 범위(원), 잠긴 문·예측 지점·잠복 자리·뒤지는 숨는 곳 표시
public class ChaseDemoView : MonoBehaviour
{
    public Vector2 boundsMin = new Vector2(-13f, -10f);
    public Vector2 boundsMax = new Vector2(13f, 10f);

    private Camera view;
    private Material lineMaterial;
    private LineRenderer pathLine, coneLine, doorRing, markRing, spotRing;
    private readonly List<LineRenderer> noiseRings = new List<LineRenderer>();
    private FatherAI father;
    private ChaserSenses senses;
    private NavMeshAgent agent;
    private GUIStyle labelStyle;
    private readonly List<(Vector3 world, string text, Color color)> labels = new List<(Vector3, string, Color)>();

    static readonly Color PathColor = new Color(1f, 0.85f, 0.2f);
    static readonly Color SneakColor = new Color(0.4f, 0.85f, 1f);
    static readonly Color FatherColor = new Color(0.85f, 0.6f, 1f);

    public static ChaseDemoView Enable()
    {
        var existing = FindAnyObjectByType<ChaseDemoView>();
        return existing != null ? existing : new GameObject("Chase Demo View").AddComponent<ChaseDemoView>();
    }

    void Start()
    {
        view = gameObject.AddComponent<Camera>();
        view.orthographic = true;
        view.depth = 50f; // 방 카메라(메인 카메라) 위에 그림
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
        view.farClipPlane = 150f;

        Shader shader = Shader.Find("Sprites/Default");
        lineMaterial = new Material(shader);
        pathLine = NewLine("Path", 0.09f);
        coneLine = NewLine("View Cone", 0.04f);
        doorRing = NewLine("Door", 0.08f);
        markRing = NewLine("Mark", 0.07f);
        spotRing = NewLine("Spot", 0.07f);
        for (int i = 0; i < 8; i++) noiseRings.Add(NewLine("Noise", 0.05f));
    }

    void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    LineRenderer NewLine(string name, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial;
        line.widthMultiplier = width;
        line.useWorldSpace = true;
        line.numCapVertices = 2;
        line.enabled = false;
        return line;
    }

    void FitCamera()
    {
        Quaternion rotation = Quaternion.LookRotation(-new Vector3(0f, 16f, -10f)); // 게임 탑뷰와 같은 각도
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up, forward = rotation * Vector3.forward;
        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        foreach (float x in new[] { boundsMin.x, boundsMax.x })
        foreach (float y in new[] { 0f, 2f })
        foreach (float z in new[] { boundsMin.y, boundsMax.y })
        {
            var corner = new Vector3(x, y, z);
            minR = Mathf.Min(minR, Vector3.Dot(corner, right)); maxR = Mathf.Max(maxR, Vector3.Dot(corner, right));
            minU = Mathf.Min(minU, Vector3.Dot(corner, up)); maxU = Mathf.Max(maxU, Vector3.Dot(corner, up));
        }
        Vector3 middle = right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f);
        // 아래쪽 자막 자리만큼 조금 여유
        view.transform.SetPositionAndRotation(middle - forward * 60f - up * 1.2f, rotation);
        view.orthographicSize = Mathf.Max((maxU - minU) * 0.5f, (maxR - minR) * 0.5f / Mathf.Max(0.1f, view.aspect)) + 1.6f;
    }

    void LateUpdate()
    {
        if (view == null) return;
        FitCamera();
        labels.Clear();

        if (father == null)
        {
            father = FindAnyObjectByType<FatherAI>();
            if (father != null) { senses = father.GetComponent<ChaserSenses>(); agent = father.GetComponent<NavMeshAgent>(); }
        }

        DrawNoises();
        foreach (CharacterStatus character in CharacterStatus.All)
        {
            string shortName = character.name.Split(' ')[0];
            string state = character.IsCaught ? " (잡힘)" : character.IsHidden ? $" ({character.HidingIn.displayName}에 숨음)" : "";
            labels.Add((character.transform.position + Vector3.up * 1.3f, shortName + state, Color.white));
        }

        // 학습한 것 (문·숨는 곳)
        Color learnedColor = new Color(1f, 0.5f, 0.8f);
        foreach (EntryPoint entry in EntryPoint.All)
        {
            int count = FatherMemory.DoorEscapes(entry);
            if (count > 0) labels.Add(((entry.PointA + entry.PointB) * 0.5f + Vector3.up * 1.1f, $"학습: 문으로 버팀 {count}회", learnedColor));
        }
        foreach (HidingSpot learnedSpot in HidingSpot.All)
        {
            int count = FatherMemory.HideEscapes(learnedSpot);
            if (count > 0) labels.Add((learnedSpot.transform.position + Vector3.up * 1.0f, $"학습: 숨어서 넘김 {count}회", learnedColor));
        }

        bool active = father != null && father.enabled && father.gameObject.activeInHierarchy;
        pathLine.enabled = coneLine.enabled = doorRing.enabled = markRing.enabled = spotRing.enabled = false;
        if (!active) return;

        DrawCone();
        DrawPath();
        labels.Add((father.transform.position + Vector3.up * 1.6f, "아버지: " + father.StateLabel, FatherColor));

        if (father.DoorTarget != null)
        {
            Vector3 door = (father.DoorTarget.PointA + father.DoorTarget.PointB) * 0.5f;
            bool bashing = father.Current == FatherAI.Mode.Bash;
            Ring(doorRing, door, 0.9f, bashing ? Color.red : new Color(1f, 0.55f, 0.1f));
            string tactic = father.Current == FatherAI.Mode.DoorApproach ? "잠김? (확인 중)" : $"잠긴 문 → {TacticName(father.LastDoorTactic)}";
            labels.Add((door + Vector3.up * 0.5f, tactic, new Color(1f, 0.7f, 0.3f)));
        }
        if (father.Current == FatherAI.Mode.Ambush)
        {
            Ring(markRing, father.AmbushPoint, 0.5f, SneakColor);
        }
        else if (father.HasPrediction && (father.Current == FatherAI.Mode.Intercept || father.Current == FatherAI.Mode.SearchSpots))
        {
            Ring(markRing, father.PredictedPoint, 0.6f, Color.magenta);
            labels.Add((father.PredictedPoint + Vector3.up * 0.4f, father.PredictionLearned ? "길목 (학습한 도망 경로)" : "예측 지점", Color.magenta));
        }
        else if (father.Current == FatherAI.Mode.FeignRetreat)
        {
            Ring(markRing, father.Destination, 0.5f, PathColor);
        }

        HidingSpot spot = father.Current == FatherAI.Mode.SearchSpots ? father.SpotBeingChecked
                        : father.Current == FatherAI.Mode.Investigate ? father.InvestigateSpot : null;
        if (spot != null)
        {
            Ring(spotRing, spot.transform.position, 0.55f, new Color(0.4f, 1f, 0.5f));
            labels.Add((spot.transform.position + Vector3.up * 0.4f, $"뒤질 곳: {spot.displayName}", new Color(0.5f, 1f, 0.6f)));
        }
    }

    static string TacticName(DoorTactic tactic) => tactic switch
    {
        DoorTactic.Detour => "우회",
        DoorTactic.FeignRetreat => "위장 후퇴",
        DoorTactic.Ambush => "잠복",
        DoorTactic.Bash => "파괴",
        _ => "?",
    };

    void DrawPath()
    {
        if (agent == null || !agent.isOnNavMesh || !agent.hasPath) return;
        Vector3[] corners = agent.path.corners;
        if (corners.Length == 0) return;
        pathLine.positionCount = corners.Length + 1;
        pathLine.SetPosition(0, Flat(father.transform.position));
        for (int i = 0; i < corners.Length; i++) pathLine.SetPosition(i + 1, Flat(corners[i]));
        Color color = father.IsSneaking ? SneakColor : PathColor;
        pathLine.startColor = pathLine.endColor = color;
        pathLine.enabled = true;
    }

    void DrawCone()
    {
        if (senses == null) return;
        const int segments = 16;
        float half = senses.viewAngle * 0.5f;
        Vector3 origin = Flat(father.transform.position);
        coneLine.positionCount = segments + 3;
        coneLine.SetPosition(0, origin);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (float)segments);
            coneLine.SetPosition(i + 1, origin + Quaternion.Euler(0f, angle, 0f) * father.transform.forward * senses.viewDistance);
        }
        coneLine.SetPosition(segments + 2, origin);
        float awareness = 0f;
        foreach (CharacterStatus character in CharacterStatus.All) awareness = Mathf.Max(awareness, senses.Awareness(character));
        Color color = Color.Lerp(new Color(1f, 1f, 1f, 0.35f), new Color(1f, 0.2f, 0.2f, 0.9f), awareness);
        coneLine.startColor = coneLine.endColor = color;
        coneLine.enabled = true;
    }

    void DrawNoises()
    {
        int used = 0;
        foreach (NoiseEvent noise in NoiseSystem.Recent)
        {
            if (used >= noiseRings.Count) break;
            if (noise.source == null || noise.source.GetComponent<CharacterStatus>() == null) continue; // 캐릭터가 낸 소리만
            float age = Time.time - noise.time;
            if (age > 1.5f) continue;
            float alpha = Mathf.Lerp(0.9f, 0f, age / 1.5f);
            Ring(noiseRings[used++], noise.position, noise.radius, new Color(0.5f, 0.9f, 1f, alpha));
            if (age < 0.8f) labels.Add((noise.position + Vector3.up * 0.3f, $"소리 ({NoiseName(noise.kind)})", new Color(0.6f, 0.95f, 1f, alpha)));
        }
        for (int i = used; i < noiseRings.Count; i++) noiseRings[i].enabled = false;
    }

    static string NoiseName(NoiseKind kind) => kind switch
    {
        NoiseKind.Footstep => "발소리",
        NoiseKind.Run => "뛰는 소리",
        NoiseKind.Door => "문",
        NoiseKind.DoorLock => "잠금",
        NoiseKind.DoorBash => "쾅",
        NoiseKind.Hiding => "숨는 소리",
        _ => kind.ToString(),
    };

    static void Ring(LineRenderer line, Vector3 center, float radius, Color color)
    {
        const int segments = 40;
        line.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, Flat(center) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
        line.startColor = line.endColor = color;
        line.enabled = true;
    }

    static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0.15f, p.z);

    void OnGUI()
    {
        if (view == null) return;
        GUI.depth = 1; // 개발용 상태 표시·자막이 이 글자들 위에 오도록
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = false };
        }
        foreach (var (world, text, color) in labels)
        {
            Vector3 screen = view.WorldToScreenPoint(world);
            if (screen.z < 0f) continue;
            Vector2 size = labelStyle.CalcSize(new GUIContent(text));
            var rect = new Rect(screen.x - size.x * 0.5f - 4f, Screen.height - screen.y - size.y * 0.5f, size.x + 8f, size.y + 2f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f * color.a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.Label(rect, text, labelStyle);
        }
        GUI.color = Color.white;
    }
}
