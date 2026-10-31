using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using Material = UnityEngine.Material;

/// <summary>
/// Shows the real controller model on a hand: asks ControllerRenderModelFeature for the headset's .glb, loads it with
/// glTFast, and hides the fallback (the old sphere) once it appears. Until then, or if the headset offers no model
/// (e.g. in the editor), the fallback stays. Retries once a second, since a controller may connect late.
/// The model's renderers become the grabber's indicators, so it's lightly tinted when something is in reach / held.
/// Logs with a "[RenderModel]" tag.
/// </summary>
public class ControllerModel : MonoBehaviour
{
    private enum Hand { Left, Right }

    [SerializeField] private Hand _hand = Hand.Right;

    [Tooltip("Shown until the real model loads (the old sphere).")]
    [SerializeField] private GameObject _fallback;

    [Tooltip("Material copied for every model part (only its base color/texture change), so no extra shaders are needed in the build.")]
    [SerializeField] private Material _materialTemplate;

    [SerializeField] private SplatGrabber _grabber;

    [Tooltip("How strongly the grab-state color tints the model (0 = never, 1 = full color).")]
    [SerializeField] private float _tintStrength = 0.35f;

    // glTF and Unity convert OpenXR's right-handed space by mirroring different axes (glTFast flips X, Unity's OpenXR
    // flips Z), which differ by a half turn about Y.
    [SerializeField] private Vector3 _rotationOffset = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 _positionOffset = Vector3.zero;

    private GltfImport _gltf;
    private Transform _modelRoot;
    private float _nextAttempt;
    private bool _busy;
    private string _lastFailure;

    private void Update()
    {
        if (_modelRoot != null || _busy || Time.time < _nextAttempt)
            return;
        _nextAttempt = Time.time + 1f;
        if (!ControllerRenderModelFeature.IsReady)
            return;

        string path = _hand == Hand.Left ? ControllerRenderModelFeature.LeftPath : ControllerRenderModelFeature.RightPath;
        if (!ControllerRenderModelFeature.TryLoadModel(path, out byte[] glb, out string description))
        {
            // Log each new reason once, not every second.
            if (description != _lastFailure)
                Debug.Log($"[RenderModel] {_hand}: {description}");
            _lastFailure = description;
            return;
        }

        Debug.Log($"[RenderModel] {_hand}: got {description}");
        LoadAsync(glb);
    }

    private async void LoadAsync(byte[] glb)
    {
        _busy = true;
        _gltf = new GltfImport(materialGenerator: new TemplateMaterialGenerator(_materialTemplate), logger: new ConsoleLogger());
        bool ok = await _gltf.Load(glb);
        if (ok)
        {
            var root = new GameObject("Controller Model").transform;
            root.SetParent(transform, false);
            root.localPosition = _positionOffset;
            root.localRotation = Quaternion.Euler(_rotationOffset);
            ok = await _gltf.InstantiateMainSceneAsync(root);
            if (ok)
                _modelRoot = root;
            else
                Destroy(root.gameObject);
        }

        _busy = false;
        if (!ok)
        {
            Debug.LogWarning($"[RenderModel] {_hand}: glTF load failed, keeping the fallback");
            _gltf.Dispose();
            _gltf = null;
            enabled = false;
            return;
        }

        if (_fallback != null)
            _fallback.SetActive(false);
        Renderer[] renderers = _modelRoot.GetComponentsInChildren<Renderer>();
        if (_grabber != null)
            _grabber.SetIndicators(renderers, _tintStrength);
        Debug.Log($"[RenderModel] {_hand}: model shown ({renderers.Length} renderers)");
    }

    private void OnDestroy() => _gltf?.Dispose();

    /// <summary>Turns each glTF material into a copy of the template with the glTF base color and texture.</summary>
    private class TemplateMaterialGenerator : IMaterialGenerator
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private readonly Material _template;

        public TemplateMaterialGenerator(Material template) => _template = template;

        public Material GetDefaultMaterial(bool pointsSupport = false) => new Material(_template) { color = Color.white };

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
        {
            var material = GetDefaultMaterial();
            material.name = gltfMaterial.name;
            PbrMetallicRoughnessBase pbr = gltfMaterial.PbrMetallicRoughness;
            if (pbr == null)
                return material;
            material.color = pbr.BaseColor;
            int textureIndex = pbr.BaseColorTexture != null ? pbr.BaseColorTexture.index : -1;
            if (textureIndex < 0)
                return material;

            material.SetTexture(BaseMap, gltf.GetTexture(textureIndex));
            // Some textures (e.g. KTX, which the headset's models use) load upside down; glTFast's own materials flip
            // them back with the texture scale/offset, so do the same, or the texture lands in the wrong places.
            if (gltf.IsTextureYFlipped(textureIndex))
            {
                material.SetTextureScale(BaseMap, new Vector2(1f, -1f));
                material.SetTextureOffset(BaseMap, new Vector2(0f, 1f));
            }
            return material;
        }

        public void SetLogger(ICodeLogger logger) { }
    }
}
