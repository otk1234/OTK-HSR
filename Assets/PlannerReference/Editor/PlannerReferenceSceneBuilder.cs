using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlannerReferenceSceneBuilder
{
    private const string Root = "Assets/PlannerReference/Scenes";
    private static readonly string[] Sources =
    {
        "Assets/Scenes/Prototype.unity",
        "Assets/Map_Graybox/Scenes/Test_TopView.unity",
        "Assets/Map_Graybox/Scenes/Test_ThreeQuarter.unity",
        "Assets/Map_Graybox/Scenes/Test_Isometric_2F.unity"
    };

    [MenuItem("OTK/기획자 참고 씬 복제 및 갱신")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/PlannerReference")) AssetDatabase.CreateFolder("Assets", "PlannerReference");
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/PlannerReference", "Scenes");
        foreach (string source in Sources)
        {
            string target = Root + "/" + Path.GetFileNameWithoutExtension(source) + "_PlannerReference.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(target) != null) AssetDatabase.DeleteAsset(target);
            if (!AssetDatabase.CopyAsset(source, target)) throw new Exception("씬 복제 실패: " + source);
        }

        string main = Root + "/Prototype_PlannerReference.unity";
        var scene = EditorSceneManager.OpenScene(main, OpenSceneMode.Single);
        if (UnityEngine.Object.FindFirstObjectByType<PlannerReferenceDemo>() == null)
            new GameObject("기획 요구사항 더미 시스템").AddComponent<PlannerReferenceDemo>();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        buildScenes.Add(new EditorBuildSettingsScene(main, true));
        foreach (var configured in EditorBuildSettings.scenes)
            if (configured.path != main) buildScenes.Add(configured);
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[PlannerReference] 원본을 보존하고 참고용 씬 4개를 생성했습니다.");
    }

    public static void BuildFromCommandLine()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }
}
