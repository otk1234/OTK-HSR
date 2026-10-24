using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OTK.Midterm;

[InitializeOnLoad]
public static class MidtermNotebookCheck
{
    const string Key="OTK.Midterm.NotebookCheck";

    static MidtermNotebookCheck()
    {
        if(SessionState.GetBool(Key,false))EditorApplication.update+=Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(Key,true);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        EditorSceneManager.OpenScene("Assets/PlannerReference/Midterm/Scenes/Midterm_Town.unity");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling)return;
        try
        {
            var demo=UnityEngine.Object.FindFirstObjectByType<MidtermDemo>();
            var notebookObject=GameObject.Find("떨어진 공책");
            if(demo==null || notebookObject==null)return;
            var notebook=notebookObject.GetComponent<MidtermPickup>();
            if(notebook==null)throw new Exception("Notebook pickup component missing");
            var type=typeof(MidtermDemo);
            int actor=(int)type.GetField("active",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            int before=demo.UsedSlots(actor);
            type.GetField("pickupPrompt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(demo,notebook);
            type.GetMethod("ConfirmPickup",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(demo,null);
            if(!demo.HasItem(actor,"town.notebook"))throw new Exception("Notebook was not added to the active sibling");
            if(demo.UsedSlots(actor)!=before+1)throw new Exception("Notebook did not occupy exactly one slot");
            if(notebookObject.activeSelf)throw new Exception("Notebook remained on the street after pickup");
            Debug.Log("[Midterm Notebook Check] ALL PASS");Finish(0);
        }
        catch(Exception e){Debug.LogException(e);Finish(2);}
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Key,false);EditorApplication.update-=Tick;
        EditorApplication.Exit(code);
    }
}
