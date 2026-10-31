using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the player rig (the parent of the camera and both hands) so you can reach things beyond your play space.
/// Controller (editor keys in brackets):
///   left stick (WASD)          = walk where you're looking (flat, no flying), smooth
///   right stick left/right (QE) = snap turn, around your head so you stay in place
/// The player has no collider: you walk through furniture.
/// Turns are logged with a "[Locomotion]" tag (walking isn't, it would flood the log).
/// </summary>
public class SimpleLocomotion : MonoBehaviour
{
    [Tooltip("The headset camera (child of this rig).")]
    [SerializeField] private Transform _head;

    [SerializeField] private float _speed = 1.5f;
    [SerializeField] private float _snapAngle = 45f;

    [Tooltip("Stick deflection that triggers a snap turn; it must come back under half of this before the next one.")]
    [SerializeField] private float _snapThreshold = 0.7f;

    private InputAction _moveAction;
    private InputAction _turnAction;
    private bool _turnArmed = true;

    private void Awake()
    {
        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _moveAction = new InputAction("Move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
        _moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

        _turnAction = new InputAction("Snap Turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");
        _turnAction.AddCompositeBinding("2DVector")
            .With("Left", "<Keyboard>/q").With("Right", "<Keyboard>/e");
    }

    private void OnEnable()
    {
        _moveAction.Enable();
        _turnAction.Enable();
    }

    private void OnDisable()
    {
        _moveAction.Disable();
        _turnAction.Disable();
    }

    private void OnDestroy()
    {
        _moveAction?.Dispose();
        _turnAction?.Dispose();
    }

    private void Update()
    {
        Vector2 move = _moveAction.ReadValue<Vector2>();
        if (move.sqrMagnitude > 0.01f)
        {
            // Head direction flattened onto the floor, so looking down doesn't slow you or push you into the ground.
            Vector3 forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_head.right, Vector3.up).normalized;
            transform.position += (forward * move.y + right * move.x) * (_speed * Time.deltaTime);
        }

        float turn = _turnAction.ReadValue<Vector2>().x;
        if (_turnArmed && Mathf.Abs(turn) >= _snapThreshold)
        {
            _turnArmed = false;
            float angle = Mathf.Sign(turn) * _snapAngle;
            transform.RotateAround(_head.position, Vector3.up, angle);
            Debug.Log($"[Locomotion] t={Time.realtimeSinceStartup:F1}s snap turn {angle:+0;-0}°");
        }
        else if (Mathf.Abs(turn) < _snapThreshold * 0.5f)
        {
            _turnArmed = true;
        }
    }
}
