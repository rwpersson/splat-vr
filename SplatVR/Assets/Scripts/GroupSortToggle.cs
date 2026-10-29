using Gaussians.ThreeD;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Benchmark toggles for a GaussiansGroup, so its settings can be compared in one build.
/// Left trigger = sort every frame / every 2nd frame (editor: N).
/// Left thumbstick click = group on/off (editor: G). Off hands drawing back to the individual
/// splat renderers (each sorted separately); ninjamode documents this as the intended way to disable a group.
/// Both are safe at runtime: no buffer-format or pixel-format changes (unlike the HDR toggle that froze the Quest).
/// Every change is logged with a "[Group]" tag so headset logs and traces can be matched to it.
/// </summary>
[RequireComponent(typeof(GaussiansGroup))]
public class GroupSortToggle : MonoBehaviour
{
    [SerializeField] private int _slowSortEveryN = 2;

    private GaussiansGroup _group;
    private InputAction _sortAction;
    private InputAction _groupAction;

    private void Awake()
    {
        _group = GetComponent<GaussiansGroup>();

        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _sortAction = new InputAction("Toggle Group Sort Cadence", InputActionType.Button);
        _sortAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
        _sortAction.AddBinding("<Keyboard>/n");

        _groupAction = new InputAction("Toggle Group On/Off", InputActionType.Button);
        _groupAction.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        _groupAction.AddBinding("<Keyboard>/g");
    }

    private void OnEnable()
    {
        // The actions live on this component, so disabling the GaussiansGroup component (not this one) keeps them working.
        _sortAction.performed += OnToggleSort;
        _groupAction.performed += OnToggleGroup;
        _sortAction.Enable();
        _groupAction.Enable();
    }

    private void OnDisable()
    {
        _sortAction.performed -= OnToggleSort;
        _groupAction.performed -= OnToggleGroup;
        _sortAction.Disable();
        _groupAction.Disable();
    }

    private void OnDestroy()
    {
        _sortAction?.Dispose();
        _groupAction?.Dispose();
    }

    private void Start() => Report("start");

    private void OnToggleSort(InputAction.CallbackContext context)
    {
        _group.m_SortNthFrame = _group.m_SortNthFrame == 1 ? _slowSortEveryN : 1;
        Report("sort");
    }

    private void OnToggleGroup(InputAction.CallbackContext context)
    {
        _group.enabled = !_group.enabled;
        Report("group");
    }

    private void Report(string changed) =>
        Debug.Log($"[Group] t={Time.realtimeSinceStartup:F1}s changed={changed} groupEnabled={_group.enabled} sortEveryN={_group.m_SortNthFrame}");
}
