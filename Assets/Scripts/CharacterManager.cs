using System.Collections;
using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    // 싱글톤: 다른 스크립트에서 쉽게 접근할 수 있도록 자신을 저장합니다.
    public static CharacterManager instance;

    [Header("플레이어 캐릭터들")]
    public GameObject[] players; // 캡슐 2개를 여기에 넣을 겁니다.

    // 현재 조종 중인 캐릭터의 인덱스 (0 = 첫 번째, 1 = 두 번째)
    private int activePlayerIndex = 0;

    void Awake()
    {
        // 게임 시작 시 자신을 등록합니다.
        instance = this;
    }

    void Start()
    {
        // 시작할 때 첫 번째 캐릭터(0번)에게 조작 권한을 줍니다.
        SwitchPlayer(0);
    }

    [System.Obsolete]
    void Update()
    {

        if (FadeManager.instance != null && FadeManager.instance.isFading) return;

        // Tab 키를 누르면 다음 캐릭터로 전환합니다.
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // 0이면 1로, 1이면 0으로 바뀝니다.
            int nextIndex = (activePlayerIndex + 1) % players.Length;
            StartCoroutine(SwitchWithFadeCoroutine(nextIndex));
        }
    }

    [System.Obsolete]
    private IEnumerator SwitchWithFadeCoroutine(int nextIndex)
    {
        FadeManager.instance.isFading = true;


        yield return StartCoroutine(FadeManager.instance.FadeOut());


        SwitchPlayer(nextIndex);


        CameraTrigger[] triggers = FindObjectsByType<CameraTrigger>(FindObjectsSortMode.None);
        foreach (var t in triggers)
        {
            t.UpdateCameraStateInstant();
        }


        yield return null;


        yield return new WaitForSeconds(0.2f);


        yield return StartCoroutine(FadeManager.instance.FadeIn());
        FadeManager.instance.isFading = false;
    }

    void SwitchPlayer(int index)
    {
        activePlayerIndex = index;

        for (int i = 0; i < players.Length; i++)
        {

            PlayerMovement movement = players[i].GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.enabled = (i == activePlayerIndex);
            }
        }
    }


    public GameObject GetActivePlayer()
    {
        return players[activePlayerIndex];
    }
}