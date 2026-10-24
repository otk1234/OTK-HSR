using UnityEngine;
using UnityEngine.Rendering;
namespace OTK.Midterm
{
    // Only foreground walls obstructing a sibling fade. Background walls stay intact.
    public sealed class MidtermWallFade : MonoBehaviour
    {
        public Transform[] actors;
        private Material material;
        private Collider wall;
        private Color tint;
        private MidtermDemo demo;
        private void Awake()
        {
            demo=FindFirstObjectByType<MidtermDemo>();
            wall=GetComponent<Collider>();material=GetComponent<Renderer>().material;tint=material.color;
            material.SetFloat("_Surface",1);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            material.SetOverrideTag("RenderType","Transparent");
        }
        private void LateUpdate()
        {
            var camera=Camera.main;if(camera==null)return;
            bool obstructs=false;
            if(demo!=null && demo.IsInspecting && demo.investigation!=null)
            {
                Vector3 delta=demo.investigation.position+Vector3.up*.7f-camera.transform.position;
                obstructs=wall.Raycast(new Ray(camera.transform.position,delta.normalized),out var inspectionHit,delta.magnitude-.1f);
            }
            foreach(var actor in actors)
            {
                Vector3 target=actor.position+Vector3.up;
                Vector3 delta=target-camera.transform.position;
                if(wall.Raycast(new Ray(camera.transform.position,delta.normalized),out var hit,delta.magnitude-.1f))obstructs=true;
            }
            tint.a=Mathf.MoveTowards(tint.a,obstructs?.12f:1,Time.deltaTime*2.5f);material.color=tint;
        }
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
