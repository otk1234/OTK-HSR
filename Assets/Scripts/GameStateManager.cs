using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public enum GameState { Investigation, Chase }

// 평소 추리 상태 ↔ 추격 상태를 관리합니다.
// 추격 상태: 방 카메라 즉시 컷(페이드 없음), Tab 전환도 즉시 컷, 조사·대화 시작 금지.
// 씬에 없으면 항상 추리 상태로 취급하므로, 기존 씬(프로토타입·SampleScene)은 그대로 동작합니다.
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager instance;

    public static GameState State => instance != null ? instance.state : GameState.Investigation;
    public static bool IsChase => State == GameState.Chase;
    public static bool InstantCameraCuts => IsChase;

    public static event Action<GameState> StateChanged;
    public static event Action<CharacterStatus> PlayerCaught;

    [Header("마지막 추격자가 포기한 뒤 추리 상태로 돌아가기까지 (초)")]
    public float chaseEndDelay = 1.5f;

    [Header("잡힌 뒤 씬 재시작까지 (초) — 잡힘 연출은 나중에")]
    public float restartDelay = 2f;

    private GameState state = GameState.Investigation;
    private readonly Dictionary<Object, List<CharacterStatus>> chases = new Dictionary<Object, List<CharacterStatus>>();
    private bool forcedChase;
    private float noChaserSince = -1f;

    public bool RestartPending { get; private set; }

    void Awake() { instance = this; }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // 추격자가 대상을 쫓기 시작/대상 변경
    public void BeginChase(Object chaser, IEnumerable<CharacterStatus> targets)
    {
        chases[chaser] = new List<CharacterStatus>(targets);
        SetState(GameState.Chase);
    }

    // 추격자가 대상을 놓치고 포기
    public void EndChase(Object chaser)
    {
        chases.Remove(chaser);
    }

    // 이벤트로 추격을 시작/끝낼 때 (나중에 연출용)
    public void ForceChase(bool on)
    {
        forcedChase = on;
        if (on) SetState(GameState.Chase);
    }

    public bool IsChasing(Object chaser) => chases.ContainsKey(chaser);

    public bool IsTargeted(CharacterStatus character)
    {
        foreach (List<CharacterStatus> targets in chases.Values)
            if (targets.Contains(character)) return true;
        return false;
    }

    public IReadOnlyList<CharacterStatus> TargetsOf(Object chaser) =>
        chases.TryGetValue(chaser, out List<CharacterStatus> targets) ? targets : (IReadOnlyList<CharacterStatus>)Array.Empty<CharacterStatus>();

    public void ReportCaught(CharacterStatus character, Object byChaser)
    {
        if (character == null || character.IsCaught) return;
        character.SetCaught();
        Debug.Log($"[추격] {character.name} 잡힘 ({(byChaser != null ? byChaser.name : "?")})");
        PlayerCaught?.Invoke(character);
        if (!RestartPending) StartCoroutine(RestartScene());
    }

    IEnumerator RestartScene()
    {
        RestartPending = true;
        yield return new WaitForSeconds(restartDelay);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else Debug.LogWarning("[추격] 빌드 설정에 없는 씬이라 다시 시작할 수 없습니다: " + scene.path);
    }

    void Update()
    {
        // 사라진 추격자 정리
        var gone = new List<Object>();
        foreach (Object chaser in chases.Keys)
            if (chaser == null) gone.Add(chaser);
        foreach (Object chaser in gone) chases.Remove(chaser);

        if (state != GameState.Chase) return;
        if (chases.Count > 0 || forcedChase)
        {
            noChaserSince = -1f;
            return;
        }
        if (noChaserSince < 0f) noChaserSince = Time.time;
        if (Time.time - noChaserSince >= chaseEndDelay) SetState(GameState.Investigation);
    }

    void SetState(GameState next)
    {
        if (state == next) return;
        state = next;
        noChaserSince = -1f;
        Debug.Log("[게임 상태] " + (next == GameState.Chase ? "추격" : "추리"));
        StateChanged?.Invoke(next);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        StateChanged = null;
        PlayerCaught = null;
    }
}
