using Gaussians.ThreeD;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime A/B toggles for Phase 1 benchmarking, so each setting can be compared in one build.
/// Left controller: thumbstick click = Minimum Splat Radius 0.7/0 (popping test),
/// trigger = sort precision 32/16 bit, grip = SH order 3/0 (view-dependent color on/off).
/// In the editor: R = radius, S = sort precision, O = SH order.
/// Every change is logged with a "[Settings]" tag so headset frame-rate logs can be matched to settings.
///
/// Only settings the renderer re-reads every frame are toggled here. HDR is intentionally NOT toggled:
/// switching Camera.allowHDR at runtime changes the frame's pixel format mid-run, which caused repeated
/// GPU faults and froze the Quest (2026-10-23). Set HDR in the URP asset instead.
/// </summary>
public class SettingsToggles : MonoBehaviour
{
    [Tooltip("Optional label that shows the current settings.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Minimum splat radius (in pixels) used when pruning is on. ninjamode's default is 0.7.")]
    [SerializeField] private float _pruneRadius = 0.7f;

    private GaussianSplat3DRenderer[] _splatRenderers;
    private InputAction _radiusAction;
    private InputAction _sortAction;
    private InputAction _shAction;
    private bool _pruneOn = true;
    private bool _sort16;
    private bool _shOff;

    private void Awake()
    {
        // Bindings in code, same reason as the other switchers: Inspector-made actions can end up empty.
        _radiusAction = CreateButton("Toggle Min Splat Radius", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/r");
        _sortAction = CreateButton("Toggle Sort Precision", "<XRController>{LeftHand}/triggerPressed", "<Keyboard>/s");
        _shAction = CreateButton("Toggle SH Order", "<XRController>{LeftHand}/gripPressed", "<Keyboard>/o");
    }

    private static InputAction CreateButton(string name, string controllerPath, string keyboardPath)
    {
        var action = new InputAction(name, InputActionType.Button);
        action.AddBinding(controllerPath);
        action.AddBinding(keyboardPath);
        return action;
    }

    private void OnEnable()
    {
        _radiusAction.performed += OnToggleRadius;
        _sortAction.performed += OnToggleSort;
        _shAction.performed += OnToggleSh;
        _radiusAction.Enable();
        _sortAction.Enable();
        _shAction.Enable();
    }

    private void OnDisable()
    {
        _radiusAction.performed -= OnToggleRadius;
        _sortAction.performed -= OnToggleSort;
        _shAction.performed -= OnToggleSh;
        _radiusAction.Disable();
        _sortAction.Disable();
        _shAction.Disable();
    }

    private void OnDestroy()
    {
        _radiusAction?.Dispose();
        _sortAction?.Dispose();
        _shAction?.Dispose();
    }

    private void Start()
    {
        // Include inactive objects: the version switcher keeps all but one splat version switched off.
        _splatRenderers = FindObjectsByType<GaussianSplat3DRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ApplyAll();
        Report("start");
    }

    private void OnToggleRadius(InputAction.CallbackContext context)
    {
        _pruneOn = !_pruneOn;
        ApplyAll();
        Report("MinRadius");
    }

    private void OnToggleSort(InputAction.CallbackContext context)
    {
        _sort16 = !_sort16;
        ApplyAll();
        Report("Sort");
    }

    private void OnToggleSh(InputAction.CallbackContext context)
    {
        _shOff = !_shOff;
        ApplyAll();
        Report("SH");
    }

    private void ApplyAll()
    {
        foreach (GaussianSplat3DRenderer splatRenderer in _splatRenderers)
        {
            splatRenderer.m_MinimumSplatRadiusPixels = _pruneOn ? _pruneRadius : 0f;
            splatRenderer.m_SortPrecision = _sort16
                ? GaussianSplat3DRenderer.SortPrecision.Bits16
                : GaussianSplat3DRenderer.SortPrecision.Bits32;
            splatRenderer.m_SHOrder = _shOff ? 0 : 3;
        }
    }

    private void Report(string changed)
    {
        // URP renders in HDR only when both the camera allows it and the pipeline asset supports it.
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        bool hdr = Camera.main != null && Camera.main.allowHDR && urpAsset != null && urpAsset.supportsHDR;

        float radius = _pruneOn ? _pruneRadius : 0f;
        int sortBits = _sort16 ? 16 : 32;
        int shOrder = _shOff ? 0 : 3;

        if (_label != null)
            _label.text = $"HDR {(hdr ? "on" : "off")} | Radius {radius:0.0} | Sort {sortBits}-bit | SH {shOrder}";

        Debug.Log($"[Settings] t={Time.realtimeSinceStartup:F1}s changed={changed} HDR={(hdr ? "on" : "off")} MinRadius={radius:0.0} Sort={sortBits} SH={shOrder} renderers={_splatRenderers.Length}");
    }
}
