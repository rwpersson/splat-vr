using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds a splat's collider from its source scan with splat-transform's collision mesh (DECISIONS 2026-10-31):
/// scan → splat-transform (voxelize, keep voxels above the solid threshold, mesh their surface) → .glb in
/// Assets/Colliders → MeshCollider on an "Auto Collider" child of the splat.
/// Logs with a "[SplatCollider]" tag.
/// </summary>
public static class SplatColliderGenerator
{
    private const string ChildName = "Auto Collider";
    private const string OutputFolder = "Assets/Colliders";

    [MenuItem("Tools/SplatVR/Generate All Colliders")]
    private static void GenerateAll()
    {
        SplatCollider[] all = Object.FindObjectsByType<SplatCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length == 0)
            Debug.LogWarning("[SplatCollider] no SplatCollider components in the open scenes");
        foreach (SplatCollider settings in all)
            Generate(settings);
    }

    /// <summary>Returns the new collider, or null if it failed (the reason is logged).</summary>
    public static MeshCollider Generate(SplatCollider settings)
    {
        string projectFolder = Path.GetDirectoryName(Application.dataPath);
        string source = Path.GetFullPath(Path.Combine(projectFolder, settings.SourceFile));
        if (string.IsNullOrWhiteSpace(settings.SourceFile) || !File.Exists(source))
        {
            Debug.LogError($"[SplatCollider] {settings.name}: source file not found: {source}", settings);
            return null;
        }

        // splat-transform writes <name>.voxel.json, <name>.voxel.bin and <name>.collision.glb next to each other.
        string workFolder = Path.Combine(projectFolder, "Temp", "SplatCollider");
        Directory.CreateDirectory(workFolder);
        string voxelOutput = Path.Combine(workFolder, $"{settings.name}.voxel.json");
        string meshOutput = Path.Combine(workFolder, $"{settings.name}.collision.glb");
        File.Delete(meshOutput);

        string arguments = string.Format(CultureInfo.InvariantCulture,
            "--no-tty -w \"{0}\"{1} --voxel-size {2} --voxel-opacity {3} --collision-mesh \"{4}\"",
            source, settings.RemoveFloaters ? " -F" : "", settings.VoxelSize, settings.SolidThreshold, voxelOutput);
        if (!Run(settings.ToolCommand, settings.name, arguments, out string log) || !File.Exists(meshOutput))
        {
            Debug.LogError($"[SplatCollider] {settings.name}: splat-transform failed\n{settings.ToolCommand} {arguments}\n{log}", settings);
            return null;
        }

        string assetPath = $"{OutputFolder}/{settings.name}.collision.glb";
        Directory.CreateDirectory(Path.Combine(projectFolder, OutputFolder));
        File.Copy(meshOutput, Path.Combine(projectFolder, assetPath), true);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().FirstOrDefault();
        if (mesh == null)
        {
            Debug.LogError($"[SplatCollider] {settings.name}: no mesh in {assetPath} (is glTFast installed?)", settings);
            return null;
        }

        MeshCollider collider = GetOrCreateChildCollider(settings.transform);
        Undo.RecordObject(collider, "Generate Splat Collider");
        collider.sharedMesh = mesh;
        EditorUtility.SetDirty(collider);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(collider.gameObject.scene);

        Debug.Log($"[SplatCollider] {settings.name}: {mesh.triangles.Length / 3} triangles → {assetPath} " +
                  $"(voxel {settings.VoxelSize} m, solid ≥ {settings.SolidThreshold}, floaters {(settings.RemoveFloaters ? "removed" : "kept")})", collider);

        if (settings.GenerateFloor)
            GenerateFloor(settings, source);
        return collider;
    }

    /// <summary>
    /// Finds the floor (TryFindFloor, the same method we used by hand in Phase 2) and puts a large thin box with its top
    /// on that plane on an "Auto Floor" child. A floor tilted under 2° gets a level box.
    /// </summary>
    public static BoxCollider GenerateFloor(SplatCollider settings, string source)
    {
        const float maxFlatTiltDegrees = 2f;
        const float thickness = 0.1f;

        if (!TryFindFloor(settings.transform, source, settings.SolidThreshold, out Vector3 centroid, out Vector3 normal, out int n, out string error))
        {
            Debug.LogError($"[SplatCollider] {settings.name}: floor skipped: {error}", settings);
            return null;
        }
        float tilt = Vector3.Angle(normal, Vector3.up);
        bool flat = tilt < maxFlatTiltDegrees;

        Transform child = settings.transform.Find("Auto Floor");
        if (child == null)
        {
            child = new GameObject("Auto Floor").transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Generate Splat Floor");
            child.SetParent(settings.transform, false);
        }
        Undo.RecordObject(child, "Generate Splat Floor");
        // Undo the splat's mirror (e.g. Scale (1, −1, 1)) so the box has a positive scale, then place it in world space.
        Vector3 parentScale = settings.transform.lossyScale;
        child.localScale = new Vector3(Mathf.Sign(parentScale.x), Mathf.Sign(parentScale.y), Mathf.Sign(parentScale.z));
        child.SetPositionAndRotation(centroid, flat ? Quaternion.identity : Quaternion.FromToRotation(Vector3.up, normal));

        BoxCollider box = child.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(child.gameObject);
        Undo.RecordObject(box, "Generate Splat Floor");
        box.size = new Vector3(settings.FloorSize, thickness, settings.FloorSize);
        box.center = new Vector3(0f, -thickness / 2f, 0f); // top face on the floor plane
        EditorUtility.SetDirty(box);

        Debug.Log($"[SplatCollider] {settings.name}: floor at y={centroid.y:F3} m from {n} solid splats, tilt {tilt:F1}° " +
                  $"({(flat ? "kept level" : "box tilted to match")}), box {settings.FloorSize} × {settings.FloorSize} m", box);
        return box;
    }

    /// <summary>
    /// Floor plane in world space: a histogram of solid splat heights in 1 cm slices; the floor is the lowest strong
    /// peak; a least-squares plane through the splats in that slice gives its point and normal.
    /// </summary>
    public static bool TryFindFloor(Transform splat, string source, float solidThreshold,
        out Vector3 point, out Vector3 normal, out int n, out string error)
    {
        const float binSize = 0.01f;
        const float peakFraction = 0.3f; // a floor peak needs at least 30% of the biggest peak's count
        const float bandHalfWidth = 0.02f;
        point = Vector3.zero;
        normal = Vector3.up;
        n = 0;

        if (!SplatPly.TryReadSolidPositions(source, solidThreshold, out Vector3[] local, out error))
            return false;

        // The splat renderer draws the file's positions in the splat's local space, so its matrix takes them to world.
        Matrix4x4 toWorld = splat.localToWorldMatrix;
        var world = new Vector3[local.Length];
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < local.Length; i++)
        {
            world[i] = toWorld.MultiplyPoint3x4(local[i]);
            minY = Mathf.Min(minY, world[i].y);
            maxY = Mathf.Max(maxY, world[i].y);
        }

        int binCount = Mathf.FloorToInt((maxY - minY) / binSize) + 1;
        var counts = new int[binCount];
        foreach (Vector3 p in world)
            counts[Mathf.FloorToInt((p.y - minY) / binSize)]++;

        // Smooth over 3 slices so a surface split across two slices still counts as one peak.
        var smooth = new int[binCount];
        for (int i = 0; i < binCount; i++)
            smooth[i] = counts[i] + (i > 0 ? counts[i - 1] : 0) + (i < binCount - 1 ? counts[i + 1] : 0);
        int biggest = smooth.Max();

        int floorBin = -1;
        for (int i = 1; i < binCount - 1 && floorBin < 0; i++)
        {
            if (smooth[i] >= smooth[i - 1] && smooth[i] >= smooth[i + 1] && smooth[i] >= peakFraction * biggest)
                floorBin = i;
        }
        if (floorBin < 0)
        {
            error = "no floor peak found";
            return false;
        }

        // Least-squares plane y = a·x + b·z + c through the splats near the peak.
        float peakY = minY + (floorBin + 0.5f) * binSize;
        double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sxy = 0, szy = 0, sy = 0;
        foreach (Vector3 p in world)
        {
            if (Mathf.Abs(p.y - peakY) > bandHalfWidth)
                continue;
            sxx += p.x * p.x; sxz += p.x * p.z; szz += p.z * p.z;
            sx += p.x; sz += p.z; sy += p.y;
            sxy += p.x * p.y; szy += p.z * p.y;
            n++;
        }
        point = new Vector3((float)(sx / n), (float)(sy / n), (float)(sz / n));
        normal = Vector3.up;
        if (SolvePlane(sxx, sxz, szz, sx, sz, sxy, szy, sy, n, out double a, out double b, out double c))
        {
            normal = new Vector3((float)-a, 1f, (float)-b).normalized;
            point.y = (float)(a * point.x + b * point.z + c);
        }
        error = null;
        return true;
    }

    // Normal equations for y = a·x + b·z + c.
    private static bool SolvePlane(double sxx, double sxz, double szz, double sx, double sz, double sxy, double szy, double sy, int n,
        out double a, out double b, out double c)
    {
        double[,] A = { { sxx, sxz, sx }, { sxz, szz, sz }, { sx, sz, n } };
        double[] r = { sxy, szy, sy };
        double det = Det3(A);
        a = b = c = 0;
        if (System.Math.Abs(det) < 1e-12)
            return false;
        a = Det3(Replace(A, 0, r)) / det;
        b = Det3(Replace(A, 1, r)) / det;
        c = Det3(Replace(A, 2, r)) / det;
        return true;
    }

    private static double Det3(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
        - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
        + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    private static double[,] Replace(double[,] m, int column, double[] values)
    {
        var copy = (double[,])m.Clone();
        for (int row = 0; row < 3; row++)
            copy[row, column] = values[row];
        return copy;
    }

    private static MeshCollider GetOrCreateChildCollider(Transform splat)
    {
        Transform child = splat.Find(ChildName);
        if (child == null)
        {
            child = new GameObject(ChildName).transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Generate Splat Collider");
            child.SetParent(splat, false);
        }

        // The mesh is in splat-transform's PlayCanvas convention, (−x, −y, z) of the scan; glTFast flips x on import,
        // leaving (x, −y, z). Flipping y back gives the scan's own coordinates, which the splat renderer uses too.
        Undo.RecordObject(child, "Generate Splat Collider");
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = new Vector3(1f, -1f, 1f);

        MeshCollider collider = child.GetComponent<MeshCollider>();
        if (collider == null)
            collider = Undo.AddComponent<MeshCollider>(child.gameObject);
        return collider;
    }

    /// <summary>Runs splat-transform (or another command) and waits; <paramref name="label"/> is shown in the progress bar.</summary>
    public static bool Run(string toolCommand, string label, string arguments, out string log)
    {
        // Through cmd so npm's .cmd launcher on PATH is found, the same as typing the command in a terminal.
        var startInfo = new ProcessStartInfo("cmd.exe", $"/s /c \"{toolCommand} {arguments}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        EditorUtility.DisplayProgressBar("Splat Collider", $"Running splat-transform on {label}…", 0.5f);
        try
        {
            using Process process = Process.Start(startInfo);
            // Read both streams at once: reading one to the end first can deadlock if the other fills up.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            log = output.Result + error.Result;
            return process.ExitCode == 0;
        }
        catch (System.Exception e)
        {
            log = e.Message;
            return false;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}

[CustomEditor(typeof(SplatCollider))]
public class SplatColliderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Collider"))
        {
            foreach (Object target in targets)
                SplatColliderGenerator.Generate((SplatCollider)target);
        }
    }
}
