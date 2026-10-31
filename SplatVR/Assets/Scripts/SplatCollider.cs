using UnityEngine;

/// <summary>
/// Settings for generating this splat's collider automatically from its source scan (Phase 5).
/// In the editor: click "Generate Collider" in the Inspector (or Tools → SplatVR → Generate All Colliders).
/// SplatColliderGenerator then runs splat-transform on the source file, imports the resulting collision mesh and puts it
/// on an "Auto Collider" child of this object, so the collider follows the splat's transform.
/// Holds data only; nothing runs on the headset.
/// </summary>
[RequireComponent(typeof(Gaussians.ThreeD.GaussianSplat3DRenderer))]
public class SplatCollider : MonoBehaviour
{
    [Tooltip("Scan the splat asset was made from (.ply, .spz, …), relative to the Unity project folder, e.g. ../captures/Desk 100k.ply")]
    public string SourceFile = "";

    [Tooltip("Voxel edge length in meters. Smaller = more detail and more triangles.")]
    public float VoxelSize = 0.02f;

    [Tooltip("How opaque a voxel must be to count as solid. Low values turn haze into walls; 0.7 fit the desk.")]
    [Range(0f, 1f)] public float SolidThreshold = 0.7f;

    [Tooltip("Remove floaters (splats that don't contribute to any solid voxel) first.")]
    public bool RemoveFloaters = true;

    [Tooltip("Command for splat-transform (npm i -g @playcanvas/splat-transform). A full path works too.")]
    public string ToolCommand = "splat-transform";
}
