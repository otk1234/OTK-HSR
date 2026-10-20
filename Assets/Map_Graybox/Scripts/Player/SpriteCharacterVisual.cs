using UnityEngine;

namespace NarrativeGraybox.Player
{
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteCharacterVisual : MonoBehaviour
    {
        [SerializeField] private Texture2D fourDirectionSheet;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 300f;
        [SerializeField, Min(1f)] private float walkFramesPerSecond = 8f;
        private SpriteRenderer spriteRenderer;
        private readonly Sprite[][] directionFrames = new Sprite[4][];
        private Vector2 lastDirection = Vector2.down;
        private bool isMoving;
        private float animationTime;
        private Vector3 restPosition;
        private float walkBlend;

        public void Configure(Texture2D sheet)
        {
            fourDirectionSheet = sheet;
            BuildSprites();
        }

        public void SetMovementInput(Vector2 input)
        {
            isMoving = input.sqrMagnitude >= 0.01f;
            if (!isMoving)
            {
                animationTime = 0f;
                ApplyDirection();
                return;
            }
            lastDirection = input;
            ApplyDirection();
        }

        private void OnEnable() { restPosition = transform.localPosition; BuildSprites(); }

        private void LateUpdate()
        {
            Camera targetCamera = Camera.main;
            if (targetCamera != null) transform.rotation = targetCamera.transform.rotation;
            if (Application.isPlaying)
            {
                walkBlend = Mathf.MoveTowards(walkBlend, isMoving ? 1 : 0, Time.deltaTime * 8);
                transform.localPosition = restPosition + Vector3.up * (Mathf.Abs(Mathf.Sin(animationTime * Mathf.PI * 4)) * .025f * walkBlend);
            }
            if (Application.isPlaying && isMoving)
            {
                animationTime += Time.deltaTime;
                ApplyDirection();
            }
        }

        private void BuildSprites()
        {
            if (fourDirectionSheet == null) return;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

            float cellWidth = fourDirectionSheet.width * 0.25f;
            float cellHeight = fourDirectionSheet.height * 0.25f;
            Vector2 pivot = new Vector2(0.5f, 0.08f);
            for (int direction = 0; direction < 4; direction++)
            {
                directionFrames[direction] = new Sprite[4];
                float y = (3 - direction) * cellHeight;
                for (int frame = 0; frame < 4; frame++)
                    directionFrames[direction][frame] = CreateCell(frame * cellWidth, y, cellWidth, cellHeight, pivot);
            }
            spriteRenderer.sortingOrder = 100;
            ApplyDirection();
        }

        private Sprite CreateCell(float x, float y, float width, float height, Vector2 pivot)
        {
            Sprite sprite = Sprite.Create(fourDirectionSheet, new Rect(x, y, width, height), pivot, pixelsPerUnit);
            sprite.name = "Runtime Direction Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void ApplyDirection()
        {
            if (spriteRenderer == null) return;
            int direction;
            if (Mathf.Abs(lastDirection.x) > Mathf.Abs(lastDirection.y))
                direction = lastDirection.x < 0f ? 2 : 3;
            else
                direction = lastDirection.y < 0f ? 0 : 1;

            int frame = isMoving ? Mathf.FloorToInt(animationTime * walkFramesPerSecond) % 4 : 0;
            if (directionFrames[direction] != null)
                spriteRenderer.sprite = directionFrames[direction][frame];
        }
    }
}
