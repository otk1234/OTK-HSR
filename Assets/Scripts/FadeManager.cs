using UnityEngine;
using System.Collections;

public class FadeManager : MonoBehaviour
{
    public static FadeManager instance;

    [Header("페이드 화면 (Canvas Group)")]
    public CanvasGroup fadePanel;
    public float fadeTime = 0.5f;
    public bool isFading = false;

    void Awake() { instance = this; }

    void OnEnable() { GameStateManager.StateChanged += OnGameStateChanged; }
    void OnDisable() { GameStateManager.StateChanged -= OnGameStateChanged; }

    // 추격이 시작되면 진행 중인 페이드를 바로 끊습니다 (페이드 동안 플레이어만 멈춰 있지 않도록).
    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Chase) CancelFade();
    }

    public void CancelFade()
    {
        StopAllCoroutines();
        fadePanel.alpha = 0f;
        isFading = false;
        // 페이드 도중 건너뛴 구역 카메라 전환을 바로 맞춤
        if (CharacterManager.instance != null)
            foreach (CameraTrigger room in CameraTrigger.All) room.UpdateCameraStateInstant();
    }

    // 추격 중에는 페이드 없이 바로 넘어감
    static bool Instant => GameStateManager.InstantCameraCuts;


    public void DoFadeTransition(GameObject targetCamera)
    {
        if (!isFading) StartCoroutine(RoomTransitionCoroutine(targetCamera));
    }

    private IEnumerator RoomTransitionCoroutine(GameObject targetCamera)
    {
        isFading = true;
        yield return StartCoroutine(FadeOut());

        targetCamera.SetActive(true);
        yield return new WaitForSeconds(0.2f);

        yield return StartCoroutine(FadeIn());
        isFading = false;
    }


    // 다른 스크립트(캐릭터 전환·조사 카메라 복귀)가 직접 돌리는 페이드도, 추격이 시작되면 그 자리에서 끝납니다.
    public IEnumerator FadeOut()
    {
        float timer = 0f;
        while (timer < fadeTime && !Instant)
        {
            timer += Time.deltaTime;
            fadePanel.alpha = timer / fadeTime;
            yield return null;
        }
        fadePanel.alpha = Instant ? 0f : 1f;
    }

    public IEnumerator FadeIn()
    {
        float timer = 0f;
        while (timer < fadeTime && !Instant)
        {
            timer += Time.deltaTime;
            fadePanel.alpha = 1f - (timer / fadeTime);
            yield return null;
        }
        fadePanel.alpha = 0f;
    }
}
