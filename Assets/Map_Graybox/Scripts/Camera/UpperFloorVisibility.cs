using UnityEngine;

namespace NarrativeGraybox.CameraSystem
{
    // The upper floor remains solid but reveals the lower floor while the player is underneath.
    public sealed class UpperFloorVisibility : MonoBehaviour
    {
        [SerializeField] private Transform player;
        private Renderer[] surfaces;
        public void Configure(Transform value) => player = value;
        private void Awake() => surfaces = GetComponentsInChildren<Renderer>();
        private void LateUpdate()
        {
            if (player == null) return;
            Vector3 p = player.position;
            bool underneath = p.y < 5f && p.x > 2.5f && p.x < 13.5f && p.z > 1.5f && p.z < 10.5f;
            foreach (Renderer surface in surfaces) surface.enabled = !underneath;
        }
    }
}
