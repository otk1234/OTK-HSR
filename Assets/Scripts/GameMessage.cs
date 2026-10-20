using System;
using UnityEngine;

// 게임 시스템 → 화면(대사 패널·HUD)으로 메시지를 보내는 통로.
// 화면 쪽이 없는 씬(SampleScene 등)에서는 기존처럼 콘솔 로그만 남습니다.
public static class GameMessage
{
    public static event Action<string, Sprite> Shown;  // 캐릭터 독백·단서 문구 (힌트 이미지 선택)
    public static event Action<string> PromptChanged;  // "[E] 문 열기" 같은 상호작용 안내

    public static void Show(string message, Sprite hint = null)
    {
        Debug.Log(message);
        Shown?.Invoke(message, hint);
    }

    public static void Prompt(string message)
    {
        PromptChanged?.Invoke(message);
    }
}
