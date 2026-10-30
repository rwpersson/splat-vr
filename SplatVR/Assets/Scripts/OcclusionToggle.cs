using Gaussians.ThreeD;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test toggles for splat-vs-splat occlusion (ninjamode SplatVR patch 2: occluder depth pre-pass).
/// The environment splats (e.g. the room scan) become occluders: they draw their color first, then write depth for
/// their solid splats, so other splats (the mug) behind them are hidden. An occluder never hides its own splats.
/// Right controller (editor key in brackets):
///   trigger (O)          = occlusion on/off (off = original ninjamode behavior)
///   thumbstick click (P) = cycle the "solid" threshold (occluder alpha cutoff)
/// Every change is logged with an "[Occlusion]" tag.
/// </summary>
public class OcclusionToggle : MonoBehaviour
{
    [Tooltip("Environment splats that hide objects behind them (not the grabbable objects).")]
    [SerializeField] private GaussianSplat3DRenderer[] _renderers;

    [SerializeField] private bool _startEnabled = true;
    [SerializeField] private float[] _alphaCutoffs = { 0.3f, 0.5f, 0.7f };
    [SerializeField] private int _startCutoffIndex = 2;

    [Tooltip("Meters the occluder depth is pushed back: how far an object may sink into a surface before it's hidden.")]
    [SerializeField] private float _depthOffset = 0.01f;

    private InputAction _toggleAction;
    private InputAction _cutoffAction;
    private bool _enabled;
    private int _cutoffIndex;

    private void Awake()
    {
        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _toggleAction = CreateButton("Toggle Occlusion", "<XRController>{RightHand}/triggerPressed", "<Keyboard>/o");
        _cutoffAction = CreateButton("Cycle Occluder Cutoff", "<XRController>{RightHand}/thumbstickClicked", "<Keyboard>/p");

        _enabled = _startEnabled;
        _cutoffIndex = Mathf.Clamp(_startCutoffIndex, 0, _alphaCutoffs.Length - 1);
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
        _toggleAction.Enable();
        _cutoffAction.Enable();
    }

    private void OnDisable()
    {
        _toggleAction.performed -= OnToggle;
        _cutoffAction.performed -= OnCycleCutoff;
        _toggleAction.Disable();
        _cutoffAction.Disable();
    }

    private void OnDestroy()
    {
        _toggleAction?.Dispose();
        _cutoffAction?.Dispose();
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

    // The renderer re-reads these every frame, so no buffers or formats change at runtime.
    private void Apply()
    {
        foreach (GaussianSplat3DRenderer splat in _renderers)
        {
            if (splat == null)
                continue;
            splat.m_Occluder = _enabled;
            splat.m_OccluderAlphaCutoff = _alphaCutoffs[_cutoffIndex];
            splat.m_OccluderDepthOffset = _depthOffset;
        }
    }

    private void Report(string changed) =>
        Debug.Log($"[Occlusion] t={Time.realtimeSinceStartup:F1}s changed={changed} enabled={_enabled} " +
                  $"cutoff={_alphaCutoffs[_cutoffIndex]:F2} offset={_depthOffset:F3}m renderers={_renderers.Length}");
}
