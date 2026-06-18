using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One node on a hand-authored map. Works two ways:
///
///  • WORLD-SPACE (recommended for this 2D game): put it on a GameObject with a SpriteRenderer +
///    a Collider2D. Click the sprite directly — no Canvas needed. Position it in the scene.
///  • UI: put it on a UI Button under a Canvas. The Button's onClick is hooked automatically.
///
/// Either way, link each node's <see cref="next"/> to form the branching graph. The node's
/// <see cref="kind"/> decides what clicking it does; combat kinds hand off to
/// <see cref="RunManager.GoToEncounter"/>. State (cleared / reachable) is driven by
/// <see cref="MapController"/> from RunManager, so it survives the map -> combat -> map reload.
///
/// See MAP_DESIGN.md (node types) and MAP_INTEGRATION.md (the combat handoff).
/// </summary>
public class MapNode : MonoBehaviour
{
    public enum NodeKind { Combat, Dire, Shop, Rest, Boss, Event }

    [Header("Identity")]
    [Tooltip("Stable, unique id within this map. Remembers it's cleared. Defaults to the object name if blank.")]
    public string nodeId;

    public NodeKind kind = NodeKind.Combat;

    [Header("Combat kinds (Combat / Dire / Boss)")]
    [Tooltip("The fight this node starts. Required for combat kinds.")]
    public EncounterData encounter;

    [Tooltip("Combat scene to load. Must be in Build Settings.")]
    public string combatScene = "Combat";

    [Header("Graph")]
    [Tooltip("Nodes this one unlocks once cleared (edges going 'up' the map).")]
    public MapNode[] next;

    [Header("Visual feedback (world-space sprite nodes)")]
    [Tooltip("Tinted to show state. Auto-found from this object's SpriteRenderer if left empty.")]
    public SpriteRenderer icon;
    public Color reachableTint = Color.white;
    public Color lockedTint = new Color(1f, 1f, 1f, 0.35f);
    public Color clearedTint = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Runtime (set by MapController)")]
    public bool isCleared;
    public bool isReachable;

    Button button;          // optional (UI setup)
    MapController controller;

    public void Bind(MapController owner)
    {
        controller = owner;
        if (string.IsNullOrEmpty(nodeId)) nodeId = name;

        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
            button.onClick.AddListener(OnClicked);
        }
        if (icon == null) icon = GetComponent<SpriteRenderer>();
    }

    /// <summary>MapController calls this to reflect run state: only reachable, uncleared nodes respond.</summary>
    public void SetState(bool cleared, bool reachable)
    {
        isCleared = cleared;
        isReachable = reachable;

        if (button == null) button = GetComponent<Button>();
        if (button != null) button.interactable = reachable && !cleared;

        if (icon == null) icon = GetComponent<SpriteRenderer>();
        if (icon != null)
            icon.color = cleared ? clearedTint : (reachable ? reachableTint : lockedTint);
    }

    // World-space nodes: clicked directly via their Collider2D. Gated by state so locked/cleared
    // nodes ignore clicks. (Needs a Collider2D on the same object; uses the scene camera.)
    void OnMouseDown()
    {
        if (isReachable && !isCleared) OnClicked();
    }

    void OnClicked()
    {
        if (controller != null) controller.OnNodeChosen(this);
    }
}
