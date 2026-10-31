using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test toggle between two collider sets for the same room: the hand-made boxes (Phase 4b) and the colliders
/// generated automatically from the splat (Phase 5, e.g. splat-transform's collision mesh). Colliders shared by both
/// (e.g. the floor plane) are simply left out of both lists.
/// Controller (editor key in brackets): left X (C) = switch sets.
/// Every switch is logged with a "[Colliders]" tag.
/// </summary>
public class ColliderSetToggle : MonoBehaviour
{
    [SerializeField] private Collider[] _handMade;
    [SerializeField] private Collider[] _auto;
    [SerializeField] private bool _startWithAuto = true;

    [Tooltip("Bodies woken on a switch, so a resting object reacts to the new colliders right away.")]
    [SerializeField] private Rigidbody[] _bodies;

    private InputAction _switchAction;
    private bool _useAuto;

    private void Awake()
    {
        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _switchAction = new InputAction("Switch Collider Set", InputActionType.Button);
        _switchAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        _switchAction.AddBinding("<Keyboard>/c");

        _useAuto = _startWithAuto;
        Apply();
    }

    private void OnEnable()
    {
        _switchAction.performed += OnSwitch;
        _switchAction.Enable();
    }

    private void OnDisable()
    {
        _switchAction.performed -= OnSwitch;
        _switchAction.Disable();
    }

    private void OnDestroy() => _switchAction?.Dispose();

    private void Start() => Report();

    private void OnSwitch(InputAction.CallbackContext context)
    {
        _useAuto = !_useAuto;
        Apply();
        Report();
    }

    private void Apply()
    {
        foreach (Collider collider in _handMade)
        {
            if (collider != null)
                collider.enabled = !_useAuto;
        }
        foreach (Collider collider in _auto)
        {
            if (collider != null)
                collider.enabled = _useAuto;
        }
        foreach (Rigidbody body in _bodies)
        {
            if (body != null && !body.isKinematic)
                body.WakeUp();
        }
    }

    private void Report() =>
        Debug.Log($"[Colliders] t={Time.realtimeSinceStartup:F1}s set={(_useAuto ? "auto" : "hand-made")}");
}
