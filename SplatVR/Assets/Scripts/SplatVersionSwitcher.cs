using System.Collections.Generic;
using Gaussians.ThreeD;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows one splat version at a time and cycles through them with a controller button,
/// so several splat counts can be compared in one build (Phase 1 benchmarking).
/// Right controller A = next, B = previous. In the editor: Space = next, Backspace = previous.
/// Every switch is logged with a "[SplatSwitch]" tag so headset frame-rate logs can be matched to versions.
/// </summary>
public class SplatVersionSwitcher : MonoBehaviour
{
    [Tooltip("The splat objects to cycle through, in order. Leave empty to use this object's children.")]
    [SerializeField] private List<GameObject> _versions = new();

    [Tooltip("Optional floating label that shows which version is active.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Which version to show at startup (0 = first).")]
    [SerializeField] private int _startIndex;

    private InputAction _nextAction;
    private InputAction _previousAction;
    private int _currentIndex = -1;

    private void Awake()
    {
        if (_versions.Count == 0)
        {
            foreach (Transform child in transform)
                _versions.Add(child.gameObject);
        }

        // Bindings are created in code on purpose: an InputAction set up in the Inspector can
        // silently end up with no bindings (that's what made the camera head-locked in Phase 0).
        _nextAction = new InputAction("Next Version", InputActionType.Button);
        _nextAction.AddBinding("<XRController>{RightHand}/primaryButton");
        _nextAction.AddBinding("<Keyboard>/space");

        _previousAction = new InputAction("Previous Version", InputActionType.Button);
        _previousAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        _previousAction.AddBinding("<Keyboard>/backspace");
    }

    private void OnEnable()
    {
        _nextAction.performed += OnNext;
        _previousAction.performed += OnPrevious;
        _nextAction.Enable();
        _previousAction.Enable();
    }

    private void OnDisable()
    {
        _nextAction.performed -= OnNext;
        _previousAction.performed -= OnPrevious;
        _nextAction.Disable();
        _previousAction.Disable();
    }

    private void OnDestroy()
    {
        _nextAction?.Dispose();
        _previousAction?.Dispose();
    }

    private void Start()
    {
        if (_versions.Count == 0)
        {
            Debug.LogWarning("[SplatSwitch] No versions assigned and no children found.");
            return;
        }

        // Turn everything off first so only one version is ever drawn.
        foreach (GameObject version in _versions)
            version.SetActive(false);

        Show(Mathf.Clamp(_startIndex, 0, _versions.Count - 1));
    }

    private void OnNext(InputAction.CallbackContext context) => Show((_currentIndex + 1) % _versions.Count);

    private void OnPrevious(InputAction.CallbackContext context) => Show((_currentIndex - 1 + _versions.Count) % _versions.Count);

    private void Show(int index)
    {
        if (_currentIndex >= 0)
            _versions[_currentIndex].SetActive(false);

        _currentIndex = index;
        GameObject version = _versions[_currentIndex];
        version.SetActive(true);

        var splatRenderer = version.GetComponent<GaussianSplat3DRenderer>();
        int splatCount = splatRenderer != null && splatRenderer.asset != null ? splatRenderer.asset.splatCount : 0;

        string text = $"{version.name} ({splatCount:N0} splats)";
        if (_label != null)
            _label.text = text;

        // realtimeSinceStartup lets the switch time be matched against the headset's frame-rate log.
        Debug.Log($"[SplatSwitch] t={Time.realtimeSinceStartup:F1}s {text}");
    }
}
