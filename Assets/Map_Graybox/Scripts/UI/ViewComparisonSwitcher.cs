using UnityEngine;
using UnityEngine.SceneManagement;

namespace NarrativeGraybox.UI
{
    public sealed class ViewComparisonSwitcher : MonoBehaviour
    {
        private static readonly string[] Scenes = { "Test_TopView", "Test_Isometric_2F", "Test_ThreeQuarter" };
        private static readonly string[] Labels = { "F1  탑뷰", "F2  아이소메트릭 · 2층", "F3  3/4 · 30도" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSwitcher()
        {
            if (Object.FindFirstObjectByType<PrototypeIntegration>() != null) return; // [OTK 통합] 프로토타입 씬에서는 시점 비교 버튼 숨김
            if (Object.FindFirstObjectByType<ViewComparisonSwitcher>() == null)
                new GameObject("View Comparison — F1 F2 F3").AddComponent<ViewComparisonSwitcher>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) Switch(0);
            if (Input.GetKeyDown(KeyCode.F2)) Switch(1);
            if (Input.GetKeyDown(KeyCode.F3)) Switch(2);
        }

        public void Switch(int index)
        {
            if (index < 0 || index >= Scenes.Length) return;
            if (SceneManager.GetActiveScene().name != Scenes[index]) SceneManager.LoadScene(Scenes[index]);
        }

        private void OnGUI()
        {
            Matrix4x4 old = GUI.matrix;
            float scale = Mathf.Max(0.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float x = (Screen.width / scale - 576) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = SceneManager.GetActiveScene().name != Scenes[i];
                if (GUI.Button(new Rect(x + i * 192, 24, 184, 36), Labels[i])) Switch(i);
                GUI.enabled = enabled;
            }
            GUI.matrix = old;
        }
    }
}
