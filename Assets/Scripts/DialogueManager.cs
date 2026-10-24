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

    public bool IsShowing => playing != null || InConversation;

    // ---------------- 대화 (한 줄씩 E/Space로 넘김) ----------------
    // 남매 대화·휴식 독백처럼 읽고 넘겨야 하는 대사. 잠금은 부르는 쪽이 함. 대화 중에 들어온 Say는 끝난 뒤에 보여 줌.

    public KeyCode[] advanceKeys = { KeyCode.E, KeyCode.Space };
    public bool InConversation { get; private set; }
    public string CurrentSpeaker => speakerText.text;
    public string CurrentLine => bodyText.text;

    public IEnumerator Conversation(IList<(string speaker, string text)> lines)
    {
        if (playing != null) { StopCoroutine(playing); playing = null; }
        InConversation = true;
        yield return null;   // 대화를 시작한 E가 첫 줄을 넘기지 않게
        yield return StartCoroutine(FadePanel(1f));
        foreach ((string speaker, string text) in lines)
        {
            speakerText.text = speaker;
            hintImage.gameObject.SetActive(false);
            bodyText.text = "";
            float shown = 0f;
            while (shown < text.Length)
            {
                if (AdvancePressed()) break;   // 누르면 문장 전체를 바로 보여 줌
                shown += charactersPerSecond * Time.unscaledDeltaTime;
                bodyText.text = text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(shown)));
                yield return null;
            }
            bodyText.text = text;
            yield return null;
            while (!AdvancePressed()) yield return null;
        }
        yield return StartCoroutine(FadePanel(0f));
        InConversation = false;
        if (queue.Count > 0) playing = StartCoroutine(PlayQueue());
    }

    // 점검·연출에서 키 대신 넘길 때
    [System.NonSerialized] public bool scriptedAdvance;

    bool AdvancePressed()
    {
        if (scriptedAdvance) { scriptedAdvance = false; return true; }
        foreach (KeyCode key in advanceKeys)
            if (Input.GetKeyDown(key)) return true;
        return false;
    }

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
        if (playing == null && !InConversation) playing = StartCoroutine(PlayQueue());
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
