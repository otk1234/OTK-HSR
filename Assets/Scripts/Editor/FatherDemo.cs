using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// 아버지 AI 시연 (3-1단계): 메뉴 OTK → 아버지 AI 시연
// 플레이 모드에서 자막과 함께 순서대로 보여 주고, 끝나면 직접 조작할 수 있습니다.
// 명령줄 녹화: -executeMethod FatherDemo.RecordFromCommandLine -demoFramesDir <폴더>
//  → 집 전체를 일정 간격으로 찍은 JPG와 자막 목록(frames.txt)을 남기고 에디터를 닫습니다.
public static class FatherDemo
{
    static IEnumerator<float> steps;
    static double waitUntil;

    // 녹화
    static bool recording;
    static string framesDir;
    static int frameIndex;
    static double nextFrame;
    static Camera recordCamera;
    static RenderTexture recordTarget;
    static StreamWriter frameLog;
    const double FrameInterval = 0.5;

    static string caption = "";

    [MenuItem("OTK/아버지 AI 시연")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Start();
    }

    public static void RecordFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-demoFramesDir");
        framesDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : "FatherDemoFrames";
        Directory.CreateDirectory(framesDir);
        foreach (string old in Directory.GetFiles(framesDir)) File.Delete(old);
        frameLog = new StreamWriter(Path.Combine(framesDir, "frames.txt"), false, new System.Text.UTF8Encoding(false));
        recording = true;
        frameIndex = 0;
        Start();
    }

    static void Start()
    {
        EditorSceneManager.OpenScene(FatherTestSceneBuilder.TargetScene, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            steps = Steps();
            waitUntil = EditorApplication.timeSinceStartup + 1.5;
            nextFrame = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Stop();
        }
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        steps = null;
    }

    static void Tick()
    {
        if (recording && EditorApplication.timeSinceStartup >= nextFrame)
        {
            CaptureFrame();
            nextFrame = EditorApplication.timeSinceStartup + FrameInterval;
        }
        if (steps == null || EditorApplication.timeSinceStartup < waitUntil) return;
        bool more;
        try { more = steps.MoveNext(); }
        catch (Exception e) { Debug.LogException(e); more = false; }
        if (more) { waitUntil = EditorApplication.timeSinceStartup + steps.Current; return; }

        Stop();
        if (recording)
        {
            frameLog.Close();
            recording = false;
            Debug.Log($"[시연 녹화] 프레임 {frameIndex}장: {framesDir}");
            EditorApplication.Exit(0);
        }
    }

    static void Say(string text)
    {
        caption = text;
        ChaseDebugOverlay.Caption = text;
        Debug.Log("[시연] " + text.Replace("\n", " "));
    }

    // ---------------- 녹화 ----------------

    // 게임과 같은 비스듬한 탑뷰로 집 전체 (캐릭터 스프라이트가 보이도록)
    static void CaptureFrame()
    {
        if (recordCamera == null)
        {
            var go = new GameObject("Demo Record Camera");
            Object.DontDestroyOnLoad(go);
            recordCamera = go.AddComponent<Camera>();
            recordCamera.enabled = false;
            recordCamera.orthographic = true;
            recordCamera.clearFlags = CameraClearFlags.SolidColor;
            recordCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            Quaternion rotation = PrototypeSceneBuilder.TopDownRotation;
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up, forward = rotation * Vector3.forward;
            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            foreach (float x in new[] { -13f, 13f })
            foreach (float y in new[] { 0f, 2f })
            foreach (float z in new[] { -10f, 10f })
            {
                var corner = new Vector3(x, y, z);
                minR = Mathf.Min(minR, Vector3.Dot(corner, right)); maxR = Mathf.Max(maxR, Vector3.Dot(corner, right));
                minU = Mathf.Min(minU, Vector3.Dot(corner, up)); maxU = Mathf.Max(maxU, Vector3.Dot(corner, up));
            }
            Vector3 middle = right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f);
            go.transform.SetPositionAndRotation(middle - forward * 40f, rotation);
            recordTarget = new RenderTexture(1100, 800, 24);
            recordCamera.targetTexture = recordTarget;
            recordCamera.aspect = 1100f / 800f;
            recordCamera.orthographicSize = Mathf.Max((maxU - minU) * 0.5f, (maxR - minR) * 0.5f / recordCamera.aspect) + 0.3f;
            recordCamera.farClipPlane = 120f;
        }

        recordCamera.Render();
        RenderTexture.active = recordTarget;
        var image = new Texture2D(recordTarget.width, recordTarget.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, recordTarget.width, recordTarget.height), 0, 0);
        RenderTexture.active = null;
        string file = $"f{frameIndex:0000}.jpg";
        File.WriteAllBytes(Path.Combine(framesDir, file), image.EncodeToJPG(72));
        Object.DestroyImmediate(image);

        var father = Object.FindAnyObjectByType<FatherAI>();
        string state = father != null && father.enabled ? father.StateLabel : "(멈춤)";
        string game = GameStateManager.IsChase ? "추격" : "추리";
        frameLog.WriteLine($"{file}\t{game}\t{state}\t{caption.Replace("\n", " / ").Replace("\t", " ")}");
        frameLog.Flush();
        frameIndex++;
    }

    // ---------------- 시연 순서 ----------------

    static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        var game = GameStateManager.instance;
        CharacterStatus p1 = manager.players[0].GetComponent<CharacterStatus>();
        CharacterStatus p2 = manager.players[1].GetComponent<CharacterStatus>();
        PlayerMovement move1 = p1.GetComponent<PlayerMovement>();
        Inventory inventory = p1.GetComponent<Inventory>();
        var father = Object.FindAnyObjectByType<FatherAI>();
        var motor = father.GetComponent<ChaserMotor>();
        var senses = father.GetComponent<ChaserSenses>();
        EntryPoint Entry(string name) => EntryPoint.All.First(e => e.displayName == name);
        HidingSpot Spot(string name) => HidingSpot.All.First(s => s.displayName == name);
        float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        void Place(Component who, Vector3 floor, float yaw = float.NaN)
        {
            ChaserMotor m = who.GetComponent<ChaserMotor>();
            if (m != null) m.Teleport(floor);
            else who.GetComponent<NavMeshAgent>().Warp(floor);
            if (!float.IsNaN(yaw)) who.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        void Freeze()
        {
            father.ResetToProwl();
            father.enabled = false;
            motor.Stop();
        }
        void ForgetAll() { senses.Forget(p1); senses.Forget(p2); }
        IEnumerable<float> SetLock(EntryPoint door, bool locked)
        {
            if (door.door.IsOpen) { door.door.Interact(inventory); yield return 1f; }
            if (door.door.IsLocked != locked) door.door.ToggleLock(inventory);
            yield return 0.1f;
        }
        float PathLength(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(from, to, motor.Agent.areaMask, path);
            if (path.status != NavMeshPathStatus.PathComplete) return -1f;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return length;
        }
        // 기다리는 동안 자막 끝에 아버지 상태를 붙여서 보여 줌
        IEnumerable<float> Watch(string text, float seconds, Func<bool> until = null, float step = 0.25f)
        {
            for (float t = 0f; t < seconds && (until == null || !until()); t += step)
            {
                Say($"{text}\n<color=#d9a6ff>아버지: {father.StateLabel}</color>");
                yield return step;
            }
        }

        // 씬 뷰: 집 전체를 위에서, 아버지를 선택해 시야 기즈모가 보이게
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.orthographic = true;
            sceneView.LookAt(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 12f);
        }
        Selection.activeGameObject = father.gameObject;

        Freeze();
        father.intuitionChance = 0f;   // 직감은 ⑤에서만
        father.searchTime = 4f;
        father.Reseed(7);
        Place(father, new Vector3(0f, 0f, -1.5f), 180f);
        Say("아버지 AI 시연 (3-1단계)\n창문·환풍구를 쓰고, 놓치면 예측해서 앞지르고, 숨는 곳을 뒤지는 고지능 추격자입니다.");
        yield return 4f;

        // ① 맵: 잠긴 문 너머로 가는 길
        Vector3 hall = new Vector3(0f, 0f, -1.5f);
        foreach (float s in SetLock(Entry("아이방 문"), true)) yield return s;
        foreach (float s in SetLock(Entry("안방 문"), true)) yield return s;
        foreach (float s in SetLock(Entry("서재 문"), true)) yield return s;
        float kid = PathLength(hall, new Vector3(-8f, 0f, 5f)), master = PathLength(hall, new Vector3(-2f, 0f, 5f)), study = PathLength(hall, new Vector3(6f, 0f, 4.5f));
        Say($"① 북쪽 방 문 3개를 모두 잠갔을 때, 아버지가 들어갈 길:\n" +
            $"아이방 = 환풍구로 짧게 ({kid:0}m) · 서재 = 현관→마당→창문으로 멀리 ({study:0}m) · 안방 = {(master < 0f ? "길 없음 (다음 단계 '파괴' 대상)" : master.ToString("0") + "m")}");
        yield return 7f;
        foreach (float s in SetLock(Entry("서재 문"), false)) yield return s;

        // ② 배회 (두 캐릭터는 잠긴 안방 안에서 대기)
        Place(p1, new Vector3(-2f, 0f, 5f));
        Place(p2, new Vector3(-1f, 0f, 4f));
        ForgetAll();
        father.enabled = true;
        father.ResetToProwl();
        foreach (float s in Watch("② 배회: 방을 골라 돌아다닙니다. 캐릭터를 자주 본 방일수록 자주 들릅니다.\n(두 캐릭터는 잠긴 안방에 있어 아버지가 들어갈 수 없음)", 7f)) yield return s;

        // ③ 숨는 소리 → 잠긴 아이방에 환풍구로 들어가 그 숨는 곳을 뒤짐
        HidingSpot toyBox = Spot("장난감 상자");
        Place(father, hall, 0f);
        father.StandStill(5f);
        Say("③ 잠긴 아이방에서 '숨는 소리'(장난감 상자 뚜껑)가 납니다…");
        yield return 1.5f;
        NoiseSystem.Emit(toyBox.transform.position, 20f, NoiseKind.Hiding, p2.gameObject);
        foreach (float s in Watch("③ 숨는 소리를 들으면 그 숨는 곳을 직접 뒤지러 갑니다. 문이 잠겨 있으니 서쪽 복도의 환풍구로 기어 들어갑니다.", 20f,
                     () => Flat(father.transform.position, toyBox.transform.position) <= 1.5f)) yield return s;
        foreach (float s in Watch("③ 장난감 상자를 열어 봤지만 비어 있음 → 둘러보고 다시 배회.", 4f)) yield return s;
        Freeze();
        foreach (float s in SetLock(Entry("아이방 문"), false)) yield return s;

        // ④ 추격 → 놓침 → 예측해 앞질러 감 → 수색 → 포기
        Place(father, new Vector3(4.8f, 0f, -2f), 11f);
        Place(p1, new Vector3(5.5f, 0f, 1.5f));
        ForgetAll();
        Say("④ 동쪽 복도에서 탐정이 아버지 눈에 띕니다.");
        yield return 1.5f;
        ForgetAll();   // 자막을 보여 주는 동안 눈(감각)은 켜져 있었으므로 새로 시작
        father.enabled = true;
        father.StandStill(5f);
        for (float t = 0f; t < 3f && father.Current != FatherAI.Mode.Chase; t += 0.05f) yield return 0.05f;
        move1.scriptedRun = true;
        move1.scriptedInput = new Vector2(1f, 0f);
        Say("④ 발견 → 추격 상태! 탐정이 동쪽으로 달아납니다.");
        yield return 0.3f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        Place(p1, new Vector3(-2f, 0f, 5f));    // 시야에서 사라짐 (시연용 순간이동, 잠긴 안방)
        foreach (float s in Watch("④ 탐정이 시야에서 사라짐 (시연용 순간이동).\n아버지는 마지막으로 본 곳을 지나, 탐정이 달리던 방향(동쪽 끝)을 예측해 앞질러 갑니다. (씬 뷰 보라색 선)", 6f,
                     () => father.Current == FatherAI.Mode.SearchSpots || father.Current == FatherAI.Mode.Search)) yield return s;
        foreach (float s in Watch("④ 예측한 곳에도 없으면 근처 숨는 곳 일부를 무작위로 뒤지고, 주변을 수색하다가…", 20f,
                     () => father.Current == FatherAI.Mode.Prowl)) yield return s;
        foreach (float s in Watch("④ 포기 → 배회. 잠시 뒤 추리 상태로 돌아갑니다.", 2.5f)) yield return s;
        Freeze();
        yield return 0.5f;

        // ⑤ 직감: 조작하지 않는 캐릭터
        foreach (float s in SetLock(Entry("안방 문"), false)) yield return s;
        HidingSpot closet = Spot("안방 옷장");
        Place(p1, closet.transform.position);
        closet.TryEnter(p1);
        yield return closet.enterTime + 0.2f;
        Place(p2, new Vector3(5f, 0f, -8.2f));
        Place(father, new Vector3(-2f, 0f, -1.5f), 180f);
        ForgetAll();
        Say("⑤ 직감: 조작 중인 탐정은 안방 옷장에 숨었고, 조작하지 않는 조수는 세탁실에 서 있습니다.\n(실제 게임에서 직감은 20% 확률 · 35초에 최대 1번 · 숨은 캐릭터는 제외. 시연을 위해 이번엔 반드시 작동)");
        yield return 4f;
        father.intuitionChance = 1f;
        father.ClearIntuitionCooldown();
        father.enabled = true;
        father.ResetToProwl();
        father.intuitionChance = 0f;
        foreach (float s in Watch("⑤ 아버지가 '왠지' 세탁실 쪽으로 향합니다. 정확한 위치는 모르고 방까지만 갑니다.", 20f,
                     () => game.IsTargeted(p2), 0.02f)) yield return s;   // 잡기 전에 멈추도록 촘촘히 확인
        if (game.RestartPending) { Say("⑤ 조수가 잡혀 씬이 재시작됩니다. 시연은 여기서 끝납니다."); yield return 4f; ChaseDebugOverlay.Caption = ""; yield break; }
        Freeze();
        Say("⑤ 조작하지 않던 조수를 발견! (시연을 위해 여기서 멈춤)");
        yield return 3.5f;
        closet.Exit(p1);
        Place(p2, new Vector3(-1f, 0f, 5f));
        yield return game.chaseEndDelay + 0.5f;

        // ⑥ 피날레: 숨는 장면을 못 봤어도 놓친 곳 근처를 뒤져 잡음
        HidingSpot pantry = Spot("팬트리");
        foreach (float s in SetLock(Entry("주방 문"), false)) yield return s;
        if (!Entry("주방 문").door.IsOpen) Entry("주방 문").door.Interact(inventory);   // 열어 둠 (아버지가 이미 열어 뒀으면 그대로)
        yield return 1f;
        Place(p1, new Vector3(-8f, 0f, -5.5f));
        Place(father, new Vector3(-8f, 0f, -0.5f), 180f);
        ForgetAll();
        Say("⑥ 마지막: 열린 주방 문 너머로 탐정이 보입니다.");
        yield return 1.5f;
        ForgetAll();   // 자막을 보여 주는 동안 눈(감각)은 켜져 있었으므로 새로 시작
        father.enabled = true;
        father.StandStill(5f);
        for (float t = 0f; t < 3f && father.Current != FatherAI.Mode.Chase; t += 0.05f) yield return 0.05f;
        Say("⑥ 발견 → 추격!");
        yield return 0.3f;
        father.enabled = false;
        motor.Stop();
        Place(father, new Vector3(-11f, 0f, 1.5f), 0f);
        Place(p1, pantry.transform.position);
        pantry.TryEnter(p1);
        Say("⑥ 탐정은 아버지가 못 보는 사이에 팬트리에 숨었습니다. (경찰이었다면 숨는 장면을 못 봤으니 지나쳤을 상황)");
        yield return 3f;
        father.enabled = true;
        foreach (float s in Watch("⑥ 아버지는 놓친 곳 근처의 숨는 곳을 뒤집니다…", 25f, () => p1.IsCaught)) yield return s;

        Say("⑦ 팬트리를 열어 잡음 → 씬 재시작.\n시연 끝! 다음 단계(3-2)에서 잠긴 문 판단(우회·위장 후퇴·잠복·파괴)을 넣습니다.\n이제 직접 조작해 보세요: WASD · Shift · E · F · Tab");
        yield return recording ? 5f : 12f;
        ChaseDebugOverlay.Caption = "";
        caption = "";
    }
}
