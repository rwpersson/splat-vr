using Gaussians.ThreeD;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Benchmark toggles for a GaussiansGroup, so its settings can be compared in one build.
/// Left controller (editor key in brackets):
///   trigger (N)         = group sorts every frame / every 2nd frame
///   thumbstick click (G) = group on/off (off hands drawing back to the individual renderers, sorted separately)
///   X (H)               = SH order 3/0 on all member renderers (view-dependent color on/off; SH is most of each splat's data)
///   Y (C)               = group compaction default/always (always = load SH only for visible splats)
/// All are values the renderer/group re-reads each frame: no buffer-format or pixel-format changes
/// (unlike the HDR toggle that froze the Quest). Every change is logged with a "[Group]" tag.
/// </summary>
[RequireComponent(typeof(GaussiansGroup))]
public class GroupSortToggle : MonoBehaviour
{
    [SerializeField] private int _slowSortEveryN = 2;

    private GaussiansGroup _group;
    private int _defaultCompactionThreshold;
    private InputAction _sortAction;
    private InputAction _groupAction;
    private InputAction _shAction;
    private InputAction _compactionAction;
    private InputAction _submitAction;

    private void Awake()
    {
        _group = GetComponent<GaussiansGroup>();
        _defaultCompactionThreshold = _group.m_CompactionThreshold;

        // Bindings in code, same reason as the other scripts: Inspector-made actions can end up empty.
        _sortAction = CreateButton("Toggle Group Sort Cadence", "<XRController>{LeftHand}/triggerPressed", "<Keyboard>/n");
        _groupAction = CreateButton("Toggle Group On/Off", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/g");
        _shAction = CreateButton("Toggle SH Order", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/h");
        _compactionAction = CreateButton("Toggle Group Compaction", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/c");
        _submitAction = CreateButton("Toggle Group Submit Path", "<XRController>{RightHand}/primaryButton", "<Keyboard>/p");
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
        // The actions live on this component, so disabling the GaussiansGroup component (not this one) keeps them working.
        _sortAction.performed += OnToggleSort;
        _groupAction.performed += OnToggleGroup;
        _shAction.performed += OnToggleSh;
        _compactionAction.performed += OnToggleCompaction;
        _submitAction.performed += OnToggleSubmit;
        _submitAction.Enable();
        _sortAction.Enable();
        _groupAction.Enable();
        _shAction.Enable();
        _compactionAction.Enable();
    }

    private void OnDisable()
    {
        _sortAction.performed -= OnToggleSort;
        _groupAction.performed -= OnToggleGroup;
        _shAction.performed -= OnToggleSh;
        _compactionAction.performed -= OnToggleCompaction;
        _submitAction.performed -= OnToggleSubmit;
        _submitAction.Disable();
        _sortAction.Disable();
        _groupAction.Disable();
        _shAction.Disable();
        _compactionAction.Disable();
    }

    private void OnDestroy()
    {
        _sortAction?.Dispose();
        _groupAction?.Dispose();
        _shAction?.Dispose();
        _compactionAction?.Dispose();
        _submitAction?.Dispose();
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

    private void OnToggleSh(InputAction.CallbackContext context)
    {
        // The group doesn't override SH order, so it is set on each member renderer.
        int newOrder = CurrentShOrder() == 0 ? 3 : 0;
        foreach (GaussianSplat3DRenderer member in _group.Members)
        {
            if (member != null)
                member.m_SHOrder = newOrder;
        }
        Report("sh");
    }

    private void OnToggleCompaction(InputAction.CallbackContext context)
    {
        _group.m_CompactionThreshold = _group.m_CompactionThreshold == 0 ? _defaultCompactionThreshold : 0;
        Report("compaction");
    }

    private void OnToggleSubmit(InputAction.CallbackContext context)
    {
        // SplatVR patch 2 A/B: instanced quad-mesh path (like individual renderers) vs ninjamode's DrawProcedural path.
        GaussiansGroup.UseMeshPrimitivesSubmit = !GaussiansGroup.UseMeshPrimitivesSubmit;
        Report("submit");
    }

    private int CurrentShOrder()
    {
        foreach (GaussianSplat3DRenderer member in _group.Members)
        {
            if (member != null)
                return member.m_SHOrder;
        }
        return 3;
    }

    private void Report(string changed) =>
        Debug.Log($"[Group] t={Time.realtimeSinceStartup:F1}s changed={changed} groupEnabled={_group.enabled} " +
                  $"sortEveryN={_group.m_SortNthFrame} shOrder={CurrentShOrder()} compactionThreshold={_group.m_CompactionThreshold} " +
                  $"submit={(GaussiansGroup.UseMeshPrimitivesSubmit ? "meshPrimitives" : "drawProcedural")}");
}
