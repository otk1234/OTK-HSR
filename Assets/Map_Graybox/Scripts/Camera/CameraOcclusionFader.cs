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
        private readonly HashSet<OccludingWall> wallsThisFrame = new HashSet<OccludingWall>();
        private Transform[] inspectionTargets;
        public void SetInspectionTargets(Transform[] targets) => inspectionTargets = targets;

        public void Configure(Transform newTarget) => target = newTarget;

        private void LateUpdate() => RefreshVisibility();
        public void RefreshVisibility()
        {
            wallsThisFrame.Clear();
            if (target == null)
            {
                ApplyVisibility();
                return;
            }

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

            ApplyVisibility();
        }

        private void TraceTo(Vector3 point)
        {
            Camera camera = GetComponent<Camera>();
            Vector3 origin = camera.transform.position;
            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance <= .08f) return;

            // 얇은 벽의 모서리를 스치거나 카메라가 이동 중이어도 안정적으로 감지하도록
            // 한 줄짜리 Raycast 대신 작은 반지름의 SphereCast를 사용합니다.
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, .22f, toPoint / distance,
                         distance - .08f, obstructionLayers, QueryTriggerInteraction.Ignore))
                Hide(hit.collider.GetComponentInParent<OccludingWall>());
        }

        private void Hide(OccludingWall wall)
        {
            if (wall != null) wallsThisFrame.Add(wall);
        }

        private void ApplyVisibility()
        {
            foreach (OccludingWall wall in hiddenWalls)
                if (wall != null && !wallsThisFrame.Contains(wall)) wall.SetOccluded(false);

            foreach (OccludingWall wall in wallsThisFrame)
                if (wall != null && !hiddenWalls.Contains(wall)) wall.SetOccluded(true);

            hiddenWalls.Clear();
            hiddenWalls.UnionWith(wallsThisFrame);
        }

        private void OnDisable()
        {
            foreach (OccludingWall wall in hiddenWalls)
                if (wall != null) wall.SetOccluded(false, true);
            hiddenWalls.Clear();
            wallsThisFrame.Clear();
        }
    }
}
