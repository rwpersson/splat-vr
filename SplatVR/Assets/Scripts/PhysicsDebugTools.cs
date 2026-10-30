using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physics test helpers for the Interaction scene.
/// Right controller (editor key in brackets):
///   B (V) = show/hide wireframes of every BoxCollider, to check they line up with the splats
///   A (R) = put the reset target (the mug) back where it started, at rest
/// Also runs physics at the display rate, so a falling object moves once per frame (no stepping judder).
/// Every action is logged with a "[Physics]" tag.
/// </summary>
public class PhysicsDebugTools : MonoBehaviour
{
    [Tooltip("Object put back on reset (the mug body).")]
    [SerializeField] private Rigidbody _resetTarget;

    [Tooltip("Unlit material for the wireframes. Assigned here so the shader is included in builds.")]
    [SerializeField] private Material _lineMaterial;

    [SerializeField] private float _lineWidth = 0.005f;
    [SerializeField] private float _physicsRate = 72f;

    // One continuous path over all 12 edges of a box (3 edges are walked twice); corner i has bits (x, y, z) = (i & 1, i & 2, i & 4).
    private static readonly int[] EdgePath = { 0, 1, 3, 2, 0, 4, 5, 7, 6, 4, 5, 1, 3, 7, 6, 2 };

    private InputAction _wireframeAction;
    private InputAction _resetAction;
    private readonly List<LineRenderer> _wireframes = new List<LineRenderer>();
    private bool _wireframesBuilt;
    private bool _wireframesVisible;
    private Vector3 _startPosition;
    private Quaternion _startRotation;

    private void Awake()
    {
        Time.fixedDeltaTime = 1f / _physicsRate;

        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _wireframeAction = CreateButton("Toggle Collider Wireframes", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/v");
        _resetAction = CreateButton("Reset Mug", "<XRController>{RightHand}/primaryButton", "<Keyboard>/r");

        if (_resetTarget != null)
        {
            _startPosition = _resetTarget.position;
            _startRotation = _resetTarget.rotation;
        }
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
        _wireframeAction.performed += OnToggleWireframes;
        _resetAction.performed += OnReset;
        _wireframeAction.Enable();
        _resetAction.Enable();
    }

    private void OnDisable()
    {
        _wireframeAction.performed -= OnToggleWireframes;
        _resetAction.performed -= OnReset;
        _wireframeAction.Disable();
        _resetAction.Disable();
    }

    private void OnDestroy()
    {
        _wireframeAction?.Dispose();
        _resetAction?.Dispose();
    }

    private void OnToggleWireframes(InputAction.CallbackContext context)
    {
        if (!_wireframesBuilt)
            BuildWireframes();

        _wireframesVisible = !_wireframesVisible;
        foreach (LineRenderer line in _wireframes)
        {
            if (line != null)
                line.enabled = _wireframesVisible;
        }
        Debug.Log($"[Physics] t={Time.realtimeSinceStartup:F1}s wireframes={_wireframesVisible} count={_wireframes.Count}");
    }

    private void OnReset(InputAction.CallbackContext context)
    {
        if (_resetTarget == null)
            return;

        // A held mug stays with the hand: the grabber owns it until release.
        var grabbable = _resetTarget.GetComponent<Grabbable>();
        if (grabbable != null && grabbable.IsHeld)
            return;

        _resetTarget.isKinematic = false;
        _resetTarget.linearVelocity = Vector3.zero;
        _resetTarget.angularVelocity = Vector3.zero;
        _resetTarget.transform.SetPositionAndRotation(_startPosition, _startRotation);
        Debug.Log($"[Physics] t={Time.realtimeSinceStartup:F1}s reset {_resetTarget.name}");
    }

    /// <summary>Makes one LineRenderer per BoxCollider, parented to the collider so it follows a moving body.</summary>
    private void BuildWireframes()
    {
        _wireframesBuilt = true;
        foreach (BoxCollider box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            var lineObject = new GameObject($"Wireframe {box.name}");
            lineObject.transform.SetParent(box.transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = _lineMaterial;
            line.startColor = line.endColor = box.attachedRigidbody != null ? Color.green : Color.cyan;
            line.startWidth = line.endWidth = _lineWidth;

            Vector3 half = box.size * 0.5f;
            line.positionCount = EdgePath.Length;
            for (int i = 0; i < EdgePath.Length; i++)
            {
                int corner = EdgePath[i];
                line.SetPosition(i, box.center + new Vector3(
                    (corner & 1) != 0 ? half.x : -half.x,
                    (corner & 2) != 0 ? half.y : -half.y,
                    (corner & 4) != 0 ? half.z : -half.z));
            }
            _wireframes.Add(line);
        }
    }
}
