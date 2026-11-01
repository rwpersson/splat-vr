using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Places AutoPlace objects on their scan (Phase 5): probes the scan's auto collider from above on a 5 cm grid and
/// keeps spots that are
///   - level (surface within 15° of horizontal),
///   - at furniture height (AutoPlace.MinHeight–MaxHeight above the auto floor),
///   - flat across the object's footprint (a ring of probes all within 1.5 cm of the same height: no edges or lumps),
///   - free (nothing else in the box the object would occupy, e.g. not inside the tissue box),
/// then puts the object 1 cm above the one nearest the player, upright.
/// Logs with a "[Placer]" tag.
/// </summary>
public static class SplatObjectPlacer
{
    private const float GridStep = 0.05f;
    private const float MaxTiltDegrees = 15f;
    private const float MaxHeightDifference = 0.015f;
    private const int RingProbes = 8;
    private const float Gap = 0.01f;

    [MenuItem("Tools/SplatVR/Place Objects")]
    private static void PlaceAll()
    {
        foreach (AutoPlace target in Object.FindObjectsByType<AutoPlace>(FindObjectsSortMode.None))
            Place(target);
    }

    public static bool Place(AutoPlace target)
    {
        if (target.Scan == null)
        {
            Debug.LogError($"[Placer] {target.name}: no Scan set", target);
            return false;
        }
        Transform scan = target.Scan.transform;
        Transform colliderChild = scan.Find("Auto Collider");
        MeshCollider surface = colliderChild != null ? colliderChild.GetComponent<MeshCollider>() : null;
        Transform floorChild = scan.Find("Auto Floor");
        if (surface == null || floorChild == null)
        {
            Debug.LogError($"[Placer] {target.name}: {scan.name} has no Auto Collider / Auto Floor (Generate Collider first)", target);
            return false;
        }
        float floorY = floorChild.GetComponent<BoxCollider>().bounds.max.y;

        // The object's own size, from its colliders (switched off while probing so they don't block themselves).
        Collider[] own = target.GetComponentsInChildren<Collider>();
        if (own.Length == 0)
        {
            Debug.LogError($"[Placer] {target.name}: needs a collider to know its size", target);
            return false;
        }
        Bounds size = own[0].bounds;
        foreach (Collider c in own)
            size.Encapsulate(c.bounds);
        float footprint = Mathf.Max(size.extents.x, size.extents.z);
        float pivotAboveBottom = target.transform.position.y - size.min.y;
        Vector3 pivotOffsetXZ = target.transform.position - size.center;
        pivotOffsetXZ.y = 0f;

        var player = Object.FindAnyObjectByType<SimpleLocomotion>();
        Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;

        var wasEnabled = new bool[own.Length];
        for (int i = 0; i < own.Length; i++)
        {
            wasEnabled[i] = own[i].enabled;
            own[i].enabled = false;
        }
        Physics.SyncTransforms();

        Vector3 best = default;
        float bestDistance = float.MaxValue;
        int level = 0, inBand = 0, flat = 0, free = 0;
        try
        {
            Bounds area = surface.bounds;
            float top = area.max.y + 0.5f;
            float minCos = Mathf.Cos(MaxTiltDegrees * Mathf.Deg2Rad);
            for (float x = area.min.x; x <= area.max.x; x += GridStep)
            {
                for (float z = area.min.z; z <= area.max.z; z += GridStep)
                {
                    if (!Probe(surface, x, z, top, out RaycastHit hit) || hit.normal.y < minCos)
                        continue;
                    level++;
                    float height = hit.point.y - floorY;
                    if (height < target.MinHeight || height > target.MaxHeight)
                        continue;
                    inBand++;
                    if (!IsFlat(surface, hit.point, footprint, top, minCos))
                        continue;
                    flat++;
                    // Room for the object: its box, lifted by the gap, must not touch anything.
                    Vector3 boxCenter = hit.point + Vector3.up * (Gap + size.extents.y);
                    if (Physics.CheckBox(boxCenter, new Vector3(footprint, size.extents.y, footprint)))
                        continue;
                    free++;
                    float distance = Vector2.Distance(new Vector2(x, z), new Vector2(playerPosition.x, playerPosition.z));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = hit.point;
                    }
                }
            }
        }
        finally
        {
            for (int i = 0; i < own.Length; i++)
                own[i].enabled = wasEnabled[i];
        }

        string counts = $"{level} level spots → {inBand} at {target.MinHeight}–{target.MaxHeight} m → {flat} flat for a {footprint * 2:F2} m footprint → {free} free";
        if (bestDistance == float.MaxValue)
        {
            Debug.LogWarning($"[Placer] {target.name}: no spot found ({counts})", target);
            return false;
        }

        Undo.RecordObject(target.transform, "Auto Place");
        target.transform.SetPositionAndRotation(
            best + pivotOffsetXZ + Vector3.up * (Gap + pivotAboveBottom),
            Quaternion.Euler(0f, target.transform.eulerAngles.y, 0f));
        var body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            Undo.RecordObject(body, "Auto Place");
            body.position = target.transform.position;
            body.rotation = target.transform.rotation;
        }
        EditorUtility.SetDirty(target.transform);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.gameObject.scene);

        Debug.Log($"[Placer] {target.name}: on {best.y - floorY:F2} m surface at ({best.x:F2}, {best.z:F2}), {bestDistance:F2} m from the player ({counts})", target);
        return true;
    }

    private static bool Probe(MeshCollider surface, float x, float z, float top, out RaycastHit hit) =>
        surface.Raycast(new Ray(new Vector3(x, top, z), Vector3.down), out hit, top + 10f);

    // A ring of probes around the spot must land on the same level surface.
    private static bool IsFlat(MeshCollider surface, Vector3 center, float radius, float top, float minCos)
    {
        for (int i = 0; i < RingProbes; i++)
        {
            float angle = i * Mathf.PI * 2f / RingProbes;
            float x = center.x + Mathf.Cos(angle) * radius;
            float z = center.z + Mathf.Sin(angle) * radius;
            if (!Probe(surface, x, z, top, out RaycastHit hit) || hit.normal.y < minCos ||
                Mathf.Abs(hit.point.y - center.y) > MaxHeightDifference)
                return false;
        }
        return true;
    }
}

[CustomEditor(typeof(AutoPlace))]
public class AutoPlaceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Place"))
        {
            foreach (Object target in targets)
                SplatObjectPlacer.Place((AutoPlace)target);
        }
    }
}
