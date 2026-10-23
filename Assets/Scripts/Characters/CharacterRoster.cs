using UnityEngine;

// 캐릭터 프로필 목록. Resources/CharacterRoster 에셋을 읽고, 없으면 기본값(여동생·오빠)을 씁니다.
//  - 이름: CharacterRoster.NameOf(CharacterType.Player1)
//  - "누가 할 수 있나" 판정은 모두 CanUse 하나로 (특정 캐릭터 조건 + 특성 조건)
[CreateAssetMenu(menuName = "OTK/캐릭터 목록", fileName = "CharacterRoster")]
public class CharacterRoster : ScriptableObject
{
    public const string RosterResource = "CharacterRoster";

    public CharacterProfile[] profiles;

    static CharacterRoster instance;

    public static CharacterRoster Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<CharacterRoster>(RosterResource);
            if (instance == null)
            {
                Debug.LogWarning($"[캐릭터] Resources/{RosterResource} 목록이 없어 기본값을 씁니다.");
                instance = CreateInstance<CharacterRoster>();
                instance.profiles = CreateDefaults();
            }
            return instance;
        }
    }

    // 기본 프로필 (사용자 확정 2026-10-22·23, 이름·나이·수치는 임시)
    //  0번 여동생 = 관찰·통찰·화술, 가벼운 몸(발소리 ×0.75), 스태미나 ×0.85
    //  1번 오빠 = 힘·기계 조작, 발소리 기본, 체력(스태미나 ×1.25)
    public static CharacterProfile[] CreateDefaults()
    {
        return new[]
        {
            Make(CharacterType.Player1, "여동생", 21, 5, CharacterTrait.Investigation | CharacterTrait.Insight | CharacterTrait.Persuasion, 0.75f, 0.85f),
            Make(CharacterType.Player2, "오빠", 23, 6, CharacterTrait.Strength | CharacterTrait.Mechanics, 1f, 1.25f),
        };
    }

    static CharacterProfile Make(CharacterType type, string name, int age, int capacity, CharacterTrait traits, float footstep, float stamina)
    {
        var profile = CreateInstance<CharacterProfile>();
        profile.name = name;
        profile.type = type;
        profile.displayName = name;
        profile.age = age;
        profile.inventoryCapacity = capacity;
        profile.traits = traits;
        profile.footstepMultiplier = footstep;
        profile.staminaMultiplier = stamina;
        return profile;
    }

    public static CharacterProfile For(CharacterType type)
    {
        foreach (CharacterProfile profile in Instance.profiles)
            if (profile != null && profile.type == type) return profile;
        return null;
    }

    public static CharacterType TypeOf(GameObject player)
    {
        PlayerInteraction interaction = player != null ? player.GetComponent<PlayerInteraction>() : null;
        return interaction != null ? interaction.myType : CharacterType.All;
    }

    public static string NameOf(CharacterType type)
    {
        CharacterProfile profile = For(type);
        return profile != null ? profile.displayName : type.ToString();
    }

    public static string NameOf(GameObject player)
    {
        CharacterProfile profile = For(TypeOf(player));
        return profile != null ? profile.displayName : player.name;
    }

    public static int CapacityOf(CharacterType type)
    {
        CharacterProfile profile = For(type);
        return profile != null ? profile.inventoryCapacity : int.MaxValue;
    }

    public static float FootstepMultiplierOf(GameObject player)
    {
        CharacterProfile profile = For(TypeOf(player));
        return profile != null ? profile.footstepMultiplier : 1f;
    }

    public static float StaminaMultiplierOf(GameObject player)
    {
        CharacterProfile profile = For(TypeOf(player));
        return profile != null ? profile.staminaMultiplier : 1f;
    }

    public static bool Has(CharacterType type, CharacterTrait trait)
    {
        if (trait == CharacterTrait.None) return true;
        CharacterProfile profile = For(type);
        return profile != null && profile.Has(trait);
    }

    // 특정 캐릭터 조건(All = 누구나)과 특성 조건(None = 없음)을 둘 다 만족해야 함
    public static bool CanUse(CharacterType who, CharacterType requiredCharacter, CharacterTrait requiredTrait) =>
        (requiredCharacter == CharacterType.All || requiredCharacter == who) && Has(who, requiredTrait);

    // 에셋을 새로 만들거나 고친 뒤 다시 읽게 함 (도메인 리로드가 꺼져 있어 static이 남음)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reload() => instance = null;
}
