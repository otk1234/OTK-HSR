using UnityEngine;

namespace OTK.Midterm
{
    public sealed class MidtermInspectable : MonoBehaviour
    {
        public string title;
        [TextArea(2, 12)] public string[] lines;
        public MidtermClue[] clues;
        private GameObject outline;
        private MeshFilter[] surfaces;
        public void Highlight(bool on)
        {
            if (on && outline == null)
            {
                // Outline-only selection frame; does not change the object's materials.
                surfaces=GetComponentsInChildren<MeshFilter>();
                outline = new GameObject("Hover stroke");
                outline.transform.SetParent(transform);
                var line = outline.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = line.endColor = new Color(1, .82f, .35f);
                line.startWidth = line.endWidth = .045f;
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = 4;
            }
            if (outline != null) outline.SetActive(on);
            if(on)FitStroke();
        }
        private void LateUpdate(){if(outline!=null && outline.activeSelf)FitStroke();}
        private void FitStroke()
        {
            var cam=Camera.main;if(cam==null)return;
            float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue,depth=float.MaxValue;
            foreach(var mesh in surfaces)foreach(var vertex in mesh.sharedMesh.vertices)
            {
                Vector3 p=cam.WorldToViewportPoint(mesh.transform.TransformPoint(vertex));
                minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);depth=Mathf.Min(depth,p.z);
            }
            float px=1.5f/cam.pixelWidth,py=1.5f/cam.pixelHeight;depth=Mathf.Max(cam.nearClipPlane+.01f,depth-.05f);
            var stroke=outline.GetComponent<LineRenderer>();stroke.startWidth=stroke.endWidth=cam.orthographicSize*2/cam.pixelHeight;
            stroke.SetPositions(new[]{cam.ViewportToWorldPoint(new Vector3(minX-px,minY-py,depth)),cam.ViewportToWorldPoint(new Vector3(minX-px,maxY+py,depth)),cam.ViewportToWorldPoint(new Vector3(maxX+px,maxY+py,depth)),cam.ViewportToWorldPoint(new Vector3(maxX+px,minY-py,depth))});
        }
        private void OnDestroy()
        {
            if (outline != null) Destroy(outline.GetComponent<LineRenderer>().sharedMaterial);
        }
    }
}
