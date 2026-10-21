using System;
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

    // 조사·대화 중처럼 캐릭터를 바꾸면 안 될 때, 이유별로 잠급니다.
    public readonly LockSet SwitchLocks = new LockSet();

    // 예전 방식 호환용 (읽으면 어떤 이유로든 잠겨 있는지)
    public bool inputLocked
    {
        get => SwitchLocks.IsLocked;
        set => SwitchLocks.Set(LockReason.Legacy, value);
    }

    public int ActivePlayerIndex => activePlayerIndex;

    public event Action<GameObject> ActivePlayerChanged;

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
        if (SwitchLocks.IsLocked) return;

        // Tab 키를 누르면 다음 캐릭터로 전환합니다.
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // 0이면 1로, 1이면 0으로 바뀝니다.
            int nextIndex = (activePlayerIndex + 1) % players.Length;
            if (GameStateManager.InstantCameraCuts) SwitchInstant(nextIndex); // 추격 중에는 페이드 없이 바로 컷
            else StartCoroutine(SwitchWithFadeCoroutine(nextIndex));
        }
    }

    [System.Obsolete]
    private IEnumerator SwitchWithFadeCoroutine(int nextIndex)
    {
        FadeManager.instance.isFading = true;


        yield return StartCoroutine(FadeManager.instance.FadeOut());


        SwitchPlayer(nextIndex);
        RefreshRoomCameras();


        yield return null;


        yield return new WaitForSeconds(0.2f);


        yield return StartCoroutine(FadeManager.instance.FadeIn());
        FadeManager.instance.isFading = false;
    }

    // 페이드 없이 바로 전환 (추격 상태)
    public void SwitchInstant(int index)
    {
        SwitchPlayer(index);
        RefreshRoomCameras();
    }

    static void RefreshRoomCameras()
    {
        foreach (CameraTrigger t in CameraTrigger.All)
        {
            t.UpdateCameraStateInstant();
        }
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

        ActivePlayerChanged?.Invoke(players[activePlayerIndex]);
    }


    public GameObject GetActivePlayer()
    {
        return players[activePlayerIndex];
    }
}
