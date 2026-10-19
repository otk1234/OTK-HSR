using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    [Header("보유 중인 아이템 목록")]
    public List<string> items = new List<string>();

    // 아이템을 습득할 때 외부에서 부를 함수
    public void AddItem(string itemName)
    {
        items.Add(itemName);
        Debug.Log($"[{gameObject.name}] {itemName} 획득.");
    }

    // 특정 아이템을 가지고 있는지 확인하는 함수
    public bool HasItem(string itemName)
    {
        return items.Contains(itemName);
    }
}