using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gaussians.ThreeD;
using Gaussians.ThreeD.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Raw scan → playable splat in the open scene, with no hand steps (Phase 5):
///   1. Prepare: crop to the scan's dense core (drops the far background shell Scaniverse adds) and thin it to the
///      headset budget, with splat-transform → "&lt;name&gt; 100k.ply" next to the raw file.
///   2. Import it as a splat asset (same quality settings as the desk).
///   3. Create the splat object, flipped like every 3DGS scan (Scale (1, −1, 1)), and place it so its floor is at
///      y = 0 and its dense core is in front of the player.
///   4. Generate its colliders (SplatColliderGenerator: collision mesh + floor).
/// Logs with a "[SplatScan]" tag.
/// </summary>
public static class SplatScanImporter
{
    private const string AssetFolder = "Assets/Gaussians/3D";

    [MenuItem("Tools/SplatVR/Import Scan…")]
    private static void ImportFromMenu()
    {
        string projectFolder = Path.GetDirectoryName(Application.dataPath);
        string path = EditorUtility.OpenFilePanel("Import scan (.ply)", Path.Combine(projectFolder, "..", "captures"), "ply");
        if (!string.IsNullOrEmpty(path))
            Import(path, new Vector3(0f, 0f, 1.5f), 100000);
    }

    /// <param name="corePosition">World position (x, z) for the scan's dense core; y is ignored (the floor goes to 0).</param>
    public static GaussianSplat3DRenderer Import(string rawPath, Vector3 corePosition, int splatBudget, string toolCommand = "splat-transform")
    {
        string name = Path.GetFileNameWithoutExtension(rawPath);
        if (!TryFindCore(rawPath, out Vector3 center, out float radius, out string error))
        {
            Debug.LogError($"[SplatScan] {name}: {error}");
            return null;
        }

        // 1. Prepare. The crop runs first: the later GPU steps can't handle the full ±240 m background shell.
        string prepared = Path.Combine(Path.GetDirectoryName(rawPath), $"{name} {splatBudget / 1000}k.ply");
        string arguments = string.Format(CultureInfo.InvariantCulture,
            "--no-tty -w \"{0}\" -S {1},{2},{3},{4} --decimate-adaptive {5} \"{6}\"",
            rawPath, center.x, center.y, center.z, radius, splatBudget, prepared);
        if (!SplatColliderGenerator.Run(toolCommand, name, arguments, out string log) || !File.Exists(prepared))
        {
            Debug.LogError($"[SplatScan] {name}: preparing failed\n{toolCommand} {arguments}\n{log}");
            return null;
        }

        // 2. Import (same settings as the desk scan).
        var settings = new GaussianSplat3DImportSettings(
            GaussianSplat3DAsset.VectorFormat.Norm16, GaussianSplat3DAsset.VectorFormat.Norm16,
            GaussianSplat3DAsset.ColorFormat.Float16x4, GaussianSplat3DAsset.SHFormat.Norm11);
        GaussianSplat3DAsset asset = GaussianSplat3DImporter.Import(prepared, AssetFolder, settings, false)[0];

        // 3. Scene object, flipped like every 3DGS scan (they're Y-down; see CLAUDE.md).
        var go = new GameObject(Path.GetFileNameWithoutExtension(prepared));
        Undo.RegisterCreatedObjectUndo(go, "Import Scan");
        go.transform.localScale = new Vector3(1f, -1f, 1f);
        var renderer = go.AddComponent<GaussianSplat3DRenderer>();
        renderer.m_Asset = asset;
        var colliderSettings = go.AddComponent<SplatCollider>();
        string projectFolder = Path.GetDirectoryName(Application.dataPath);
        colliderSettings.SourceFile = Path.GetRelativePath(projectFolder, prepared).Replace('\\', '/');
        colliderSettings.ToolCommand = toolCommand;

        // Place: floor at y = 0, dense core at the chosen (x, z).
        if (!SplatColliderGenerator.TryFindFloor(go.transform, prepared, colliderSettings.SolidThreshold,
                out Vector3 floorPoint, out Vector3 floorNormal, out int floorSplats, out error))
        {
            Debug.LogWarning($"[SplatScan] {name}: no floor found ({error}); left at the origin");
        }
        else
        {
            Vector3 coreWorld = go.transform.TransformPoint(center);
            go.transform.position = new Vector3(corePosition.x - coreWorld.x, -floorPoint.y, corePosition.z - coreWorld.z);
        }

        // 4. Colliders.
        SplatColliderGenerator.Generate(colliderSettings);

        Debug.Log($"[SplatScan] {name}: core at {center} (scan coordinates), crop radius {radius:F2} m → {prepared} " +
                  $"({splatBudget} splats) → {AssetDatabase.GetAssetPath(asset)}; placed at {go.transform.position}", go);
        Selection.activeGameObject = go;
        return renderer;
    }

    /// <summary>
    /// The scan's dense core, in scan coordinates: the center is the 0.5 m cell with the most solid splats (refined to
    /// the median of solid splats in the 5 × 5 × 5 cells around it). The radius is where the splats thin out: in 0.5 m
    /// distance shells, the first shell past the busiest one holding under 5% of it, plus 25% margin.
    /// Couch: 3.1 m (the background shell is ~240 m away). Raw desk corner: 1.25 m.
    /// </summary>
    public static bool TryFindCore(string path, out Vector3 center, out float radius, out string error)
    {
        const float cellSize = 0.5f;
        const float shellWidth = 0.5f;
        const int shellCount = 100; // up to 50 m; anything farther is background
        const float dropFraction = 0.05f;
        const float margin = 1.25f;
        const float solidOpacity = 0.7f;
        center = Vector3.zero;
        radius = 0f;

        if (!SplatPly.TryRead(path, out Vector3[] positions, out float[] opacities, out error))
            return false;

        var cells = new Dictionary<Vector3Int, int>();
        Vector3Int best = default;
        int bestCount = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            if (opacities[i] < solidOpacity)
                continue;
            Vector3Int cell = Vector3Int.FloorToInt(positions[i] / cellSize);
            cells.TryGetValue(cell, out int count);
            cells[cell] = ++count;
            if (count > bestCount)
            {
                bestCount = count;
                best = cell;
            }
        }
        if (bestCount == 0)
        {
            error = "no solid splats";
            return false;
        }

        var xs = new List<float>();
        var ys = new List<float>();
        var zs = new List<float>();
        for (int i = 0; i < positions.Length; i++)
        {
            if (opacities[i] < solidOpacity)
                continue;
            Vector3Int offset = Vector3Int.FloorToInt(positions[i] / cellSize) - best;
            if (Mathf.Abs(offset.x) > 2 || Mathf.Abs(offset.y) > 2 || Mathf.Abs(offset.z) > 2)
                continue;
            xs.Add(positions[i].x);
            ys.Add(positions[i].y);
            zs.Add(positions[i].z);
        }
        center = new Vector3(Median(xs), Median(ys), Median(zs));

        var shells = new int[shellCount];
        foreach (Vector3 p in positions)
        {
            int shell = (int)(Vector3.Distance(p, center) / shellWidth);
            if (shell < shellCount)
                shells[shell]++;
        }
        int busiest = 0;
        for (int i = 1; i < shellCount; i++)
        {
            if (shells[i] > shells[busiest])
                busiest = i;
        }
        int edge = shellCount;
        for (int i = busiest + 1; i < shellCount; i++)
        {
            if (shells[i] < dropFraction * shells[busiest])
            {
                edge = i;
                break;
            }
        }
        radius = edge * shellWidth * margin;
        error = null;
        return true;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}
