using UnityEngine;

namespace OTK.Midterm
{
    // ID is the branching key. Display text can change without invalidating knowledge.
    [CreateAssetMenu(menuName = "OTK/Midterm/Clue")]
    public sealed class MidtermClue : ScriptableObject
    {
        public string id;
        public string place = "골목";
        public string incident = "쓰러진 남자";
        [TextArea] public string description;
    }
}
