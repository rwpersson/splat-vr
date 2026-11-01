using UnityEngine;

/// <summary>
/// Marks an object to be placed automatically on the scan (Phase 5): on a flat, solid, free spot of the scan's
/// auto collider at furniture height (a seat, a desk), the one nearest the player.
/// In the editor: "Place" in the Inspector, or Tools → SplatVR → Place Objects. SplatObjectPlacer does the work.
/// Holds data only; nothing runs on the headset.
/// </summary>
public class AutoPlace : MonoBehaviour
{
    [Tooltip("Scan to place the object on (its Auto Collider and Auto Floor are used).")]
    public SplatCollider Scan;

    [Tooltip("Lowest surface to use, in meters above the scan's floor (skips the floor and clutter on it).")]
    public float MinHeight = 0.3f;

    [Tooltip("Highest surface to use, in meters above the floor (skips shelves out of reach).")]
    public float MaxHeight = 1.3f;
}
