using UnityEngine;
namespace NarrativeGraybox.Interaction
{
    [RequireComponent(typeof(FocusableObject))]
    public sealed class InspectionArea : MonoBehaviour
    {
        public string Title = "서재 책상 조사";
        public InspectionItem[] Items;
        public FocusableObject Focus => GetComponent<FocusableObject>();
        public Transform[] ViewTargets()
        {
            var points=new Transform[Items.Length+1];points[0]=transform;
            for(int i=0;i<Items.Length;i++)points[i+1]=Items[i].transform;
            return points;
        }
    }
}
