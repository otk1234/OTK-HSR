using UnityEngine;

namespace NarrativeGraybox.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class IsometricPlayerController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField] private Transform cameraTransform;
        private CharacterController controller;
        private SpriteCharacterVisual spriteVisual;
        private bool movementEnabled = true;
        private float verticalSpeed;

        public void SetCamera(Transform targetCamera) => cameraTransform = targetCamera;
        public void SetMovementEnabled(bool enabled) => movementEnabled = enabled;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            spriteVisual = GetComponentInChildren<SpriteCharacterVisual>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed + Physics.gravity.y * Time.deltaTime, -25f);
            if (!movementEnabled)
            {
                if (spriteVisual != null) spriteVisual.SetMovementInput(Vector2.zero);
                controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
                return;
            }

            float horizontal = 0f;
            float vertical = 0f;
            if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D)) horizontal += 1f;
            if (Input.GetKey(KeyCode.S)) vertical -= 1f;
            if (Input.GetKey(KeyCode.W)) vertical += 1f;

            Vector2 input = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
            if (spriteVisual != null) spriteVisual.SetMovementInput(input);
            if (cameraTransform == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            Vector3 movement = forward * input.y + right * input.x;
            controller.Move((movement * moveSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);

            if (movement.sqrMagnitude > 0.001f) transform.forward = movement.normalized;
        }
    }
}
