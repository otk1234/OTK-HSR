using UnityEngine;

namespace NarrativeGraybox.Interaction
{
    public sealed class PickupItem : MonoBehaviour
    {
        [SerializeField] private string itemName = "낡은 열쇠";
        [SerializeField] private string prompt = "낡은 열쇠 줍기";

        public string ItemName => itemName;
        public string Prompt => prompt;

        public void Configure(string newItemName, string newPrompt)
        {
            itemName = newItemName;
            prompt = newPrompt;
        }
    }
}
