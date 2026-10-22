using UnityEngine;
using NarrativeGraybox.Interaction;

namespace NarrativeGraybox.CameraSystem
{
    public sealed class IsometricCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(-10f, 12f, -10f);
        [SerializeField, Min(0.01f)] private float smoothTime = 0.14f;
        [SerializeField, Min(0.05f)] private float transitionDuration = 0.55f;
        private Vector3 velocity;
        private FocusableObject focusTarget;
        private Quaternion normalRotation;
        private Camera controlledCamera;
        private float normalViewSize;
        private bool returningToPlayer;
        private float transitionStartedAt;
        private Vector3 transitionStartPosition;
        private Quaternion transitionStartRotation;
        private float transitionStartViewSize;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            normalRotation = transform.rotation;
            if (controlledCamera != null) normalViewSize = controlledCamera.orthographicSize;
        }

        public bool IsFocusing => focusTarget != null;
        public void SnapToTarget()
        {
            focusTarget=null;returningToPlayer=false;velocity=Vector3.zero;
            if(target!=null)transform.position=target.position+offset;
            transform.rotation=normalRotation;
            if(controlledCamera!=null)controlledCamera.orthographicSize=normalViewSize;
        }

        public void Configure(Transform newTarget, Vector3 newOffset)
        {
            target = newTarget;
            offset = newOffset;
            transform.position = target.position + offset;
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
            normalRotation = transform.rotation;
            controlledCamera = GetComponent<Camera>();
            if (controlledCamera != null) normalViewSize = controlledCamera.orthographicSize;
        }

        public void FocusOn(FocusableObject newFocusTarget)
        {
            focusTarget = newFocusTarget;
            velocity = Vector3.zero;
            returningToPlayer = false;
            BeginTransition();
        }

        public void ExitFocus()
        {
            focusTarget = null;
            velocity = Vector3.zero;
            returningToPlayer = true;
            BeginTransition();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            if (focusTarget != null)
            {
                ApplyFocusedView();
                return;
            }

            Vector3 desired = target.position + offset;
            if (returningToPlayer)
            {
                float progress = TransitionProgress();
                transform.position = Vector3.Lerp(transitionStartPosition, desired, progress);
                transform.rotation = Quaternion.Slerp(transitionStartRotation, normalRotation, progress);
                if (controlledCamera != null)
                    controlledCamera.orthographicSize = Mathf.Lerp(transitionStartViewSize, normalViewSize, progress);
                if (progress >= 1f) returningToPlayer = false;
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, normalRotation, 7f * Time.deltaTime);
            if (controlledCamera != null)
                controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize,
                    normalViewSize, 7f * Time.deltaTime);
        }

        private void ApplyFocusedView()
        {
            if (focusTarget == null) return;

            Vector3 lookPoint = focusTarget.LookPosition;
            Vector3 focusPosition = focusTarget.CameraPosition;
            Quaternion focusRotation = Quaternion.LookRotation(lookPoint - focusPosition, Vector3.up);
            float progress = TransitionProgress();
            transform.position = Vector3.Lerp(transitionStartPosition, focusPosition, progress);
            transform.rotation = Quaternion.Slerp(transitionStartRotation, focusRotation, progress);
            if (controlledCamera != null)
                controlledCamera.orthographicSize = Mathf.Lerp(transitionStartViewSize,
                    focusTarget.FocusedViewSize, progress);
        }

        private void BeginTransition()
        {
            transitionStartedAt = Time.unscaledTime;
            transitionStartPosition = transform.position;
            transitionStartRotation = transform.rotation;
            transitionStartViewSize = controlledCamera == null ? normalViewSize : controlledCamera.orthographicSize;
        }

        private float TransitionProgress()
        {
            float linear = Mathf.Clamp01((Time.unscaledTime - transitionStartedAt) / transitionDuration);
            return linear * linear * (3f - 2f * linear);
        }
    }
}
