using Gaussians.ThreeD;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Benchmark toggle for a GaussiansGroup: left trigger switches between sorting every frame and every 2nd frame.
/// In the editor: N. Safe at runtime: the group re-reads this value each frame (no buffer or format changes).
/// Every change is logged with a "[Group]" tag so headset frame-rate logs can be matched to it.
/// </summary>
[RequireComponent(typeof(GaussiansGroup))]
public class GroupSortToggle : MonoBehaviour
{
    [SerializeField] private int _slowSortEveryN = 2;

    private GaussiansGroup _group;
    private InputAction _toggleAction;

    private void Awake()
    {
        _group = GetComponent<GaussiansGroup>();

        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _toggleAction = new InputAction("Toggle Group Sort Cadence", InputActionType.Button);
        _toggleAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
        _toggleAction.AddBinding("<Keyboard>/n");
    }

    private void OnEnable()
    {
        _toggleAction.performed += OnToggle;
        _toggleAction.Enable();
    }

    private void OnDisable()
    {
        _toggleAction.performed -= OnToggle;
        _toggleAction.Disable();
    }

    private void OnDestroy() => _toggleAction?.Dispose();

    private void Start() => Report();

    private void OnToggle(InputAction.CallbackContext context)
    {
        _group.m_SortNthFrame = _group.m_SortNthFrame == 1 ? _slowSortEveryN : 1;
        Report();
    }

    private void Report() =>
        Debug.Log($"[Group] t={Time.realtimeSinceStartup:F1}s sortEveryN={_group.m_SortNthFrame}");
}
