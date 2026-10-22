using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Turns on fixed foveated rendering (sharp center, lower-resolution edges) through Unity's SRP Foveation API
/// and lets the strength be cycled in the headset for benchmarking.
/// Left controller X = next level. In the editor: F = next level.
/// Requires OpenXR settings: Foveated Rendering API = SRP Foveation, and the Foveated Rendering feature enabled.
/// Every change is logged with a "[Foveation]" tag so headset frame-rate logs can be matched to levels.
/// </summary>
public class FoveationController : MonoBehaviour
{
    [Tooltip("Strengths to cycle through, 0 = off, 1 = maximum. Quest maps these to its discrete off/low/medium/high levels.")]
    [SerializeField] private float[] _levels = { 0f, 0.33f, 0.67f, 1f };

    [Tooltip("Which level to use at startup (index into Levels).")]
    [SerializeField] private int _startIndex;

    [Tooltip("Optional label that shows the current foveation level.")]
    [SerializeField] private TMP_Text _label;

    private readonly List<XRDisplaySubsystem> _displays = new();
    private InputAction _nextAction;
    private int _currentIndex;

    private void Awake()
    {
        // Bindings in code, same reason as SplatVersionSwitcher: Inspector-made actions can end up empty.
        _nextAction = new InputAction("Next Foveation Level", InputActionType.Button);
        _nextAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        _nextAction.AddBinding("<Keyboard>/f");
    }

    private void OnEnable()
    {
        _nextAction.performed += OnNext;
        _nextAction.Enable();
    }

    private void OnDisable()
    {
        _nextAction.performed -= OnNext;
        _nextAction.Disable();
    }

    private void OnDestroy() => _nextAction?.Dispose();

    private void Start() => Apply(Mathf.Clamp(_startIndex, 0, _levels.Length - 1));

    private void OnNext(InputAction.CallbackContext context) => Apply((_currentIndex + 1) % _levels.Length);

    private void Apply(int index)
    {
        _currentIndex = index;
        float level = _levels[_currentIndex];

        SubsystemManager.GetSubsystems(_displays);
        if (_displays.Count == 0)
        {
            // Normal in editor Play mode without a headset.
            Debug.LogWarning($"[Foveation] No XR display running; level {level:F2} not applied.");
        }
        foreach (XRDisplaySubsystem display in _displays)
            display.foveatedRenderingLevel = level;

        string text = level <= 0f ? "Foveation off" : $"Foveation {level:F2}";
        if (_label != null)
            _label.text = text;

        Debug.Log($"[Foveation] t={Time.realtimeSinceStartup:F1}s level={level:F2}");
    }
}
