using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// 추격자 이동. NavMeshAgent로 걷고/뛰며, 문·창문·환풍구(EntryPoint 링크)를 직접 처리합니다.
//  - 잠기지 않은 닫힌 문: 열고 지나감 (문 소리)
//  - 지나가려는 순간 잠긴 문: 되돌아가고 Blocked 이벤트
//  - 창문·환풍구: 정해진 시간 동안 넘어가거나 기어서 지나감
[RequireComponent(typeof(NavMeshAgent))]
public class ChaserMotor : MonoBehaviour
{
    public ChaserType chaserType = ChaserType.Police;
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.6f;
    [Tooltip("문을 연 뒤 지나가기 전까지 기다리는 최대 시간")]
    public float doorOpenWait = 0.6f;
    [Tooltip("true면 문·통로 소음을 줄임 (잠입)")]
    public bool quiet;
    public float quietNoiseScale = 0.35f;

    public NavMeshAgent Agent { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsTraversing { get; private set; }
    public EntryPoint CurrentEntry { get; private set; }
    public EntryPoint LastBlockedBy { get; private set; }

    public event Action<EntryPoint> Blocked;
    public event Action<EntryPoint> Traversed;

    float NoiseScale => quiet ? quietNoiseScale : 1f;

    void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        Agent.autoTraverseOffMeshLink = false;
        Agent.areaMask = EntryPoint.AreaMaskFor(chaserType);
        SetRunning(false);
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
        Agent.speed = running ? runSpeed : walkSpeed;
    }

    public bool SetDestination(Vector3 destination)
    {
        LastBlockedBy = null;
        return Agent.isOnNavMesh && Agent.SetDestination(destination);
    }

    public void Stop()
    {
        if (Agent.isOnNavMesh && !IsTraversing) Agent.ResetPath();
    }

    // 순간 이동 (연출·디버그용). 문·창문을 지나는 중이면 그 동작을 취소합니다.
    public void Teleport(Vector3 floorPosition)
    {
        StopAllCoroutines();
        IsTraversing = false;
        CurrentEntry = null;
        Agent.Warp(floorPosition);
        Agent.ResetPath();
    }

    // 목적지까지 끊김 없는 길이 있는가 (잠긴 문 때문에 끊기면 false)
    public bool HasCompletePath => !Agent.pathPending && Agent.hasPath && Agent.pathStatus == NavMeshPathStatus.PathComplete;

    public bool Arrived(float tolerance = 0.3f) =>
        !IsTraversing && !Agent.pathPending && (!Agent.hasPath || Agent.remainingDistance <= Mathf.Max(tolerance, Agent.stoppingDistance));

    void Update()
    {
        if (!IsTraversing && Agent.isOnOffMeshLink) StartCoroutine(Traverse());
    }

    IEnumerator Traverse()
    {
        IsTraversing = true;
        OffMeshLinkData data = Agent.currentOffMeshLinkData;
        EntryPoint entry = EntryPoint.FromLink(data.owner, data.startPos);
        CurrentEntry = entry;
        Vector3 end = data.endPos + Vector3.up * Agent.baseOffset;
        float duration = 0.4f;

        if (entry != null)
        {
            duration = entry.traverseTime;

            if (entry.door != null)
            {
                InteractableDoor door = entry.door;
                if (door.IsLocked)
                {
                    // 길을 정한 뒤에 누군가 잠갔음: 링크에 들어가지 않고 되돌아갑니다.
                    Agent.Warp(data.startPos);
                    Agent.ResetPath();
                    IsTraversing = false;
                    CurrentEntry = null;
                    LastBlockedBy = entry;
                    Blocked?.Invoke(entry);
                    yield break;
                }
                if (!door.IsOpen)
                {
                    door.OpenByAI(gameObject, NoiseScale);
                    for (float t = 0f; t < doorOpenWait && door.IsMoving; t += Time.deltaTime) yield return null;
                }
            }
            else
            {
                NoiseSystem.Emit(transform.position, entry.traverseNoise * NoiseScale,
                    entry.kind == EntryKind.Vent ? NoiseKind.Vent : NoiseKind.Window, gameObject);
            }
        }

        Vector3 from = transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(end - from, Vector3.up);
        if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, end, t / duration);
            yield return null;
        }
        transform.position = end;

        Agent.CompleteOffMeshLink();
        IsTraversing = false;
        CurrentEntry = null;
        if (entry != null) Traversed?.Invoke(entry);
    }
}
