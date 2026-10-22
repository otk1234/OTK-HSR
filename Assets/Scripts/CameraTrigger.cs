using System.Collections.Generic;
using UnityEngine;

public class CameraTrigger : MonoBehaviour
{
    [Header("이 구역의 가상 카메라")]
    public GameObject virtualCamera;

    [Header("구역 이름 (HUD 위치 표시용)")]
    public string roomName;

    private List<GameObject> playersInRoom = new List<GameObject>();

    public bool Contains(GameObject player) => playersInRoom.Contains(player);

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !playersInRoom.Contains(other.gameObject))
        {
            playersInRoom.Add(other.gameObject);

            if (CharacterManager.instance.GetActivePlayer() == other.gameObject)
            {
                FadeManager.instance.DoFadeTransition(virtualCamera);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && playersInRoom.Contains(other.gameObject))
        {
            playersInRoom.Remove(other.gameObject);
            // 조작 중인 캐릭터가 아직 이 구역에 있으면 카메라를 끄지 않습니다.
            UpdateCameraStateInstant();
        }
    }


    public void UpdateCameraStateInstant()
    {
        bool isActivePlayerHere = playersInRoom.Contains(CharacterManager.instance.GetActivePlayer());
        virtualCamera.SetActive(isActivePlayerHere);
    }

}
