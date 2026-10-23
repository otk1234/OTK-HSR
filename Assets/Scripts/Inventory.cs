using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public const string FullMessage = "더는 들 수 없다...";

    // 이 아이템을 다룰 수 있는 조건 (주운 물건의 조건을 기억해 두고, 건네줄 때 받는 쪽도 만족해야 함)
    public struct ItemRule
    {
        public CharacterType requiredCharacter;
        public CharacterTrait requiredTrait;
    }

    [Header("보유 중인 아이템 목록")]
    public List<string> items = new List<string>();

    // 조건이 있는 아이템만 (조사로 얻은 열쇠처럼 조건이 없으면 누구나)
    private readonly Dictionary<string, ItemRule> rules = new Dictionary<string, ItemRule>();

    // 최대 개수는 캐릭터 프로필에서 (같은 오브젝트의 PlayerInteraction.myType 기준)
    public int Capacity => CharacterRoster.CapacityOf(CharacterRoster.TypeOf(gameObject));
    public bool IsFull => items.Count >= Capacity;

    // 아이템을 습득할 때 외부에서 부를 함수 (칸 수를 따지지 않음 — 줍기는 TryAddItem)
    public void AddItem(string itemName)
    {
        items.Add(itemName);
        Debug.Log($"[{gameObject.name}] {itemName} 획득.");
    }

    // 다룰 수 있는 조건과 함께 넣음 (InteractableItem 줍기, 건네받기)
    public void AddItem(string itemName, ItemRule rule)
    {
        AddItem(itemName);
        if (rule.requiredCharacter != CharacterType.All || rule.requiredTrait != CharacterTrait.None) rules[itemName] = rule;
    }

    // 칸이 남아 있을 때만 넣음
    public bool TryAddItem(string itemName)
    {
        if (IsFull) return false;
        AddItem(itemName);
        return true;
    }

    // 빼기 (건네줄 때). 문·퍼즐은 아이템을 쓰고도 빼지 않음 (사용자 결정 2026-10-23)
    public bool RemoveItem(string itemName)
    {
        if (!items.Remove(itemName)) return false;
        if (!items.Contains(itemName)) rules.Remove(itemName);
        return true;
    }

    public ItemRule RuleOf(string itemName) => rules.TryGetValue(itemName, out ItemRule rule) ? rule : default;

    // 이 캐릭터가 이 아이템을 다룰 수 있는가
    public static bool CanHandle(CharacterType who, ItemRule rule) =>
        CharacterRoster.CanUse(who, rule.requiredCharacter, rule.requiredTrait);

    // 특정 아이템을 가지고 있는지 확인하는 함수
    public bool HasItem(string itemName)
    {
        return items.Contains(itemName);
    }
}
