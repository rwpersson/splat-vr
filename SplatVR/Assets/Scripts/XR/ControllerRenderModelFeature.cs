using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

/// <summary>
/// Gets the real controller 3D models from the headset (OpenXR extension XR_FB_render_model, plain OpenXR, no Meta SDK).
/// The runtime hands back each model as a glTF binary (.glb); ControllerModel loads and shows it.
/// Quest only offers the extension if the app's manifest has the uses-feature `com.oculus.feature.RENDER_MODEL` and the
/// uses-permission `com.oculus.permission.RENDER_MODEL` (added at build time by RenderModelManifestPostprocessor).
/// Calls go straight to the OpenXR functions, fetched by name. Structs are written by hand at the byte offsets from the
/// OpenXR headers (64-bit), so there is no hidden marshalling.
/// </summary>
#if UNITY_EDITOR
[OpenXRFeature(UiName = "SplatVR Controller Render Models",
    BuildTargetGroups = new[] { BuildTargetGroup.Android },
    Company = "SplatVR",
    Desc = "Loads the real controller models from the runtime (XR_FB_render_model).",
    OpenxrExtensionStrings = ExtensionName,
    Version = "0.1.0",
    FeatureId = FeatureIdString)]
#endif
public class ControllerRenderModelFeature : OpenXRFeature
{
    public const string FeatureIdString = "com.reidpersson.splatvr.controller-render-models";
    public const string ExtensionName = "XR_FB_render_model";

    public const string LeftPath = "/model_fb/controller/left";
    public const string RightPath = "/model_fb/controller/right";

    // XrStructureType values (XR_FB_render_model)
    private const int TypePathInfo = 1000119000;
    private const int TypeProperties = 1000119001;
    private const int TypeBuffer = 1000119002;
    private const int TypeLoadInfo = 1000119003;
    private const int TypeCapabilitiesRequest = 1000119005;

    // XrRenderModelFlagsFB: which glTF feature level we can load. Subset 2 allows more (newer controllers need it).
    private const ulong SupportsGltfSubset1 = 1;
    private const ulong SupportsGltfSubset2 = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetInstanceProcAddrFn(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumerateRenderModelPathsFn(ulong session, uint capacity, out uint count, IntPtr paths);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetRenderModelPropertiesFn(ulong session, ulong path, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LoadRenderModelFn(ulong session, IntPtr loadInfo, IntPtr buffer);

    private static ulong s_Instance;
    private static ulong s_Session;
    private static EnumerateRenderModelPathsFn s_EnumeratePaths;
    private static GetRenderModelPropertiesFn s_GetProperties;
    private static LoadRenderModelFn s_LoadModel;
    private static bool s_PathsEnumerated;
    private static string[] s_PathNames = Array.Empty<string>();
    private static ulong[] s_Paths = Array.Empty<ulong>();

    /// <summary>True once the extension is enabled and a session exists, so models can be requested.</summary>
    public static bool IsReady => s_Session != 0 && s_LoadModel != null;

    protected override bool OnInstanceCreate(ulong xrInstance)
    {
        // Never fail the whole app over controller models: without the extension we just keep the fallback.
        if (!OpenXRRuntime.IsExtensionEnabled(ExtensionName))
        {
            Debug.LogWarning($"[RenderModel] {ExtensionName} not enabled by the runtime (manifest feature/permission missing?)");
            return true;
        }

        s_Instance = xrInstance;
        var getProc = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(xrGetInstanceProcAddr);
        s_EnumeratePaths = GetFunction<EnumerateRenderModelPathsFn>(getProc, "xrEnumerateRenderModelPathsFB");
        s_GetProperties = GetFunction<GetRenderModelPropertiesFn>(getProc, "xrGetRenderModelPropertiesFB");
        s_LoadModel = GetFunction<LoadRenderModelFn>(getProc, "xrLoadRenderModelFB");
        if (s_EnumeratePaths == null || s_GetProperties == null)
            s_LoadModel = null;
        Debug.Log($"[RenderModel] extension enabled, functions {(s_LoadModel != null ? "found" : "MISSING")}");
        return true;
    }

    private T GetFunction<T>(GetInstanceProcAddrFn getProc, string name) where T : Delegate
    {
        int result = getProc(s_Instance, name, out IntPtr function);
        if (result < 0 || function == IntPtr.Zero)
        {
            Debug.LogWarning($"[RenderModel] {name} not found (XrResult {result})");
            return null;
        }
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    protected override void OnSessionCreate(ulong xrSession) => s_Session = xrSession;

    protected override void OnSessionDestroy(ulong xrSession)
    {
        s_Session = 0;
        s_PathsEnumerated = false;
    }

    protected override void OnInstanceDestroy(ulong xrInstance)
    {
        s_Instance = 0;
        s_LoadModel = null;
    }

    /// <summary>
    /// Fetches the .glb for a model path (e.g. <see cref="LeftPath"/>). Returns false if it isn't available yet
    /// (e.g. the controller isn't connected); callers just retry later.
    /// </summary>
    public static bool TryLoadModel(string modelPath, out byte[] glb, out string description)
    {
        glb = null;
        description = "not ready";
        if (!IsReady)
            return false;

        // The spec requires enumerating the paths before asking for properties.
        if (!s_PathsEnumerated && !EnumeratePaths())
            return false;

        int index = Array.IndexOf(s_PathNames, modelPath);
        if (index < 0)
        {
            description = $"path {modelPath} not offered (offered: {string.Join(", ", s_PathNames)})";
            return false;
        }

        // Ask for the richest glTF level first, then the basic one.
        foreach (ulong level in new[] { SupportsGltfSubset2, SupportsGltfSubset1 })
        {
            if (!GetModelKey(s_Paths[index], level, out ulong modelKey, out string modelName, out uint version, out int result))
            {
                description = $"properties failed (XrResult {result}) for level {level}";
                continue;
            }
            if (modelKey == 0)
            {
                description = $"no model at level {level}";
                continue;
            }
            if (LoadBuffer(modelKey, out glb, out result))
            {
                description = $"{modelName} v{version} level {level} key {modelKey} {glb.Length} bytes";
                return true;
            }
            description = $"load failed (XrResult {result}) for {modelName}";
        }
        return false;
    }

    private static bool EnumeratePaths()
    {
        const int pathInfoSize = 24; // type, next, XrPath
        int result = s_EnumeratePaths(s_Session, 0, out uint count, IntPtr.Zero);
        if (result < 0 || count == 0)
        {
            Debug.LogWarning($"[RenderModel] enumerate paths: XrResult {result}, count {count}");
            return false;
        }

        IntPtr infos = Marshal.AllocHGlobal(pathInfoSize * (int)count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr info = infos + i * pathInfoSize;
                Marshal.WriteInt32(info, 0, TypePathInfo);
                Marshal.WriteIntPtr(info, 8, IntPtr.Zero);
                Marshal.WriteInt64(info, 16, 0);
            }
            result = s_EnumeratePaths(s_Session, count, out count, infos);
            if (result < 0)
            {
                Debug.LogWarning($"[RenderModel] enumerate paths (2nd call): XrResult {result}");
                return false;
            }

            s_Paths = new ulong[count];
            s_PathNames = new string[count];
            for (int i = 0; i < count; i++)
            {
                s_Paths[i] = (ulong)Marshal.ReadInt64(infos + i * pathInfoSize, 16);
                s_PathNames[i] = PathToString(s_Paths[i]);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infos);
        }

        s_PathsEnumerated = true;
        Debug.Log($"[RenderModel] paths offered: {string.Join(", ", s_PathNames)}");
        return true;
    }

    private static bool GetModelKey(ulong path, ulong supportLevel, out ulong modelKey, out string modelName, out uint version, out int result)
    {
        // XrRenderModelPropertiesFB: type@0, next@8, vendorId@16, modelName[64]@20, modelKey@88, modelVersion@96, flags@104
        // XrRenderModelCapabilitiesRequestFB (chained via next): type@0, next@8, flags@16
        const int propertiesSize = 112;
        const int capabilitiesSize = 24;
        IntPtr properties = Marshal.AllocHGlobal(propertiesSize);
        IntPtr capabilities = Marshal.AllocHGlobal(capabilitiesSize);
        try
        {
            for (int i = 0; i < propertiesSize; i += 8)
                Marshal.WriteInt64(properties, i, 0);
            Marshal.WriteInt32(capabilities, 0, TypeCapabilitiesRequest);
            Marshal.WriteIntPtr(capabilities, 8, IntPtr.Zero);
            Marshal.WriteInt64(capabilities, 16, (long)supportLevel);
            Marshal.WriteInt32(properties, 0, TypeProperties);
            Marshal.WriteIntPtr(properties, 8, capabilities);

            result = s_GetProperties(s_Session, path, properties);
            modelName = Marshal.PtrToStringAnsi(properties + 20);
            modelKey = (ulong)Marshal.ReadInt64(properties, 88);
            version = (uint)Marshal.ReadInt32(properties, 96);
            return result >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(capabilities);
            Marshal.FreeHGlobal(properties);
        }
    }

    private static bool LoadBuffer(ulong modelKey, out byte[] glb, out int result)
    {
        // XrRenderModelLoadInfoFB: type@0, next@8, modelKey@16
        // XrRenderModelBufferFB: type@0, next@8, bufferCapacityInput@16, bufferCountOutput@20, buffer@24
        glb = null;
        IntPtr loadInfo = Marshal.AllocHGlobal(24);
        IntPtr buffer = Marshal.AllocHGlobal(32);
        IntPtr data = IntPtr.Zero;
        try
        {
            Marshal.WriteInt32(loadInfo, 0, TypeLoadInfo);
            Marshal.WriteIntPtr(loadInfo, 8, IntPtr.Zero);
            Marshal.WriteInt64(loadInfo, 16, (long)modelKey);

            // Two-call idiom: ask for the size, then fill.
            Marshal.WriteInt32(buffer, 0, TypeBuffer);
            Marshal.WriteIntPtr(buffer, 8, IntPtr.Zero);
            Marshal.WriteInt32(buffer, 16, 0);
            Marshal.WriteInt32(buffer, 20, 0);
            Marshal.WriteIntPtr(buffer, 24, IntPtr.Zero);
            result = s_LoadModel(s_Session, loadInfo, buffer);
            int size = Marshal.ReadInt32(buffer, 20);
            if (result < 0 || size <= 0)
                return false;

            data = Marshal.AllocHGlobal(size);
            Marshal.WriteInt32(buffer, 16, size);
            Marshal.WriteIntPtr(buffer, 24, data);
            result = s_LoadModel(s_Session, loadInfo, buffer);
            if (result < 0)
                return false;

            glb = new byte[Marshal.ReadInt32(buffer, 20)];
            Marshal.Copy(data, glb, 0, glb.Length);
            return true;
        }
        finally
        {
            if (data != IntPtr.Zero)
                Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(loadInfo);
        }
    }
}
