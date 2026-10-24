using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets one controller pick up Grabbable objects: hold grip near one to grab it, release grip to let go.
/// Put this on a hand object that a Tracked Pose Driver moves with the controller.
/// The optional indicator (e.g. a small sphere) shows the state: white = idle, yellow = in reach, green = holding.
/// Every grab and release is logged with a "[Grab]" tag.
/// </summary>
public class SplatGrabber : MonoBehaviour
{
    private enum Hand { Left, Right }

    [SerializeField] private Hand _hand = Hand.Right;

    [Tooltip("Optional renderer whose color shows the grab state.")]
    [SerializeField] private Renderer _indicator;

    [SerializeField] private Color _idleColor = Color.white;
    [SerializeField] private Color _inReachColor = Color.yellow;
    [SerializeField] private Color _holdingColor = Color.green;

    private Grabbable[] _grabbables;
    private InputAction _gripAction;
    private Grabbable _inReach;
    private Grabbable _held;
    private Transform _heldOriginalParent;

    private void Awake()
    {
        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        string handName = _hand == Hand.Left ? "LeftHand" : "RightHand";
        _gripAction = new InputAction($"Grip {handName}", InputActionType.Button);
        _gripAction.AddBinding($"<XRController>{{{handName}}}/gripPressed");
    }

    private void OnEnable()
    {
        _gripAction.performed += OnGripPressed;
        _gripAction.canceled += OnGripReleased;
        _gripAction.Enable();
    }

    private void OnDisable()
    {
        _gripAction.performed -= OnGripPressed;
        _gripAction.canceled -= OnGripReleased;
        _gripAction.Disable();
        Release();
    }

    private void OnDestroy() => _gripAction?.Dispose();

    private void Start()
    {
        _grabbables = FindObjectsByType<Grabbable>(FindObjectsSortMode.None);
        SetIndicator(_idleColor);
    }

    private void Update()
    {
        if (_held != null)
            return;

        // Find the closest grabbable whose grab zone contains this hand.
        _inReach = null;
        float closest = float.MaxValue;
        foreach (Grabbable grabbable in _grabbables)
        {
            if (grabbable == null || grabbable.IsHeld)
                continue;
            float distance = Vector3.Distance(transform.position, grabbable.Center);
            if (distance <= grabbable.GrabRadius && distance < closest)
            {
                closest = distance;
                _inReach = grabbable;
            }
        }

        SetIndicator(_inReach != null ? _inReachColor : _idleColor);
    }

    private void OnGripPressed(InputAction.CallbackContext context)
    {
        if (_held != null || _inReach == null)
            return;

        _held = _inReach;
        _held.IsHeld = true;
        _heldOriginalParent = _held.transform.parent;

        // Parenting to the hand keeps the object's current offset from the hand ("worldPositionStays"),
        // so it doesn't snap into the palm, and it moves exactly with the hand every frame.
        _held.transform.SetParent(transform, true);
        SetIndicator(_holdingColor);
        Debug.Log($"[Grab] t={Time.realtimeSinceStartup:F1}s {_hand} grabbed {_held.name}");
    }

    private void OnGripReleased(InputAction.CallbackContext context) => Release();

    private void Release()
    {
        if (_held == null)
            return;

        _held.transform.SetParent(_heldOriginalParent, true);
        _held.IsHeld = false;
        Debug.Log($"[Grab] t={Time.realtimeSinceStartup:F1}s {_hand} released {_held.name} at {_held.transform.position}");
        _held = null;
        SetIndicator(_idleColor);
    }

    private void SetIndicator(Color color)
    {
        if (_indicator != null)
            _indicator.material.color = color;
    }
}
