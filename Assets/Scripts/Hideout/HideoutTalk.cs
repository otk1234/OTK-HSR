using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 아지트 대화 공통: 대사 한 줄(누가 말하는지 + 문장)과, 대화 중 남매 둘 다 이동·입력·전환을 잠그는 것.
// 대사는 기존 대사 패널(DialogueManager)에 한 줄씩, E/Space로 넘김.
[Serializable]
public class TalkLine
{
    public CharacterType speaker = CharacterType.Player1;
    [TextArea] public string text = "";
}

[Serializable]
public class Talk
{
    public TalkLine[] lines = new TalkLine[0];
}

public static class HideoutTalk
{
    public static bool Playing { get; private set; }

    public static IEnumerator Play(IEnumerable<TalkLine> lines)
    {
        var shown = new List<(string, string)>();
        foreach (TalkLine line in lines)
            if (line != null && !string.IsNullOrEmpty(line.text)) shown.Add((CharacterRoster.NameOf(line.speaker), line.text));
        if (shown.Count == 0 || DialogueManager.instance == null) yield break;

        Playing = true;
        Lock(true);
        yield return DialogueManager.instance.StartCoroutine(DialogueManager.instance.Conversation(shown));
        Lock(false);
        Playing = false;
    }

    public static void Lock(bool locked)
    {
        if (CharacterManager.instance == null) return;
        CharacterManager.instance.SwitchLocks.Set(LockReason.Scripted, locked);
        foreach (GameObject player in CharacterManager.instance.players)
        {
            player.GetComponent<PlayerInteraction>().Locks.Set(LockReason.Scripted, locked);
            player.GetComponent<PlayerMovement>().Locks.Set(LockReason.Scripted, locked);
        }
    }

    // 사건 기록이 하나라도 있으면 "사건 뒤" 대화를 고름
    public static bool AnyCaseRecorded => CaseFile.CaseIds.Count > 0 || CaseState.Answers.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Playing = false;
}
