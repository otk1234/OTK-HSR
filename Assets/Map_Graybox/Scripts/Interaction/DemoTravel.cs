using System.Collections;
using UnityEngine;
using NarrativeGraybox.Player;
using NarrativeGraybox.UI;
using NarrativeGraybox.CameraSystem;
namespace NarrativeGraybox.Interaction
{
    // Temporary rooms live separately in world space; player/inventory remain intact.
    public sealed class DemoTravel : MonoBehaviour
    {
        private PlayerInteractor3D actor;
        private SimpleGameHud hud;
        private IsometricPlayerController movement;
        private int region;
        private float cooldown, fade;
        public int Region => region;
        public void Build(PlayerInteractor3D player,SimpleGameHud gameHud)
        {
            actor=player;hud=gameHud;movement=player.GetComponent<IsometricPlayerController>();
            hud.SetLocation("1층");
            var bench=GameObject.Find("Foyer_Bench");
            if(bench!=null)bench.transform.position=new Vector3(-1.15f,.3f,5.75f);
            var npc=new GameObject("가정부");npc.transform.position=new Vector3(1.3f,1,1.2f);
            npc.AddComponent<DummyNpc>();var body=npc.AddComponent<CapsuleCollider>();body.height=2;body.radius=.35f;
            var visual=actor.GetComponentInChildren<SpriteCharacterVisual>();
            if(visual!=null)
            {
                var copy=Instantiate(visual.gameObject,npc.transform,false);
                copy.transform.localPosition=new Vector3(0,-1,0);
                copy.GetComponent<SpriteCharacterVisual>().SetMovementInput(Vector2.down);
                copy.GetComponent<SpriteRenderer>().color=new Color(.8f,.85f,.8f);
            }
            var floor=new Material(Shader.Find("Standard")){color=new Color(.46f,.47f,.44f)};
            var border=new Material(Shader.Find("Standard")){color=new Color(.3f,.32f,.33f)};
            var marker=new Material(Shader.Find("Standard")){color=new Color(.65f,.59f,.36f)};
            var upper=new GameObject("Temporary Second Floor").transform;upper.SetParent(transform);
            Box(upper,"2F Floor",new Vector3(50,-.25f,0),new Vector3(12,.5f,10),floor);
            Box(upper,"2F North",new Vector3(50,.6f,5),new Vector3(12,1.2f,.2f),border);
            Box(upper,"2F South",new Vector3(50,.6f,-5),new Vector3(12,1.2f,.2f),border);
            Box(upper,"2F West",new Vector3(44,.6f,0),new Vector3(.2f,1.2f,10),border);
            Box(upper,"2F East",new Vector3(56,.6f,0),new Vector3(.2f,1.2f,10),border);
            for(int i=0;i<3;i++)Box(upper,"Downstairs marker",new Vector3(50,.025f,-3.8f-i*.24f),new Vector3(1.8f,.05f,.15f),marker);
            var outside=new GameObject("Temporary Exterior").transform;outside.SetParent(transform);
            Box(outside,"Exterior Floor",new Vector3(100,-.25f,0),new Vector3(30,.5f,24),floor);
            Box(outside,"Exterior North",new Vector3(100,.5f,12),new Vector3(30,1,.2f),border);
            Box(outside,"Exterior South",new Vector3(100,.5f,-12),new Vector3(30,1,.2f),border);
            Box(outside,"Exterior West",new Vector3(85,.5f,0),new Vector3(.2f,1,24),border);
            Box(outside,"Exterior East",new Vector3(115,.5f,0),new Vector3(.2f,1,24),border);
            Box(outside,"Entrance Left",new Vector3(99,1.2f,-10),new Vector3(.2f,2.4f,.3f),marker);
            Box(outside,"Entrance Right",new Vector3(101,1.2f,-10),new Vector3(.2f,2.4f,.3f),marker);
            Box(outside,"Entrance Lintel",new Vector3(100,2.5f,-10),new Vector3(2.2f,.2f,.3f),marker);
            Box(transform,"Foyer Exit Landing",new Vector3(0,-.25f,6.65f),new Vector3(1.4f,.5f,1.1f),floor);
        }
        private static void Box(Transform root,string name,Vector3 position,Vector3 size,Material material)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name=name;box.transform.SetParent(root);
            box.transform.position=position;box.transform.localScale=size;box.GetComponent<Renderer>().sharedMaterial=material;
        }
        private void Update()
        {
            if(actor==null||actor.IsBusy||Time.unscaledTime<cooldown)return;
            Vector3 p=actor.transform.position;
            if(region==0 && new Bounds(new Vector3(8.4f,2.2f,-.98f),new Vector3(1.9f,3,.35f)).Contains(p))StartCoroutine(Travel(1));
            else if(region==0 && new Bounds(new Vector3(0,1,6.65f),new Vector3(1.35f,3,.4f)).Contains(p))StartCoroutine(Travel(2));
            else if(region==1 && new Bounds(new Vector3(50,1,-4.25f),new Vector3(1.9f,3,.6f)).Contains(p))StartCoroutine(Travel(0));
            else if(region==2 && new Bounds(new Vector3(100,1,-10),new Vector3(1.75f,3,.6f)).Contains(p))StartCoroutine(Travel(0));
        }
        private void LateUpdate()
        {
            if(actor==null||actor.IsBusy||hud.Selecting)return;
            Vector3 p=actor.transform.position;
            if(region==1&&Vector3.Distance(p,new Vector3(50,1,-4.25f))<2.4f)hud.SetInteractionPrompt("계단으로 내려가기");
            else if(region==2&&Vector3.Distance(p,new Vector3(100,1,-10))<3)hud.SetInteractionPrompt("현관으로 들어가기");
        }
        private IEnumerator Travel(int destination)
        {
            actor.ExternalLock=true;movement.SetMovementEnabled(false);hud.enabled=false;
            for(float t=0;t<.2f;t+=Time.unscaledDeltaTime){fade=t/.2f;yield return null;}
            fade=1;MoveTo(destination);yield return null;
            for(float t=0;t<.2f;t+=Time.unscaledDeltaTime){fade=1-t/.2f;yield return null;}
            fade=0;cooldown=Time.unscaledTime+.8f;hud.enabled=true;movement.SetMovementEnabled(true);actor.ExternalLock=false;
        }
        public void MoveTo(int destination)
        {
            Vector3 spawn=destination==1?new Vector3(50,1,-2.5f):destination==2?new Vector3(100,1,-8):region==1?new Vector3(8.4f,1,1.6f):new Vector3(0,1,5.05f);
            var cc=actor.GetComponent<CharacterController>();cc.enabled=false;actor.transform.position=spawn;cc.enabled=true;
            region=destination;Physics.SyncTransforms();Camera.main.GetComponent<IsometricCameraFollow>().SnapToTarget();
            hud.SetLocation(region==0?"1층":region==1?"2층":"바깥");
        }
        private void OnGUI()
        {
            if(fade<=0)return;Color old=GUI.color;int depth=GUI.depth;GUI.depth=-100;
            GUI.color=new Color(0,0,0,fade);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=old;GUI.depth=depth;
        }
    }
}
