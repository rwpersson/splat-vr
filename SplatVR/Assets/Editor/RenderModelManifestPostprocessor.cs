using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Adds what Quest requires before it offers the XR_FB_render_model extension (logcat "skipping extension=
/// `XR_FB_render_model` due to: missing …"): `uses-feature com.oculus.feature.RENDER_MODEL` (not required, so the app
/// still runs without it) and `uses-permission com.oculus.permission.RENDER_MODEL`.
/// Without them ControllerRenderModelFeature can't get the controller models and the fallback spheres stay.
/// </summary>
public class RenderModelManifestPostprocessor : IPostGenerateGradleAndroidProject
{
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private const string FeatureName = "com.oculus.feature.RENDER_MODEL";
    private const string PermissionName = "com.oculus.permission.RENDER_MODEL";

    // Run after Unity's own and the OpenXR package's manifest edits.
    public int callbackOrder => 10000;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
        var document = new XmlDocument();
        document.Load(manifestPath);

        bool changed = AddIfMissing(document, "uses-feature", FeatureName, required: false);
        changed |= AddIfMissing(document, "uses-permission", PermissionName, required: null);
        if (!changed)
            return;

        document.Save(manifestPath);
        Debug.Log($"[RenderModel] added {FeatureName} feature + permission to {manifestPath}");
    }

    private static bool AddIfMissing(XmlDocument document, string tag, string name, bool? required)
    {
        XmlElement manifest = document.DocumentElement;
        foreach (XmlElement existing in manifest.GetElementsByTagName(tag))
        {
            if (existing.GetAttribute("name", AndroidNamespace) == name)
                return false;
        }

        XmlElement element = document.CreateElement(tag);
        element.SetAttribute("name", AndroidNamespace, name);
        if (required.HasValue)
            element.SetAttribute("required", AndroidNamespace, required.Value ? "true" : "false");
        manifest.AppendChild(element);
        return true;
    }
}
