using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets one controller pick up Grabbable objects: hold grip near one to grab it, release grip to let go.
/// Put this on a hand object that a Tracked Pose Driver moves with the controller.
/// If the object has a Rigidbody, physics is paused while it's held (kinematic) and, on release,
/// it gets the hand's recent velocity and spin, so it can be dropped or thrown.
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

    [Tooltip("Multiplier on the hand velocity given to a released object (1 = realistic).")]
    [SerializeField] private float _throwScale = 1f;

    // Hand motion is averaged over a short window: a single frame's velocity is too noisy for throwing.
    private const int VelocitySamples = 6;
    private readonly Vector3[] _positions = new Vector3[VelocitySamples];
    private readonly Quaternion[] _rotations = new Quaternion[VelocitySamples];
    private readonly float[] _times = new float[VelocitySamples];
    private int _sampleCount;
    private int _sampleIndex;

    private Grabbable[] _grabbables;
    private InputAction _gripAction;
    private Grabbable _inReach;
    private Grabbable _held;
    private Rigidbody _heldBody;
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
        RecordHandSample();

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

        // Physics would fight the hand, so the body ignores forces/gravity while held.
        _heldBody = _held.GetComponent<Rigidbody>();
        if (_heldBody != null)
            _heldBody.isKinematic = true;

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

        Vector3 velocity = Vector3.zero;
        if (_heldBody != null)
        {
            // Hand back to physics with the hand's motion, so a flick throws and a gentle release just drops.
            _heldBody.isKinematic = false;
            EstimateHandMotion(out velocity, out Vector3 angularVelocity);
            _heldBody.linearVelocity = velocity * _throwScale;
            _heldBody.angularVelocity = angularVelocity;
        }

        Debug.Log($"[Grab] t={Time.realtimeSinceStartup:F1}s {_hand} released {_held.name} at {_held.transform.position} speed={velocity.magnitude:F2} m/s");
        _held = null;
        _heldBody = null;
        SetIndicator(_idleColor);
    }

    private void RecordHandSample()
    {
        _positions[_sampleIndex] = transform.position;
        _rotations[_sampleIndex] = transform.rotation;
        _times[_sampleIndex] = Time.time;
        _sampleIndex = (_sampleIndex + 1) % VelocitySamples;
        _sampleCount = Mathf.Min(_sampleCount + 1, VelocitySamples);
    }

    /// <summary>Average linear and angular velocity of the hand over the recorded window (oldest → newest sample).</summary>
    private void EstimateHandMotion(out Vector3 velocity, out Vector3 angularVelocity)
    {
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;
        if (_sampleCount < 2)
            return;

        int newest = (_sampleIndex - 1 + VelocitySamples) % VelocitySamples;
        int oldest = (_sampleIndex - _sampleCount + VelocitySamples) % VelocitySamples;
        float dt = _times[newest] - _times[oldest];
        if (dt <= 0f)
            return;

        velocity = (_positions[newest] - _positions[oldest]) / dt;

        Quaternion delta = _rotations[newest] * Quaternion.Inverse(_rotations[oldest]);
        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (angleDegrees > 180f)
            angleDegrees -= 360f;
        if (!float.IsNaN(axis.x))
            angularVelocity = axis * (angleDegrees * Mathf.Deg2Rad / dt);
    }

    private void SetIndicator(Color color)
    {
        if (_indicator != null)
            _indicator.material.color = color;
    }
}
