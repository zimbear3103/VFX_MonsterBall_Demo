using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Generates and maintains optimized MeshCollider or compound convex colliders for a mesh source.
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [AddComponentMenu("Physics/Mesh Collider Optimizer")]
    [HelpURL("https://occlusionn.gitbook.io/docs")]
    public partial class MeshColliderOptimizer : MonoBehaviour
    {
        #region Settings
        [Header("Editor Performance")]
        public bool autoUpdate = true;

        [Header("Preset")]
        [Tooltip("Assign a saved OptimizationPreset to quickly apply settings. Create presets via Assets > Create > Mesh Collider Optimizer > Preset.")]
        public OptimizationPreset preset;

        [Header("Source MeshCollider Settings")]
        public bool convex = false;
        public bool isTrigger = false;
        public PhysicsMaterial material;

        [Header("Layer Overrides")]
        [Tooltip("Layer override priority used by generated colliders.")]
        public int layerOverridePriority;
        [Tooltip("Layers this collider can collide with.")]
        public LayerMask includeLayers = ~0;
        [Tooltip("Layers this collider should never collide with.")]
        public LayerMask excludeLayers;

        [FormerlySerializedAs("colliderLayerOverride")]
        [SerializeField, HideInInspector] private int _legacyColliderLayerOverride = -1;

        [Tooltip("Cooking options applied to every generated MeshCollider. Controls mesh cleaning, weld, and fast-cook behaviour.")]
        public MeshColliderCookingOptions cookingOptions =
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.CookForFasterSimulation;

        [Tooltip("When enabled, Unity generates contact data for this collider in OnCollisionStay / collision queries (matches MeshCollider.providesContacts).")]
        public bool providesContacts;

        [Header("Source Override")]
        [Tooltip("Optional external source renderer on another hierarchy. Supports SkinnedMeshRenderer or MeshRenderer (with MeshFilter).")]
        public Renderer externalTargetRenderer;
        [Tooltip("Optional external MeshFilter source. Useful when you want to target a specific non-skinned mesh directly.")]
        public MeshFilter externalTargetMeshFilter;
        [Tooltip("Optional external root transform used for Merge Children source collection.")]
        public Transform externalSourceRoot;

        [Header("Skinned Mesh & BlendShape")]
        public bool capturePoseSnapshot = true;
        public bool trackBlendShapes;

        [Header("LOD")]
        [Tooltip("When enabled, generated colliders are only enabled on the currently active LOD level (prevents multiple LOD colliders from being active at once).")]
        public bool enableCollidersOnlyOnActiveLod;

        [Header("Convex Decomposition")]
        [Range(2, 10)] public int decompositionGrid = 2;

        [Header("Optimization & Modification")]
        [Range(0.001f, 1f)] public float quality = 0.5f;
        [Range(0.01f, 1f)] public float shapeFidelity = 0.5f;

        [Tooltip("Positive inflates along normals, negative deflates.")]
        [Range(-0.2f, 0.2f)] public float inflationAmount;
        [Tooltip("More aggressive simplify/split to avoid PhysX 256-polygon convex warnings.")]
        public bool strict256Guard = true;
        private const int CONVEX_STRICT_VERTEX_LIMIT = 120;
        private const int CONVEX_STRICT_TRI_LIMIT    = 900;

        [Header("Merge Settings")]
        public bool mergeChildObjects = false;
        [SerializeField, HideInInspector] private bool _addedRootMeshCollider = false;

        [Header("Visual Error Analysis (Heatmap)")]
        public bool showHeatmap = false;
        [Header("Gizmos")]
        [Tooltip("When disabled, this component will not draw any SceneView gizmos (wire meshes, heatmap points, etc.).")]
        public bool showGizmos = true;
        [Range(0.01f, 0.5f)] public float maxErrorTolerance = 0.1f;
        [Range(1, 100)] public int heatmapStride = 10;
        public bool drawErrorLines = true;

        [HideInInspector] public int stat_VertsBefore;
        [HideInInspector] public int stat_VertsAfter;
        [HideInInspector] public int stat_TrisBefore;
        [HideInInspector] public int stat_TrisAfter;
        [HideInInspector] public float savingsRatio;
        [HideInInspector] public string stat_MemoryBefore;
        [HideInInspector] public string stat_MemoryAfter;
        [HideInInspector] public string stat_BakeTime;
        [HideInInspector] public int stat_ConvexPartCount;

        private const HideFlags GENERATED_MESH_COLLIDER_HIDE_FLAGS = HideFlags.None;
        private const string HIDDEN_ROOT_NAME = "Hidden_Root_Collider";
        private const string CONVEX_PART_PREFIX = "Convex_Part_";
        private const string OPT_MESH_PREFIX = "Opt_Process_";

        [Header("Active Colliders List")]
        public List<Collider> generatedColliders = new List<Collider>();

        private const HideFlags COLLIDER_HIDE_FLAGS = HideFlags.HideInInspector | HideFlags.NotEditable;

        private const HideFlags PART_GO_HIDE_FLAGS = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;
        #endregion

        #region Collider Management

        private Transform GetOrCreateHiddenRootTransform()
        {
            Transform t = transform.Find(HIDDEN_ROOT_NAME);
            if (t != null) return t;

            GameObject go = new GameObject(HIDDEN_ROOT_NAME);
            go.layer = gameObject.layer;
            go.hideFlags = PART_GO_HIDE_FLAGS;

            Transform tr = go.transform;
            tr.SetParent(transform, false);
            tr.localPosition = Vector3.zero;
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one;

            return tr;
        }

        private UnityEngine.MeshCollider GetOrCreateHiddenRootCollider()
        {
            if (_targetMeshCollider != null &&
                _targetMeshCollider.gameObject != null &&
                _targetMeshCollider.gameObject.name == HIDDEN_ROOT_NAME)
            {
                return _targetMeshCollider;
            }

            Transform t = GetOrCreateHiddenRootTransform();
            GameObject go = t.gameObject;
            go.hideFlags = PART_GO_HIDE_FLAGS;

            var mc = go.GetComponent<UnityEngine.MeshCollider>();
            if (mc == null) mc = go.AddComponent<UnityEngine.MeshCollider>();

            mc.hideFlags = COLLIDER_HIDE_FLAGS;

            _targetMeshCollider = mc;
            return mc;
        }

        private void RegisterGeneratedCollider(Collider col)
        {
            if (col == null) return;
            if (!generatedColliders.Contains(col))
            {
                generatedColliders.Add(col);
            }
            col.hideFlags = COLLIDER_HIDE_FLAGS;

            ApplyLodColliderEnableState(col);
            ApplyLayerOverrideSettings(col);

            if (col is UnityEngine.MeshCollider mc)
            {
                mc.cookingOptions = cookingOptions;
#if UNITY_2022_1_OR_NEWER
                mc.providesContacts = providesContacts;
#endif
            }
        }

        private void ApplyLayerOverrideSettings(Collider col)
        {
            if (col == null) return;

            TrySetColliderLayerOverrideValue(col, "layerOverridePriority", layerOverridePriority);
            TrySetColliderLayerOverrideValue(col, "includeLayers", includeLayers.value);
            TrySetColliderLayerOverrideValue(col, "excludeLayers", excludeLayers.value);
        }

        private static void TrySetColliderLayerOverrideValue(Collider col, string propertyName, int value)
        {
            if (col == null || string.IsNullOrEmpty(propertyName)) return;

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var prop = col.GetType().GetProperty(propertyName, flags);
            if (prop == null || !prop.CanWrite) return;

            try
            {
                if (prop.PropertyType == typeof(int))
                {
                    prop.SetValue(col, value);
                }
                else if (prop.PropertyType == typeof(LayerMask))
                {
                    prop.SetValue(col, (LayerMask)value);
                }
            }
            catch
            {
                // Unity version/collider type might not expose this property.
            }
        }

        private bool ShouldUseCompoundConvexColliders()
        {
            return convex || isTrigger || HasNonKinematicRigidbody(out _);
        }

        private static int CountGeneratedConvexParts(Transform hiddenRoot)
        {
            if (hiddenRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < hiddenRoot.childCount; i++)
            {
                var child = hiddenRoot.GetChild(i);
                if (child != null && child.name.StartsWith(CONVEX_PART_PREFIX))
                    count++;
            }
            return count;
        }

        private void DisableHiddenRootCollider(bool clearSharedMesh)
        {
            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot == null) return;

            var rootMc = hiddenRoot.GetComponent<UnityEngine.MeshCollider>();
            if (rootMc == null) return;

            rootMc.enabled = false;
            rootMc.isTrigger = false;

            if (clearSharedMesh)
                rootMc.sharedMesh = null;
        }

        private void MigrateLegacyHiddenRootColliderIfNeeded()
        {
            if (!ShouldUseCompoundConvexColliders()) return;

            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot == null) return;

            var rootMc = hiddenRoot.GetComponent<UnityEngine.MeshCollider>();
            if (rootMc == null || rootMc.sharedMesh == null || rootMc.convex) return;

            if (CountGeneratedConvexParts(hiddenRoot) == 0)
            {
                GenerateCompoundColliders(rootMc.sharedMesh);
            }

            rootMc.enabled = false;
            rootMc.isTrigger = false;
        }


        void ApplyMeshToColliders()
        {
            if (FinalMesh == null) return;

            ClearConvexHullGizmoCache();

            var rootColliderOld = GetComponent<UnityEngine.MeshCollider>();
            if (rootColliderOld != null) DestroyObjSafe(rootColliderOld);

            if (generatedColliders == null) generatedColliders = new List<Collider>();
            generatedColliders.RemoveAll(c => c == null);

            bool useCompoundConvex = ShouldUseCompoundConvexColliders();

            if (!useCompoundConvex)
            {
                UnityEngine.MeshCollider targetMC = GetOrCreateHiddenRootCollider();

                targetMC.enabled = true;
                targetMC.convex = false;
                targetMC.isTrigger = false;
                targetMC.sharedMaterial = material;
                targetMC.sharedMesh = FinalMesh;

                RegisterGeneratedCollider(targetMC);

                DestroyConvexChildrenAndMeshes(false);

                if (isTrigger) GenerateCompoundColliders(FinalMesh);

                stat_ConvexPartCount = 1;
            }
            else
            {
                if (_targetMeshCollider != null)
                {
                    _targetMeshCollider.sharedMesh = null;
                    _targetMeshCollider.enabled = false;
                }

                DisableHiddenRootCollider(clearSharedMesh: false);
                DestroyConvexChildrenAndMeshes(false);
                GenerateCompoundColliders(FinalMesh);

                {
                    Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
                    if (hiddenRoot != null)
                    {
                        stat_ConvexPartCount = CountGeneratedConvexParts(hiddenRoot);
                    }
                    else
                    {
                        stat_ConvexPartCount = 0;
                    }
                }
            }
        }

        private void RefreshGeneratedColliderList()
        {
            if (generatedColliders == null) generatedColliders = new List<Collider>();
            generatedColliders.Clear();

            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot != null)
            {
                var rootMC = hiddenRoot.GetComponent<UnityEngine.MeshCollider>();
                if (rootMC != null) RegisterGeneratedCollider(rootMC);

                var rootBC = hiddenRoot.GetComponent<BoxCollider>();
                if (rootBC != null) RegisterGeneratedCollider(rootBC);

                for (int i = 0; i < hiddenRoot.childCount; i++)
                {
                    var child = hiddenRoot.GetChild(i);
                    if (child == null) continue;
                    if (!child.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                    var mc = child.GetComponent<UnityEngine.MeshCollider>();
                    if (mc != null) RegisterGeneratedCollider(mc);

                    var bc = child.GetComponent<BoxCollider>();
                    if (bc != null) RegisterGeneratedCollider(bc);
                }
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (!child.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                var mc = child.GetComponent<UnityEngine.MeshCollider>();
                if (mc != null) RegisterGeneratedCollider(mc);

                var bc = child.GetComponent<BoxCollider>();
                if (bc != null) RegisterGeneratedCollider(bc);
            }
        }


        private bool _isVisible = true;
        public Mesh FinalMesh { get; private set; }
        public bool IsProcessing { get; private set; } = false;

        private bool _needsUpdate = false;
        private CancellationTokenSource _optimizeCts;
        private UnityEngine.MeshCollider _targetMeshCollider;
        private MeshFilter _meshFilter;
        private SkinnedMeshRenderer _skinnedMeshRenderer;
        private float[] _lastBlendShapeWeights;
        private Mesh _bakedMesh;
        private Mesh _combinedMesh;
        private readonly List<Mesh> _tmpCombinedBakedMeshes = new List<Mesh>(8);

        private struct SourceMeshSnapshot
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public int[] Triangles;
            public Matrix4x4 Transform;
        }

        private NativeArray<float3> _nativeVerts;
        private NativeArray<float3> _nativeNormals;
        private NativeArray<float3> _inflatedResults;
        private bool _pendingEditorAutoUpdateRequest;
        private bool _isEditorAutoUpdateRun;
        private bool _limitConvexComplexityForCurrentRun;
        private int _convexCellsProcessedThisRun;
        private int _convexPartsGeneratedThisRun;
        private int _convexMaxCellsThisRun;
        private bool _convexBudgetReachedThisRun;

#if UNITY_EDITOR
        private const double EDITOR_AUTO_UPDATE_DEBOUNCE_SECONDS = 0.10d;
        private const long EDITOR_AUTO_UPDATE_MAX_TRI_EVALS = 12_000_000L;
        private const int EDITOR_AUTO_UPDATE_MAX_CONVEX_CELLS = 1800;
        private const int EDITOR_AUTO_UPDATE_MAX_PARTS = 350;
        private double _editorAutoUpdateNotBefore;
#endif

        private void OnEnable()
        {
            CancelActiveOptimization();
            DisposeOptimizeTokenSource();
            IsProcessing = false;
            _needsUpdate = false;
            _pendingEditorAutoUpdateRequest = false;
            _isEditorAutoUpdateRun = false;
            _limitConvexComplexityForCurrentRun = false;
            _convexCellsProcessedThisRun = 0;
            _convexPartsGeneratedThisRun = 0;
            _convexMaxCellsThisRun = 0;
            _convexBudgetReachedThisRun = false;
#if UNITY_EDITOR
            _editorAutoUpdateNotBefore = 0d;
#endif
            CleanupNativeArrays();
            ResetAnimationRuntimeState();
            Initialize();
        }

        private void OnDisable()
        {
            CancelActiveOptimization();
            DisposeOptimizeTokenSource();
            CleanupNativeArrays();
            ClearConvexHullGizmoCache();
            ResetAnimationRuntimeState();

            if (_bakedMesh != null)
            {
                DestroyObjSafe(_bakedMesh);
                _bakedMesh = null;
            }

            if (_combinedMesh != null)
            {
                DestroyObjSafe(_combinedMesh);
                _combinedMesh = null;
            }

            DestroyTemporaryBakedMeshes();
        }


        private static bool HasAnyNonDegenerateTriangle(Mesh m)
        {
            if (m == null) return false;

            var tris = m.triangles;
            var verts = m.vertices;

            if (tris == null || verts == null) return false;
            if (tris.Length < 3 || verts.Length < 3) return false;

            const float areaEps = 1e-12f;

            int tCount = tris.Length;
            for (int i = 0; i < tCount; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];

                if ((uint)a >= (uint)verts.Length || (uint)b >= (uint)verts.Length || (uint)c >= (uint)verts.Length)
                    continue;

                if (a == b || b == c || a == c)
                    continue;

                Vector3 va = verts[a];
                Vector3 vb = verts[b];
                Vector3 vc = verts[c];

                Vector3 ab = vb - va;
                Vector3 ac = vc - va;
                float area2 = Vector3.Cross(ab, ac).sqrMagnitude;

                if (area2 > areaEps)
                    return true;
            }

            return false;
        }


        private bool HasNonKinematicRigidbody(out Rigidbody rb)
        {
            rb = GetComponentInParent<Rigidbody>();
            return rb != null && !rb.isKinematic;
        }

        private void OnDestroy()
        {
            CancelActiveOptimization();
            DisposeOptimizeTokenSource();
            CleanupNativeArrays();
            DestroyTemporaryBakedMeshes();

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEditor.EditorApplication.isCompiling) return;

            if (Application.isPlaying) return;

            int goId = gameObject != null ? gameObject.GetInstanceID() : 0;
            bool addedRoot = _addedRootMeshCollider;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                CleanupAfterOptimizerRemoved(goId, addedRoot);
            };
#endif
        }

#if UNITY_EDITOR
        private static void CleanupAfterOptimizerRemoved(int goInstanceId, bool addedRootMeshCollider)
        {
            var go = UnityEditor.EditorUtility.InstanceIDToObject(goInstanceId) as GameObject;

            if (go == null) return;

            const string CONVEX_PART_PREFIX = "Convex_Part_";
            const string OPT_MESH_PREFIX = "Opt_Process_";
            const string CONVEX_MESH_PREFIX = "ConvexPart_";

            var meshesToDestroy = new HashSet<Mesh>();
            var gosToDestroy = new List<GameObject>();

            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i);
                if (child == null) continue;

                var cgo = child.gameObject;
                if (cgo == null) continue;
                if (!cgo.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                var mc = cgo.GetComponent<UnityEngine.MeshCollider>();
                if (mc != null && mc.sharedMesh != null) meshesToDestroy.Add(mc.sharedMesh);

                var mf = cgo.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) meshesToDestroy.Add(mf.sharedMesh);

                gosToDestroy.Add(cgo);
            }

            for (int i = 0; i < gosToDestroy.Count; i++)
                DestroyImmediateSafe(gosToDestroy[i]);

            foreach (var m in meshesToDestroy)
            {
                if (m == null) continue;
                if (IsAsset(m)) continue;
                DestroyImmediateSafe(m);
            }

            var rootMc = go.GetComponent<UnityEngine.MeshCollider>();
            if (rootMc != null)
            {
                Mesh sm = rootMc.sharedMesh;

                bool looksGeneratedMesh =
                    (sm != null && (sm.name.StartsWith(OPT_MESH_PREFIX) || sm.name.StartsWith(CONVEX_MESH_PREFIX)));

                if (addedRootMeshCollider || looksGeneratedMesh)
                {
                    Mesh m = rootMc.sharedMesh;

                    DestroyImmediateSafe(rootMc);

                    if (m != null && !IsAsset(m) && (m.name.StartsWith(OPT_MESH_PREFIX) || m.name.StartsWith(CONVEX_MESH_PREFIX)))
                        DestroyImmediateSafe(m);
                }
                else
                {
                    rootMc.hideFlags = HideFlags.None;
                }
            }
        }

        private static bool IsAsset(UnityEngine.Object obj)
        {
            return obj != null && UnityEditor.AssetDatabase.Contains(obj);
        }

        private static void DestroyImmediateSafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            UnityEngine.Object.DestroyImmediate(obj);
        }
#endif


        private void CleanupNativeArrays()
        {
            if (_nativeVerts.IsCreated) _nativeVerts.Dispose();
            if (_nativeNormals.IsCreated) _nativeNormals.Dispose();
            if (_inflatedResults.IsCreated) _inflatedResults.Dispose();
        }

        private void CancelActiveOptimization()
        {
            if (_optimizeCts == null) return;
            try
            {
                if (!_optimizeCts.IsCancellationRequested)
                    _optimizeCts.Cancel();
            }
            catch (System.ObjectDisposedException)
            {
            }
        }

        private void DisposeOptimizeTokenSource()
        {
            if (_optimizeCts == null) return;
            _optimizeCts.Dispose();
            _optimizeCts = null;
        }

        private void OnBecameVisible() { _isVisible = true; }
        private void OnBecameInvisible() { _isVisible = false; }

        private void Update()
        {
            if (Application.isPlaying)
            {
                UpdateLodColliderActivation();
            }

            if (Application.isPlaying && !_isVisible) return;

            if (Application.isPlaying && animationMode) return;

            if (trackBlendShapes && autoUpdate && !IsProcessing)
            {
                EnsureSourceReferences();
                if (_skinnedMeshRenderer != null)
                    CheckBlendShapeChanges();
            }

            if (_needsUpdate && !IsProcessing)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying &&
                    _pendingEditorAutoUpdateRequest &&
                    UnityEditor.EditorApplication.timeSinceStartup < _editorAutoUpdateNotBefore)
                {
                    // Keep pumping editor updates until debounce window ends.
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
#endif
                _needsUpdate = false;
                _isEditorAutoUpdateRun = _pendingEditorAutoUpdateRequest;
                _pendingEditorAutoUpdateRequest = false;
                _ = OptimizeAsync();
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying)
            {
                if (animationMode)
                {
                    // Force immediate refresh on next LateUpdate after runtime inspector edits.
                    m_AnimFrameCounter = Mathf.Max(1, animationEveryNFrames);
                    m_NextAnimUpdateTime = 0f;
                }
                return;
            }

            if (!autoUpdate) return;

            _needsUpdate = true;
            _pendingEditorAutoUpdateRequest = true;

#if UNITY_EDITOR
            _editorAutoUpdateNotBefore = UnityEditor.EditorApplication.timeSinceStartup + EDITOR_AUTO_UPDATE_DEBOUNCE_SECONDS;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }
        #endregion

        #region Initialization
        private void Initialize()
        {
            _targetMeshCollider = GetComponent<UnityEngine.MeshCollider>();
            if (_targetMeshCollider != null)
                _targetMeshCollider.hideFlags = GENERATED_MESH_COLLIDER_HIDE_FLAGS;

            EnsureSourceReferences();

            ApplyHideFlagsToExistingGeneratedObjects();
            MigrateLegacyHiddenRootColliderIfNeeded();
            RefreshBlendShapeWeightCache();
        }

        private void RefreshBlendShapeWeightCache()
        {
            if (_skinnedMeshRenderer == null || _skinnedMeshRenderer.sharedMesh == null)
            {
                _lastBlendShapeWeights = null;
                return;
            }

            int count = _skinnedMeshRenderer.sharedMesh.blendShapeCount;
            _lastBlendShapeWeights = new float[count];
            for (int i = 0; i < count; i++)
                _lastBlendShapeWeights[i] = _skinnedMeshRenderer.GetBlendShapeWeight(i);
        }
        private void ApplyHideFlagsToExistingGeneratedObjects()
        {
            var rootMC = GetComponent<UnityEngine.MeshCollider>();
            if (rootMC != null && rootMC.sharedMesh != null && rootMC.sharedMesh.name.StartsWith(OPT_MESH_PREFIX))
            {
                DestroyObjSafe(rootMC);
            }

            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot != null)
            {
                hiddenRoot.gameObject.hideFlags = PART_GO_HIDE_FLAGS;

                var mcRoot = hiddenRoot.GetComponent<UnityEngine.MeshCollider>();
                if (mcRoot != null) mcRoot.hideFlags = COLLIDER_HIDE_FLAGS;

                var bcRoot = hiddenRoot.GetComponent<BoxCollider>();
                if (bcRoot != null) bcRoot.hideFlags = COLLIDER_HIDE_FLAGS;

                for (int i = 0; i < hiddenRoot.childCount; i++)
                {
                    var child = hiddenRoot.GetChild(i);
                    if (child == null) continue;
                    if (!child.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                    child.gameObject.hideFlags = PART_GO_HIDE_FLAGS;

                    var mc = child.GetComponent<UnityEngine.MeshCollider>();
                    if (mc != null) mc.hideFlags = COLLIDER_HIDE_FLAGS;

                    var bc = child.GetComponent<BoxCollider>();
                    if (bc != null) bc.hideFlags = COLLIDER_HIDE_FLAGS;
                }
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;

                if (child.name.StartsWith(CONVEX_PART_PREFIX))
                {
                    child.gameObject.hideFlags = PART_GO_HIDE_FLAGS;

                    var mc = child.GetComponent<UnityEngine.MeshCollider>();
                    if (mc != null) mc.hideFlags = COLLIDER_HIDE_FLAGS;

                    var bc = child.GetComponent<BoxCollider>();
                    if (bc != null) bc.hideFlags = COLLIDER_HIDE_FLAGS;
                }
            }
        }

        private void DestroyConvexChildrenAndMeshes(bool destroyHiddenRoot = false)
        {
            var meshesToDestroy = new HashSet<Mesh>();
            var gosToDestroy = new List<GameObject>();

            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot != null)
            {
                for (int i = hiddenRoot.childCount - 1; i >= 0; i--)
                {
                    var child = hiddenRoot.GetChild(i);
                    if (child == null) continue;

                    var go = child.gameObject;
                    if (!go.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                    var mc = go.GetComponent<UnityEngine.MeshCollider>();
                    if (mc != null && mc.sharedMesh != null) meshesToDestroy.Add(mc.sharedMesh);

                    gosToDestroy.Add(go);
                }

                if (destroyHiddenRoot)
                {
                    var mcRoot = hiddenRoot.GetComponent<UnityEngine.MeshCollider>();
                    if (mcRoot != null && mcRoot.sharedMesh != null) meshesToDestroy.Add(mcRoot.sharedMesh);

                    gosToDestroy.Add(hiddenRoot.gameObject);
                }
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;

                var go = child.gameObject;
                if (!go.name.StartsWith(CONVEX_PART_PREFIX)) continue;

                var mc = go.GetComponent<UnityEngine.MeshCollider>();
                if (mc != null && mc.sharedMesh != null) meshesToDestroy.Add(mc.sharedMesh);

                gosToDestroy.Add(go);
            }

            for (int i = 0; i < gosToDestroy.Count; i++) DestroyObjSafe(gosToDestroy[i]);
            foreach (var m in meshesToDestroy) { if (m != null && !IsAsset(m)) DestroyObjSafe(m); }

            if (generatedColliders != null) generatedColliders.RemoveAll(c => c == null);
        }


        private static void DestroyObjSafe(Object obj)
        {
            if (obj == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(obj);
            else
                Destroy(obj);
#else
    UnityEngine.Object.Destroy(obj);
#endif
        }


        private void CheckBlendShapeChanges()
        {
            EnsureSourceReferences();
            if (_skinnedMeshRenderer == null || _skinnedMeshRenderer.sharedMesh == null)
            {
                _lastBlendShapeWeights = null;
                return;
            }

            if (_lastBlendShapeWeights == null || _skinnedMeshRenderer.sharedMesh.blendShapeCount != _lastBlendShapeWeights.Length)
            {
                RefreshBlendShapeWeightCache();
                return;
            }

            bool hasChanges = false;
            for (int i = 0; i < _lastBlendShapeWeights.Length; i++)
            {
                float w = _skinnedMeshRenderer.GetBlendShapeWeight(i);
                if (Mathf.Abs(w - _lastBlendShapeWeights[i]) > 0.001f) { _lastBlendShapeWeights[i] = w; hasChanges = true; }
            }
            if (hasChanges)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) RequestAutoOptimization();
                else RequestOptimization();
#else
                RequestOptimization();
#endif
            }
        }
        #endregion

        #region Optimization Pipeline
        public async Task OptimizeAsync()
        {
            if (IsProcessing) return;
            if (Application.isPlaying && animationMode) return;

            bool limitConvexComplexityThisRun = false;
#if UNITY_EDITOR
            limitConvexComplexityThisRun = !Application.isPlaying && _isEditorAutoUpdateRun;
#endif
            _isEditorAutoUpdateRun = false;
            _limitConvexComplexityForCurrentRun = limitConvexComplexityThisRun;

            if (_optimizeCts == null || _optimizeCts.IsCancellationRequested)
            {
                DisposeOptimizeTokenSource();
                _optimizeCts = new CancellationTokenSource();
            }

            CancellationTokenSource localCts = _optimizeCts;
            CancellationToken token = localCts.Token;

            IsProcessing = true;
            CleanupNativeArrays();

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            bool wasCanceled = false;

            try
            {
                token.ThrowIfCancellationRequested();

                var sourceSnapshots = new List<SourceMeshSnapshot>(8);
                CollectSourceMeshSnapshots(sourceSnapshots, capturePoseSnapshot || trackBlendShapes);
                if (sourceSnapshots.Count == 0) return;

                AccumulateSourceStats(sourceSnapshots, out stat_VertsBefore, out stat_TrisBefore);
                stat_MemoryBefore = CalculateMemory(stat_VertsBefore, stat_TrisBefore);

                global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer.MeshData resultData = null;

                token.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    resultData = BuildOptimizedMeshDataFromSources(sourceSnapshots, _limitConvexComplexityForCurrentRun, token);
                    token.ThrowIfCancellationRequested();
                }, token);
                token.ThrowIfCancellationRequested();

                if (resultData != null && resultData.Vertices != null && resultData.Vertices.Length >= 3 &&
                    resultData.Triangles != null && resultData.Triangles.Length >= 3)
                {
                    ClearProxyChildren();

                    FinalMesh = ApplyMeshDataToMesh(
                        FinalMesh,
                        resultData,
                        OPT_MESH_PREFIX + System.DateTime.Now.Ticks,
                        recalculateNormals: true);

                    ApplyMeshToColliders();

                    stat_VertsAfter = FinalMesh.vertexCount;
                    stat_TrisAfter = FinalMesh.triangles.Length;
                    stat_MemoryAfter = CalculateMemory(stat_VertsAfter, stat_TrisAfter);
                    savingsRatio = 1f - (float)stat_VertsAfter / (stat_VertsBefore == 0 ? 1 : stat_VertsBefore);
                }
            }
            catch (System.OperationCanceledException)
            {
                wasCanceled = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MCO_OPT_001] Mesh collider optimization failed on '{name}'.", this);
                Debug.LogException(ex, this);
            }
            finally
            {
                CleanupNativeArrays();
                _limitConvexComplexityForCurrentRun = false;
                sw.Stop();

                if (!wasCanceled && showHeatmap)
                    RecalculateHeatmap();

                stat_BakeTime = sw.ElapsedMilliseconds + " ms";
                IsProcessing = false;

                if (ReferenceEquals(_optimizeCts, localCts))
                    DisposeOptimizeTokenSource();
            }

#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        private global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer.MeshData ProcessMeshLogic_Optimized(Vector3[] sourceVerts, int[] sourceTris, float threshold)
        {
            if (threshold <= 0.0001f)
                return new global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer.MeshData { Vertices = sourceVerts, Triangles = sourceTris };

            int vCount = sourceVerts.Length;
            int tCount = sourceTris.Length;


            NativeArray<float3> inputVerts = new NativeArray<float3>(vCount, Allocator.TempJob);
            for (int i = 0; i < vCount; i++) inputVerts[i] = sourceVerts[i];

            NativeArray<int> inputTris = new NativeArray<int>(tCount, Allocator.TempJob);
            inputTris.CopyFrom(sourceTris);

            NativeList<float3> outputVerts = new NativeList<float3>(vCount, Allocator.TempJob);
            NativeList<int> outputTris = new NativeList<int>(tCount, Allocator.TempJob);

            var job = new SimplificationJob
            {
                InputVerts  = inputVerts,
                InputTris   = inputTris,
                CellSize    = Mathf.Max(threshold, 0.0001f),
                OutputVerts = outputVerts,
                OutputTris  = outputTris
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            Vector3[] finalVerts = new Vector3[outputVerts.Length];
            int[] finalTris = new int[outputTris.Length];

            for (int i = 0; i < outputVerts.Length; i++) finalVerts[i] = outputVerts[i];
            outputTris.AsArray().CopyTo(finalTris);

            inputVerts.Dispose();
            inputTris.Dispose();
            outputVerts.Dispose();
            outputTris.Dispose();

            return new global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer.MeshData { Vertices = finalVerts, Triangles = finalTris };
        }
        private bool HasLocalMeshSource()
        {
            var localMeshFilter = GetComponent<MeshFilter>();
            if (localMeshFilter != null && localMeshFilter.sharedMesh != null) return true;

            var localSkinned = GetComponent<SkinnedMeshRenderer>();
            return localSkinned != null && localSkinned.sharedMesh != null;
        }

        private bool ShouldUseCombinedMeshSource()
        {
            if (TryResolveExternalSource(out _, out _))
                return mergeChildObjects;

            if (mergeChildObjects) return true;

            // Root has no direct source mesh. Combine children so every child mesh is included
            // and transformed into optimizer-local space.
            return !HasLocalMeshSource();
        }

        private bool TryGetExternalSourceRoot(out Transform sourceRoot)
        {
            sourceRoot = null;

            if (externalSourceRoot != null)
            {
                sourceRoot = externalSourceRoot;
                return true;
            }

            if (externalTargetMeshFilter != null)
            {
                sourceRoot = externalTargetMeshFilter.transform;
                return sourceRoot != null;
            }

            if (externalTargetRenderer != null)
            {
                sourceRoot = externalTargetRenderer.transform;
                return sourceRoot != null;
            }

            return false;
        }

        private bool TryResolveExternalSource(out MeshFilter meshFilter, out SkinnedMeshRenderer skinnedRenderer)
        {
            meshFilter = null;
            skinnedRenderer = null;

            if (externalTargetRenderer == null && externalTargetMeshFilter == null) return false;

            if (externalTargetRenderer is SkinnedMeshRenderer externalSkinned)
            {
                if (externalSkinned.sharedMesh == null) return false;
                skinnedRenderer = externalSkinned;
                return true;
            }

            if (externalTargetMeshFilter != null)
            {
                if (externalTargetMeshFilter.sharedMesh == null) return false;
                meshFilter = externalTargetMeshFilter;
                return true;
            }

            if (externalTargetRenderer is MeshRenderer externalMeshRenderer)
            {
                MeshFilter externalMeshFilter = externalMeshRenderer.GetComponent<MeshFilter>();
                if (externalMeshFilter == null)
                    externalMeshFilter = externalMeshRenderer.GetComponentInChildren<MeshFilter>(true);
                if (externalMeshFilter == null || externalMeshFilter.sharedMesh == null) return false;
                meshFilter = externalMeshFilter;
                return true;
            }

            return false;
        }

        private void EnsureSourceReferences()
        {
            if (TryResolveExternalSource(out MeshFilter externalMeshFilter, out SkinnedMeshRenderer externalSkinnedRenderer))
            {
                _meshFilter = externalMeshFilter;
                _skinnedMeshRenderer = externalSkinnedRenderer;
                return;
            }

            _meshFilter = GetComponent<MeshFilter>();
            _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();

            if (_meshFilter == null && _skinnedMeshRenderer == null)
            {
                _skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (_skinnedMeshRenderer == null)
                    _meshFilter = GetComponentInChildren<MeshFilter>(true);
            }
        }

        private static bool IsGeneratedSourceMeshName(string meshName)
        {
            if (string.IsNullOrEmpty(meshName)) return false;
            return meshName.StartsWith(OPT_MESH_PREFIX) || meshName.StartsWith("ConvexPart_");
        }

        private bool ShouldSkipSourceObject(Transform sourceTransform, Mesh sourceMesh)
        {
            if (sourceTransform == null || sourceMesh == null) return true;

            string objectName = sourceTransform.name ?? string.Empty;
            if (objectName == HIDDEN_ROOT_NAME || objectName.StartsWith(CONVEX_PART_PREFIX))
                return true;

            return IsGeneratedSourceMeshName(sourceMesh.name);
        }

        private static void AddMeshSnapshot(List<SourceMeshSnapshot> snapshots, Mesh mesh, Matrix4x4 transformMatrix)
        {
            if (snapshots == null || mesh == null) return;

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            if (verts == null || verts.Length < 3 || tris == null || tris.Length < 3) return;

            snapshots.Add(new SourceMeshSnapshot
            {
                Vertices = verts,
                Normals = mesh.normals,
                Triangles = tris,
                Transform = transformMatrix
            });
        }

        private Matrix4x4 GetSnapshotTransformMatrix(Transform sourceTransform)
        {
            if (sourceTransform == null) return Matrix4x4.identity;
            return transform.worldToLocalMatrix * sourceTransform.localToWorldMatrix;
        }

        private static void BakeSkinnedMeshToLocalSpace(SkinnedMeshRenderer renderer, Mesh targetMesh)
        {
            if (renderer == null || targetMesh == null) return;
            renderer.BakeMesh(targetMesh);
        }

        private Mesh GetOrCreateTemporaryBakedMesh(int index)
        {
            while (_tmpCombinedBakedMeshes.Count <= index)
                _tmpCombinedBakedMeshes.Add(null);

            if (_tmpCombinedBakedMeshes[index] == null)
                _tmpCombinedBakedMeshes[index] = new Mesh();

            _tmpCombinedBakedMeshes[index].Clear();
            return _tmpCombinedBakedMeshes[index];
        }

        private void DestroyTemporaryBakedMeshes()
        {
            for (int i = 0; i < _tmpCombinedBakedMeshes.Count; i++)
            {
                if (_tmpCombinedBakedMeshes[i] != null)
                    DestroyObjSafe(_tmpCombinedBakedMeshes[i]);
            }

            _tmpCombinedBakedMeshes.Clear();
        }

        private void CollectSourceMeshSnapshots(List<SourceMeshSnapshot> snapshots, bool bakeSkinnedMeshes, bool forceCombineSources = false)
        {
            if (snapshots == null) return;

            snapshots.Clear();
            EnsureSourceReferences();
            bool hasExternalSourceRoot = TryGetExternalSourceRoot(out Transform externalSourceRoot);
            Transform sourceRoot = hasExternalSourceRoot ? externalSourceRoot : transform;

            bool useCombinedSources = forceCombineSources || ShouldUseCombinedMeshSource();
            int bakedMeshIndex = 0;

            if (!useCombinedSources)
            {
                if (_meshFilter != null && _meshFilter.sharedMesh != null)
                {
                    AddMeshSnapshot(snapshots, _meshFilter.sharedMesh, GetSnapshotTransformMatrix(_meshFilter.transform));
                    return;
                }

                if (_skinnedMeshRenderer != null && _skinnedMeshRenderer.sharedMesh != null)
                {
                    if (bakeSkinnedMeshes)
                    {
                        Mesh bakedMesh = GetOrCreateTemporaryBakedMesh(bakedMeshIndex++);
                        BakeSkinnedMeshToLocalSpace(_skinnedMeshRenderer, bakedMesh);
                        AddMeshSnapshot(snapshots, bakedMesh, GetSnapshotTransformMatrix(_skinnedMeshRenderer.transform));
                    }
                    else
                    {
                        AddMeshSnapshot(snapshots, _skinnedMeshRenderer.sharedMesh, GetSnapshotTransformMatrix(_skinnedMeshRenderer.transform));
                    }
                }

                return;
            }

            MeshFilter[] filters = sourceRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                if (ShouldSkipSourceObject(mf.transform, mf.sharedMesh)) continue;

                AddMeshSnapshot(snapshots, mf.sharedMesh, GetSnapshotTransformMatrix(mf.transform));
            }

            SkinnedMeshRenderer[] skinneds = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinneds.Length; i++)
            {
                SkinnedMeshRenderer smr = skinneds[i];
                if (smr == null || smr.sharedMesh == null) continue;
                if (ShouldSkipSourceObject(smr.transform, smr.sharedMesh)) continue;

                if (bakeSkinnedMeshes)
                {
                    Mesh bakedMesh = GetOrCreateTemporaryBakedMesh(bakedMeshIndex++);
                    BakeSkinnedMeshToLocalSpace(smr, bakedMesh);
                    AddMeshSnapshot(snapshots, bakedMesh, GetSnapshotTransformMatrix(smr.transform));
                }
                else
                {
                    AddMeshSnapshot(snapshots, smr.sharedMesh, GetSnapshotTransformMatrix(smr.transform));
                }
            }
        }

        private static void AccumulateSourceStats(List<SourceMeshSnapshot> snapshots, out int vertexCount, out int triangleIndexCount)
        {
            vertexCount = 0;
            triangleIndexCount = 0;

            if (snapshots == null) return;

            for (int i = 0; i < snapshots.Count; i++)
            {
                vertexCount += snapshots[i].Vertices != null ? snapshots[i].Vertices.Length : 0;
                triangleIndexCount += snapshots[i].Triangles != null ? snapshots[i].Triangles.Length : 0;
            }
        }

        private static float GetMaxDimension(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return 0f;

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            Vector3 size = max - min;
            return Mathf.Max(size.x, size.y, size.z);
        }

        private Vector3[] InflateVertices(Vector3[] rawVerts, Vector3[] rawNormals, CancellationToken token)
        {
            int vCount = rawVerts != null ? rawVerts.Length : 0;
            if (vCount == 0) return rawVerts;

            var nVerts = new NativeArray<float3>(vCount, Allocator.TempJob);
            var nNormals = new NativeArray<float3>(vCount, Allocator.TempJob);
            var nOut = new NativeArray<float3>(vCount, Allocator.TempJob);

            try
            {
                for (int i = 0; i < vCount; i++)
                {
                    nVerts[i] = rawVerts[i];
                    nNormals[i] = (rawNormals != null && rawNormals.Length == vCount) ? (float3)rawNormals[i] : float3.zero;
                }

                token.ThrowIfCancellationRequested();

                var job = new InflationJob
                {
                    Vertices = nVerts,
                    Normals = nNormals,
                    Results = nOut,
                    Amount = inflationAmount
                };

                JobHandle handle = job.Schedule(vCount, 64);
                handle.Complete();
                token.ThrowIfCancellationRequested();

                Vector3[] inflated = new Vector3[vCount];
                for (int i = 0; i < vCount; i++)
                    inflated[i] = nOut[i];

                return inflated;
            }
            finally
            {
                if (nVerts.IsCreated) nVerts.Dispose();
                if (nNormals.IsCreated) nNormals.Dispose();
                if (nOut.IsCreated) nOut.Dispose();
            }
        }

        private float GetSimplificationThreshold(Vector3[] sourceVerts, int triIndexCount, bool applyPreviewQualityCap)
        {
            float maxDim = GetMaxDimension(sourceVerts);
            float effectiveQuality = quality;

#if UNITY_EDITOR
            if (applyPreviewQualityCap && convex && triIndexCount > 0)
            {
                float previewQualityCap = GetAutoPreviewQualityCap(triIndexCount / 3);
                if (effectiveQuality > previewQualityCap)
                    effectiveQuality = previewQualityCap;
            }
#endif

            return (1f - effectiveQuality) * (maxDim * 0.2f * shapeFidelity);
        }

        private MeshData BuildOptimizedMeshDataFromSources(List<SourceMeshSnapshot> snapshots, bool applyPreviewQualityCap, CancellationToken token)
        {
            if (snapshots == null || snapshots.Count == 0) return null;

            int estimatedVertCapacity = 0;
            int estimatedTriCapacity = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                estimatedVertCapacity += snapshots[i].Vertices != null ? snapshots[i].Vertices.Length : 0;
                estimatedTriCapacity += snapshots[i].Triangles != null ? snapshots[i].Triangles.Length : 0;
            }

            var combinedVerts = new List<Vector3>(Mathf.Max(estimatedVertCapacity, 3));
            var combinedTris = new List<int>(Mathf.Max(estimatedTriCapacity, 3));

            for (int i = 0; i < snapshots.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var snapshot = snapshots[i];
                Vector3[] rawVerts = snapshot.Vertices;
                int[] rawTris = snapshot.Triangles;
                if (rawVerts == null || rawVerts.Length < 3 || rawTris == null || rawTris.Length < 3)
                    continue;

                Vector3[] processedVerts = rawVerts;
                if (Mathf.Abs(inflationAmount) > 0.00001f)
                    processedVerts = InflateVertices(rawVerts, snapshot.Normals, token);

                float threshold = GetSimplificationThreshold(processedVerts, rawTris.Length, applyPreviewQualityCap);
                MeshData resultData = ProcessMeshLogic_Optimized(processedVerts, rawTris, threshold);

                Vector3[] resultVerts = resultData?.Vertices;
                int[] resultTris = resultData?.Triangles;
                if (resultVerts == null || resultVerts.Length < 3 || resultTris == null || resultTris.Length < 3)
                {
                    resultVerts = processedVerts;
                    resultTris = rawTris;
                }

                int baseVertex = combinedVerts.Count;
                for (int v = 0; v < resultVerts.Length; v++)
                    combinedVerts.Add(snapshot.Transform.MultiplyPoint3x4(resultVerts[v]));

                for (int t = 0; t < resultTris.Length; t++)
                    combinedTris.Add(baseVertex + resultTris[t]);
            }

            if (combinedVerts.Count < 3 || combinedTris.Count < 3)
                return null;

            return new MeshData
            {
                Vertices = combinedVerts.ToArray(),
                Triangles = combinedTris.ToArray()
            };
        }

        private static Mesh ApplyMeshDataToMesh(Mesh targetMesh, MeshData meshData, string meshName, bool recalculateNormals)
        {
            if (meshData == null || meshData.Vertices == null || meshData.Vertices.Length < 3 ||
                meshData.Triangles == null || meshData.Triangles.Length < 3)
            {
                return null;
            }

            if (targetMesh == null)
                targetMesh = new Mesh();

            targetMesh.Clear();
            targetMesh.name = meshName;
            targetMesh.indexFormat = meshData.Vertices.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            targetMesh.vertices = meshData.Vertices;
            targetMesh.triangles = meshData.Triangles;

#if UNITY_EDITOR
            if (recalculateNormals)
                targetMesh.RecalculateNormals();
#endif

            targetMesh.RecalculateBounds();
            return targetMesh;
        }

        private Mesh GetMeshToProcess()
        {
            EnsureSourceReferences();
            if (ShouldUseCombinedMeshSource()) return CreateCombinedMesh();

            if (_meshFilter != null)
            {
                bool hasExternalSourceOverride = externalTargetRenderer != null || externalTargetMeshFilter != null;
                if (!hasExternalSourceOverride && _meshFilter.transform != transform) return CreateCombinedMesh();
                return _meshFilter.sharedMesh;
            }

            if (_skinnedMeshRenderer != null)
            {
                bool isExternalSource = externalTargetRenderer != null && _skinnedMeshRenderer == externalTargetRenderer;
                if (!isExternalSource && _skinnedMeshRenderer.transform != transform) return CreateCombinedMesh();

                if (capturePoseSnapshot || trackBlendShapes)
                {
                    if (_bakedMesh == null) _bakedMesh = new Mesh();
                    _bakedMesh.Clear();
                    BakeSkinnedMeshToLocalSpace(_skinnedMeshRenderer, _bakedMesh);
                    return _bakedMesh;
                }
                return _skinnedMeshRenderer.sharedMesh;
            }

            return null;
        }

        private Mesh CreateCombinedMesh()
        {
            if (_combinedMesh == null) _combinedMesh = new Mesh();
            _combinedMesh.Clear();

            bool hasExternalSourceRoot = TryGetExternalSourceRoot(out Transform externalSourceRoot);
            Transform sourceRoot = hasExternalSourceRoot ? externalSourceRoot : transform;

            MeshFilter[] filters = sourceRoot.GetComponentsInChildren<MeshFilter>();
            SkinnedMeshRenderer[] skinneds = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>();

            var combine = new List<CombineInstance>(filters.Length + skinneds.Length);
            int bakedMeshIndex = 0;

            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                string mn = mf.sharedMesh.name ?? string.Empty;
                if (mn.StartsWith(OPT_MESH_PREFIX) || mn.StartsWith("ConvexPart_")) continue;

                CombineInstance ci = new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = GetSnapshotTransformMatrix(mf.transform)
                };
                combine.Add(ci);
            }

            foreach (SkinnedMeshRenderer smr in skinneds)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                Mesh tempBaked = GetOrCreateTemporaryBakedMesh(bakedMeshIndex++);
                BakeSkinnedMeshToLocalSpace(smr, tempBaked);

                CombineInstance ci = new CombineInstance
                {
                    mesh = tempBaked,
                    transform = GetSnapshotTransformMatrix(smr.transform)
                };
                combine.Add(ci);
            }

            if (combine.Count == 0)
            {
                return null;
            }

            _combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _combinedMesh.CombineMeshes(combine.ToArray(), true, true);
            _combinedMesh.RecalculateBounds();

            return _combinedMesh;
        }

        private void ClearProxyChildren()
        {
            DestroyConvexChildrenAndMeshes();
        }
        #endregion

        #region Convex Decomposition
        private void GenerateCompoundColliders(Mesh source)
        {
            if (source == null) return;

            Vector3[] verts = source.vertices;
            int[] tris = source.triangles;
            int grid = Mathf.Max(2, decompositionGrid);
            int triCount = tris != null ? (tris.Length / 3) : 0;

#if UNITY_EDITOR
            if (_limitConvexComplexityForCurrentRun)
            {
                if (triCount > 0)
                {
                    long estimatedTriEvaluations = (long)triCount * grid * grid * grid;
                    if (estimatedTriEvaluations > EDITOR_AUTO_UPDATE_MAX_TRI_EVALS)
                    {
                        float targetCells = (float)EDITOR_AUTO_UPDATE_MAX_TRI_EVALS / triCount;
                        int safeGrid = Mathf.Clamp(
                            Mathf.FloorToInt(Mathf.Pow(Mathf.Max(8f, targetCells), 1f / 3f)),
                            2,
                            grid);

                        if (safeGrid < grid)
                        {
                            Debug.LogWarning(
                                $"[MCO_OPT_003] Auto Update preview grid reduced from {grid} to {safeGrid} on '{name}' to keep the editor responsive. Use Rebuild for full quality.",
                                this);
                            grid = safeGrid;
                        }
                    }
                }
            }
#endif

            _convexCellsProcessedThisRun = 0;
            _convexPartsGeneratedThisRun = 0;
            _convexBudgetReachedThisRun = false;
            _convexMaxCellsThisRun = int.MaxValue;

#if UNITY_EDITOR
            if (_limitConvexComplexityForCurrentRun && triCount > 0)
            {
                int budgetFromTriEvals = Mathf.Clamp(
                    (int)(EDITOR_AUTO_UPDATE_MAX_TRI_EVALS / Mathf.Max(1, triCount)),
                    24,
                    EDITOR_AUTO_UPDATE_MAX_CONVEX_CELLS);

                int budgetFromGrid = Mathf.Clamp(grid * grid * grid * 4, 24, EDITOR_AUTO_UPDATE_MAX_CONVEX_CELLS);
                _convexMaxCellsThisRun = Mathf.Min(budgetFromTriEvals, budgetFromGrid);
            }
#endif

            Bounds b = source.bounds;
            float stepX = b.size.x / grid;
            float stepY = b.size.y / grid;
            float stepZ = b.size.z / grid;

            for (int x = 0; x < grid; x++)
            {
                if (_convexBudgetReachedThisRun) break;

                for (int y = 0; y < grid; y++)
                {
                    if (_convexBudgetReachedThisRun) break;

                    for (int z = 0; z < grid; z++)
                    {
                        if (_convexBudgetReachedThisRun) break;

                        Vector3 min = b.min + new Vector3(x * stepX, y * stepY, z * stepZ);
                        Vector3 max = min + new Vector3(stepX, stepY, stepZ);

                        CreatePartInBounds(source, verts, tris,
                            min - Vector3.one * 0.01f,
                            max + Vector3.one * 0.01f,
                            $"{x}_{y}_{z}",
                            0);
                    }
                }
            }

#if UNITY_EDITOR
            if (_limitConvexComplexityForCurrentRun && _convexBudgetReachedThisRun)
            {
                Debug.LogWarning(
                    $"[MCO_OPT_004] Auto Update convex generation hit safety budget on '{name}' ({_convexPartsGeneratedThisRun} parts, {_convexCellsProcessedThisRun}/{_convexMaxCellsThisRun} cells). Use Rebuild for full-quality decomposition.",
                    this);
            }
#endif
        }

        private const int CONVEX_SOFT_VERTEX_LIMIT = 220;
        private const int CONVEX_SOFT_TRI_LIMIT = 1200;
        private const int CONVEX_SPLIT_VERTS_THRESHOLD = 600;
        private const int CONVEX_SPLIT_TRIS_THRESHOLD = 2000;
        private const int CONVEX_MAX_SPLIT_DEPTH = 2;
        private const int CONVEX_SIMPLIFY_ITERS = 8;

        private void SubdivideBounds(Mesh source, Vector3[] sourceVerts, int[] sourceTris, Vector3 min, Vector3 max, string suffix, int depth)
        {
            if (_convexBudgetReachedThisRun) return;
            if (depth >= CONVEX_MAX_SPLIT_DEPTH) return;

            Vector3 mid = (min + max) * 0.5f;

            for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
            for (int zi = 0; zi < 2; zi++)
            {
                if (_convexBudgetReachedThisRun) return;

                Vector3 subMin = new Vector3(xi == 0 ? min.x : mid.x, yi == 0 ? min.y : mid.y, zi == 0 ? min.z : mid.z);
                Vector3 subMax = new Vector3(xi == 0 ? mid.x : max.x, yi == 0 ? mid.y : max.y, zi == 0 ? mid.z : max.z);

                CreatePartInBounds(source, sourceVerts, sourceTris, subMin, subMax, $"{suffix}_S{xi}{yi}{zi}", depth + 1);
            }
        }


        private void CreatePartInBounds(Mesh source, Vector3[] verts, int[] tris, Vector3 min, Vector3 max, string suffix, int depth)
        {
            if (_convexBudgetReachedThisRun) return;

#if UNITY_EDITOR
            if (_limitConvexComplexityForCurrentRun)
            {
                _convexCellsProcessedThisRun++;
                int maxCells = Mathf.Max(24, _convexMaxCellsThisRun);
                if (_convexCellsProcessedThisRun > maxCells)
                {
                    _convexBudgetReachedThisRun = true;
                    return;
                }
            }
#endif

            var pVerts = new List<Vector3>();
            var pTris = new List<int>();
            var vMap = new Dictionary<int, int>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v1 = verts[tris[i]];
                Vector3 v2 = verts[tris[i + 1]];
                Vector3 v3 = verts[tris[i + 2]];
                Vector3 center = (v1 + v2 + v3) / 3f;

                if (center.x >= min.x && center.x <= max.x &&
                    center.y >= min.y && center.y <= max.y &&
                    center.z >= min.z && center.z <= max.z)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int oldIdx = tris[i + k];
                        if (!vMap.TryGetValue(oldIdx, out int newIdx))
                        {
                            newIdx = pVerts.Count;
                            vMap.Add(oldIdx, newIdx);
                            pVerts.Add(verts[oldIdx]);
                        }
                        pTris.Add(newIdx);
                    }
                }
            }

            if (pTris.Count < 3 || pVerts.Count < 3) return;

            Vector3 bMin = pVerts[0], bMax = pVerts[0];
            for (int i = 1; i < pVerts.Count; i++)
            {
                bMin = Vector3.Min(bMin, pVerts[i]);
                bMax = Vector3.Max(bMax, pVerts[i]);
            }
            Vector3 size = bMax - bMin;
            if (size.sqrMagnitude < 0.0001f) return;

            float maxDim = Mathf.Max(size.x, size.y, size.z);
            float flatEps = Mathf.Max(0.0005f, maxDim * 0.01f);
            bool isFlat = (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) <= flatEps);

            Vector3[] partVerts = pVerts.ToArray();
            int[] partTris = pTris.ToArray();
            int triCount = partTris.Length / 3;

            int vLimit = strict256Guard ? CONVEX_STRICT_VERTEX_LIMIT : CONVEX_SOFT_VERTEX_LIMIT;
            int tLimit = strict256Guard ? CONVEX_STRICT_TRI_LIMIT : CONVEX_SOFT_TRI_LIMIT;
            int iters = strict256Guard ? (CONVEX_SIMPLIFY_ITERS + 8) : CONVEX_SIMPLIFY_ITERS;

            if (depth < CONVEX_MAX_SPLIT_DEPTH &&
                (pVerts.Count > CONVEX_SPLIT_VERTS_THRESHOLD || (pTris.Count/3) > CONVEX_SPLIT_TRIS_THRESHOLD))
            {
                SubdivideBounds(source, verts, tris, min, max, suffix, depth);
                return;
            }

            if (partVerts.Length > vLimit || triCount > tLimit)
            {
                float thr = Mathf.Max(0.001f, maxDim * 0.002f);
                for (int iter = 0; iter < iters; iter++)
                {
                    if (partVerts.Length <= vLimit && triCount <= tLimit) break;

                    global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer.MeshData md = ProcessMeshLogic_Optimized(partVerts, partTris, thr);
                    partVerts = md.Vertices;
                    partTris = md.Triangles;
                    triCount = partTris.Length / 3;

                    if (partVerts == null || partVerts.Length < 4 || triCount < 2) break;

                    thr *= 1.5f;
                }
            }

            if (partVerts.Length > vLimit || triCount > tLimit)
            {
                if (depth < CONVEX_MAX_SPLIT_DEPTH)
                {
                    SubdivideBounds(source, verts, tris, min, max, suffix, depth);
                    return;
                }
                return;
            }

            bool lowVerts = partVerts.Length < 6;
            if (isFlat || lowVerts) return;

#if UNITY_EDITOR
            if (_limitConvexComplexityForCurrentRun && _convexPartsGeneratedThisRun >= EDITOR_AUTO_UPDATE_MAX_PARTS)
            {
                _convexBudgetReachedThisRun = true;
                return;
            }
#endif

            GameObject go = new GameObject("Convex_Part_" + suffix);
            go.layer = gameObject.layer;

            go.hideFlags = PART_GO_HIDE_FLAGS;

            go.transform.SetParent(GetOrCreateHiddenRootTransform(), false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Mesh m = new Mesh();
            m.name = "ConvexPart_" + suffix;
            if (partVerts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // Use the simplified/split-safe part mesh to avoid expensive convex cooking on dense raw cells.
            m.vertices = partVerts;
            m.triangles = partTris;
            m.RecalculateNormals();
            m.RecalculateBounds();

            UnityEngine.MeshCollider mc = go.AddComponent<UnityEngine.MeshCollider>();

            mc.hideFlags = COLLIDER_HIDE_FLAGS;

            mc.convex = true;
            mc.isTrigger = isTrigger;
            mc.sharedMaterial = material;
            mc.sharedMesh = m;

            RegisterGeneratedCollider(mc);

            if (_limitConvexComplexityForCurrentRun)
                _convexPartsGeneratedThisRun++;
        }
        private string CalculateMemory(int verts, int tris)
        {
            long bytes = (verts * 12) + (tris * 2);
            if (bytes < 1024) return bytes + " B";
            return (bytes / 1024f).ToString("F1") + " KB";
        }

        private static float GetAutoPreviewQualityCap(int triCount)
        {
            if (triCount >= 200000) return 0.55f;
            if (triCount >= 100000) return 0.65f;
            if (triCount >= 50000) return 0.75f;
            if (triCount >= 25000) return 0.82f;
            return 0.9f;
        }

        public void RecalculateHeatmap()
        {
            _heatmapCache.Clear();
            if (!showHeatmap || FinalMesh == null) return;

            EnsureSourceReferences();

            Mesh srcMesh = null;
            if (ShouldUseCombinedMeshSource())
            {
                srcMesh = CreateCombinedMesh();
            }
            else if (_meshFilter)
            {
                srcMesh = _meshFilter.sharedMesh;
            }
            else if (_skinnedMeshRenderer)
            {
                srcMesh = _skinnedMeshRenderer.sharedMesh;
            }
            if (srcMesh == null) return;

            Vector3[] verts = srcMesh.vertices;
            Vector3[] optimizedVerts = FinalMesh.vertices;

            var activeColliders = new List<Collider>();
            foreach (var col in generatedColliders)
            {
                if (col != null && col.enabled) activeColliders.Add(col);
            }
            if (activeColliders.Count == 0)
            {
                Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
                if (hiddenRoot != null)
                {
                    Collider rootCol = hiddenRoot.GetComponent<Collider>();
                    if (rootCol != null && rootCol.enabled) activeColliders.Add(rootCol);

                    for (int i = 0; i < hiddenRoot.childCount; i++)
                    {
                        var child = hiddenRoot.GetChild(i);
                        if (child == null) continue;

                        Collider childCol = child.GetComponent<Collider>();
                        if (childCol != null && childCol.enabled) activeColliders.Add(childCol);
                    }
                }

                foreach (Transform child in transform)
                {
                    Collider childCol = child.GetComponent<Collider>();
                    if (childCol != null && childCol.enabled) activeColliders.Add(childCol);
                }
            }
            Collider selfCollider = GetComponent<Collider>();
            if (selfCollider != null && selfCollider.enabled && !activeColliders.Contains(selfCollider))
                activeColliders.Add(selfCollider);

            bool hasAnyCollider = activeColliders.Count > 0;

            for (int i = 0; i < verts.Length; i += heatmapStride)
            {
                Vector3 worldPt = transform.TransformPoint(verts[i]);
                Vector3 closestWorld = worldPt;

                if (hasAnyCollider)
                {
                    float minSqrDist = float.MaxValue;
                    bool foundClosest = false;

                    foreach (var col in activeColliders)
                    {
                        bool supportsClosestPoint = (col is BoxCollider ||
                                                     col is SphereCollider ||
                                                     col is CapsuleCollider ||
                                                     (col is UnityEngine.MeshCollider mcCheck && mcCheck.convex));

                        if (supportsClosestPoint)
                        {
                            Vector3 cp = col.ClosestPoint(worldPt);
                            float sqr = (cp - worldPt).sqrMagnitude;
                            if (sqr < minSqrDist) { minSqrDist = sqr; closestWorld = cp; foundClosest = true; }
                        }
                    }

                    if (!foundClosest && optimizedVerts != null && optimizedVerts.Length > 0)
                    {
                        Vector3 localSearchPt = verts[i];
                        Vector3 bestLocalVert = localSearchPt;
                        float bestSqr = float.MaxValue;

                        for (int k = 0; k < optimizedVerts.Length; k++)
                        {
                            float sqr = (optimizedVerts[k] - localSearchPt).sqrMagnitude;
                            if (sqr < bestSqr) { bestSqr = sqr; bestLocalVert = optimizedVerts[k]; }
                        }
                        closestWorld = transform.TransformPoint(bestLocalVert);
                    }
                }

                float dist = Vector3.Distance(worldPt, closestWorld);
                float t = Mathf.Clamp01(dist / maxErrorTolerance);
                HeatPoint pStruct = new HeatPoint { localPos = verts[i], localClosest = transform.InverseTransformPoint(closestWorld), color = Color.Lerp(Color.green, Color.red, t), hasError = dist > 0.001f };
                _heatmapCache.Add(pStruct);
            }
        }
        #endregion

        #region Visualization Helpers
        private List<HeatPoint> _heatmapCache = new List<HeatPoint>();
        private struct HeatPoint { public Vector3 localPos; public Vector3 localClosest; public Color color; public bool hasError; }

        private Dictionary<int, Mesh> _convexHullGizmoCache = new Dictionary<int, Mesh>();

        /// <summary>
        /// Clears the convex-hull gizmo cache. Call after generating new colliders/meshes.
        /// </summary>
        public void ClearConvexHullGizmoCache()
        {
            if (_convexHullGizmoCache != null)
            {
                foreach (var kvp in _convexHullGizmoCache)
                {
                    if (kvp.Value != null) DestroyObjSafe(kvp.Value);
                }
                _convexHullGizmoCache.Clear();
            }
        }

        /// <summary>
        /// Returns a visualization mesh of the convex hull for a convex MeshCollider.
        /// Computed once and cached per collider instance.
        /// </summary>
        private Mesh GetOrBuildConvexHullGizmoMesh(UnityEngine.MeshCollider mc)
        {
            if (mc == null || mc.sharedMesh == null) return null;

            int id = mc.GetInstanceID();
            if (_convexHullGizmoCache.TryGetValue(id, out Mesh cached) && cached != null)
                return cached;

            Mesh hullMesh = BuildConvexHullMesh(mc.sharedMesh.vertices);
            if (hullMesh != null)
            {
                hullMesh.name = "GizmoHull_" + mc.sharedMesh.name;
                _convexHullGizmoCache[id] = hullMesh;
            }
            return hullMesh;
        }

        /// <summary>
        /// Builds an approximate convex-hull visualization mesh from an input point set.
        /// Intended as a lightweight visualization similar to the PhysX convex hull result.
        /// </summary>
        private static Mesh BuildConvexHullMesh(Vector3[] points)
        {
            if (points == null || points.Length < 4) return null;

            var unique = new List<Vector3>(points.Length);
            var seen = new HashSet<Vector3Int>();
            const float quantize = 10000f;
            foreach (var p in points)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(p.x * quantize),
                    Mathf.RoundToInt(p.y * quantize),
                    Mathf.RoundToInt(p.z * quantize));
                if (seen.Add(key)) unique.Add(p);
            }

            if (unique.Count < 4) return null;

            int n = unique.Count;
            Vector3[] pts = unique.ToArray();

            int i0 = 0, i1 = 1;
            float maxDistSq = 0f;
            int searchLimit = Mathf.Min(n, 200);
            for (int i = 0; i < searchLimit; i++)
            {
                for (int j = i + 1; j < searchLimit; j++)
                {
                    float d = (pts[i] - pts[j]).sqrMagnitude;
                    if (d > maxDistSq) { maxDistSq = d; i0 = i; i1 = j; }
                }
            }

            int i2 = -1;
            float maxLineDist = 0f;
            Vector3 lineDir = (pts[i1] - pts[i0]).normalized;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                Vector3 diff = pts[i] - pts[i0];
                float proj = Vector3.Dot(diff, lineDir);
                float dist = (diff - lineDir * proj).sqrMagnitude;
                if (dist > maxLineDist) { maxLineDist = dist; i2 = i; }
            }
            if (i2 < 0) return null;

            Vector3 triNormal = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]).normalized;
            int i3 = -1;
            float maxPlaneDist = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1 || i == i2) continue;
                float dist = Mathf.Abs(Vector3.Dot(pts[i] - pts[i0], triNormal));
                if (dist > maxPlaneDist) { maxPlaneDist = dist; i3 = i; }
            }
            if (i3 < 0 || maxPlaneDist < 1e-6f) return null;

            if (Vector3.Dot(pts[i3] - pts[i0], triNormal) > 0f)
            {
                int tmp = i1; i1 = i2; i2 = tmp;
            }

            var faces = new List<int[]>();
            faces.Add(new[] { i0, i1, i2 });
            faces.Add(new[] { i0, i2, i3 });
            faces.Add(new[] { i0, i3, i1 });
            faces.Add(new[] { i1, i3, i2 });

            var usedInHull = new HashSet<int> { i0, i1, i2, i3 };

            for (int pi = 0; pi < n; pi++)
            {
                if (usedInHull.Contains(pi)) continue;

                Vector3 pt = pts[pi];

                var visible = new List<int>();
                for (int fi = 0; fi < faces.Count; fi++)
                {
                    var f = faces[fi];
                    Vector3 fNorm = Vector3.Cross(pts[f[1]] - pts[f[0]], pts[f[2]] - pts[f[0]]);
                    if (Vector3.Dot(pt - pts[f[0]], fNorm) > 1e-6f)
                    {
                        visible.Add(fi);
                    }
                }

                if (visible.Count == 0) continue;

                var horizon = new List<int[]>();
                var visibleSet = new HashSet<int>(visible);

                foreach (int fi in visible)
                {
                    var f = faces[fi];
                    for (int e = 0; e < 3; e++)
                    {
                        int ea = f[e], eb = f[(e + 1) % 3];
                        bool edgeShared = false;
                        for (int fj = 0; fj < faces.Count; fj++)
                        {
                            if (fj == fi || visibleSet.Contains(fj)) continue;
                            var of = faces[fj];
                            for (int oe = 0; oe < 3; oe++)
                            {
                                if (of[oe] == eb && of[(oe + 1) % 3] == ea)
                                {
                                    edgeShared = true;
                                    break;
                                }
                            }
                            if (edgeShared) break;
                        }
                        if (edgeShared) horizon.Add(new[] { ea, eb });
                    }
                }

                visible.Sort();
                for (int ri = visible.Count - 1; ri >= 0; ri--)
                    faces.RemoveAt(visible[ri]);

                foreach (var edge in horizon)
                {
                    faces.Add(new[] { edge[0], edge[1], pi });
                }

                usedInHull.Add(pi);
            }

            var meshVerts = new List<Vector3>();
            var meshTris = new List<int>();
            var vertMap = new Dictionary<int, int>();

            foreach (var f in faces)
            {
                Vector3 fn = Vector3.Cross(pts[f[1]] - pts[f[0]], pts[f[2]] - pts[f[0]]);
                if (fn.sqrMagnitude < 1e-12f) continue;

                for (int vi = 0; vi < 3; vi++)
                {
                    int origIdx = f[vi];
                    if (!vertMap.TryGetValue(origIdx, out int newIdx))
                    {
                        newIdx = meshVerts.Count;
                        vertMap[origIdx] = newIdx;
                        meshVerts.Add(pts[origIdx]);
                    }
                    meshTris.Add(newIdx);
                }
            }

            if (meshVerts.Count < 4 || meshTris.Count < 12) return null;

            var mesh = new Mesh();
            mesh.vertices = meshVerts.ToArray();
            mesh.triangles = meshTris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        #endregion
    }
}

