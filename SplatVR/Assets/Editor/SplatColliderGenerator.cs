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
        if (!Run(settings, arguments, out string log) || !File.Exists(meshOutput))
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
        return collider;
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

    private static bool Run(SplatCollider settings, string arguments, out string log)
    {
        // Through cmd so npm's .cmd launcher on PATH is found, the same as typing the command in a terminal.
        var startInfo = new ProcessStartInfo("cmd.exe", $"/s /c \"{settings.ToolCommand} {arguments}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        EditorUtility.DisplayProgressBar("Splat Collider", $"Running splat-transform on {settings.name}…", 0.5f);
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
