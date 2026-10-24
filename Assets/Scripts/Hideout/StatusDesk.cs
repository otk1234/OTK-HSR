using System.Text;
using UnityEngine;

// 아지트 작업대: 장비·현황 점검 (남매 프로필·소지품·몸, 죄책감 단계). 개발용 창, 실제 화면은 UI 담당.
public class StatusDesk : HideoutStation
{
    protected override string Title => "작업대 — 장비·현황 점검";
    protected override bool CenteredWindow => true;

    protected override string BuildText() => Describe();

    public static string Describe()
    {
        var text = new StringBuilder();
        if (CharacterManager.instance != null)
            foreach (GameObject player in CharacterManager.instance.players)
            {
                if (player == null) continue;
                CharacterType type = CharacterRoster.TypeOf(player);
                CharacterProfile profile = CharacterRoster.For(type);
                var inventory = player.GetComponent<Inventory>();
                text.AppendLine($"<b>{profile.displayName}</b> ({profile.age}세)   소지품 {inventory.items.Count} / {inventory.Capacity}");
                text.AppendLine($"  특성: {TraitNames(profile.traits)}");
                text.AppendLine($"  몸: 발소리 ×{profile.footstepMultiplier:0.##} · 스태미나 ×{profile.staminaMultiplier:0.##}");
                text.Append("  장비: ");
                if (inventory.items.Count == 0) text.AppendLine("(없음)");
                else
                {
                    text.AppendLine();
                    foreach (string item in inventory.items) text.AppendLine($"    · {item}{RuleLabel(inventory.RuleOf(item))}");
                }
                text.AppendLine();
            }
        text.AppendLine($"<b>상자</b>: {GameSession.Stash.Count}개");
        text.Append($"<b>죄책감</b>: {GuiltSystem.TierName} ({GuiltSystem.Value})");
        return text.ToString();
    }
}
