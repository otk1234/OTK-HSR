using UnityEngine;

// 페이드 도중 다른 구역에 들어가 카메라 전환이 건너뛰어진 경우를 바로잡는 안전장치.
// (프로토타입 씬은 PrototypeIntegration이 같은 일을 하므로 필요 없음)
public class RoomCameraSync : MonoBehaviour
{
    void LateUpdate()
    {
        if (CharacterManager.instance == null) return;
        if (FadeManager.instance != null && FadeManager.instance.isFading) return;
        GameObject active = CharacterManager.instance.GetActivePlayer();
        foreach (CameraTrigger room in CameraTrigger.All)
            if (room.virtualCamera.activeSelf != room.Contains(active)) room.UpdateCameraStateInstant();
    }
}
