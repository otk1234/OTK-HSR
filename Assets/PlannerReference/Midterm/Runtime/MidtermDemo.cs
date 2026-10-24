using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using NarrativeGraybox.Player;

namespace OTK.Midterm
{
    // Independent presentation sandbox. No dependency on the developer's gameplay managers.
    public sealed class MidtermDemo : MonoBehaviour
    {
        public string location;
        public Transform[] actors;
        public Transform investigation;
        public Transform dog;
        public AudioClip bark;
        public Texture2D characterSheet;
        public string leftScene, rightScene;
        public float boundary = 30;
        public bool isAlley;
        public MidtermClue[] catalog;
        private sealed class ActorState
        {
            public HashSet<string> knowledge = new HashSet<string>();
            public List<Item> items = new List<Item>();
            public string left = "", right = "";
            public string map;
        }
        public sealed class Item
        {
            public string id, name;
            public int slots;
            public bool capturesEvidence;
        }
        private static ActorState[] states;
        private static int active;
        private static bool introductionSeen;
        private static string arrival;
        private static HashSet<string> collectedItems;
        private CharacterController[] controllers;
        private MidtermDummyVisual[] visuals;
        private List<Vector3> trail = new List<Vector3>();
        private Camera cam;
        private Camera outputCamera;
        private RenderTexture pixels;
        private bool inspecting, panel, unread, menu, map, selecting;
        private int slot, tab;
        private string[] dialogue;
        private int line;
        private MidtermInspectable pending, hover;
        private MidtermPickup nearbyPickup, pickupPrompt;
        private GUIStyle label;
        private Vector2 informationScroll;
        private AudioSource dogAudio;
        private float nextBark;
        private string notice = "";
        private float noticeUntil;
        public static readonly string[] Introduction = {
            "여동생 : 이게... 이게 뭐야?", "오빠 : 죽은 거야?", "여동생 : 모르겠어...",
            "오빠 : 경찰에 신고하자. 너 응급처치 같은 건 못해? 심폐소생술 같은 거 있잖아.",
            "여동생 : 그걸 지금 왜 써!", "오빠 : 아무튼!", "여동생 : 일단 상태를 좀 보자."
        };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { states = null; active = 0; introductionSeen = false; arrival = null; collectedItems = null; }
        public bool CanSwitch => actors[0].gameObject.activeInHierarchy && actors[1].gameObject.activeInHierarchy && actors[0].gameObject.scene == actors[1].gameObject.scene && states[0].map == states[1].map;
        public bool IsInspecting => inspecting;
        public bool Knows(int actor, string id) => states[actor].knowledge.Contains(id);
        public int UsedSlots(int actor) => states[actor].items.Sum(i => i.slots);
        public int Capacity(int actor) => actor == 0 ? 5 : 6;
        public bool HasItem(int actor, string id) => states[actor].items.Exists(i => i.id == id);
        public bool TryAddItem(int actor, Item item)
        {
            if (item == null || item.slots < 1 || UsedSlots(actor) + item.slots > Capacity(actor)) return false;
            states[actor].items.Add(item); return true;
        }
        public void GrantClue(MidtermClue clue)
        {
            if (clue == null) return;
            bool added = states[active].knowledge.Add(clue.id);
            if (CanSwitch) added |= states[1-active].knowledge.Add(clue.id);
            if (added) unread = true;
        }
        public void SwitchActor()
        {
            if (!CanSwitch) return;
            active = 1-active;
            trail.Clear(); // Positions stay unchanged; the newly selected actor leads from here.
            trail.Add(actors[active].position);
        }
        private void Awake()
        {
            if (states == null) states = new[] { new ActorState(), new ActorState() };
            if (collectedItems == null) collectedItems = new HashSet<string>();
            string scene = gameObject.scene.name;
            foreach (var s in states) s.map = scene;
            controllers = new CharacterController[2];
            visuals = new MidtermDummyVisual[2];
            for (int i=0;i<2;i++)
            {
                controllers[i] = actors[i].GetComponent<CharacterController>();
                visuals[i] = actors[i].GetComponentInChildren<MidtermDummyVisual>();
            }
            Physics.IgnoreCollision(controllers[0],controllers[1]);
            if (arrival != null)
            {
                Vector3 spawn = arrival=="south" ? new Vector3(-10,0,-12) : arrival=="north" ? new Vector3(10,0,15) : new Vector3(arrival=="left"?-boundary+3:boundary-3,0,isAlley?3:0);
                Vector3 behind = arrival=="south" ? Vector3.back : arrival=="north" ? Vector3.forward : arrival=="left" ? Vector3.left : Vector3.right;
                for (int i=0;i<2;i++) { controllers[i].enabled=false; actors[i].position = spawn+behind*(i==active?0:1.2f); controllers[i].enabled=true; }
                arrival=null;
            }
            cam = Camera.main;
            cam.transform.rotation = Quaternion.Euler(40,0,0);
            cam.transform.position = actors[active].position + new Vector3(0,13,-8);
            pixels = new RenderTexture(640,360,24) { filterMode=FilterMode.Point, name="Midterm pixel world" };
            cam.targetTexture = pixels;
            outputCamera = new GameObject("Pixel output background").AddComponent<Camera>();
            outputCamera.cullingMask=0; outputCamera.depth=cam.depth+1;
            outputCamera.clearFlags=CameraClearFlags.SolidColor; outputCamera.backgroundColor=Color.black;
            if (dog != null)
            {
                dogAudio=dog.gameObject.AddComponent<AudioSource>();
                dogAudio.clip=bark; dogAudio.spatialBlend=1; dogAudio.minDistance=6; dogAudio.maxDistance=45;
                dogAudio.rolloffMode=AudioRolloffMode.Linear; dogAudio.volume=.6f;
            }
            trail.Add(actors[active].position);
            foreach (var pickup in FindObjectsByType<MidtermPickup>(FindObjectsSortMode.None))
                if (collectedItems.Contains(pickup.itemId)) pickup.gameObject.SetActive(false);
        }
        private void Update()
        {
            if(!inspecting && hover!=null)SetHover(null);
            if (dogAudio != null && Time.time >= nextBark) { dogAudio.Play(); nextBark=Time.time+4.5f; }
            if (dialogue != null)
            {
                foreach (var v in visuals) v.SetMovementInput(Vector2.zero);
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) NextLine();
                return;
            }
            if (pickupPrompt != null)
            {
                foreach (var v in visuals) v.SetMovementInput(Vector2.zero);
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape)) { pickupPrompt=null; return; }
                if (Input.GetKeyDown(KeyCode.E)) ConfirmPickup();
                return;
            }
            if (Input.GetKeyDown(KeyCode.I)) { panel=!panel; if(panel) unread=false; }
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape))
            {
                if (panel) panel=false; else if(menu) menu=false; else if(map) map=false; else if(selecting) selecting=false; else inspecting=false;
            }
            if (panel || menu || map) { foreach(var v in visuals)v.SetMovementInput(Vector2.zero); return; }
            if (inspecting)
            {
                foreach(var v in visuals)v.SetMovementInput(Vector2.zero);
                if (Input.GetKeyDown(KeyCode.E)) { inspecting=false; SetHover(null); return; }
                Vector3 mouse = new Vector3(Input.mousePosition.x/Screen.width, Input.mousePosition.y/Screen.height,0);
                MidtermInspectable target=null;
                if (Physics.Raycast(cam.ViewportPointToRay(mouse),out var hit,100)) target=hit.collider.GetComponentInParent<MidtermInspectable>();
                SetHover(target);
                if (hover != null && Input.GetMouseButtonDown(0) && Input.mousePosition.y>145 && Input.mousePosition.x<Screen.width-65) BeginInspection(hover);
                return;
            }
            nearbyPickup=FindNearbyPickup();
            if(nearbyPickup!=null && Input.GetKeyDown(KeyCode.E)) {pickupPrompt=nearbyPickup;return;}
            if(Input.GetKeyDown(KeyCode.Tab)) SwitchActor();
            if(Input.GetKeyDown(KeyCode.R))
            {
                if(selecting) { Equip(); selecting=false; } else { selecting=true; slot=0; }
            }
            if(selecting)
            {
                foreach(var v in visuals)v.SetMovementInput(Vector2.zero);
                int count=states[active].items.Count;
                if(count>0 && Input.GetKeyDown(KeyCode.RightArrow))slot=(slot+1)%count;
                if(count>0 && Input.GetKeyDown(KeyCode.LeftArrow))slot=(slot+count-1)%count;
                return;
            }
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")).normalized;
            controllers[active].Move(new Vector3(input.x,0,input.y)*4*Time.deltaTime + Vector3.down*2*Time.deltaTime);
            visuals[active].SetMovementInput(input);
            Follow(input);
            if(isAlley && !introductionSeen && actors[active].position.x>0 && actors[active].position.z>0)
            { dialogue=Introduction;line=0;introductionSeen=true;return; }
            if (isAlley && Vector3.Distance(actors[active].position,investigation.position)<4.5f && Input.GetKeyDown(KeyCode.E)) inspecting=true;
            if(location=="마을" && Mathf.Abs(actors[active].position.x-10)<2.7f && actors[active].position.z>18) {Travel("Midterm_Alley","south");return;}
            if(isAlley && actors[active].position.z < -15) {Travel("Midterm_Town","north");return;}
            if (actors[active].position.x < -boundary && !string.IsNullOrEmpty(leftScene)) Travel(leftScene,"right");
            else if (actors[active].position.x > boundary && !string.IsNullOrEmpty(rightScene)) Travel(rightScene,"left");
        }
        private void Follow(Vector2 input)
        {
            var leader=actors[active]; int other=1-active;
            if(Vector3.Distance(trail[trail.Count-1],leader.position)>.12f) trail.Add(leader.position);
            Vector3 goal=actors[other].position;
            float length=0;
            for(int i=trail.Count-1;i>0;i--) { length+=Vector3.Distance(trail[i],trail[i-1]); if(length>=1.2f) {goal=trail[i-1]; if(i>1)trail.RemoveRange(0,i-1); break;} }
            if(length<1.2f && input.sqrMagnitude>.1f && Vector3.Distance(leader.position,goal)>1.4f)goal=leader.position-new Vector3(input.x,0,input.y)*1.2f;
            Vector3 delta=goal-actors[other].position; delta.y=0;
            Vector3 move=Vector3.ClampMagnitude(delta,4.5f*Time.deltaTime);
            controllers[other].Move(move+Vector3.down*2*Time.deltaTime);
            visuals[other].SetMovementInput(move.sqrMagnitude>.00001f?new Vector2(move.x,move.z).normalized:Vector2.zero);
        }
        private void LateUpdate()
        {
            // Keep the actors alive for shared knowledge, hide only their visuals.
            foreach(var visual in visuals)foreach(var renderer in visual.GetComponentsInChildren<Renderer>())renderer.enabled=!inspecting;
            Vector3 focus=inspecting ? investigation.position+new Vector3(0,1.65f,.8f) : actors[active].position+Vector3.up*.7f;
            Quaternion rotation=Quaternion.Euler(inspecting?12:40,0,0);
            Vector3 position=focus-rotation*Vector3.forward*(inspecting?16:18);
            float t=1-Mathf.Exp(-5*Time.deltaTime);
            cam.transform.position=Vector3.Lerp(cam.transform.position,position,t);
            cam.transform.rotation=Quaternion.Slerp(cam.transform.rotation,rotation,t);
            cam.orthographicSize=Mathf.Lerp(cam.orthographicSize,inspecting?2.65f:5.6f,t);
        }
        private void Travel(string scene,string side) { arrival=side; SceneManager.LoadScene(scene); }
        private MidtermPickup FindNearbyPickup()
        {
            MidtermPickup nearest=null;float distance=float.MaxValue;
            foreach(var candidate in FindObjectsByType<MidtermPickup>(FindObjectsSortMode.None))
            {
                if(!candidate.gameObject.activeInHierarchy)continue;
                float d=Vector3.Distance(actors[active].position,candidate.transform.position);
                if(d<=candidate.interactionDistance && d<distance){nearest=candidate;distance=d;}
            }
            return nearest;
        }
        private void ConfirmPickup()
        {
            if(pickupPrompt==null)return;
            var item=new Item{id=pickupPrompt.itemId,name=pickupPrompt.itemName,slots=pickupPrompt.slots,capturesEvidence=pickupPrompt.capturesEvidence};
            if(!TryAddItem(active,item))
            {
                notice="소지품 칸이 부족하다.";noticeUntil=Time.time+2.5f;return;
            }
            collectedItems.Add(pickupPrompt.itemId);
            notice=pickupPrompt.itemName+"을(를) 주웠다.";noticeUntil=Time.time+2.5f;
            pickupPrompt.gameObject.SetActive(false);pickupPrompt=null;nearbyPickup=null;
        }
        private void SetHover(MidtermInspectable value) { if(hover==value)return; if(hover!=null)hover.Highlight(false); hover=value; if(hover!=null)hover.Highlight(true); }
        private void BeginInspection(MidtermInspectable target) { pending=target; dialogue=target.lines; line=0; SetHover(null); }
        private void NextLine()
        {
            if(++line < dialogue.Length)return;
            dialogue=null;
            if(pending!=null) { foreach(var clue in pending.clues)GrantClue(clue); pending=null; }
        }
        private void Equip()
        {
            var s=states[active]; if(s.items.Count==0)return;
            string item=s.items[Mathf.Clamp(slot,0,s.items.Count-1)].id;
            if(s.left==item || s.right==item)return;
            if(string.IsNullOrEmpty(s.left))s.left=item; else if(string.IsNullOrEmpty(s.right))s.right=item;
        }
        private static void Fill(Rect r,Color c) { GUI.color=c; GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=Color.white; }
        private static void Box(Rect r)
        {
            Fill(r,new Color(.05f,.06f,.08f,.97f));
            Color edge=new Color(.7f,.72f,.74f);
            Fill(new Rect(r.x+4,r.y,r.width-8,3),edge); Fill(new Rect(r.x+4,r.yMax-3,r.width-8,3),edge);
            Fill(new Rect(r.x,r.y+4,3,r.height-8),edge); Fill(new Rect(r.xMax-3,r.y+4,3,r.height-8),edge);
            foreach(float x in new[]{r.x+3,r.xMax-6})foreach(float y in new[]{r.y+3,r.yMax-6})Fill(new Rect(x,y,3,3),edge);
        }
        private void Text(Rect r,string value,int size=17) { label.fontSize=size; GUI.Label(r,value,label); }
        private bool Button(Rect r,string value) { Box(r); Text(new Rect(r.x+9,r.y+7,r.width-18,r.height-10),value); return GUI.Button(r,GUIContent.none,GUIStyle.none); }
        private string Name(int i)=>i==0?"여동생":"오빠";
        private string ItemName(string id)=>states[active].items.Find(i=>i.id==id)?.name??"비어 있음";
        private void OnGUI()
        {
            if(pixels==null)return;
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),pixels,ScaleMode.StretchToFill,false);
            if(label==null)label=new GUIStyle(GUI.skin.label){wordWrap=true,normal={textColor=new Color(.94f,.94f,.91f)}};
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1280*scale)/2,(Screen.height-720*scale)/2,0),Quaternion.identity,Vector3.one*scale);
            Box(new Rect(18,18,255,75)); Text(new Rect(32,27,230,28),"1일차  21:40"); Text(new Rect(32,56,230,28),location,22);
            if(Button(new Rect(1090,18,82,48),"지도"))map=!map;
            if(Button(new Rect(1180,18,82,48),"메뉴"))menu=!menu;
            Box(new Rect(18,590,465,112));
            Text(new Rect(32,599,110,24),Name(active),19);
            // Pixel portrait placeholder.
            Fill(new Rect(48,632,24,24),active==0?Color.white:Color.gray); Fill(new Rect(40,659,40,28),active==0?Color.gray:Color.white);
            Text(new Rect(126,599,340,28),"소지품 "+UsedSlots(active)+" / "+Capacity(active));
            int occupied=0;
            int itemIndex=0;
            foreach(var item in states[active].items)
            {
                Rect r=new Rect(126+occupied*55,636,item.slots*55-5,50); Box(r);
                if(selecting && slot==itemIndex)Fill(new Rect(r.x+4,r.y+4,r.width-8,4),Color.yellow);
                Text(new Rect(r.x+4,r.y+10,r.width-8,36),item.name,12); occupied+=item.slots;itemIndex++;
            }
            for(int i=occupied;i<Capacity(active);i++)Box(new Rect(126+i*55,636,50,50));
            Box(new Rect(984,590,278,112)); Text(new Rect(998,600,245,25),Name(active)+" · 장착");
            Box(new Rect(998,635,118,53)); Text(new Rect(1005,640,108,45),"왼손\n"+ItemName(states[active].left),13);
            Box(new Rect(1126,635,122,53)); Text(new Rect(1133,640,108,45),"오른손\n"+ItemName(states[active].right),13);
            if(Button(new Rect(1212,295,50,64),panel?">":"<")) {panel=!panel;if(panel)unread=false;}
            if(unread) {Fill(new Rect(1246,287,12,20),Color.red);Fill(new Rect(1242,291,20,12),Color.red);}
            Text(new Rect(490,665,480,45),inspecting?"마우스 조사 · E / Q 돌아가기":"WASD 이동 · Tab 전환 · E 조사 · I 정보",14);
            if(dialogue==null && pickupPrompt==null && !inspecting && nearbyPickup!=null)Text(new Rect(490,550,340,30),"[E] "+nearbyPickup.itemName+" 확인",22);
            if(dialogue==null && !inspecting && isAlley && Vector3.Distance(actors[active].position,investigation.position)<4.5f)Text(new Rect(490,550,340,30),"[E] 현장 조사",22);
            if(dialogue==null && location=="마을" && Mathf.Abs(actors[active].position.x-10)<3 && actors[active].position.z>6)Text(new Rect(490,550,340,30),"↑ 골목",22);
            if(hover!=null)Text(new Rect(490,510,320,35),hover.title,22);
            if(panel) DrawInformation();
            if(map) {Box(new Rect(330,130,620,420));Text(new Rect(355,155,560,35),location+" · 지도",25);Fill(new Rect(465,245,25,180),Color.gray);Fill(new Rect(465,270,320,25),Color.gray);Text(new Rect(505,220,330,40),"골목 · 현장 → 건너편",23);Text(new Rect(505,375,250,40),"↓ 마을",23);if(Button(new Rect(800,475,120,45),"닫기"))map=false;}
            if(menu) {Box(new Rect(450,230,380,220));Text(new Rect(480,255,320,60),"중간발표 시연",24);if(Button(new Rect(480,350,320,50),"계속하기"))menu=false;}
            if(dialogue!=null)
            {
                SplitDialogue(dialogue[line],out string speaker,out string body);
                Box(new Rect(65,435,1150,267));
                if(!string.IsNullOrEmpty(speaker)) {Box(new Rect(88,384,220,55));Text(new Rect(105,394,186,38),speaker,27);}
                Text(new Rect(100,465,1080,157),body,28);
                if(Button(new Rect(1005,642,175,43),"E · 다음"))NextLine();
            }
            if(pickupPrompt!=null)
            {
                Box(new Rect(210,205,860,310));
                Text(new Rect(250,240,780,45),pickupPrompt.itemName,30);
                Text(new Rect(250,310,780,100),pickupPrompt.description,23);
                Text(new Rect(250,445,780,42),"E · 습득     Q · 돌아가기",19);
            }
            if(Time.time<noticeUntil)Text(new Rect(400,100,480,40),notice);
            GUI.matrix=Matrix4x4.identity;
        }
        private void DrawInformation()
        {
            Box(new Rect(730,100,470,470));
            if(Button(new Rect(748,116,180,42),"정보"))tab=0;
            bool hasDevice=states[active].items.Exists(i=>i.capturesEvidence);
            if(hasDevice && Button(new Rect(938,116,244,42),"증거"))tab=1;
            if(!hasDevice)tab=0;
            if(tab==1) {Text(new Rect(752,190,420,45),"수집된 증거가 없습니다.");return;}
            var known=catalog.Where(c=>c!=null && Knows(active,c.id)).ToArray();
            if(known.Length==0){Text(new Rect(752,185,420,50),"아직 조사한 정보가 없습니다.");return;}
            float contentHeight=known.Length*52+known.Select(c=>c.place).Distinct().Count()*50+known.Select(c=>c.place+"/"+c.incident).Distinct().Count()*40;
            informationScroll=GUI.BeginScrollView(new Rect(748,173,438,377),informationScroll,new Rect(0,0,414,contentHeight));
            float y=0;
            foreach(var place in known.GroupBy(c=>c.place))
            {
                Text(new Rect(4,y,402,45),place.Key,30);y+=50;
                foreach(var incident in place.GroupBy(c=>c.incident))
                {
                    Text(new Rect(4,y,402,35),incident.Key,22);y+=40;
                    foreach(var c in incident){Text(new Rect(4,y,402,48),"· "+c.description,15);y+=52;}
                }
            }
            GUI.EndScrollView();
        }
        public static void SplitDialogue(string value,out string speaker,out string body)
        {
            int colon=value.IndexOf(':');
            speaker=colon>=0?value.Substring(0,colon).Trim():"";
            body=colon>=0?value.Substring(colon+1).Trim():value;
        }
        private void OnDestroy() {if(cam!=null)cam.targetTexture=null;if(pixels!=null){pixels.Release();Destroy(pixels);}if(outputCamera!=null)Destroy(outputCamera.gameObject);}
    }
}
