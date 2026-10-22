using UnityEngine;
using UnityEngine.SceneManagement;
using NarrativeGraybox.UI;
namespace NarrativeGraybox.Interaction
{
    // Runtime-only dressing keeps saved scenes and their original lighting intact.
    public sealed class FlashlightDemo : MonoBehaviour
    {
        private SimpleGameHud hud;
        private PlayerInteractor3D actor;
        private NarrativeGraybox.CameraSystem.PixelVision vision;
        private float radius = .22f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
        }
        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            var area = Object.FindFirstObjectByType<InspectionArea>();
            if (area == null || Object.FindFirstObjectByType<FlashlightDemo>() != null) return;
            if (Object.FindFirstObjectByType<PrototypeIntegration>() != null) return; // [OTK 통합] 프로토타입 씬에서는 데모 연출을 만들지 않음
            new GameObject("Flashlight Demo").AddComponent<FlashlightDemo>();
        }
        private void Start()
        {
            hud = Object.FindFirstObjectByType<SimpleGameHud>();
            actor = Object.FindFirstObjectByType<PlayerInteractor3D>();
            var area = Object.FindFirstObjectByType<InspectionArea>();
            foreach (var item in area.Items)
            {
                if(item.Title=="가족사진") item.Description="네 사람이 함께 찍은 사진.";
                if(item.Title=="탁상시계") item.Description="9시에 멈춘 시계.";
                if(item.Title=="황동 열쇠") item.Description="창고 열쇠.";
                if(item.Title=="접힌 메모") item.Description="‘오늘 저녁, 창고에서.’";
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name="손전등"; go.transform.SetParent(area.transform);
            go.transform.position=new Vector3(8.03f,.94f,2.98f);
            go.transform.localScale=new Vector3(.16f,.13f,.38f);
            go.GetComponent<BoxCollider>().isTrigger=true;
            var material=new Material(Shader.Find("Standard"));material.color=new Color(.17f,.2f,.23f);
            go.GetComponent<Renderer>().material=material;
            var lens=GameObject.CreatePrimitive(PrimitiveType.Cube);
            lens.transform.SetParent(go.transform,false);lens.transform.localPosition=new Vector3(0,0,-.52f);
            lens.transform.localScale=new Vector3(1.12f,1.12f,.08f);
            var glass=new Material(Shader.Find("Standard"));glass.color=new Color(.9f,.86f,.58f);lens.GetComponent<Renderer>().material=glass;
            var shell=GameObject.CreatePrimitive(PrimitiveType.Cube);shell.transform.SetParent(go.transform,false);
            shell.transform.localScale=new Vector3(1.18f,1.18f,1.1f);Destroy(shell.GetComponent<Collider>());
            var stroke=new Material(Shader.Find("NarrativeGraybox/InspectionStroke"));stroke.color=new Color(1,.83f,.18f);
            shell.GetComponent<Renderer>().material=stroke;
            var flashlight=go.AddComponent<InspectionItem>();flashlight.Configure("손전등","장착하면 시야가 넓어진다.",true,new[]{shell.GetComponent<Renderer>()});
            var items=new InspectionItem[area.Items.Length+1];area.Items.CopyTo(items,0);items[items.Length-1]=flashlight;area.Items=items;
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.type==LightType.Directional)light.intensity*=.45f;
            vision=Camera.main.GetComponent<NarrativeGraybox.CameraSystem.PixelVision>();
            if(vision==null)vision=Camera.main.gameObject.AddComponent<NarrativeGraybox.CameraSystem.PixelVision>();
            vision.Actor=actor;
            GrayboxSolidObjects.Apply();
            gameObject.AddComponent<DemoTravel>().Build(actor,hud);
        }
        private void Update()
        {
            radius=Mathf.Lerp(radius,hud!=null&&hud.FlashlightEquipped?.46f:.22f,1-Mathf.Exp(-5*Time.deltaTime));
            if(vision!=null)vision.Radius=radius;
        }
    }
}
