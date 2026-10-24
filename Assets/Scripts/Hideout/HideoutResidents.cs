using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// 아지트의 남매 자리: 조작하지 않는 캐릭터는 자기 쉬는 자리(HideoutSpot)에 있음.
// Tab으로 바꾸면 조작하던 캐릭터는 전환 페이드 동안 자기 자리로 순간이동 (사용자 결정 2026-10-24).
// 자리는 휴식할 때와 임무에서 돌아올 때 무작위로 바뀜 (전환할 때마다 바뀌면 산만). 임무 장소에 가 있는 동안은 아무도 자리에 없음.
public class HideoutResidents : MonoBehaviour
{
    public static HideoutResidents instance;

    public HideoutSpot[] spots = new HideoutSpot[0];

    private readonly Dictionary<CharacterType, HideoutSpot> current = new Dictionary<CharacterType, HideoutSpot>();

    public HideoutSpot SpotOf(CharacterType who) => current.TryGetValue(who, out HideoutSpot spot) ? spot : null;

    void Awake() => instance = this;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Start()
    {
        Shuffle();
        PlaceIdle();
        if (CharacterManager.instance != null) CharacterManager.instance.ActivePlayerChanged += OnActiveChanged;
    }

    void OnDisable()
    {
        if (CharacterManager.instance != null) CharacterManager.instance.ActivePlayerChanged -= OnActiveChanged;
    }

    void OnActiveChanged(GameObject active) => PlaceIdle();

    // 남매마다 자기 자리 중 하나를 고름 (가능하면 지금과 다른 자리)
    public void Shuffle()
    {
        foreach (CharacterType who in new[] { CharacterType.Player1, CharacterType.Player2 })
        {
            HideoutSpot[] mine = spots.Where(s => s != null && s.owner == who).ToArray();
            if (mine.Length == 0) continue;
            HideoutSpot before = SpotOf(who);
            HideoutSpot[] choices = mine.Length > 1 ? mine.Where(s => s != before).ToArray() : mine;
            current[who] = choices[Random.Range(0, choices.Length)];
        }
    }

    // 조작하지 않는 캐릭터를 자기 자리로 (조작 중인 캐릭터는 그대로)
    public void PlaceIdle()
    {
        foreach (HideoutSpot spot in spots) if (spot != null) spot.Occupied = false;
        if (CharacterManager.instance == null || GameSession.Deployed) return;
        GameObject active = CharacterManager.instance.GetActivePlayer();
        foreach (GameObject player in CharacterManager.instance.players)
        {
            if (player == active) continue;
            HideoutSpot spot = SpotOf(CharacterRoster.TypeOf(player));
            if (spot == null) continue;
            var agent = player.GetComponent<NavMeshAgent>();
            if (agent != null && NavMesh.SamplePosition(spot.transform.position, out NavMeshHit hit, 1f, NavMesh.AllAreas)) agent.Warp(hit.position);
            else player.transform.position = spot.transform.position;
            player.transform.rotation = spot.transform.rotation;
            spot.Occupied = true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
}
