using System.Collections.Generic;
using UnityEngine;

namespace NarrativeGraybox.CameraSystem
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraOcclusionFader : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private LayerMask obstructionLayers = ~0;
        private readonly HashSet<OccludingWall> hiddenWalls = new HashSet<OccludingWall>();
        private Transform[] inspectionTargets;
        public void SetInspectionTargets(Transform[] targets) => inspectionTargets = targets;

        public void Configure(Transform newTarget) => target = newTarget;

        private void LateUpdate() => RefreshVisibility();
        public void RefreshVisibility()
        {
            foreach (OccludingWall wall in hiddenWalls)
                if (wall != null) wall.SetVisible(true);
            hiddenWalls.Clear();
            if (target == null) return;

            if (inspectionTargets != null)
            {
                foreach (Transform point in inspectionTargets)
                    if (point != null && point.gameObject.activeInHierarchy) TraceTo(point.position);
            }
            else
            {
                TraceTo(target.position + Vector3.up * .55f);
                TraceTo(target.position - Vector3.up * .65f);
            }
        }

        private void TraceTo(Vector3 point)
        {
            Camera camera = GetComponent<Camera>();
            Vector3 screen = camera.WorldToViewportPoint(point);
            if (screen.z <= 0) return;
            Ray ray = camera.ViewportPointToRay(screen);
            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Max(0, distance - .08f),
                         obstructionLayers, QueryTriggerInteraction.Ignore))
                Hide(hit.collider.GetComponent<OccludingWall>());
        }

        private void Hide(OccludingWall wall)
        {
            if (wall == null || !hiddenWalls.Add(wall)) return;
            wall.SetVisible(false);
        }

        private void OnDisable()
        {
            foreach (OccludingWall wall in hiddenWalls)
                if (wall != null) wall.SetVisible(true);
            hiddenWalls.Clear();
        }
    }
}
