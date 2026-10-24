using UnityEditor;
using UnityEditor.SceneManagement;

// Unity를 새로 열 때 개발자에게 기획 참고 씬이 먼저 보이도록 합니다.
// 배치 빌드와 플레이 중에는 작동하지 않으며, 한 에디터 세션에서 한 번만 실행됩니다.
[InitializeOnLoad]
public static class PlannerReferenceDefaultScene
{
    private const string ScenePath = "Assets/PlannerReference/Midterm/Scenes/Midterm_Town.unity";
    private const string SessionKey = "OTK.PlannerReference.DefaultSceneOpened";

    static PlannerReferenceDefaultScene()
    {
        if (UnityEngine.Application.isBatchMode || SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += OpenReferenceScene;
    }

    private static void OpenReferenceScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
        if (EditorSceneManager.GetActiveScene().path == ScenePath) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
