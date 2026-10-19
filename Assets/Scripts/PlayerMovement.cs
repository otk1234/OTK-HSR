using UnityEngine;
using UnityEngine.AI;

public class PlayerMovement : MonoBehaviour
{
    [Header("이동 속도")]
    public float moveSpeed = 5f;

    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
    }

    void Update()
    {

        if (FadeManager.instance != null && FadeManager.instance.isFading)
        {

            return;
        }


        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 moveDir = new Vector3(h, 0f, v).normalized;

        if (moveDir.magnitude >= 0.1f)
        {
            agent.Move(moveDir * moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * Time.deltaTime);
        }
    }
}