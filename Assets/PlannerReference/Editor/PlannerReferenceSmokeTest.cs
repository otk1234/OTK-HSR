using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlannerReferenceSmokeTest
{
    private static IEnumerator<float> steps;
    private static double next;
    private static int errors;
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/PlannerReference/Scenes/Prototype_PlannerReference.unity");
        Application.logMessageReceived += (m, s, t) => { if (t == LogType.Error || t == LogType.Exception) errors++; };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            steps = Steps(); next = EditorApplication.timeSinceStartup + 3; EditorApplication.update += Tick;
        };
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (steps.MoveNext()) { next = EditorApplication.timeSinceStartup + steps.Current; return; }
            Debug.Log("[PlannerCheck] COMPLETE errors=" + errors); EditorApplication.Exit(errors == 0 ? 0 : 3);
        }
        catch (Exception e) { Debug.LogError("[PlannerCheck] " + e); EditorApplication.Exit(3); }
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name); Debug.Log("[PlannerCheck] PASS " + name);
    }
    private static IEnumerator<float> Steps()
    {
        var demo = UnityEngine.Object.FindFirstObjectByType<PlannerReferenceDemo>();
        var manager = CharacterManager.instance;
        var integration = UnityEngine.Object.FindFirstObjectByType<PrototypeIntegration>();
        Check(demo != null && manager.players.Length == 2, "two players and reference demo");
        Check(integration != null && integration.OverviewFollowEnabled, "overview camera follows active player only in reference scene");
        Check(integration.characterNames[0] == "오빠" && integration.characterNames[1] == "동생", "sibling character labels");
        Check(integration.InventoryCapacityFor(0) == 6 && integration.InventoryCapacityFor(1) == 5, "brother has one additional inventory slot");
        Check(demo.SaveBedExists, "second-floor dummy save bed");
        Check(UnityEngine.Object.FindFirstObjectByType<NarrativeGraybox.UI.SimpleGameHud>().enabled == false, "original HUD disabled only in clone");
        Check(demo.EquipSelected(manager.players[0]), "player 1 equips item");
        string player1Hand = demo.GetHands(0)[0];
        MethodInfo switcher = typeof(CharacterManager).GetMethod("SwitchPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        switcher.Invoke(manager, new object[] { 1 }); yield return .5f;
        Check(demo.EquipSelected(manager.players[1]), "player 2 equips item");
        string player2Hand = demo.GetHands(1)[0];
        switcher.Invoke(manager, new object[] { 0 }); yield return .5f;
        Check(demo.GetHands(0)[0] == player1Hand && demo.GetHands(1)[0] == player2Hand && player1Hand != player2Hand, "equipment persists independently after character switch");
        demo.PerformDummySave();
        Check(demo.DummySaveCount == 1, "dummy save interaction");
        var wall = UnityEngine.Object.FindFirstObjectByType<NarrativeGraybox.CameraSystem.OccludingWall>();
        var rendererField = typeof(NarrativeGraybox.CameraSystem.OccludingWall).GetField("wallRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        var wallRenderer = rendererField.GetValue(wall) as Renderer;
        wall.SetOccluded(true); yield return .5f;
        Material fadedMaterial = wallRenderer.sharedMaterials.First(material => material.HasProperty("_BaseColor") || material.HasProperty("_Color"));
        float fadedAlpha = fadedMaterial.HasProperty("_BaseColor") ? fadedMaterial.GetColor("_BaseColor").a : fadedMaterial.color.a;
        Check(wallRenderer.enabled && fadedAlpha < .2f, "occluding wall fades without disabling renderer");
        wall.SetOccluded(false, true);
        Check(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Prototype.unity") != null, "original prototype preserved");
        yield return .5f;
    }
}
