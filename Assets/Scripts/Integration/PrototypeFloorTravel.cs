using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using NarrativeGraybox.Interaction;

// 기존 씬을 다시 생성하지 않고, 플레이할 때만 임시 2층과 왕복 통로를 추가합니다.
public sealed class PrototypeFloorTravel : MonoBehaviour
{
    private PrototypeIntegration integration;
    private Bounds entrance;
    private Vector3 returnPoint;
    private readonly Vector3 upperSpawn = new Vector3(50f, 0f, -2f);
    private readonly Bounds upperExit = new Bounds(new Vector3(50f, 1f, -4f), new Vector3(2.4f, 4f, 1f));
    private bool travelling;
    private float nextTravelTime;
    private NavMeshSurface surface;

    public static void EnsureCreated(PrototypeIntegration owner)
    {
        if (owner.GetComponent<PrototypeFloorTravel>() != null) return;
        var firstStep = GameObject.Find("MainStair_0");
        var lastStep = GameObject.Find("MainStair_6");
        if (firstStep == null || lastStep == null)
        {
            Debug.LogWarning("[OTK] 층 이동: 계단 오브젝트를 찾지 못했습니다.");
            return;
        }
        var travel = owner.gameObject.AddComponent<PrototypeFloorTravel>();
        travel.integration = owner;
        travel.Build(firstStep, lastStep);
    }

    private void Build(GameObject first, GameObject last)
    {
        Bounds step = first.GetComponent<Renderer>().bounds;
        Vector3 outward = Vector3.ProjectOnPlane(first.transform.position - last.transform.position, Vector3.up).normalized;
        // 첫 단 앞의 평평한 바닥에서 감지하므로 기존 NavMesh가 계단을 못 올라가도 작동합니다.
        Vector3 approach = step.center + outward * (step.extents.z + .55f);
        approach.y = 1f;
        entrance = new Bounds(approach, new Vector3(step.size.x, 4f, 1.1f));
        returnPoint = approach + outward * 1.3f;
        returnPoint.y = 0f;

        var root = new GameObject("Prototype Temporary Second Floor");
        root.transform.SetParent(transform);
        Material material = first.GetComponent<Renderer>().sharedMaterial;
        Box(root.transform, "2F Floor", new Vector3(50, -.25f, 0), new Vector3(12, .5f, 10), material);
        Box(root.transform, "2F North", new Vector3(50, 1.25f, 5), new Vector3(12, 2.5f, .2f), material);
        Box(root.transform, "2F West", new Vector3(44, 1.25f, 0), new Vector3(.2f, 2.5f, 10), material);
        Box(root.transform, "2F East", new Vector3(56, 1.25f, 0), new Vector3(.2f, 2.5f, 10), material);
        Box(root.transform, "2F South", new Vector3(50, .25f, -5), new Vector3(12, .5f, .2f), material);
        surface = root.AddComponent<NavMeshSurface>();
        surface.agentTypeID = CharacterManager.instance.players[0].GetComponent<NavMeshAgent>().agentTypeID;
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        // 평평한 귀환 표시. 이동 경로를 막는 콜라이더는 만들지 않습니다.
        for (int i = 0; i < 3; i++)
        {
            var stripe = Box(root.transform, "Downstairs marker", new Vector3(50, .015f, -3.8f - i * .23f), new Vector3(2, .03f, .12f), material);
            stripe.GetComponent<Collider>().enabled = false;
        }
        var zone = new GameObject("Zone_SecondFloor");
        zone.transform.SetParent(root.transform);
        zone.transform.position = new Vector3(50, 1.5f, 0);
        var trigger = zone.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(12, 5, 10);
        var cameraObject = new GameObject("Cam_SecondFloor");
        cameraObject.transform.SetParent(zone.transform);
        var rotation = Quaternion.LookRotation(-new Vector3(-12, 12, -12));
        cameraObject.transform.SetPositionAndRotation(zone.transform.position - rotation * Vector3.forward * 20, rotation);
        var camera = cameraObject.AddComponent<CinemachineCamera>();
        var lens = LensSettings.Default;
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        lens.OrthographicSize = 7f;
        lens.NearClipPlane = .1f;
        lens.FarClipPlane = 100f;
        camera.Lens = lens;
        cameraObject.SetActive(false);
        var room = zone.AddComponent<CameraTrigger>();
        room.roomName = "2층";
        room.virtualCamera = cameraObject;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private void Update()
    {
        if (travelling || Time.unscaledTime < nextTravelTime || FadeManager.instance.isFading) return;
        var player = CharacterManager.instance.GetActivePlayer();
        if (player.GetComponent<PlayerInteractor3D>().IsBusy || integration.hud.Selecting) return;
        if (entrance.Contains(player.transform.position)) StartCoroutine(Travel(player, upperSpawn));
        else if (upperExit.Contains(player.transform.position)) StartCoroutine(Travel(player, returnPoint));
    }

    private IEnumerator Travel(GameObject player, Vector3 destination)
    {
        var agent = player.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, 1.2f, filter))
        {
            Debug.LogError("[OTK] 층 이동 목적지에 걸을 수 있는 바닥이 없습니다.");
            nextTravelTime = Time.unscaledTime + 2f;
            yield break;
        }
        travelling = true;
        var fade = FadeManager.instance;
        fade.isFading = true;
        try
        {
            yield return fade.FadeOut();
            if (!agent.Warp(hit.position)) Debug.LogError("[OTK] 층 이동 Warp 실패");
            Physics.SyncTransforms();
            // 트리거가 새 위치를 받아 카메라와 HUD 구역을 바꿀 때까지 기다립니다.
            yield return new WaitForFixedUpdate();
            yield return null;
            foreach (var room in FindObjectsByType<CameraTrigger>(FindObjectsSortMode.None)) room.UpdateCameraStateInstant();
            yield return null;
            yield return fade.FadeIn();
        }
        finally
        {
            fade.isFading = false;
            travelling = false;
            nextTravelTime = Time.unscaledTime + 1f;
        }
    }

    private void OnDestroy()
    {
        if (surface != null) surface.RemoveData();
    }
}
