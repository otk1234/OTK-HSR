using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 캐릭터 프로필 에셋 만들기 + 점검
//  - 에셋 (없으면 기본값으로 만듦): Assets/Data/Characters/Sister.asset · Brother.asset, 목록 Assets/Resources/CharacterRoster.asset
//  - 점검 (에디터 상태): 이름·칸 수·특성, CanUse 판정, 소지품이 가득 차면 못 넣음
public static class CharacterProfileValidator
{
    const string Tag = "[OTK 캐릭터 점검]";
    const string ProfileFolder = "Assets/Data/Characters";
    const string RosterAsset = "Assets/Resources/" + CharacterRoster.RosterResource + ".asset";
    static readonly string[] ProfileFiles = { "Sister", "Brother" }; // CreateDefaults 순서와 같게

    [MenuItem("OTK/캐릭터 프로필 열기")]
    static void SelectRoster()
    {
        Selection.activeObject = EnsureAssets();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    [MenuItem("OTK/캐릭터 프로필 점검")]
    static void ValidateFromMenu()
    {
        List<string> failures = RunChecks();
        EditorUtility.DisplayDialog("캐릭터 프로필 점검", failures.Count == 0 ? "문제 없음" : string.Join("\n", failures), "확인");
    }

    // 프로필·목록 에셋이 없으면 기본값으로 만듦 (있으면 기획이 고친 값을 그대로 둠)
    public static CharacterRoster EnsureAssets()
    {
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterAsset);
        if (roster != null && roster.profiles != null && roster.profiles.Length > 0) return roster;
        CharacterRoster.Reload(); // 에셋이 없던 동안 읽어 둔 기본값을 버림

        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(ProfileFolder)) AssetDatabase.CreateFolder("Assets/Data", "Characters");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        CharacterProfile[] defaults = CharacterRoster.CreateDefaults();
        var profiles = new CharacterProfile[defaults.Length];
        for (int i = 0; i < defaults.Length; i++)
        {
            string path = $"{ProfileFolder}/{ProfileFiles[i]}.asset";
            profiles[i] = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path);
            if (profiles[i] == null)
            {
                AssetDatabase.CreateAsset(defaults[i], path);
                profiles[i] = defaults[i];
                Debug.Log($"{Tag} 프로필을 기본값으로 만듦: {path}");
            }
        }

        if (roster == null)
        {
            roster = ScriptableObject.CreateInstance<CharacterRoster>();
            AssetDatabase.CreateAsset(roster, RosterAsset);
        }
        roster.profiles = profiles;
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 캐릭터 목록: {RosterAsset}");
        return roster;
    }

    public static List<string> RunChecks()
    {
        EnsureAssets();
        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            Debug.Log($"{Tag} {(ok ? "OK" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }

        CharacterProfile sister = CharacterRoster.For(CharacterType.Player1);
        CharacterProfile brother = CharacterRoster.For(CharacterType.Player2);
        Check(AssetDatabase.Contains(CharacterRoster.Instance), "Resources의 캐릭터 목록 에셋을 읽음 (기본값 아님)");
        Check(sister != null && brother != null, "0번·1번 프로필이 있음");
        if (sister == null || brother == null) return failures;

        Check(CharacterRoster.NameOf(CharacterType.Player1) == sister.displayName && CharacterRoster.NameOf(CharacterType.Player2) == brother.displayName,
            $"표시 이름은 프로필에서 ({sister.displayName} / {brother.displayName})");
        Check(brother.inventoryCapacity > sister.inventoryCapacity, $"오빠가 더 많이 듦 ({sister.inventoryCapacity} / {brother.inventoryCapacity})");
        Check(sister.Has(CharacterTrait.Investigation) && !brother.Has(CharacterTrait.Investigation), "정보 수집 = 여동생만");
        Check(brother.Has(CharacterTrait.Strength) && !sister.Has(CharacterTrait.Strength), "힘 = 오빠만");
        Check(brother.Has(CharacterTrait.Mechanics) && !sister.Has(CharacterTrait.Mechanics), "기계 조작 = 오빠만");

        Check(CharacterRoster.CanUse(CharacterType.Player1, CharacterType.All, CharacterTrait.None)
              && CharacterRoster.CanUse(CharacterType.Player2, CharacterType.All, CharacterTrait.None), "조건 없음 → 누구나");
        Check(CharacterRoster.CanUse(CharacterType.Player2, CharacterType.All, CharacterTrait.Strength)
              && !CharacterRoster.CanUse(CharacterType.Player1, CharacterType.All, CharacterTrait.Strength), "특성 조건 판정");
        Check(CharacterRoster.CanUse(CharacterType.Player1, CharacterType.Player1, CharacterTrait.None)
              && !CharacterRoster.CanUse(CharacterType.Player2, CharacterType.Player1, CharacterTrait.None), "특정 캐릭터 조건(예전 방식) 판정");
        Check(!CharacterRoster.CanUse(CharacterType.Player1, CharacterType.Player1, CharacterTrait.Strength), "두 조건을 모두 만족해야 함");

        // 소지품 칸 수 (임시 오브젝트)
        foreach (CharacterProfile profile in new[] { sister, brother })
        {
            var go = new GameObject("프로필 점검용");
            try
            {
                go.AddComponent<PlayerInteraction>().myType = profile.type;
                var inventory = go.AddComponent<Inventory>();
                int added = 0;
                for (int i = 0; i < profile.inventoryCapacity + 2; i++)
                    if (inventory.TryAddItem("물건 " + i)) added++;
                Check(added == profile.inventoryCapacity && inventory.IsFull, $"{profile.displayName}: {profile.inventoryCapacity}칸이 차면 더 못 넣음");
            }
            finally { Object.DestroyImmediate(go); }
        }
        return failures;
    }

    public static void RunFromCommandLine()
    {
        List<string> failures = RunChecks();
        Debug.Log($"{Tag} 완료: 실패 {failures.Count}개");
        EditorApplication.Exit(failures.Count == 0 ? 0 : 3);
    }
}
