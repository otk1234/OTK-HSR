using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// 아버지 AI 시연 (3-1·3-2·3-3단계): 메뉴 OTK → 아버지 AI 시연
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

        // 시연 화면(집 전체 + 표시 선)이 켜져 있으면 그 카메라로 찍음 (글자는 OnGUI라 안 찍힘)
        var demoView = Object.FindAnyObjectByType<ChaseDemoView>();
        Camera demoCamera = demoView != null ? demoView.GetComponent<Camera>() : null;
        if (demoCamera != null)
        {
            demoCamera.targetTexture = recordTarget;
            demoCamera.Render();
            demoCamera.targetTexture = null;
        }
        else recordCamera.Render();
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
        CameraTrigger Room(string name) => CameraTrigger.All.First(r => r.roomName == name);
        float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        string RoomOf(Component c) => CameraTrigger.FindAt(c.transform.position)?.roomName ?? "";

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
            // 멈춘 아버지가 문을 지나는 중이었으면 그 동작이 끝난 뒤 완전히 세움 (안 그러면 문을 다시 열고 지나감)
            for (int i = 0; i < 30 && motor.IsTraversing; i++) yield return 0.1f;
            if (!father.enabled) motor.Stop();
            for (int i = 0; i < 20 && door.door.IsMoving; i++) yield return 0.1f; // 움직이는 중이면 닫기가 무시됨
            if (door.door.IsOpen && !door.door.IsBroken) { door.door.Interact(inventory); yield return 1f; }
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
        ChaseDemoView.Enable(); // 게임 뷰: 집 전체 + 시야·가는 길·소리 범위·판단 표시

        Freeze();
        father.intuitionChance = 0f;   // 직감은 ⑤에서만
        father.searchTime = 4f;
        father.Reseed(7);
        Place(father, new Vector3(0f, 0f, -1.5f), 180f);
        Say("아버지 AI 시연 (3-1·3-2·3-3단계)\n창문·환풍구를 쓰고, 놓치면 예측해서 앞지르고, 숨는 곳을 뒤지는 고지능 추격자입니다.");
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
        Say("④ 동쪽 복도에서 여동생이 아버지 눈에 띕니다.");
        yield return 1.5f;
        ForgetAll();   // 자막을 보여 주는 동안 눈(감각)은 켜져 있었으므로 새로 시작
        father.enabled = true;
        father.StandStill(5f);
        for (float t = 0f; t < 3f && father.Current != FatherAI.Mode.Chase; t += 0.05f) yield return 0.05f;
        move1.scriptedRun = true;
        move1.scriptedInput = new Vector2(1f, 0f);
        Say("④ 발견 → 추격 상태! 여동생이 동쪽으로 달아납니다.");
        yield return 0.3f;
        move1.scriptedInput = null;
        move1.scriptedRun = false;
        Place(p1, new Vector3(-2f, 0f, 5f));    // 시야에서 사라짐 (시연용 순간이동, 잠긴 안방)
        foreach (float s in Watch("④ 여동생이 시야에서 사라짐 (시연용 순간이동).\n아버지는 잠깐(1.2초) 두리번거린 뒤, 마지막으로 본 곳을 지나 여동생이 달리던 방향(동쪽 끝)을 예측해 앞질러 갑니다. (씬 뷰 보라색 선)", 6f,
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
        Say("⑤ 직감: 조작 중인 여동생은 안방 옷장에 숨었고, 조작하지 않는 오빠는 세탁실에 서 있습니다.\n(실제 게임에서 직감은 20% 확률 · 35초에 최대 1번 · 숨은 캐릭터는 제외. 시연을 위해 이번엔 반드시 작동)");
        yield return 4f;
        father.intuitionChance = 1f;
        father.ClearIntuitionCooldown();
        father.enabled = true;
        father.ResetToProwl();
        father.intuitionChance = 0f;
        foreach (float s in Watch("⑤ 아버지가 '왠지' 세탁실 쪽으로 향합니다. 정확한 위치는 모르고 방까지만 갑니다.", 20f,
                     () => game.IsTargeted(p2), 0.02f)) yield return s;   // 잡기 전에 멈추도록 촘촘히 확인
        if (game.RestartPending) { Say("⑤ 오빠가 잡혀 씬이 재시작됩니다. 시연은 여기서 끝납니다."); yield return 4f; ChaseDebugOverlay.Caption = ""; yield break; }
        Freeze();
        Say("⑤ 조작하지 않던 오빠를 발견! (시연을 위해 여기서 멈춤)");
        yield return 3.5f;
        closet.Exit(p1);
        Place(p2, new Vector3(-1f, 0f, 5f));
        yield return game.chaseEndDelay + 0.5f;

        // ⑥ 잠긴 문 판단 (3-2단계). 두 캐릭터는 잠긴 안방에서 대기, 소리는 아이방 안에서
        EntryPoint kidDoor = Entry("아이방 문"), masterDoor = Entry("안방 문");
        Vector3 westHall = new Vector3(-11f, 0f, -1.5f), kidInside = new Vector3(-10f, 0f, 5f);
        Vector3 kidDoorMid = (kidDoor.PointA + kidDoor.PointB) * 0.5f;
        Place(p1, new Vector3(-2f, 0f, 5f));
        foreach (float s in SetLock(masterDoor, true)) yield return s;
        foreach (float s in SetLock(kidDoor, true)) yield return s;
        IEnumerable<float> SoundInKidRoom(DoorTactic tactic)
        {
            Place(father, westHall, 0f);
            father.forceDoorTactic = tactic;
            ForgetAll();
            father.enabled = true;
            father.StandStill(1f);
            yield return 0.3f;
            NoiseSystem.Emit(kidInside, 20f, NoiseKind.Run, p1.gameObject);
            yield return 0.1f;
        }
        Say("⑥ 잠긴 문 판단 (3-2단계): 아이방 문이 잠겨 있고 안에서 소리가 납니다. 아버지는 먼저 문 앞에서 손잡이를 돌려 봅니다.\n(실제로는 우회 45% · 위장 후퇴 25% · 잠복 30% 중 확률로 고름. 시연에서는 하나씩 보여 줌)");
        yield return 5f;

        foreach (float s in SoundInKidRoom(DoorTactic.Ambush)) yield return s;
        foreach (float s in Watch("⑥-a 잠복: 문 옆에 붙어 조용히 기다립니다.", 15f,
                     () => father.Current == FatherAI.Mode.Ambush && Flat(father.transform.position, kidDoorMid) < 2.2f)) yield return s;
        foreach (float s in Watch("⑥-a 잠복: 문 옆에 붙어 조용히 기다립니다… (문 너머에서 소리가 나면 더 오래 기다림)", 3f)) yield return s;
        foreach (float s in SetLock(kidDoor, false)) yield return s;
        foreach (float s in Watch("⑥-a 떠난 줄 알고 문을 열면 → 바로 들이닥칩니다!", 4f)) yield return s;
        Freeze();
        foreach (float s in SetLock(kidDoor, true)) yield return s;

        foreach (float s in SoundInKidRoom(DoorTactic.Detour)) yield return s;
        foreach (float s in Watch("⑥-b 우회: 문 대신 서쪽 복도의 환풍구로 조용히 기어 들어갑니다 (잠입 중엔 통과 소음이 35%).", 25f,
                     () => RoomOf(father) == "아이방")) yield return s;
        foreach (float s in Watch("⑥-b 아이방 안에 들어왔습니다. 문은 여전히 잠겨 있습니다.", 2.5f)) yield return s;
        Freeze();

        father.feignDetourChance = 1f;
        foreach (float s in SoundInKidRoom(DoorTactic.FeignRetreat)) yield return s;
        foreach (float s in Watch("⑥-c 위장 후퇴: 보란 듯이 문에서 멀어져 포기한 척 서 있다가…", 15f,
                     () => father.Current == FatherAI.Mode.Detour || father.Current == FatherAI.Mode.Ambush)) yield return s;
        foreach (float s in Watch("⑥-c …몰래 환풍구로 돌아 들어갑니다. (반반 확률로, 몰래 돌아와 문 옆에 잠복하기도 함)", 25f,
                     () => RoomOf(father) == "아이방")) yield return s;
        father.feignDetourChance = 0.5f;
        Freeze();
        foreach (float s in SetLock(kidDoor, false)) yield return s;

        father.forceDoorTactic = DoorTactic.None;
        Place(p1, new Vector3(6f, 0f, 4.5f));   // 두 캐릭터는 서재로
        Place(p2, new Vector3(7f, 0f, 4f));
        father.hasWeapon = true;
        Say("⑥-d 안방은 우회로가 없습니다. 무기가 없으면 위장 후퇴·잠복만 하지만,\n무기가 있으면(지금은 테스트용 체크박스) 최후의 수단으로 문을 부숩니다.");
        yield return 4f;
        Place(father, new Vector3(0f, 0f, -1.5f), 0f);
        ForgetAll();
        father.enabled = true;
        father.StandStill(1f);
        yield return 0.3f;
        NoiseSystem.Emit(new Vector3(-2f, 0f, 5f), 20f, NoiseKind.Run, p1.gameObject);
        foreach (float s in Watch("⑥-d 쾅! 쾅! (한 번에 25, 내구도 100)", 15f, () => masterDoor.door.IsBroken)) yield return s;
        foreach (float s in Watch("⑥-d 문이 부서졌습니다 → 들어가 확인.", 3f)) yield return s;
        father.hasWeapon = false;
        Freeze();
        yield return game.chaseEndDelay + 0.5f;

        // ⑦ 학습 (3-3단계): 플레이어가 빠져나간 방법을 기억했다가 그걸 막는 쪽을 우선
        //    실제로는 아버지가 추격·문 앞에서 포기할 때마다 쌓임. 시연에서는 기록을 미리 넣고 효과를 보여 줌
        Say("⑦ 학습 (3-3단계): 아버지는 추격을 포기할 때마다, 플레이어가 어떻게 빠져나갔는지(잠근 문·숨은 곳·도망친 경로)를 기억합니다.\n같은 수법을 2번 쓰면 그걸 막는 쪽을 먼저 고릅니다. 잡혀서 재시작돼도 기억은 남습니다.");
        yield return 6f;

        // ⑦-a 문
        foreach (float s in SetLock(kidDoor, true)) yield return s;
        FatherMemory.RecordDoorEscape(kidDoor);
        FatherMemory.RecordDoorEscape(kidDoor);
        Say("⑦-a (시연을 위해 기록을 미리 넣음) '아이방 문을 잠그고 버텨서 2번 빠져나감'.\n이제 이 문 앞에서는 위장 후퇴보다 우회·잠복을 더 자주 고르고(막는 판단 76% → 86%), 배회할 때 아이방을 더 자주 들릅니다.");
        yield return 5f;
        foreach (float s in SoundInKidRoom(DoorTactic.None)) yield return s;
        foreach (float s in Watch("⑦-a 다시 아이방에서 소리 → 학습한 문 앞에서의 판단 (분홍 글자 = 학습한 것)", 12f,
                     () => father.Current == FatherAI.Mode.Detour || father.Current == FatherAI.Mode.Ambush || father.Current == FatherAI.Mode.FeignRetreat)) yield return s;
        foreach (float s in Watch("⑦-a 판단 결과", 4f)) yield return s;
        Freeze();
        foreach (float s in SetLock(kidDoor, false)) yield return s;

        // ⑦-b 숨는 곳
        HidingSpot toyBoxLearned = Spot("장난감 상자");
        FatherMemory.RecordHideEscape(toyBoxLearned);
        FatherMemory.RecordHideEscape(toyBoxLearned);
        Vector3 westCenter = new Vector3(-8f, 0f, 0f);
        Place(father, westCenter, 0f);
        father.enabled = true;
        father.StandStill(10f);
        Say("⑦-b (기록을 미리 넣음) '장난감 상자에 숨어서 2번 넘김'.\n서쪽 복도에서 수색을 시작하면, 평소 후보 반경(6m) 밖인 장난감 상자(약 8m)도 가장 먼저 뒤집니다.");
        yield return 4f;
        father.SearchSpotsAround(westCenter);
        foreach (float s in Watch("⑦-b 자주 숨은 곳부터 확인 (초록 원)", 15f,
                     () => Flat(father.transform.position, toyBoxLearned.transform.position) < 1.5f)) yield return s;
        foreach (float s in Watch("⑦-b 장난감 상자를 열어 봄. (배회하다 아이방에 들러도 35% 확률로 열어 봄)", 3f)) yield return s;
        Freeze();

        // ⑦-c 도망 경로
        FatherMemory.RecordRouteEscape(Room("서쪽 복도"), Room("주방"));
        FatherMemory.RecordRouteEscape(Room("서쪽 복도"), Room("주방"));
        Place(father, new Vector3(-10f, 0f, 1.5f), 160f);
        Place(p1, new Vector3(-9f, 0f, -1.5f));
        ForgetAll();
        Say("⑦-c (기록을 미리 넣음) '서쪽 복도에서 주방으로 도망쳐 2번 따돌림'.\n서쪽 복도에서 여동생을 발견했다가 놓치면…");
        yield return 4f;
        ForgetAll();
        father.enabled = true;
        father.StandStill(5f);
        for (float t = 0f; t < 3f && father.Current != FatherAI.Mode.Chase; t += 0.05f) yield return 0.05f;
        yield return 0.4f;
        Place(p1, new Vector3(6f, 0f, 4.5f));   // 시야에서 사라짐 (시연용 순간이동, 서재)
        foreach (float s in Watch("⑦-c 여동생이 사라짐 → 두리번 → 늘 도망치던 주방 쪽 길목(주방 문 안쪽)으로 먼저 갑니다.", 12f,
                     () => father.Current == FatherAI.Mode.Intercept && motor.Arrived(0.6f))) yield return s;
        foreach (float s in Watch("⑦-c 길목에서 기다리며 확인", 3f)) yield return s;
        Freeze();
        yield return game.chaseEndDelay + 0.5f;

        // ⑧ 피날레: 숨는 장면을 못 봤어도 놓친 곳 근처를 뒤져 잡음
        HidingSpot pantry = Spot("팬트리");
        foreach (float s in SetLock(Entry("주방 문"), false)) yield return s;
        if (!Entry("주방 문").door.IsOpen) Entry("주방 문").door.Interact(inventory);   // 열어 둠 (아버지가 이미 열어 뒀으면 그대로)
        yield return 1f;
        Place(p1, new Vector3(-8f, 0f, -5.5f));
        Place(father, new Vector3(-8f, 0f, -0.5f), 180f);
        ForgetAll();
        Say("⑧ 마지막: 열린 주방 문 너머로 여동생이 보입니다.");
        yield return 1.5f;
        ForgetAll();   // 자막을 보여 주는 동안 눈(감각)은 켜져 있었으므로 새로 시작
        father.enabled = true;
        father.StandStill(5f);
        for (float t = 0f; t < 3f && father.Current != FatherAI.Mode.Chase; t += 0.05f) yield return 0.05f;
        Say("⑧ 발견 → 추격!");
        yield return 0.3f;
        father.enabled = false;
        motor.Stop();
        Place(father, new Vector3(-11f, 0f, 1.5f), 0f);
        Place(p1, pantry.transform.position);
        pantry.TryEnter(p1);
        Say("⑧ 여동생은 아버지가 못 보는 사이에 팬트리에 숨었습니다. (경찰이었다면 숨는 장면을 못 봤으니 지나쳤을 상황)");
        yield return 3f;
        father.enabled = true;
        foreach (float s in Watch("⑧ 아버지는 놓친 곳 근처의 숨는 곳을 뒤집니다…", 25f, () => p1.IsCaught)) yield return s;

        Say("⑨ 팬트리를 열어 잡음 → 씬 재시작.\n시연 끝! 학습한 기록은 잡혀서 재시작돼도 남습니다.\n이제 직접 조작해 보세요: WASD · Shift · E · F · Tab");
        yield return recording ? 5f : 12f;
        ChaseDebugOverlay.Caption = "";
        caption = "";
    }
}
