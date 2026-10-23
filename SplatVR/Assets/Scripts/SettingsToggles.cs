using Gaussians.ThreeD;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime A/B toggles for Phase 1 benchmarking, so a setting can be compared in one build.
/// Left thumbstick click = Minimum Splat Radius on/off (popping test). In the editor: R.
/// Every change is logged with a "[Settings]" tag so headset frame-rate logs can be matched to settings.
///
/// HDR is intentionally NOT toggled here: switching Camera.allowHDR at runtime changes the frame's pixel
/// format mid-run, which caused repeated GPU faults and froze the Quest (2026-10-23). Set HDR in the URP asset instead.
/// </summary>
public class SettingsToggles : MonoBehaviour
{
    [Tooltip("Optional label that shows the current settings.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Minimum splat radius (in pixels) used when pruning is on. ninjamode's default is 0.7.")]
    [SerializeField] private float _pruneRadius = 0.7f;

    private GaussianSplat3DRenderer[] _splatRenderers;
    private InputAction _radiusAction;
    private bool _pruneOn;

    private void Awake()
    {
        // Bindings in code, same reason as the other switchers: Inspector-made actions can end up empty.
        _radiusAction = new InputAction("Toggle Min Splat Radius", InputActionType.Button);
        _radiusAction.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        _radiusAction.AddBinding("<Keyboard>/r");
    }

    private void OnEnable()
    {
        _radiusAction.performed += OnToggleRadius;
        _radiusAction.Enable();
    }

    private void OnDisable()
    {
        _radiusAction.performed -= OnToggleRadius;
        _radiusAction.Disable();
    }

    private void OnDestroy() => _radiusAction?.Dispose();

    private void Start()
    {
        // Include inactive objects: the version switcher keeps all but one splat version switched off.
        _splatRenderers = FindObjectsByType<GaussianSplat3DRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        _pruneOn = true;
        ApplyRadius();
        Report("start");
    }

    private void OnToggleRadius(InputAction.CallbackContext context)
    {
        _pruneOn = !_pruneOn;
        ApplyRadius();
        Report("MinRadius");
    }

    private void ApplyRadius()
    {
        float radius = _pruneOn ? _pruneRadius : 0f;
        foreach (GaussianSplat3DRenderer splatRenderer in _splatRenderers)
            splatRenderer.m_MinimumSplatRadiusPixels = radius;
    }

    private void Report(string changed)
    {
        // URP renders in HDR only when both the camera allows it and the pipeline asset supports it.
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        bool hdr = Camera.main != null && Camera.main.allowHDR && urpAsset != null && urpAsset.supportsHDR;
        string text = $"HDR {(hdr ? "on" : "off")} | Min radius {(_pruneOn ? _pruneRadius : 0f):0.0} px";
        if (_label != null)
            _label.text = text;

        Debug.Log($"[Settings] t={Time.realtimeSinceStartup:F1}s changed={changed} HDR={(hdr ? "on" : "off")} MinRadius={(_pruneOn ? _pruneRadius : 0f):0.0} renderers={_splatRenderers.Length}");
    }
}
