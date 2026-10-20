using UnityEngine;

namespace NarrativeGraybox.CameraSystem
{
    public sealed class OccludingWall : MonoBehaviour
    {
        [SerializeField] private Renderer wallRenderer;
        [SerializeField] private Renderer topRenderer;

        public void Configure(Renderer face, Renderer top)
        {
            wallRenderer = face;
            topRenderer = top;
        }

        public void SetVisible(bool visible)
        {
            if (wallRenderer != null) wallRenderer.enabled = visible;
            if (topRenderer != null) topRenderer.enabled = visible;
        }
    }
}
