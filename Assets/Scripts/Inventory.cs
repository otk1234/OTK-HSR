using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public const string FullMessage = "더는 들 수 없다...";

    [Header("보유 중인 아이템 목록")]
    public List<string> items = new List<string>();

    // 최대 개수는 캐릭터 프로필에서 (같은 오브젝트의 PlayerInteraction.myType 기준)
    public int Capacity => CharacterRoster.CapacityOf(CharacterRoster.TypeOf(gameObject));
    public bool IsFull => items.Count >= Capacity;

    // 아이템을 습득할 때 외부에서 부를 함수 (칸 수를 따지지 않음 — 줍기는 TryAddItem)
    public void AddItem(string itemName)
    {
        items.Add(itemName);
        Debug.Log($"[{gameObject.name}] {itemName} 획득.");
    }

    // 칸이 남아 있을 때만 넣음
    public bool TryAddItem(string itemName)
    {
        if (IsFull) return false;
        AddItem(itemName);
        return true;
    }

    // 특정 아이템을 가지고 있는지 확인하는 함수
    public bool HasItem(string itemName)
    {
        return items.Contains(itemName);
    }
}
