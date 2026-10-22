using UnityEngine;
using NarrativeGraybox.Interaction;
namespace NarrativeGraybox.CameraSystem
{
    // Camera post-processing happens before HUD/IMGUI, so it cannot darken UI.
    [RequireComponent(typeof(Camera))]
    public sealed class PixelVision : MonoBehaviour
    {
        public PlayerInteractor3D Actor;
        public float Radius = .22f;
        [Range(180,720)] public int VerticalResolution = 360;
        [Range(4,16)] public int ColorSteps = 8;
        private float inspectionBlend;
        private Material material;
        private void Update()
        {
            float target=Actor!=null&&Actor.IsInspecting?1f:0f;
            inspectionBlend=Mathf.MoveTowards(inspectionBlend,target,Time.unscaledDeltaTime/0.55f);
        }
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if(Actor==null){Graphics.Blit(source,destination);return;}
            if(material==null)
            {
                var shader=Resources.Load<Shader>("PixelVision");
                if(shader==null){Graphics.Blit(source,destination);return;}
                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            Vector3 p=GetComponent<Camera>().WorldToViewportPoint(Actor.transform.position+Vector3.up*.65f);
            Vector2 center=Vector2.Lerp(new Vector2(p.x,p.y),new Vector2(.5f,.52f),inspectionBlend);
            material.SetVector("_Center",new Vector4(center.x,center.y,0,0));
            material.SetFloat("_Radius",Mathf.Lerp(Radius,.68f,inspectionBlend));
            material.SetFloat("_Inspection",inspectionBlend);
            material.SetFloat("_PixelSize",Mathf.Max(1,Mathf.Round(source.height/(float)VerticalResolution)));
            material.SetFloat("_ColorSteps",ColorSteps);
            material.SetVector("_Resolution",new Vector4(source.width,source.height,0,0));
            Graphics.Blit(source,destination,material);
        }
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
