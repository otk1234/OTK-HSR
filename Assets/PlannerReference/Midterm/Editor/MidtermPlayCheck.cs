using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OTK.Midterm;

[InitializeOnLoad]
public static class MidtermPlayCheck
{
    const string Key="OTK.Midterm.Check";
    static double start;
    static int stage;
    static MidtermDemo demo;
    [MenuItem("OTK/중간발표/플레이 자동 검사")]
    public static void RunInteractive(){SessionState.SetBool(Key+"Interactive",true);Run();}
    static void Finish(int code)
    {
        SessionState.SetBool(Key,false);
        if(!SessionState.GetBool(Key+"Interactive",false)){EditorApplication.Exit(code);return;}
        SessionState.SetBool(Key+"Interactive",false);EditorApplication.update-=Tick;
        Application.logMessageReceived-=Log;EditorApplication.ExitPlaymode();
    }
    static MidtermPlayCheck()
    {
        if(SessionState.GetBool(Key,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;start=EditorApplication.timeSinceStartup;}
    }
    public static void Run()
    {
        SessionState.SetBool(Key,true);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        start=EditorApplication.timeSinceStartup;stage=0;
        EditorSceneManager.OpenScene("Assets/PlannerReference/Midterm/Scenes/Midterm_Alley.unity");
        EditorApplication.EnterPlaymode();
    }
    static void Assert(bool result,string name){if(!result)throw new Exception(name);Debug.Log("[Midterm Check] PASS "+name);}
    static void Set(string field,object value)=>typeof(MidtermDemo).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(demo,value);
    static void Call(string method,params object[] args)=>typeof(MidtermDemo).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(demo,args);
    static void Log(string text,string trace,LogType type)
    {if(type==LogType.Exception || type==LogType.Error){Debug.Log("[Midterm failure] "+text+"\n"+trace);Finish(3);}}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-start>90){Debug.LogError("Midterm check timeout");return;}
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling)return;
        try
        {
            if(stage==0)
            {
                demo=UnityEngine.Object.FindFirstObjectByType<MidtermDemo>();if(demo==null)return;
                Assert(demo.CanSwitch,"same-map character switching");
                MidtermDemo.SplitDialogue("오빠 : 죽은 거야?",out var speaker,out var body);
                Assert(speaker=="오빠" && body=="죽은 거야?","speaker name separated from dialogue body");
                MidtermDemo.SplitDialogue("여동생은 남자의 목에 손을 댄다.",out speaker,out body);
                Assert(speaker=="" && body.StartsWith("여동생은"),"narration has no speaker nameplate");
                var a=demo.actors[0].position;var b=demo.actors[1].position;demo.SwitchActor();
                Assert(a==demo.actors[0].position && b==demo.actors[1].position,"switch preserves both positions");demo.SwitchActor();
                demo.GrantClue(demo.catalog[0]);Assert(demo.Knows(0,demo.catalog[0].id)&&demo.Knows(1,demo.catalog[0].id),"shared knowledge by stable ID");
                demo.actors[1].gameObject.SetActive(false);Assert(!demo.CanSwitch,"absent sibling cannot switch");
                demo.GrantClue(demo.catalog[1]);Assert(demo.Knows(0,demo.catalog[1].id)&&!demo.Knows(1,demo.catalog[1].id),"absent sibling gains no information");demo.actors[1].gameObject.SetActive(true);
                Assert(demo.TryAddItem(0,new MidtermDemo.Item{id="test.large",name="large",slots=3}),"large item occupies three slots");
                Assert(!demo.TryAddItem(0,new MidtermDemo.Item{id="test.tooLarge",name="large",slots=3}),"slot overflow rejected");
                Assert(demo.UsedSlots(0)==3,"occupied slot total");
                foreach(var target in UnityEngine.Object.FindObjectsByType<MidtermInspectable>(FindObjectsSortMode.None))
                {Assert(target.GetComponent<Collider>()!=null,"collider: "+target.title);Call("BeginInspection",target);for(int i=0;i<target.lines.Length;i++)Call("NextLine");}
                foreach(var clue in demo.catalog)Assert(demo.Knows(0,clue.id),"investigation grants "+clue.id);
                Assert(demo.dog.GetComponent<AudioSource>().clip!=null,"barking sound configured");
                Set("inspecting",true);stage=1;start=EditorApplication.timeSinceStartup;
            }
            else if(stage==1 && EditorApplication.timeSinceStartup-start>2)
            {
                Assert(Quaternion.Angle(Camera.main.transform.rotation,Quaternion.Euler(12,0,0))<2,"inspection camera transition");
                Assert(Camera.main.orthographicSize<2.8f,"inspection close framing");
                foreach(var actor in demo.actors)foreach(var r in actor.GetComponentsInChildren<MeshRenderer>())Assert(!r.enabled,"actor hidden during inspection");
                var bottle=GameObject.Find("술병").GetComponent<MidtermInspectable>();bottle.Highlight(true);
                var lamp=GameObject.Find("피묻은 가로등").GetComponent<MidtermInspectable>();lamp.Highlight(true);
                var stroke=lamp.GetComponentInChildren<LineRenderer>();
                Assert(Vector3.Distance(stroke.GetPosition(1),stroke.GetPosition(2))<1.2f,"lamp outline fits narrow object width");
                var previous=RenderTexture.active;RenderTexture.active=Camera.main.targetTexture;
                var image=new Texture2D(640,360,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();
                System.IO.File.WriteAllBytes("C:/Users/2seji/Documents/Codex/2026-09-22/new-chat/midterm-inspection.png",image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);RenderTexture.active=previous;
                Set("inspecting",false);stage=2;start=EditorApplication.timeSinceStartup;
            }
            else if(stage==2 && EditorApplication.timeSinceStartup-start>2)
            {
                Assert(Quaternion.Angle(Camera.main.transform.rotation,Quaternion.Euler(40,0,0))<2,"return camera transition");
                foreach(var actor in demo.actors)foreach(var r in actor.GetComponentsInChildren<MeshRenderer>())Assert(r.enabled,"actor restored after inspection");
                typeof(MidtermDemo).GetField("introductionSeen",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
                for(int i=0;i<2;i++){var cc=demo.actors[i].GetComponent<CharacterController>();cc.enabled=false;demo.actors[i].position=new Vector3(-4-i*1.2f,0,1.3f);cc.enabled=true;}
                demo.SwitchActor();demo.SwitchActor();stage=3;start=EditorApplication.timeSinceStartup;
            }
            else if(stage==3)
            {
                demo.actors[0].GetComponent<CharacterController>().Move(Vector3.right*4*Time.deltaTime);
                Call("Follow",Vector2.right);
                if(EditorApplication.timeSinceStartup-start>2)
                {
                    float gap=demo.actors[0].position.x-demo.actors[1].position.x;
                    Assert(gap>.6f && gap<1.9f,"sibling follows in a single line");
                    demo.SwitchActor();stage=4;start=EditorApplication.timeSinceStartup;
                }
            }
            else if(stage==4)
            {
                demo.actors[1].GetComponent<CharacterController>().Move(Vector3.right*4*Time.deltaTime);
                Call("Follow",Vector2.right);
                if(EditorApplication.timeSinceStartup-start>2)
                {
                    Assert(demo.actors[1].position.x>demo.actors[0].position.x+.5f,"newly selected rear actor becomes leader without swapping positions");
                    SceneManager.LoadScene("Midterm_Beyond");stage=5;
                }
            }
            else if(stage==5 && SceneManager.GetActiveScene().name=="Midterm_Beyond")
            {
                demo=UnityEngine.Object.FindFirstObjectByType<MidtermDemo>();Assert(demo!=null&&demo.CanSwitch,"separate scene travel");
                Assert(demo.Knows(0,"alley.man.death"),"knowledge persists after travel");
                Debug.Log("[Midterm Check] ALL PASS");Finish(0);
            }
        }
        catch(Exception e){Debug.LogException(e);Finish(2);}
    }
}
