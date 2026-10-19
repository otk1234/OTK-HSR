using System.Collections.Generic;
using UnityEngine;

public class CameraTrigger : MonoBehaviour
{
    [Header("이 구역의 가상 카메라")]
    public GameObject virtualCamera;

    private List<GameObject> playersInRoom = new List<GameObject>();

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
            virtualCamera.SetActive(false);
        }
    }


    public void UpdateCameraStateInstant()
    {
        bool isActivePlayerHere = playersInRoom.Contains(CharacterManager.instance.GetActivePlayer());
        virtualCamera.SetActive(isActivePlayerHere);
    }

}