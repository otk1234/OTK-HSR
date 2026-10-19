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


    public IEnumerator FadeOut()
    {
        float timer = 0f;
        while (timer < fadeTime)
        {
            timer += Time.deltaTime;
            fadePanel.alpha = timer / fadeTime;
            yield return null;
        }
        fadePanel.alpha = 1f;
    }

    public IEnumerator FadeIn()
    {
        float timer = 0f;
        while (timer < fadeTime)
        {
            timer += Time.deltaTime;
            fadePanel.alpha = 1f - (timer / fadeTime);
            yield return null;
        }
        fadePanel.alpha = 0f;
    }
}