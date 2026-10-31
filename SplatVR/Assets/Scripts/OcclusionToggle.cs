using Gaussians.ThreeD;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test toggles for splat-vs-splat occlusion (ninjamode SplatVR patch 2: occluder pre-pass).
/// The environment splats (e.g. the room scan) become occluders: they draw their color first, then write depth for
/// their solid splats, so other splats (the mug) behind them are hidden. An occluder never hides its own splats.
/// The depth can come from the environment itself or from a cheaper "depth proxy": a decimated copy of the same scan
/// (same transform), drawn invisibly, only into depth.
/// Controller (editor key in brackets):
///   right trigger (O)          = occlusion on/off (off = original ninjamode behavior)
///   right thumbstick click (P) = cycle the "solid" threshold (occluder alpha cutoff)
///   left trigger (L)           = cycle the depth source: the environment itself, then each depth proxy
/// Every change is logged with an "[Occlusion]" tag.
/// </summary>
public class OcclusionToggle : MonoBehaviour
{
    [Tooltip("Environment splats that hide objects behind them (not the grabbable objects).")]
    [SerializeField] private GaussianSplat3DRenderer[] _renderers;

    [Tooltip("Cheaper copies of the environment that can supply its depth instead (e.g. Desk 50k, Desk 25k).")]
    [SerializeField] private GaussianSplat3DRenderer[] _depthProxies;

    [SerializeField] private bool _startEnabled = true;
    [SerializeField] private float[] _alphaCutoffs = { 0.3f, 0.5f, 0.7f };
    [SerializeField] private int _startCutoffIndex = 2;

    [Tooltip("-1 = the environment writes its own depth; otherwise the index into Depth Proxies.")]
    [SerializeField] private int _startProxyIndex = -1;

    [Tooltip("Meters the occluder depth is pushed back: how far an object may sink into a surface before it's hidden.")]
    [SerializeField] private float _depthOffset = 0.01f;

    private InputAction _toggleAction;
    private InputAction _cutoffAction;
    private InputAction _proxyAction;
    private bool _enabled;
    private int _cutoffIndex;
    private int _proxyIndex;

    private void Awake()
    {
        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _toggleAction = CreateButton("Toggle Occlusion", "<XRController>{RightHand}/triggerPressed", "<Keyboard>/o");
        _cutoffAction = CreateButton("Cycle Occluder Cutoff", "<XRController>{RightHand}/thumbstickClicked", "<Keyboard>/p");
        _proxyAction = CreateButton("Cycle Depth Source", "<XRController>{LeftHand}/triggerPressed", "<Keyboard>/l");

        _enabled = _startEnabled;
        _cutoffIndex = Mathf.Clamp(_startCutoffIndex, 0, _alphaCutoffs.Length - 1);
        _proxyIndex = Mathf.Clamp(_startProxyIndex, -1, _depthProxies.Length - 1);
        Apply();
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
        _toggleAction.performed += OnToggle;
        _cutoffAction.performed += OnCycleCutoff;
        _proxyAction.performed += OnCycleProxy;
        _toggleAction.Enable();
        _cutoffAction.Enable();
        _proxyAction.Enable();
    }

    private void OnDisable()
    {
        _toggleAction.performed -= OnToggle;
        _cutoffAction.performed -= OnCycleCutoff;
        _proxyAction.performed -= OnCycleProxy;
        _toggleAction.Disable();
        _cutoffAction.Disable();
        _proxyAction.Disable();
    }

    private void OnDestroy()
    {
        _toggleAction?.Dispose();
        _cutoffAction?.Dispose();
        _proxyAction?.Dispose();
    }

    private void Start() => Report("start");

    private void OnToggle(InputAction.CallbackContext context)
    {
        _enabled = !_enabled;
        Apply();
        Report("occlusion");
    }

    private void OnCycleCutoff(InputAction.CallbackContext context)
    {
        _cutoffIndex = (_cutoffIndex + 1) % _alphaCutoffs.Length;
        Apply();
        Report("cutoff");
    }

    private void OnCycleProxy(InputAction.CallbackContext context)
    {
        // -1 (self) → 0 → 1 → … → -1
        _proxyIndex = _proxyIndex + 1 >= _depthProxies.Length ? -1 : _proxyIndex + 1;
        Apply();
        Report("depthSource");
    }

    // The renderer re-reads these every frame, so no buffers or formats change at runtime.
    private void Apply()
    {
        bool useProxy = _proxyIndex >= 0;
        foreach (GaussianSplat3DRenderer splat in _renderers)
        {
            if (splat == null)
                continue;
            splat.m_Occluder = _enabled;
            splat.m_OccluderRole = useProxy ? GaussianSplat3DRenderer.OccluderRole.ColorOnly : GaussianSplat3DRenderer.OccluderRole.ColorAndDepth;
            splat.m_OccluderAlphaCutoff = _alphaCutoffs[_cutoffIndex];
            splat.m_OccluderDepthOffset = _depthOffset;
        }

        for (int i = 0; i < _depthProxies.Length; i++)
        {
            GaussianSplat3DRenderer proxy = _depthProxies[i];
            if (proxy == null)
                continue;
            // A proxy is never drawn in color: it's only enabled while it supplies depth.
            proxy.m_Occluder = true;
            proxy.m_OccluderRole = GaussianSplat3DRenderer.OccluderRole.DepthOnly;
            proxy.m_OccluderAlphaCutoff = _alphaCutoffs[_cutoffIndex];
            proxy.m_OccluderDepthOffset = _depthOffset;
            proxy.m_SHOrder = 0; // color is never shown, so skip the view-dependent color work
            proxy.enabled = _enabled && i == _proxyIndex;
        }
    }

    private string DepthSourceName() =>
        _proxyIndex < 0 ? "self" : (_depthProxies[_proxyIndex] != null ? _depthProxies[_proxyIndex].name : "missing");

    private void Report(string changed) =>
        Debug.Log($"[Occlusion] t={Time.realtimeSinceStartup:F1}s changed={changed} enabled={_enabled} " +
                  $"cutoff={_alphaCutoffs[_cutoffIndex]:F2} offset={_depthOffset:F3}m depthSource={DepthSourceName()}");
}
