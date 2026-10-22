using UnityEngine;

namespace NarrativeGraybox.Interaction
{
    public sealed class FocusableObject : MonoBehaviour
    {
        [SerializeField] private string prompt = "단말기 조사";
        [SerializeField] private Transform focusPoint;
        [SerializeField] private Transform cameraAnchor;
        [SerializeField] private Transform lookTarget;
        [SerializeField] private Vector3 localFrontDirection = Vector3.back;
        [SerializeField, Min(0.5f)] private float cameraDistance = 4.2f;
        [SerializeField] private float cameraHeight = 1.2f;
        [SerializeField, Min(0.5f)] private float focusedViewSize = 2.6f;

        public string Prompt => prompt;
        public Transform FocusPoint => focusPoint == null ? transform : focusPoint;
        public Vector3 CameraPosition => cameraAnchor == null
            ? ViewCenter + FrontDirection * cameraDistance + Vector3.up * cameraHeight
            : cameraAnchor.position;
        public Vector3 LookPosition => lookTarget == null ? ViewCenter : lookTarget.position;
        public Vector3 ViewCenter
        {
            get
            {
                Transform screen = transform.Find("Screen");
                return screen == null ? FocusPoint.position : screen.position;
            }
        }

        public Vector3 FrontDirection
        {
            get
            {
                Transform screen = transform.Find("Screen");
                if (screen != null)
                {
                    Vector3 towardScreen = Vector3.ProjectOnPlane(screen.position - transform.position, Vector3.up);
                    if (towardScreen.sqrMagnitude > 0.0001f) return towardScreen.normalized;
                }

                return transform.TransformDirection(localFrontDirection).normalized;
            }
        }
        public float CameraDistance => cameraDistance;
        public float CameraHeight => cameraHeight;
        public float FocusedViewSize => focusedViewSize;

        public void ConfigureFocusView(Vector3 frontDirection, float distance, float height, float viewSize)
        {
            localFrontDirection = frontDirection.normalized;
            cameraDistance = distance;
            cameraHeight = height;
            focusedViewSize = viewSize;
        }

        public void ConfigureFocusView(Transform newCameraAnchor, Transform newLookTarget, float viewSize)
        {
            cameraAnchor = newCameraAnchor;
            lookTarget = newLookTarget;
            focusedViewSize = viewSize;
        }
    }
}
