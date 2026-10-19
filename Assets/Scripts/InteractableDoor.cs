using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class InteractableDoor : MonoBehaviour
{
    [Header("필요한 아이템 (빈칸이면 그냥 열림)")]
    public string requiredItemName = "GoldKey";

    [Header("메시지 설정")]
    public string lockedMessage = "굳게 잠겨 있다.";
    public string openedMessage = "철컥 소리와 함께 문이 열렸다.";
    public string closedMessage = "문을 닫았다."; // [추가] 닫혔을 때 메시지

    [Header("문 열림 연출 설정")]
    public float openAngle = 90f;
    public float openSpeed = 2f;

    private bool isOpen = false;    
    private bool isMoving = false;   
    private Quaternion closedRotation; 

    void Start()
    {
        
        closedRotation = transform.rotation;
    }


    public void Interact(Inventory playerInventory)
    {
        if (isMoving) return; 

        if (!isOpen)
        {
       
            if (string.IsNullOrEmpty(requiredItemName) || playerInventory.HasItem(requiredItemName))
            {
                Debug.Log($"[문] {openedMessage}");
                StartCoroutine(MoveDoorRoutine(true));
            }
            else
            {
                Debug.Log($"[문] {lockedMessage}");
            }
        }
        else
        {
          
            Debug.Log($"[문] {closedMessage}");
            StartCoroutine(MoveDoorRoutine(false));
        }
    }

    private IEnumerator MoveDoorRoutine(bool opening)
    {
        isMoving = true; 
        isOpen = opening; 

        NavMeshObstacle obstacle = GetComponentInChildren<NavMeshObstacle>();


        if (opening && obstacle != null) obstacle.enabled = false;

        Quaternion startRotation = transform.rotation;
 
        Quaternion endRotation = opening ? closedRotation * Quaternion.Euler(0, openAngle, 0) : closedRotation;

        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.deltaTime * openSpeed;
            transform.rotation = Quaternion.Slerp(startRotation, endRotation, timer);
            yield return null;
        }

        transform.rotation = endRotation; 


        if (!opening && obstacle != null) obstacle.enabled = true;

        isMoving = false; 
    }
}