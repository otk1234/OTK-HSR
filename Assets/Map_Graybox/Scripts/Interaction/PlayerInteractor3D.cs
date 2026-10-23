using UnityEngine;
using NarrativeGraybox.CameraSystem;
using NarrativeGraybox.Player;
using NarrativeGraybox.UI;
namespace NarrativeGraybox.Interaction
{
    public sealed class PlayerInteractor3D : MonoBehaviour
    {
        [SerializeField] private IsometricCameraFollow cameraController;
        [SerializeField] private IsometricPlayerController playerController;
        [SerializeField] private SimpleGameHud hud;
        private InspectionArea area;
        private InspectionItem hovered, selected;
        private float unlockedAt;
        private string notice;
        private Renderer[] playerRenderers;
        private bool[] rendererStates;
        private DummyNpc talkingTo;
        public bool ExternalLock { get; set; }
        public bool IsBusy => IsInspecting || talkingTo != null || ExternalLock;
        public bool IsInspecting => area != null;
        public InspectionItem Selected => selected;
        // [OTK 통합] true를 돌려주면(근처에 아이템·문이 있음) 조사/대화 입력을 양보한다.
        public System.Func<bool> HasPriorityInteraction;
        // [OTK 통합] 이동을 NavMesh 시스템이 맡는 경우 playerController가 비어 있을 수 있다.
        private void SetPlayerMovement(bool enabled){if(playerController!=null)playerController.SetMovementEnabled(enabled);}
        public void Configure(IsometricCameraFollow cameraFollow, IsometricPlayerController movement, SimpleGameHud gameHud)
        {cameraController=cameraFollow;playerController=movement;hud=gameHud;}
        private void Update()
        {
            if(ExternalLock)return;
            if(talkingTo!=null)
            {
                if(Input.GetKeyDown(KeyCode.E)||Input.GetKeyDown(KeyCode.Q)||Input.GetKeyDown(KeyCode.Escape))EndTalk();
                return;
            }
            if(area==null)
            {
                if(HasPriorityInteraction!=null&&HasPriorityInteraction())return; // [OTK 통합]
                DummyNpc npc=null;float npcDistance=1.8f*1.8f;
                foreach(var candidate in Object.FindObjectsByType<DummyNpc>(FindObjectsSortMode.None))
                {
                    float d=(candidate.transform.position-transform.position).sqrMagnitude;
                    if(d>=npcDistance)continue;
                    Vector3 origin=transform.position;
                    if(Physics.Linecast(origin,candidate.transform.position,out var obstruction,~0,QueryTriggerInteraction.Ignore)
                        && obstruction.collider.GetComponentInParent<DummyNpc>()!=candidate)continue;
                    npc=candidate;npcDistance=d;
                }
                if(npc!=null&&!hud.Selecting)
                {
                    hud.SetInteractionPrompt("[E] "+npc.DisplayName);
                    if(Input.GetKeyDown(KeyCode.E))BeginTalk(npc);
                    return;
                }
                InspectionArea nearest=null;float closest=2.4f*2.4f;
                foreach(var candidate in Object.FindObjectsByType<InspectionArea>(FindObjectsSortMode.None))
                {
                    float distance=(candidate.transform.position-transform.position).sqrMagnitude;
                    if(distance<closest&&!CanSeeArea(candidate))continue; // [OTK 통합] 벽·닫힌 문 너머 조사 지점은 제외
                    if(distance<closest){nearest=candidate;closest=distance;}
                }
                hud.SetInteractionPrompt(hud.Selecting?"[← →] 선택   [R] 장착   [Q] 취소":nearest==null?"[R] 소지품":"[E] 조사   [R] 소지품");
                if(hud.Selecting)return;
                if(nearest!=null&&Input.GetKeyDown(KeyCode.E)) Enter(nearest);
                return;
            }
            if(Time.unscaledTime<unlockedAt)return;
            if(Input.GetKeyDown(KeyCode.Q)||Input.GetKeyDown(KeyCode.Escape)){Back();return;}
            if(Input.GetKeyDown(KeyCode.E)&&selected!=null){CollectSelected();return;}
            InspectionItem hitItem=null;float nearestHit=float.PositiveInfinity;
            if(!PanelRect().Contains(new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)))
            {
                Ray ray=Camera.main.ScreenPointToRay(Input.mousePosition);
                foreach(var hit in Physics.RaycastAll(ray,100,~0,QueryTriggerInteraction.Collide))
                {
                    var item=hit.collider.GetComponentInParent<InspectionItem>();
                    if(item!=null&&System.Array.IndexOf(area.Items,item)>=0&&hit.distance<nearestHit)
                    {hitItem=item;nearestHit=hit.distance;}
                }
            }
            if(hovered!=hitItem){if(hovered!=null)hovered.SetHover(false);hovered=hitItem;if(hovered!=null)hovered.SetHover(true);}
            if(Input.GetMouseButtonDown(0)&&hovered!=null)Select(hovered);
        }
        private bool CanSeeArea(InspectionArea candidate) // [OTK 통합] 대화(NPC)와 같은 방식의 가림 판정
        {
            if(!Physics.Linecast(transform.position,candidate.transform.position+Vector3.up*.5f,out var obstruction,~0,QueryTriggerInteraction.Ignore))return true; // [OTK 통합]
            return obstruction.collider.GetComponentInParent<InspectionArea>()==candidate||obstruction.collider.CompareTag("Player"); // [OTK 통합]
        }
        public void BeginTalk(DummyNpc npc)
        {
            if(npc==null||IsBusy)return;
            talkingTo=npc;SetPlayerMovement(false);hud.enabled=false; // [OTK 통합]
        }
        public void EndTalk()
        {
            talkingTo=null;SetPlayerMovement(true);hud.enabled=true; // [OTK 통합]
        }
        public void Enter(InspectionArea target)
        {
            area=target;selected=null;notice="";unlockedAt=Time.unscaledTime+.65f;
            SetPlayerMovement(false);hud.enabled=false; // [OTK 통합]
            playerRenderers=GetComponentsInChildren<Renderer>();rendererStates=new bool[playerRenderers.Length];
            for(int i=0;i<playerRenderers.Length;i++){rendererStates[i]=playerRenderers[i].enabled;playerRenderers[i].enabled=false;}
            cameraController.FocusOn(area.Focus);
            cameraController.GetComponent<CameraOcclusionFader>().SetInspectionTargets(area.ViewTargets());
        }
        public void Select(InspectionItem item)
        {if(area==null||item==null||!item.gameObject.activeSelf||System.Array.IndexOf(area.Items,item)<0)return;selected=item;notice="";}
        public bool CollectSelected()
        {
            if(selected==null||!selected.Collectible)return false;
            if(!hud.TryAddItem(selected.Title)){notice=hud.InventoryFullMessage;return false;} // [OTK 통합] 캐릭터 칸 수 기준 문구
            notice=selected.Title+" 획득";selected.SetHover(false);selected.gameObject.SetActive(false);selected=null;hovered=null;
            return true;
        }
        public void Back()
        {
            if(selected!=null){selected=null;notice="";return;}
            if(hovered!=null)hovered.SetHover(false);hovered=null;area=null;
            cameraController.GetComponent<CameraOcclusionFader>().SetInspectionTargets(null);
            cameraController.ExitFocus();SetPlayerMovement(true);hud.enabled=true;hud.SetInteractionPrompt(""); // [OTK 통합]
            if(playerRenderers!=null)for(int i=0;i<playerRenderers.Length;i++)if(playerRenderers[i]!=null)playerRenderers[i].enabled=rendererStates[i];
        }
        private float UiScale => Mathf.Max(.35f, Mathf.Min(Screen.width/1280f,Screen.height/720f));
        private Rect PanelRect()=>new Rect(Screen.width*.2f,Screen.height-175*UiScale,Screen.width*.6f,155*UiScale);
        private void OnGUI()
        {
            if(talkingTo!=null)
            {
                float scale=UiScale;Rect r=PanelRect();Color previous=GUI.color;
                GUI.color=new Color(.035f,.045f,.06f,.97f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;
                var text=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(10,Mathf.RoundToInt(20*scale)),wordWrap=true};text.normal.textColor=Color.white;
                GUI.Label(new Rect(r.x+18*scale,r.y+12*scale,r.width-36*scale,30*scale),talkingTo.DisplayName,text);
                GUI.Label(new Rect(r.x+18*scale,r.y+48*scale,r.width-36*scale,55*scale),talkingTo.Line,text);
                GUI.Label(new Rect(r.x+18*scale,r.y+112*scale,r.width-36*scale,30*scale),"[E / Q] 닫기",text);
                GUI.color=previous;return;
            }
            if(area==null)return;
            float s=UiScale;
            var style=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(9,Mathf.RoundToInt(18*s)),wordWrap=true};style.normal.textColor=Color.white;
            var panel=PanelRect();Color old=GUI.color;GUI.color=new Color(.035f,.045f,.06f,.96f);GUI.DrawTexture(panel,Texture2D.whiteTexture);GUI.color=old;
            string title=selected==null?area.Title:selected.Title;
            string body=selected==null?"":selected.Description;
            string action=selected==null?"[클릭] 조사   [Q] 돌아가기":selected.Collectible?"[E] 획득   [Q] 돌아가기":"[Q] 돌아가기";
            GUI.Label(new Rect(panel.x+18*s,panel.y+10*s,panel.width-36*s,28*s),title,style);
            GUI.Label(new Rect(panel.x+18*s,panel.y+42*s,panel.width-36*s,60*s),body,style);
            GUI.Label(new Rect(panel.x+18*s,panel.y+108*s,panel.width-36*s,40*s),string.IsNullOrEmpty(notice)?action:notice+"   "+action,style);
            if(hovered!=null)GUI.Label(new Rect(20*s,95*s,350*s,35*s),hovered.Title,style);
        }
    }
}
