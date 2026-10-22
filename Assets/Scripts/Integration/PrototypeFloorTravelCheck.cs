#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class PrototypeFloorTravelCheck
{
    private static IEnumerator<float> steps;
    private static double next;
    private static int errors;
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors++;
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            steps = Steps(); next = EditorApplication.timeSinceStartup + 3;
            EditorApplication.update += Tick;
        };
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (steps.MoveNext()) { next = EditorApplication.timeSinceStartup + steps.Current; return; }
            Debug.Log("[FloorCheck] COMPLETE errors=" + errors);
            EditorApplication.Exit(errors == 0 ? 0 : 3);
        }
        catch (Exception e) { Debug.LogError("[FloorCheck] " + e); EditorApplication.Exit(3); }
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Debug.Log("[FloorCheck] PASS " + name);
    }
    private static IEnumerator<float> Steps()
    {
        var manager = CharacterManager.instance;
        var player = manager.GetActivePlayer();
        var agent = player.GetComponent<NavMeshAgent>();
        var room = UnityEngine.Object.FindObjectsByType<CameraTrigger>(FindObjectsSortMode.None).First(r => r.roomName == "2층");
        var path = new NavMeshPath();
        var approach = new Vector3(8.4f, 0, 1.67f);
        Check(NavMesh.CalculatePath(agent.nextPosition, approach, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "stairs entrance reachable from first floor");
        int itemCount = player.GetComponent<Inventory>().items.Count;
        Check(agent.Warp(approach), "place player at stairs entrance");
        yield return 3f;
        Check(player.transform.position.x > 40 && agent.isOnNavMesh, "automatic upstairs travel");
        Check(room.Contains(player) && room.virtualCamera.activeSelf, "upstairs camera trigger");
        Check(!FadeManager.instance.isFading, "fade completes");
        Check(player.GetComponent<Inventory>().items.Count == itemCount, "inventory preserved");
        Vector3 before = player.transform.position;
        agent.Move(Vector3.right * .5f);
        yield return .5f;
        Check(Vector3.Distance(before, player.transform.position) > .2f, "movement on second floor");
        manager.SendMessage("SwitchPlayer", (object)1);
        yield return 2f;
        Check(!room.virtualCamera.activeSelf, "switch to downstairs character restores room camera");
        manager.SendMessage("SwitchPlayer", (object)0);
        yield return 2f;
        Check(room.virtualCamera.activeSelf, "switch back restores upstairs camera");
        Check(agent.Warp(new Vector3(50, 0, -4)), "place player at downstairs exit");
        yield return 3f;
        Check(player.transform.position.x < 20 && agent.isOnNavMesh, "automatic downstairs travel");
        Check(!room.virtualCamera.activeSelf && !FadeManager.instance.isFading, "downstairs camera and controls restored");
        yield return 2f;
        Check(player.transform.position.x < 20, "no immediate bounce upstairs");
    }
}
#endif
