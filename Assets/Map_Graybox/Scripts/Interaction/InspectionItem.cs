using UnityEngine;
namespace NarrativeGraybox.Interaction
{
    public sealed class InspectionItem : MonoBehaviour
    {
        public string Title;
        [TextArea] public string Description;
        public bool Collectible;
        [SerializeField] private Renderer[] outlines;
        public void Configure(string title, string description, bool collectible, Renderer[] stroke)
        {Title=title;Description=description;Collectible=collectible;outlines=stroke;SetHover(false);}
        public void SetHover(bool active)
        {if(outlines!=null)foreach(var r in outlines)if(r!=null)r.enabled=active;}
        private void OnDisable()=>SetHover(false);
    }
}
