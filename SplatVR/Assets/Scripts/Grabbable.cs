using UnityEngine;

/// <summary>
/// Marks an object (e.g. a splat, which has no collider) as something a SplatGrabber can pick up.
/// The grab check is a simple distance test from the hand to this object's center.
/// </summary>
public class Grabbable : MonoBehaviour
{
    [Tooltip("How close (in meters) a hand must be to the center to grab this object.")]
    [SerializeField] private float _grabRadius = 0.15f;

    [Tooltip("Center of the object in its own local space. For a splat, its origin is often not its visual center.")]
    [SerializeField] private Vector3 _localCenter;

    public float GrabRadius => _grabRadius;

    public Vector3 Center => transform.TransformPoint(_localCenter);

    public bool IsHeld { get; set; }

    private void OnDrawGizmosSelected()
    {
        // Shows the grab zone in the Scene view, since splats have no collider to look at.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Center, _grabRadius);
    }
}
