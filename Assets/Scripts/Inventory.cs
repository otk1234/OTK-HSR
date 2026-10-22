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

    [Header("손에 든 아이템 (비어 있으면 null)")]
    public string leftHand;
    public string rightHand;
    [Tooltip("켜면 문 열기 같은 사용 조건에서 소지품이 아니라 손에 든 아이템만 인정한다")]
    public bool requireInHand;

    // 특정 아이템을 가지고 있는지 확인하는 함수
    public bool HasItem(string itemName)
    {
        return items.Contains(itemName);
    }

    public bool IsInHand(string itemName)
    {
        return leftHand == itemName || rightHand == itemName;
    }

    // 아이템을 '사용'할 수 있는지: requireInHand면 손에 들고 있어야 한다
    public bool CanUse(string itemName)
    {
        return requireInHand ? IsInHand(itemName) : HasItem(itemName);
    }
}