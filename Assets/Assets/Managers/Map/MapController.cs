using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a hand-authored branching map. On load it rebuilds every node's state from RunManager
/// (which persists across the map -> combat -> map reload), so cleared nodes stay cleared and only
/// the nodes reachable from your current position are clickable. Clicking a combat node hands off
/// to <see cref="RunManager.GoToEncounter"/>; clicking a non-combat node (Shop/Rest/Event) is a
/// TODO panel hook that, for now, just advances the path (Rest also heals as a placeholder).
///
/// Authoring: place MapNode buttons in the scene, list them all in <see cref="allNodes"/>, list the
/// bottom row in <see cref="entryNodes"/>, and link each node's <c>next</c>. See MAP_DESIGN.md.
/// </summary>
public class MapController : MonoBehaviour
{
    [Header("Graph")]
    [Tooltip("Every MapNode in this map.")]
    public List<MapNode> allNodes = new();

    [Tooltip("The starting (bottom) row — reachable when the map is fresh.")]
    public List<MapNode> entryNodes = new();

    [Header("Run")]
    [Tooltip("Max HP used if we enter the map without an active run (i.e. starting a fresh run here).")]
    public int startingMaxHP = 20;

    void Start()
    {
        var rm = EnsureRunManager();
        if (!rm.runActive) rm.StartNewRun(startingMaxHP);   // entered fresh (e.g. from a menu)

        foreach (var n in allNodes) if (n) n.Bind(this);
        Rebuild(rm);
    }

    void Rebuild(RunManager rm)
    {
        var reachable = ComputeReachable(rm);
        foreach (var n in allNodes)
        {
            if (!n) continue;
            n.SetState(rm.IsNodeCleared(n.nodeId), reachable.Contains(n));
        }
    }

    // Reachable = the bottom row when nothing is cleared yet, otherwise the (uncleared) `next` of
    // every cleared node. Picking one node clears only it, so its row-siblings drop out — that's
    // what commits you to a path, StS-style. Converging paths just work (any cleared parent makes
    // a node reachable).
    HashSet<MapNode> ComputeReachable(RunManager rm)
    {
        var reachable = new HashSet<MapNode>();

        bool anyCleared = false;
        foreach (var n in allNodes)
            if (n && rm.IsNodeCleared(n.nodeId)) { anyCleared = true; break; }

        if (!anyCleared)
        {
            foreach (var e in entryNodes) if (e) reachable.Add(e);
            return reachable;
        }

        foreach (var n in allNodes)
        {
            if (!n || !rm.IsNodeCleared(n.nodeId) || n.next == null) continue;
            foreach (var nx in n.next)
                if (nx && !rm.IsNodeCleared(nx.nodeId)) reachable.Add(nx);
        }
        return reachable;
    }

    public void OnNodeChosen(MapNode node)
    {
        if (node == null) return;
        var rm = EnsureRunManager();
        rm.currentNodeId = node.nodeId;

        switch (node.kind)
        {
            case MapNode.NodeKind.Combat:
            case MapNode.NodeKind.Dire:
            case MapNode.NodeKind.Boss:
                if (node.encounter == null)
                {
                    Debug.LogWarning($"MapNode '{node.nodeId}' is a combat node but has no encounter assigned.");
                    return;
                }
                rm.GoToEncounter(node.encounter, node.combatScene, node.nodeId);
                break;

            case MapNode.NodeKind.Rest:
                // Placeholder until the Rest panel (heal / Snack / minigame) exists.
                rm.playerCurrentHP = rm.playerMaxHP;
                rm.MarkNodeCleared(node.nodeId);
                Rebuild(rm);
                break;

            // Shop / Event: open the relevant panel here later. For now, mark cleared so the path
            // advances and the loop stays testable.
            default:
                rm.MarkNodeCleared(node.nodeId);
                Rebuild(rm);
                break;
        }
    }

    RunManager EnsureRunManager()
    {
        if (RunManager.Instance != null) return RunManager.Instance;
        return new GameObject("RunManager").AddComponent<RunManager>();
    }
}
