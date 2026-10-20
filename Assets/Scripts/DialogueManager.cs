using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 화면 하단 대사 패널: 캐릭터 독백·단서 문구를 타이핑 효과로 보여 줍니다.
// 이동을 막지 않는 방식이라, 말하는 동안에도 계속 움직일 수 있습니다.
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager instance;

    [Header("UI 연결")]
    public CanvasGroup panel;
    public Text speakerText;
    public Text bodyText;
    public Image hintImage;          // 힌트 이미지가 있을 때만 켜짐

    [Header("연출")]
    public float charactersPerSecond = 40f;
    public float holdTime = 2.2f;    // 다 쓴 뒤 화면에 남아 있는 시간
    public float minReadTime = 0.8f; // 다음 대사가 밀려 있을 때 최소로 보여 주는 시간
    public float fadeTime = 0.15f;

    private struct Line
    {
        public string speaker, text;
        public Sprite hint;
    }

    private readonly Queue<Line> queue = new Queue<Line>();
    private Coroutine playing;

    public bool IsShowing => playing != null;

    void Awake()
    {
        instance = this;
        panel.alpha = 0f;
        hintImage.gameObject.SetActive(false);
    }

    public void Say(string speaker, string text, Sprite hint = null)
    {
        // 같은 문구 연타(잠긴 문 반복 등)는 쌓지 않습니다.
        foreach (Line queued in queue)
            if (queued.text == text) return;
        queue.Enqueue(new Line { speaker = speaker, text = text, hint = hint });
        if (playing == null) playing = StartCoroutine(PlayQueue());
    }

    private IEnumerator PlayQueue()
    {
        yield return StartCoroutine(FadePanel(1f));

        while (queue.Count > 0)
        {
            Line line = queue.Dequeue();
            speakerText.text = line.speaker;
            hintImage.sprite = line.hint;
            hintImage.gameObject.SetActive(line.hint != null);

            // 타이핑 효과
            bodyText.text = "";
            float shown = 0f;
            while (shown < line.text.Length)
            {
                shown += charactersPerSecond * Time.unscaledDeltaTime;
                bodyText.text = line.text.Substring(0, Mathf.Min(line.text.Length, Mathf.FloorToInt(shown)));
                if (queue.Count > 0) shown += charactersPerSecond * Time.unscaledDeltaTime; // 다음 대사가 밀려 있으면 빠르게
                yield return null;
            }
            bodyText.text = line.text;

            // 다음 대사가 기다리고 있으면 최소한 읽을 시간만 주고 넘어갑니다.
            for (float t = 0f; t < holdTime; t += Time.unscaledDeltaTime)
            {
                if (queue.Count > 0 && t >= minReadTime) break;
                yield return null;
            }
        }

        yield return StartCoroutine(FadePanel(0f));
        playing = null;
    }

    private IEnumerator FadePanel(float target)
    {
        float start = panel.alpha;
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            panel.alpha = Mathf.Lerp(start, target, t / fadeTime);
            yield return null;
        }
        panel.alpha = target;
    }
}
