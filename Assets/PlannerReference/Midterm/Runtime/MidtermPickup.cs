using UnityEngine;

namespace OTK.Midterm
{
    public sealed class MidtermPickup : MonoBehaviour
    {
        public string itemId;
        public string itemName;
        [TextArea(2, 6)] public string description;
        [Min(1)] public int slots = 1;
        public bool capturesEvidence;
        [Min(.5f)] public float interactionDistance = 2.2f;
    }
}
