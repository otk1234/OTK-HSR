using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OTK.Midterm;
using NarrativeGraybox.Player;

public static class MidtermSceneBuilder
{
    const string Root="Assets/PlannerReference/Midterm";
    static Material gray, dark, light, brown, blue;
    [MenuItem("OTK/중간발표/마을·골목 시연 씬 생성")]
    public static void Build()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        foreach(string folder in new[]{"Scenes","Data","Materials"})Ensure(Root+"/"+folder);
        gray=Mat("Gray",new Color(.4f,.43f,.46f));dark=Mat("Dark",new Color(.18f,.2f,.23f));light=Mat("Light",new Color(.7f,.7f,.64f));brown=Mat("Brown",new Color(.35f,.24f,.16f));blue=Mat("Blue",new Color(.17f,.3f,.48f));
        string[] ids={"alley.man.death","alley.man.appearance","alley.trash.nachos","alley.lamp.blood","alley.bag","alley.dog","alley.bottle"};
        string[] descriptions={"남자는 죽었다. 살해당한 것 같다.","남자는 낡은 옷을 입었고 악취가 심하게 났다.","근처에 먹다 남은 나쵸 봉지가 떨어져 있었다.","남자 근처 가로등에는 어깨 높이에 피가 넓게 묻어 있었다.","큰 타포린 백이 곁에 있다. 남자의 가방인가?","귀가 넓고 갈색인 강아지가 곁에서 짖고 있었다.","근처에 내용물이 조금 남고 금이 간 맥주병이 떨어져 있었다."};
        var clues=new MidtermClue[ids.Length];
        for(int i=0;i<ids.Length;i++)
        {
            string path=Root+"/Data/"+ids[i]+".asset";
            clues[i]=AssetDatabase.LoadAssetAtPath<MidtermClue>(path);
            if(clues[i]==null){clues[i]=ScriptableObject.CreateInstance<MidtermClue>();AssetDatabase.CreateAsset(clues[i],path);}
            clues[i].id=ids[i];clues[i].description=descriptions[i];EditorUtility.SetDirty(clues[i]);
        }
        foreach(string name in new[]{"Town","Alley","Beyond"})MakeScene(name,clues);
        var scenes=new List<EditorBuildSettingsScene>();
        foreach(string name in new[]{"Town","Alley","Beyond"})scenes.Add(new EditorBuildSettingsScene(Root+"/Scenes/Midterm_"+name+".unity",true));
        foreach(var s in EditorBuildSettings.scenes)if(!s.path.StartsWith(Root+"/Scenes/"))scenes.Add(s);
        EditorBuildSettings.scenes=scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(Root+"/Scenes/Midterm_Town.unity");
        Debug.Log("[Midterm] Created 3 scenes and 7 stable clue assets.");
    }
    static void Ensure(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int at=path.LastIndexOf('/');Ensure(path.Substring(0,at));AssetDatabase.CreateFolder(path.Substring(0,at),path.Substring(at+1));
    }
    static Material Mat(string name,Color color)
    {
        string path=Root+"/Materials/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader=Shader.Find("Universal Render Pipeline/Lit");
        if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
        mat.color=color;EditorUtility.SetDirty(mat);return mat;
    }
    static GameObject Shape(string name,Vector3 position,Vector3 scale,Material mat,PrimitiveType type=PrimitiveType.Cube,Transform parent=null)
    {
        var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.position=position;g.transform.localScale=scale;
        if(parent!=null)g.transform.SetParent(parent,true);
        g.GetComponent<Renderer>().sharedMaterial=mat;return g;
    }
    static void Label(string text,Vector3 position)
    {
        var g=new GameObject(text);g.transform.position=position;
        var t=g.AddComponent<TextMesh>();t.text=text;t.characterSize=.25f;t.fontSize=38;t.anchor=TextAnchor.MiddleCenter;t.color=Color.white;
        g.transform.rotation=Quaternion.Euler(58,0,0);
    }
    static MidtermInspectable Inspect(GameObject g,string title,string[] lines,params MidtermClue[] clues)
    {var v=g.AddComponent<MidtermInspectable>();v.title=title;v.lines=lines;v.clues=clues;return v;}
    static void MakeScene(string kind,MidtermClue[] clues)
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("중간발표 시연 · "+kind);var demo=root.AddComponent<MidtermDemo>();demo.catalog=clues;
        demo.isAlley=kind=="Alley";demo.location=kind=="Town"?"마을":kind=="Alley"?"골목":"건너편";
        demo.boundary=kind=="Town"?38:kind=="Alley"?22:18;
        demo.leftScene=kind=="Alley"?"":"Midterm_Town";
        if(kind=="Town")demo.leftScene="Midterm_Beyond";
        demo.rightScene=kind=="Beyond"?"Midterm_Alley":"Midterm_Beyond";
        var cam=new GameObject("Main Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.orthographic=true;cam.orthographicSize=8;cam.farClipPlane=160;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.09f,.1f,.13f);cam.transform.SetPositionAndRotation(new Vector3(0,13,-8),Quaternion.Euler(58,0,0));cam.gameObject.AddComponent<AudioListener>();
        var sun=new GameObject("Soft street light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.9f;sun.transform.rotation=Quaternion.Euler(55,-30,0);
        RenderSettings.ambientLight=new Color(.45f,.46f,.5f);RenderSettings.skybox=null;
        float depth=kind=="Town"?30:12;
        if(kind=="Alley")
        {
            Shape("세로 진입로",new Vector3(-10,-.2f,-3),new Vector3(6,.4f,28),gray);
            Shape("오른쪽 현장 골목",new Vector3(9.5f,-.2f,2),new Vector3(33,.4f,8),gray);
            Shape("세로 왼쪽 벽",new Vector3(-13,1.5f,-3),new Vector3(.4f,3,28),dark);
            Shape("세로 오른쪽 아래벽",new Vector3(-7,1.5f,-9.5f),new Vector3(.4f,3,15),dark);
            Shape("세로 오른쪽 위벽",new Vector3(-7,1.5f,8.5f),new Vector3(.4f,3,5),dark);
            Shape("세로 끝벽",new Vector3(-10,1.5f,11),new Vector3(6.4f,3,.4f),dark);
            Shape("현장 상단 벽",new Vector3(9.5f,1.5f,6),new Vector3(33,3,.4f),dark);
            Shape("현장 앞쪽 벽",new Vector3(9.5f,1.4f,-2),new Vector3(33,2.8f,.4f),dark);
        }
        else
        {
            Shape("바닥",new Vector3(0,-.2f,0),new Vector3(demo.boundary*2+8,.4f,depth),gray);
            if(kind=="Town")
            {
                Shape("상단 왼쪽 벽",new Vector3(-17.5f,1.5f,15),new Vector3(49,3,.4f),dark);
                Shape("상단 오른쪽 벽",new Vector3(27.5f,1.5f,15),new Vector3(29,3,.4f),dark);
                Shape("골목 연결 바닥",new Vector3(10,-.2f,17),new Vector3(6,.4f,8),gray);
                Shape("진입로 왼쪽 벽",new Vector3(7,1.5f,17),new Vector3(.4f,3,8),dark);
                Shape("진입로 오른쪽 벽",new Vector3(13,1.5f,17),new Vector3(.4f,3,8),dark);
            }
            else Shape("뒤쪽 경계",new Vector3(0,1.5f,depth/2),new Vector3(demo.boundary*2+8,3,.4f),dark);
            Shape("앞쪽 낮은 경계",new Vector3(0,.35f,-depth/2),new Vector3(demo.boundary*2+8,.7f,.4f),dark);
        }
        demo.characterSheet=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Map_Graybox/Art/TestCharacter4Directions.png");
        demo.actors=new Transform[2];
        for(int i=0;i<2;i++)
        {
            var actor=new GameObject(i==0?"여동생":"오빠");actor.transform.position=new Vector3(-4+i*1.3f,0,0);demo.actors[i]=actor.transform;
            if(kind=="Alley")actor.transform.position=new Vector3(-10,0,-12-i*1.2f);
            var controller=actor.AddComponent<CharacterController>();controller.height=1.7f;controller.radius=.28f;controller.center=new Vector3(0,.85f,0);controller.stepOffset=.2f;
            var visual=new GameObject("Rotating pixel dummy");visual.transform.SetParent(actor.transform,false);
            var motion=visual.AddComponent<MidtermDummyVisual>();
            DummyPart("몸통",visual.transform,new Vector3(0,1,0),new Vector3(.48f,.6f,.3f),i==0?light:gray);
            DummyPart("머리",visual.transform,new Vector3(0,1.5f,0),new Vector3(.35f,.38f,.34f),light);
            DummyPart("앞 · 얼굴",visual.transform,new Vector3(0,1.53f,.18f),new Vector3(.24f,.08f,.035f),dark);
            DummyPart("뒤 · 등",visual.transform,new Vector3(0,1.08f,-.17f),new Vector3(.28f,.3f,.06f),blue);
            motion.leftLeg=DummyLeg(visual.transform,-.14f);motion.rightLeg=DummyLeg(visual.transform,.14f);
        }
        Physics.IgnoreCollision(demo.actors[0].GetComponent<Collider>(),demo.actors[1].GetComponent<Collider>());
        if(kind=="Town")
        {
            for(int i=0;i<8;i++)
            {
                float x=-30+i*8;
                if(i==5)continue; // Open a real northbound street instead of putting a portal in a shop.
                Shape("상점 "+(i+1),new Vector3(x,2.5f,11),new Vector3(6,5,6),i%2==0?light:gray);
                Shape("상점 출입문",new Vector3(x,.9f,7.95f),new Vector3(1.2f,1.8f,.15f),brown);
                Label("SHOP "+(i+1),new Vector3(x,3,7.7f));
                if(i%2==0)Shape("남쪽 상점",new Vector3(x,1.4f,-11),new Vector3(6,2.8f,5),gray);
            }
            Label("ALLEY  ^",new Vector3(10,.15f,7));
            var notebook=Shape("떨어진 공책",new Vector3(9.3f,.08f,11),new Vector3(.58f,.12f,.78f),brown);
            notebook.transform.rotation=Quaternion.Euler(0,18,0);
            var pickup=notebook.AddComponent<MidtermPickup>();
            pickup.itemId="town.notebook";pickup.itemName="공책";pickup.slots=1;
            pickup.description="표지가 낡은 작은 공책이다. 여러 장이 뜯겨 나갔고, 남은 페이지에는 알아보기 어려운 글씨가 적혀 있다.";
            var sound=new GameObject("골목에서 들리는 개 소리");sound.transform.position=new Vector3(10,1,19);demo.dog=sound.transform;
        }
        if(kind=="Alley")
        {
            var focus=new GameObject("현장 조사 중심");focus.transform.position=Vector3.zero;demo.investigation=focus.transform;
            var body=Shape("쓰러진 남자",new Vector3(-.5f,.95f,1.55f),new Vector3(.85f,1.1f,.5f),blue);
            Shape("왼쪽 뻗은 다리",new Vector3(-.74f,.23f,.55f),new Vector3(.32f,.4f,1.8f),dark,PrimitiveType.Cube,body.transform);
            Shape("오른쪽 뻗은 다리",new Vector3(-.27f,.23f,.45f),new Vector3(.32f,.4f,2),dark,PrimitiveType.Cube,body.transform);
            Shape("머리",new Vector3(-.5f,1.65f,1.48f),Vector3.one*.48f,light,PrimitiveType.Sphere,body.transform);
            Inspect(body,"쓰러진 사람",new[]{"쓰러진 남자: ...","남자는 낡은 자켓 위에 얇은 파란색 패딩과 청바지를 입고 있으며 심각한 악취가 난다.","오빠 : 저기요, 괜찮으세요?","여동생은 남자의 목에 손을 댄다.","여동생 : 맥박이 없어.","오빠 : 죽은 거야?","여동생 : 아마도.","여동생 : 구급차가 올 때까지 기다려야지..."},clues[0],clues[1]);
            var lamp=Shape("피묻은 가로등",new Vector3(-3,1.9f,2),new Vector3(.22f,1.9f,.22f),gray,PrimitiveType.Cylinder);
            Shape("꺼진 조명",new Vector3(-3,3.8f,2),new Vector3(.7f,.25f,.7f),light,PrimitiveType.Cube,lamp.transform);
            Inspect(lamp,"피묻은 가로등",new[]{"불이 꺼진 가로등이다. 피가 어깨 높이에 넓게 묻어 있다. 표면은 거칠고 쇠 냄새가 나지만, 피 때문인지 가로등의 재질 때문인지는 잘 모르겠다."},clues[3]);
            var bottle=Shape("술병",new Vector3(1.1f,.18f,-.8f),new Vector3(.2f,.35f,.2f),brown,PrimitiveType.Cylinder);bottle.transform.rotation=Quaternion.Euler(0,0,80);
            Inspect(bottle,"술병",new[]{"어느 술집이나 식당에 가도 흔히 있는 맥주의 라벨이 붙은 술병이다. 내용물은 1/5정도 남았다. 바닥과 몸체에 금이 간 채로 나뒹굴고 있다."},clues[6]);
            var trash=Shape("쓰레기",new Vector3(-2,.13f,-1.5f),new Vector3(.6f,.25f,.6f),light);
            Inspect(trash,"쓰레기",new[]{"부서진 과자가 남아있는 봉지다. 짭짤한 시즈닝 냄새가 난다."},clues[2]);
            var bag=Shape("가방",new Vector3(1,.4f,1),new Vector3(.85f,.8f,.6f),brown);
            Inspect(bag,"가방",new[]{"낡은 가방이 남자의 바로 곁에 떨어져 있다. 가구 업체에서 저렴하게 판매하는 타폴린 백이다. 안에는 쓰레기나 휴지 따위로 가득 차 있다."},clues[4]);
            var dog=Shape("개",new Vector3(2.6f,.6f,.1f),new Vector3(1.2f,.55f,.5f),brown);
            Shape("머리",new Vector3(2.1f,.95f,.1f),Vector3.one*.5f,brown,PrimitiveType.Cube,dog.transform);
            foreach(float x in new[]{2.2f,3f})foreach(float z in new[]{-.07f,.27f})Shape("다리",new Vector3(x,.25f,z),new Vector3(.15f,.5f,.15f),brown,PrimitiveType.Cube,dog.transform);
            Inspect(dog,"개",new[]{"개가 남자의 곁에서 계속 짖고 있다. 목줄이 있고 관리가 잘되어 있는 하운드다.","여동생 : 주인이 있는 개 같아.","오빠 : 이 사람이 주인은 아니지 않을까?","여동생 : 왜?","오빠 : 이런 개를 키울 사람으로 보이지 않아."},clues[5]);demo.dog=dog.transform;
            Vector3 sceneOffset=new Vector3(9,0,3.8f);
            foreach(var target in UnityEngine.Object.FindObjectsByType<MidtermInspectable>(FindObjectsSortMode.None))target.transform.position+=sceneOffset;
            lamp.transform.position+=Vector3.back*.4f;
            focus.transform.position=new Vector3(9,0,3);
        }
        // Procedural placeholder bark, authored locally; replaceable with a recorded clip.
        demo.bark=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Data/DummyBark.wav");
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if(renderer.name.Contains("벽") || renderer.name.Contains("경계"))renderer.gameObject.AddComponent<MidtermWallFade>().actors=demo.actors;
        EditorSceneManager.SaveScene(scene,Root+"/Scenes/Midterm_"+kind+".unity");
    }
    static void DummyPart(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var part=Shape(name,parent.position+position,scale,material,PrimitiveType.Cube,parent);
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
    }
    static Transform DummyLeg(Transform parent,float x)
    {
        var pivot=new GameObject("다리 회전축").transform;pivot.SetParent(parent,false);pivot.localPosition=new Vector3(x,.7f,0);
        DummyPart("다리",pivot,new Vector3(0,-.3f,0),new Vector3(.18f,.6f,.22f),dark);return pivot;
    }
    public static void BuildBatch()
    {
        try { CreateBark();Build(); Validate();EditorApplication.Exit(0); }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(2);}
    }
    static void CreateBark()
    {
        Ensure(Root+"/Data");string path=Root+"/Data/DummyBark.wav";if(System.IO.File.Exists(path))return;
        const int rate=22050;int count=rate;
        using(var stream=System.IO.File.Create(path))using(var w=new System.IO.BinaryWriter(stream))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(count*2);
            var random=new System.Random(21);
            for(int i=0;i<count;i++){float t=i/(float)rate;float local=t<.4f?t:t-.47f;float e=local>=0&&local<.28f?Mathf.Sin(Mathf.PI*local/.28f)*Mathf.Exp(-local*9):0;float tone=Mathf.Sin(2*Mathf.PI*(180*local-90*local*local))*.65f+(float)(random.NextDouble()*2-1)*.35f;w.Write((short)(tone*e*18000));}
        }
        AssetDatabase.ImportAsset(path);
    }
    [MenuItem("OTK/중간발표/시연 씬 구조 검증")]
    public static void Validate()
    {
        foreach(string kind in new[]{"Town","Alley","Beyond"})
        {
            EditorSceneManager.OpenScene(Root+"/Scenes/Midterm_"+kind+".unity");var demo=UnityEngine.Object.FindFirstObjectByType<MidtermDemo>();
            if(demo==null || demo.actors.Length!=2 || demo.characterSheet==null)throw new Exception("Missing actors or sprite sheet: "+kind);
            if(kind=="Alley" && UnityEngine.Object.FindObjectsByType<MidtermInspectable>(FindObjectsSortMode.None).Length!=6)throw new Exception("Expected six investigation targets");
            if(demo.catalog.Length!=7)throw new Exception("Expected seven clue records");
            if(kind!="Beyond"&&demo.bark==null)throw new Exception("Bark missing");
            Physics.SyncTransforms();
            if(kind=="Town")
            {
                CheckPassage(new Vector3(10,1,1),new Vector3(10,1,19));
                var notebook=GameObject.Find("떨어진 공책");
                if(notebook==null || notebook.GetComponent<MidtermPickup>()==null)throw new Exception("Notebook pickup missing");
            }
            if(kind=="Alley")
            {
                CheckPassage(new Vector3(-10,1,-14),new Vector3(-10,1,3));
                CheckPassage(new Vector3(-10,1,3),new Vector3(4,1,3));
                if(GameObject.Find("쓰러진 남자").transform.position.z<3.5f)throw new Exception("Man must be near upper wall");
            }
        }
        EditorSceneManager.OpenScene(Root+"/Scenes/Midterm_Town.unity");Debug.Log("[Midterm] Scene validation PASS");
    }
    static void CheckPassage(Vector3 start,Vector3 end)
    {
        Vector3 delta=end-start;
        foreach(var hit in Physics.SphereCastAll(start,.3f,delta.normalized,delta.magnitude))
            if(!(hit.collider is CharacterController))throw new Exception("Blocked entrance: "+hit.collider.name);
        Debug.Log("[Midterm] Walkable passage PASS "+start+" to "+end);
    }
}
