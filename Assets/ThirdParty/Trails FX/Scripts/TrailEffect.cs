using System;
using System.Collections.Generic;
using UnityEngine;


namespace TrailsFX {

    public enum TrailStyle {
        Color,
        TextureStamp,
        Clone,
        Outline,
        SpaceDistortion,
        Dash,
        Custom
    }

    public enum ColorSequence {
        Fixed,
        Cycle,
        PingPong,
        Random,
        FixedRandom
    }

    public enum PositionChangeRelative {
        World,
        OtherGameObject
    }

    public enum TrailRenderOrder {
        BeforeObject = 0,
        DrawBehind = 1,
        AlwaysOnTop = 2
    }

    public enum OutlineMethod {
        Normals,
        Rim
    }

    public static class TrailStyleProperties {

        public static bool supportsColor (this TrailStyle s) {
            return s != TrailStyle.SpaceDistortion;
        }
    }

    [System.Serializable]
    public class IncludedTarget {
        public Transform target;
        
        [Tooltip("Override effect settings for this specific target")]
        public bool overrideSettings;
        
        [Tooltip("Luminance cutoff for Clone effect")]
        [Range(0, 1)]
        public float luminanceCutOff = 0.25f;
        
        [Tooltip("Outline method (Normals or Rim)")]
        public OutlineMethod outlineMethod = OutlineMethod.Normals;
        
        [Tooltip("Normal threshold for outline detection")]
        [Range(0, 1)]
        public float normalThreshold = 0.3f;
        
        [Tooltip("Rim power for rim-based outline")]
        public float rimPower = 2f;
    }

    [ExecuteInEditMode]
    [HelpURL("https://kronnect.com/docs/trails-fx/")]
    [DefaultExecutionOrder(100)]
    public partial class TrailEffect : MonoBehaviour {

        #region Public Properties

        public TrailEffectProfile profile;
        [Tooltip("If enabled, settings will be synced with profile.")]
        public bool profileSync;
        public Transform target;
        [Tooltip("Additional targets to include in the trail effect. Their trails will be rendered together with the main target.")]
        public IncludedTarget[] include;
        [Tooltip("When enabled, meshes from included children are combined with the main mesh and rendered as a single trail.")]
        public bool combineMeshesWithChildren;

        [SerializeField]
        bool _active = true;
        public bool active { get { return _active; } set { _active = value; if (!_active) wasInactive = true; } }
        [Tooltip("By default, trails are not generated if the renderer is not visibile. This option ignores renderer visibility.")]
        public bool ignoreVisibility;
        public bool executeInEditMode;
        public int ignoreFrames;
        [Tooltip("The duration of this trail.")]
        public float duration = 0.5f;
        public bool continuous;
        [Tooltip("Use max steps to create a smooth trail if trigger condition is satisfied.")]
        public bool smooth;
        public bool checkWorldPosition;
        public float minDistance = 0.1f;
        public PositionChangeRelative worldPositionRelativeOption = PositionChangeRelative.World;
        public Transform worldPositionRelativeTransform;
        public bool checkScreenPosition = true;
        public int minPixelDistance = 10;
        public int stepsBufferSize = 1023;
        public int maxStepsPerFrame = 12;
        public bool checkTime;
        public float timeInterval = 1f;
        public bool checkCollisions;
        public bool orientToSurface = true;
        public bool ground;
        public float surfaceOffset = 0.05f;
        public LayerMask collisionLayerMask = -1;
        [Tooltip("Optional mask texture to be applied to the effect. Uses the red channel as an alpha (transparency) multiplier.")]
        public Texture2D mask;
        public TrailRenderOrder renderOrder = TrailRenderOrder.DrawBehind;
        [Tooltip("Adds additional render passes to reset stencil after trail have rendered. Use this option to improve the appearance of trails on different objects when they overlap.")]
        public bool clearStencil;
        [Tooltip("Use every renderer under a hierarchy to occlude this trail with the stencil buffer.")]
        public bool hierarchyOccluder;
        [Tooltip("Root transform whose child renderers write to the stencil. When empty the current target is used.")]
        public Transform hierarchyOccluderRoot;
        [Tooltip("Layers included when collecting hierarchy occluders.")]
        public LayerMask hierarchyOccluderLayerMask = ~0;
        [Tooltip("Include inactive children when collecting hierarchy occluders.")]
        public bool hierarchyIncludeInactive = true;
        public UnityEngine.Rendering.CullMode cullMode = UnityEngine.Rendering.CullMode.Back;
        public int subMeshMask = -1;
        [GradientUsage(hdr: true)]
        public Gradient colorOverTime;
        public bool colorRamp;
        public Texture2D colorRampTexture;
        public Transform colorRampStart, colorRampEnd;
        public bool fadeOut = true;
        public ColorSequence colorSequence = ColorSequence.Fixed;
        [ColorUsage(showAlpha: true, hdr: true)]
        public Color color = Color.white;
        public float colorCycleDuration = 3f;
        public bool colorCycleLoop = true;
        public float pingPongSpeed = 1f;
        [GradientUsage(hdr: true)]
        public Gradient colorStartPalette;
        public Camera cam;
        public TrailStyle effect = TrailStyle.Color;
        public Material customMaterial;
        public Texture2D texture;
        public Vector3 scale = Vector3.one, scaleStartRandomMin = Vector3.one, scaleStartRandomMax = Vector3.one;
        public AnimationCurve scaleOverTime;
        [Tooltip("Ignores object scale when calculating trail scale.")]
        public bool ignoreTransformScale;
        [Tooltip("Applies an uniform scale to x/y/z axis.")]
        public bool scaleUniform;
        [Tooltip("If set, trail will be parented to this gameobject")]
        public Transform parent;
        public Vector3 localPositionRandomMin, localPositionRandomMax;
        public float laserBandWidth = 0.1f, laserIntensity = 20f, laserFlash = 0.2f;
        [ColorUsage(showAlpha: true, hdr: true)]
        public Color trailTint = new Color(0f, 0, 0.1f);

        [Tooltip("Fades out effects based on distance to camera")]
        public bool cameraDistanceFade;

        [Tooltip("The closest distance particles can get to the camera before they fade from the camera’s view.")]
        public float cameraDistanceFadeNear;

        [Tooltip("The farthest distance particles can get away from the camera before they fade from the camera’s view.")]
        public float cameraDistanceFadeFar = 1000;


        [Tooltip("Add trails only during these animation states. Optionally include start and end time, example: Attack or Attack(0.1-1.5)")]
        public string animationStates;

        [Range(0f, 1f)]
        [Tooltip("Start of the normalized playback window (0..1) within the filtered animation state. The trail is active when normalizedTime is inside [start, end].")]
        public float animationStateNormalizedStart = 0f;

        [Range(0f, 1f)]
        [Tooltip("End of the normalized playback window (0..1) within the filtered animation state. The trail is active when normalizedTime is inside [start, end].")]
        public float animationStateNormalizedEnd = 1f;

        [Tooltip("The animator component. If not specified, first animator component found in children or parent will be used.")]
        public Animator animator;

        public Transform lookTarget;
        public bool lookToCamera = true;
        [Range(0, 1)]
        public float textureCutOff = 0.25f;
        public OutlineMethod outlineMethod = OutlineMethod.Normals;
        public float rimPower = 2f;
        [Range(0, 1)]
        public float normalThreshold = 0.3f;

        public bool useLastAnimationState;
        public int maxBatches = 50;
        public int meshPoolSize = 256;
        [Tooltip("Interpolate vertices to provide a smoother effect.")]
        public bool interpolate;
        [Tooltip("Use original materials for each submesh.")]
        public bool preserveMultiMaterials;

        #endregion

        static Color colorTransparent = new Color(0, 0, 0, 0);

        const int MAX_BATCH_INSTANCES = 1023; // max number of instances submitted to GPU in a batch. This limit is defined by Unity.
        const int BAKED_GRADIENTS_LENGTH = 256; // number of baked values for the gradients

        struct SnapshotTransform {
            public Matrix4x4 matrix, parentMatrix;
            public float time;
            public int meshIndex;
            public Color color;
            public Vector3 rampStartPos;
            public Vector3 rampEndPos;
        }


        public struct SnapshotIndex {
            public float t;
            public int index;
        }

        static class ShaderParams {
            public static int ColorArray = Shader.PropertyToID("_Colors");
            public static int SubFrameKeys = Shader.PropertyToID("_SubFrameKeys");
            public static int ColorRamp = Shader.PropertyToID("_ColorRamp");
            public static int CutOff = Shader.PropertyToID("_CutOff");
            public static int NormalThreshold = Shader.PropertyToID("_NormalThreshold");
            public static int AdditiveTint = Shader.PropertyToID("_AdditiveTint");
            public static int LaserData = Shader.PropertyToID("_LaserData");
            public static int Cull = Shader.PropertyToID("_Cull");
            public static int ZTest = Shader.PropertyToID("_ZTest");
            public static int ZWrite = Shader.PropertyToID("_ZWrite");
            public static int ZOffset = Shader.PropertyToID("_ZOffset");
            public static int RampStartPositions = Shader.PropertyToID("_RampStartPos");
            public static int RampEndPositions = Shader.PropertyToID("_RampEndPos");
            public static int MaskTex = Shader.PropertyToID("_MaskTex");
            public static int ParentMatricesArray = Shader.PropertyToID("_ParentMatrices");
            public static int PivotMatrix = Shader.PropertyToID("_PivotMatrix");
            public static int RimPower = Shader.PropertyToID("_RimPower");
            public static int BaseColor = Shader.PropertyToID("_BaseColor");

            public const string SKW_MASK = "TRAIL_MASK";
            public const string SKW_ALPHACLIP = "TRAIL_ALPHACLIP";
            public const string SKW_INTERPOLATE = "TRAIL_INTERPOLATE";
            public const string SKW_COLOR_RAMP = "TRAIL_COLOR_RAMP";
            public const string SKW_LOCAL = "TRAIL_LOCAL";
            public const string SKW_RIM = "TRAIL_RIM";
        }

        struct AnimationStatesInfo {
            public int hash;
            public float startTime;
            public float endTime;
        }

        SnapshotTransform[] trail;
        SnapshotIndex[] sortIndices;
        int trailIndex;
        Mesh[] meshPool;
        int meshPoolIndex;
        readonly List<Vector3> prevBakedMeshVertices = new List<Vector3>();
        float[] subFrameKeys;
        Material trailMask, trailClearMask;
        Material[] trailMaterial;
        Material[][] trailMultiMaterials;
        Renderer theRenderer;
        Vector3 lastCornerMinPos, lastCornerMaxPos, lastPosition, lastRandomizedPosition, lastRelativePosition;
        Quaternion lastRotation;
        float lastIntervalTimeCheck;
        MaterialPropertyBlock properties;
        Matrix4x4[] matrices;
        Matrix4x4[] parentMatrices;
        Vector4[] colors;
        Vector4[] rampStartPositions, rampEndPositions;

        class HierarchyOccluderEntry {
            public Renderer renderer;
            public Mesh mesh;
            public SkinnedMeshRenderer skinned;
            public bool ownsMesh;
        }

        /// <summary>
        /// Data structure for child trail renderers (also used for main body in sorted list)
        /// </summary>
        class ChildTrailData {
            public Transform target;
            public Renderer renderer;
            public SkinnedMeshRenderer skinnedMeshRenderer;
            public MeshFilter meshFilter;
            public bool isSkinned;
            public bool isMain; // True if this represents the main body
            public Mesh[] meshPool;
            public int meshPoolIndex;
            public int lastBakedFrame; // Frame when BakeMesh was last called (avoids duplicate bakes per frame)
            public Mesh maskMesh; // Dedicated mesh for current-frame mask rendering
            public Material[] materials; // Own material instances for render queue control
            public SnapshotTransform[] trail;
            public int trailIndex;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public float zDepth;
            
            public bool hasOverrides;
            public float luminanceCutOffOverride;
            public OutlineMethod outlineMethodOverride;
            public float normalThresholdOverride;
            public float rimPowerOverride;
            
            public void Setup(int stepsBufferSize, int meshPoolSize) {
                if (trail == null || trail.Length != stepsBufferSize) {
                    trail = new SnapshotTransform[stepsBufferSize];
                    for (int k = 0; k < trail.Length; k++) {
                        trail[k].time = float.MinValue;
                    }
                    trailIndex = -1;
                }
                if (meshPool == null || meshPool.Length != meshPoolSize) {
                    meshPool = new Mesh[meshPoolSize];
                }
            }
            
            public void Clear() {
                if (trail != null) {
                    for (int k = 0; k < trail.Length; k++) {
                        trail[k].time = float.MinValue;
                    }
                }
                trailIndex = -1;
                meshPoolIndex = 0;
            }
            
            public Mesh GetMaskMesh() {
                if (maskMesh == null) {
                    maskMesh = new Mesh();
                    maskMesh.MarkDynamic();
                }
                return maskMesh;
            }
            
            public void DestroyMaterials() {
                if (materials != null) {
                    for (int i = 0; i < materials.Length; i++) {
                        if (materials[i] != null) {
                            UnityEngine.Object.DestroyImmediate(materials[i]);
                            materials[i] = null;
                        }
                    }
                }
            }
        }

        List<HierarchyOccluderEntry> hierarchyOccluders;
        Transform hierarchyOccluderCurrentRoot;
        int hierarchyOccluderCurrentLayerMask = ~0;
        bool hierarchyOccluderCurrentIncludeInactive = true;
        bool hierarchyOccluderLastState;
        bool hierarchyOccludersDirty = true;
        
        // Children trail data
        List<ChildTrailData> childrenTrailData;
        bool childrenDirty = true;
        
        // Combined mesh data
        Mesh[] combinedMeshPool;
        int combinedMeshPoolIndex;
        int combinedMeshBakeTime;
        List<CombineInstance> combineInstances;
        Mesh combinedMaskMesh;
        int combinedMaskBakeTime;
        
        // Main body entry for unified sorting (reused to avoid allocation)
        ChildTrailData mainTrailData;
        List<ChildTrailData> sortedRenderables; // Combined list for sorting
        
        // Current render queue offset for depth ordering (set before each draw)
        int currentRenderQueueOffset;

        static int globalRenderQueue = 3100;
        int renderQueue;
        [NonSerialized]
        public SkinnedMeshRenderer skinnedMeshRenderer;
        [NonSerialized]
        public bool isSkinned;
        [NonSerialized]
        public ParticleSystemRenderer particleRenderer;
        [NonSerialized]
        public bool isParticle;
        MeshFilter cachedMeshFilter;
        int bakeTime;
        int batchNumber;
        static Mesh quadMesh;
        Dictionary<string, Material> effectMaterials;
        bool orient;
        Vector3 groundNormal;
        int startFrameCount;
        float startTime;
        float smoothDuration;
        bool isLimitedToAnimationStates;
        AnimationStatesInfo[] stateHashes;
        bool supportsGPUInstancing;
        MaterialPropertyBlock propertyBlock;
        Color colorRandomAtStart;
        Color[] bakedColorOverTime, bakedColorStartPalette;
        float[] bakedScaleOverTime;
        bool wasInactive;
        bool interpolating;
        bool usingColorRamp;
        static readonly char[] commaSeparator = new char[] { ',' };
        static readonly char[] dashSeparator = new char[] { '-' };
        bool hasParent;
        Vector3 parentPosition, lastParentPosition;
        Quaternion parentRotation, lastParentRotation;
        Vector3 rampLocalLastStart, rampLocalCurrentStart, rampLocalLastEnd, rampLocalCurrentEnd;

        void OnEnable () {

            CheckEditorSettings();

            hierarchyOccludersDirty = true;
            childrenDirty = true;
            hierarchyOccluderLastState = hierarchyOccluder;
            if (hierarchyOccluders == null) {
                hierarchyOccluders = new List<HierarchyOccluderEntry>();
            }

            // setup materials
            renderQueue = globalRenderQueue;
            globalRenderQueue += maxBatches + 2;
            if (globalRenderQueue > 3500) {
                globalRenderQueue = 3100;
            }
            if (trailMask == null) {
                trailMask = new Material(Shader.Find("TrailsFX/Mask"));
                trailMask.hideFlags = HideFlags.DontSave;
            }
            trailMask.renderQueue = renderQueue;
            if (trailClearMask == null) {
                trailClearMask = Instantiate(Resources.Load<Material>("TrailsFX/TrailClearMask"));
                trailClearMask.hideFlags = HideFlags.DontSave;
            }

            if (properties == null) {
                properties = new MaterialPropertyBlock();
            }
            else {
                properties.Clear();
            }
            supportsGPUInstancing = SystemInfo.supportsInstancing;
            if (!supportsGPUInstancing) {
                if (propertyBlock == null) {
                    propertyBlock = new MaterialPropertyBlock();
                }
                else {
                    propertyBlock.Clear();
                }
            }

            if (profileSync && profile != null) {
                profile.Load(this);
            }
            Clear();
        }

        void DestroyMaterial (Material mat) {
            if (mat != null) {
                DestroyImmediate(mat);
            }
        }

        void OnDestroy () {
            DestroyMaterial(trailMask);
            DestroyMaterial(trailClearMask);
            if (trailMaterial != null) {
                for (int k = 0; k < trailMaterial.Length; k++) {
                    DestroyMaterial(trailMaterial[k]);
                }
            }
            if (trailMultiMaterials != null) {
                for (int k = 0; k < trailMultiMaterials.Length; k++) {
                    if (trailMultiMaterials[k] != null) {
                        for (int j = 0; j < trailMultiMaterials[k].Length; j++) {
                            DestroyMaterial(trailMultiMaterials[k][j]);
                        }
                    }
                }
            }
            // Destroy child materials
            if (childrenTrailData != null) {
                for (int c = 0; c < childrenTrailData.Count; c++) {
                    childrenTrailData[c].DestroyMaterials();
                }
            }
            ClearHierarchyOccluders();
            if (effectMaterials != null) {
                foreach (KeyValuePair<string, Material> kvp in effectMaterials) {
                    DestroyMaterial(kvp.Value);
                }
            }
            if (isSkinned && meshPool != null) {
                for (int k = 0; k < meshPool.Length; k++) {
                    if (meshPool[k] != null) {
                        DestroyImmediate(meshPool[k]);
                    }
                }
            }
        }

        void OnValidate () {
            CheckEditorSettings();
            hierarchyOccludersDirty = true;
            childrenDirty = true;
        }

        void Start () {

            startFrameCount = Time.frameCount;
            if (executeInEditMode || Application.isPlaying) {
                UpdateMaterialProperties();
            }
            colorRandomAtStart = bakedColorStartPalette[UnityEngine.Random.Range(0, BAKED_GRADIENTS_LENGTH)];
            RefreshChildren();
        }
        
        /// <summary>
        /// Call this method to refresh internal caches (children renderers, hierarchy occluders)
        /// </summary>
        public void Refresh() {
            hierarchyOccludersDirty = true;
            childrenDirty = true;
        }
        
        void RefreshChildren() {
            if (!childrenDirty) return;
            childrenDirty = false;
            
            if (childrenTrailData == null) {
                childrenTrailData = new List<ChildTrailData>();
            } else {
                // Destroy old child materials before clearing
                for (int c = 0; c < childrenTrailData.Count; c++) {
                    childrenTrailData[c].DestroyMaterials();
                }
            }
            childrenTrailData.Clear();
            
            if (include == null || include.Length == 0) return;
            
            for (int i = 0; i < include.Length; i++) {
                IncludedTarget inc = include[i];
                if (inc == null || inc.target == null) continue;
                
                Transform t = inc.target;
                Renderer r = t.GetComponent<Renderer>();
                if (r == null || r is ParticleSystemRenderer) continue;
                
                if (r == theRenderer) continue;
                
                ChildTrailData child = new ChildTrailData {
                    target = t,
                    renderer = r,
                    hasOverrides = inc.overrideSettings,
                    luminanceCutOffOverride = inc.luminanceCutOff,
                    outlineMethodOverride = inc.outlineMethod,
                    normalThresholdOverride = inc.normalThreshold,
                    rimPowerOverride = inc.rimPower
                };
                
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null) {
                    child.skinnedMeshRenderer = smr;
                    child.isSkinned = true;
                } else {
                    child.meshFilter = r.GetComponent<MeshFilter>();
                }
                
                // Initialize trail data
                child.Setup(stepsBufferSize, meshPoolSize);
                child.lastPosition = child.target.position;
                child.lastRotation = child.target.rotation;
                
                childrenTrailData.Add(child);
            }
        }
        
        void EnsureChildMaterials(ChildTrailData child) {
            if (trailMaterial == null) return;
            
            int count = trailMaterial.Length;
            if (child.materials == null || child.materials.Length != count) {
                child.DestroyMaterials();
                child.materials = new Material[count];
            }
            
            // Create/update material copies for this child
            for (int k = 0; k < count; k++) {
                if (trailMaterial[k] == null) continue;
                
                if (child.materials[k] == null || child.materials[k].shader != trailMaterial[k].shader) {
                    if (child.materials[k] != null) {
                        DestroyImmediate(child.materials[k]);
                    }
                    child.materials[k] = Instantiate(trailMaterial[k]);
                    child.materials[k].hideFlags = HideFlags.DontSave;
                } else {
                    child.materials[k].CopyPropertiesFromMaterial(trailMaterial[k]);
                }
                
                if (child.hasOverrides) {
                    ApplyChildOverrides(child, child.materials[k]);
                }
            }
        }
        
        void ApplyChildOverrides(ChildTrailData child, Material mat) {
            switch (effect) {
                case TrailStyle.Clone:
                    mat.SetFloat(ShaderParams.CutOff, child.luminanceCutOffOverride);
                    break;
                case TrailStyle.Outline:
                    if (child.outlineMethodOverride == OutlineMethod.Rim) {
                        mat.SetFloat(ShaderParams.RimPower, child.rimPowerOverride);
                        mat.EnableKeyword(ShaderParams.SKW_RIM);
                    } else {
                        mat.SetFloat(ShaderParams.NormalThreshold, child.normalThresholdOverride);
                        mat.DisableKeyword(ShaderParams.SKW_RIM);
                    }
                    break;
            }
        }


        private void OnDisable () {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= ExecuteInEditor;
#endif
        }

#if UNITY_EDITOR
        void ExecuteInEditor () {
            UnityEditor.EditorUtility.SetDirty(this);

        }
#endif


        void LateUpdate () {

            if (!executeInEditMode && !Application.isPlaying)
                return;

            if (trail == null)
                return;

            if (cam == null) {
                cam = Camera.main;
                if (cam == null) {
#if UNITY_2023_1_OR_NEWER
                    cam = FindAnyObjectByType<Camera>(FindObjectsInactive.Exclude);
#else
                    cam = FindObjectOfType<Camera>();
#endif
                    if (cam == null)
                        return;
                }
            }
            
            // Refresh children if needed
            if (childrenDirty) {
                RefreshChildren();
            }

            // Add snapshots for main target (and children if main body triggered)
            bool snapshotCreated = AddSnapshot();
            
            // Add snapshots for children only when main body creates a snapshot and not using combined mesh
            if (snapshotCreated && include != null && include.Length > 0 && childrenTrailData != null && !combineMeshesWithChildren) {
                AddChildrenSnapshots();
            }

            // Render all trails
            RenderAllTrails();
        }
        
        void AddChildrenSnapshots() {
            if (childrenTrailData == null) return;
            
            Color color = GetSnapshotColor();
            if (color.a == 0) return;
            
            Vector3 scale = GetSnapshotScale();
            float now = Time.time;
            
            int childCount = childrenTrailData.Count;
            for (int c = 0; c < childCount; c++) {
                ChildTrailData child = childrenTrailData[c];
                if (child.renderer == null || !child.renderer.enabled) continue;
                
                Vector3 pos = child.target.position;
                
                // Setup mesh for skinned children
                Mesh mesh = null;
                int snapshotMeshIndex = 0;
                if (child.isSkinned && child.skinnedMeshRenderer != null) {
                    int thisFrame = Time.frameCount;
                    if (thisFrame != child.lastBakedFrame) {
                        child.lastBakedFrame = thisFrame;
                        child.meshPoolIndex++;
                        if (child.meshPoolIndex >= child.meshPool.Length) {
                            child.meshPoolIndex = 0;
                        }
                        if (child.meshPool[child.meshPoolIndex] == null) {
                            child.meshPool[child.meshPoolIndex] = new Mesh();
                            child.meshPool[child.meshPoolIndex].MarkDynamic();
                        }
                        child.skinnedMeshRenderer.BakeMesh(child.meshPool[child.meshPoolIndex]);
                    }
                    mesh = child.meshPool[child.meshPoolIndex];
                    snapshotMeshIndex = child.meshPoolIndex;
                } else {
                    // Use cached meshFilter (no GetComponent allocation)
                    if (child.meshFilter != null) {
                        mesh = child.meshFilter.sharedMesh;
                        if (child.meshPool[0] == null || child.meshPool[0] != mesh) {
                            child.meshPool[0] = mesh;
                        }
                    }
                    snapshotMeshIndex = 0; // Non-skinned meshes always use index 0
                }
                
                if (mesh == null) continue;
                
                // Add snapshot
                child.trailIndex++;
                if (child.trailIndex >= child.trail.Length) {
                    child.trailIndex = 0;
                }
                
                Quaternion rotation = child.target.rotation;
                Vector3 childScale = child.isSkinned ? Vector3.one : child.target.lossyScale;
                childScale.x *= scale.x;
                childScale.y *= scale.y;
                childScale.z *= scale.z;
                
                child.trail[child.trailIndex].matrix = Matrix4x4.TRS(pos, rotation, childScale);
                child.trail[child.trailIndex].time = now;
                child.trail[child.trailIndex].meshIndex = snapshotMeshIndex;
                child.trail[child.trailIndex].color = color;
                
                child.lastPosition = pos;
                child.lastRotation = rotation;
            }
        }
        
        void RenderAllTrails() {
            bool hasChildren = include != null && include.Length > 0 && childrenTrailData != null && childrenTrailData.Count > 0;
            
            if (hasChildren && combineMeshesWithChildren) {
                RenderTrailCombined();
                return;
            }
            
            if (hasChildren) {
                // Initialize main body entry if needed
                if (mainTrailData == null) {
                    mainTrailData = new ChildTrailData { isMain = true };
                }
                mainTrailData.renderer = theRenderer;
                mainTrailData.target = target;
                
                // Initialize sorted list if needed
                if (sortedRenderables == null) {
                    sortedRenderables = new List<ChildTrailData>();
                }
                sortedRenderables.Clear();
                
                // Calculate z-depth for all renderables (main + children)
                // Use renderer.bounds.center for accurate position of animated/skinned meshes
                Vector3 camPos = cam.transform.position;
                Vector3 camForward = cam.transform.forward;
                
                // Add main body
                Vector3 mainCenter = theRenderer != null ? theRenderer.bounds.center : target.position;
                mainTrailData.zDepth = Vector3.Dot(mainCenter - camPos, camForward);
                sortedRenderables.Add(mainTrailData);
                
                // Add children
                int childCount = childrenTrailData.Count;
                for (int c = 0; c < childCount; c++) {
                    ChildTrailData child = childrenTrailData[c];
                    if (child.renderer != null) {
                        child.zDepth = Vector3.Dot(child.renderer.bounds.center - camPos, camForward);
                    } else if (child.target != null) {
                        child.zDepth = Vector3.Dot(child.target.position - camPos, camForward);
                    }
                    sortedRenderables.Add(child);
                }
                
                // Sort all by z-depth: front to back (closer/lower z-depth first)
                sortedRenderables.Sort((a, b) => a.zDepth.CompareTo(b.zDepth));
                
                int totalCount = sortedRenderables.Count;
                
                // Phase 1: Render ALL masks front to back (closer objects first)
                for (int c = 0; c < totalCount; c++) {
                    ChildTrailData item = sortedRenderables[c];
                    if (item.isMain) {
                        RenderMaskForMain();
                    } else if (item.renderer != null && item.renderer.enabled) {
                        RenderMaskForChild(item);
                    }
                }
                
                // Hierarchy occluders (once for all)
                if (hierarchyOccluder && renderOrder == TrailRenderOrder.DrawBehind) {
                    DrawHierarchyOccluders();
                }
                
                // Phase 2: Render effects front to back (closer objects first)
                // Use render queue offsets to force correct draw order
                for (int c = 0; c < totalCount; c++) {
                    currentRenderQueueOffset = c * 10; // 10 queues per object for batches
                    ChildTrailData item = sortedRenderables[c];
                    if (item.isMain) {
                        RenderTrailEffectsOnly();
                    } else {
                        RenderChildTrailEffects(item);
                    }
                }
                currentRenderQueueOffset = 0; // Reset
                
                // Phase 3: Render clear stencil for all
                if (clearStencil && renderOrder == TrailRenderOrder.DrawBehind) {
                    for (int c = 0; c < totalCount; c++) {
                        ChildTrailData item = sortedRenderables[c];
                        if (item.isMain) {
                            RenderClearStencilForMain();
                        } else if (item.renderer != null && item.renderer.enabled) {
                            RenderClearStencilForChild(item);
                        }
                    }
                }
            } else {
                // No children - use original rendering path
                RenderTrail();
            }
        }
        
        void RenderTrailCombined() {
            int count = CollectValidTrailEntries(trail);
            if (count == 0) return;

            batchNumber = 0;
            bool singleBatch = useLastAnimationState || effect == TrailStyle.TextureStamp;
            if (singleBatch && count <= MAX_BATCH_INSTANCES) {
                SendToGPUCombined(combinedMeshPoolIndex, 0, count);
            }
            else {
                int batchMeshIndex = trail[sortIndices[0].index].meshIndex;
                int batchStartIndex = 0;
                int batchInstancesCount = 1;

                for (int k = 1; k < count; k++) {
                    int i = sortIndices[k].index;
                    int meshIndex = trail[i].meshIndex;
                    if (meshIndex != batchMeshIndex || batchInstancesCount >= MAX_BATCH_INSTANCES) {
                        SendToGPUCombined(batchMeshIndex, batchStartIndex, batchInstancesCount);
                        batchMeshIndex = meshIndex;
                        batchStartIndex += batchInstancesCount;
                        batchInstancesCount = 0;
                    }
                    batchInstancesCount++;
                }
                if (batchInstancesCount > 0) {
                    SendToGPUCombined(batchMeshIndex, batchStartIndex, batchInstancesCount);
                }
            }
        }
        
        void SendToGPUCombined(int meshIndex, int startIndex, int count) {
            if (combinedMeshPool == null || meshIndex < 0 || meshIndex >= combinedMeshPool.Length)
                return;

            Mesh batchMesh = effect == TrailStyle.TextureStamp ? quadMesh : combinedMeshPool[meshIndex];
            if (batchMesh == null)
                return;

            int layer = target.gameObject.layer;

            if (renderOrder == TrailRenderOrder.DrawBehind && batchNumber == 0 && (theRenderer.isVisible || ignoreVisibility)) {
                Vector3 pos = target.position;
                Mesh mask = GetCombinedMaskMesh();
                if (mask != null) {
                    Matrix4x4 m = Matrix4x4.TRS(pos, GetRotation(), Vector3.one);
                    int subMeshCount = mask.subMeshCount;
                    for (int k = 0; k < subMeshCount; k++) {
                        Graphics.DrawMesh(mask, m, trailMask, layer, null, k);
                        if (clearStencil) {
                            Graphics.DrawMesh(mask, m, trailClearMask, layer, null, k);
                        }
                    }
                    if (hierarchyOccluder) {
                        DrawHierarchyOccluders();
                    }
                }
            }

            for (int o = 0; o < count; o++, startIndex++) {
                int index = sortIndices[startIndex].index;
                float t = sortIndices[startIndex].t;
                if (t < 0) t = 0;
                int it = (int)(BAKED_GRADIENTS_LENGTH * t) % BAKED_GRADIENTS_LENGTH;

                Color baseColor = trail[index].color;
                Color color = bakedColorOverTime[it];
                colors[o].x = color.r * baseColor.r;
                colors[o].y = color.g * baseColor.g;
                colors[o].z = color.b * baseColor.b;
                colors[o].w = color.a * baseColor.a;
                if (fadeOut) colors[o].w *= 1f - t;

                subFrameKeys[o] = (float)o / count;

                float scale = bakedScaleOverTime[it];
                matrices[o].m00 = trail[index].matrix.m00 * scale;
                matrices[o].m01 = trail[index].matrix.m01 * scale;
                matrices[o].m02 = trail[index].matrix.m02 * scale;
                matrices[o].m03 = trail[index].matrix.m03;
                matrices[o].m10 = trail[index].matrix.m10 * scale;
                matrices[o].m11 = trail[index].matrix.m11 * scale;
                matrices[o].m12 = trail[index].matrix.m12 * scale;
                matrices[o].m13 = trail[index].matrix.m13;
                matrices[o].m20 = trail[index].matrix.m20 * scale;
                matrices[o].m21 = trail[index].matrix.m21 * scale;
                matrices[o].m22 = trail[index].matrix.m22 * scale;
                matrices[o].m23 = trail[index].matrix.m23;
                matrices[o].m30 = trail[index].matrix.m30;
                matrices[o].m31 = trail[index].matrix.m31;
                matrices[o].m32 = trail[index].matrix.m32;
                matrices[o].m33 = trail[index].matrix.m33;

                if (usingColorRamp) {
                    rampStartPositions[o].x = trail[index].rampStartPos.x;
                    rampStartPositions[o].y = trail[index].rampStartPos.y;
                    rampStartPositions[o].z = trail[index].rampStartPos.z;
                    rampEndPositions[o].x = trail[index].rampEndPos.x;
                    rampEndPositions[o].y = trail[index].rampEndPos.y;
                    rampEndPositions[o].z = trail[index].rampEndPos.z;
                }

                if (hasParent) {
                    parentMatrices[o].m00 = trail[index].parentMatrix.m00;
                    parentMatrices[o].m01 = trail[index].parentMatrix.m01;
                    parentMatrices[o].m02 = trail[index].parentMatrix.m02;
                    parentMatrices[o].m03 = trail[index].parentMatrix.m03;
                    parentMatrices[o].m10 = trail[index].parentMatrix.m10;
                    parentMatrices[o].m11 = trail[index].parentMatrix.m11;
                    parentMatrices[o].m12 = trail[index].parentMatrix.m12;
                    parentMatrices[o].m13 = trail[index].parentMatrix.m13;
                    parentMatrices[o].m20 = trail[index].parentMatrix.m20;
                    parentMatrices[o].m21 = trail[index].parentMatrix.m21;
                    parentMatrices[o].m22 = trail[index].parentMatrix.m22;
                    parentMatrices[o].m23 = trail[index].parentMatrix.m23;
                    parentMatrices[o].m30 = trail[index].parentMatrix.m30;
                    parentMatrices[o].m31 = trail[index].parentMatrix.m31;
                    parentMatrices[o].m32 = trail[index].parentMatrix.m32;
                    parentMatrices[o].m33 = trail[index].parentMatrix.m33;
                }
            }

            properties.SetVectorArray(ShaderParams.ColorArray, colors);
            if (interpolating || usingColorRamp) {
                properties.SetFloatArray(ShaderParams.SubFrameKeys, subFrameKeys);
            }
            if (usingColorRamp) {
                properties.SetVectorArray(ShaderParams.RampStartPositions, rampStartPositions);
                properties.SetVectorArray(ShaderParams.RampEndPositions, rampEndPositions);
            }
            if (hasParent) {
                properties.SetMatrixArray(ShaderParams.ParentMatricesArray, parentMatrices);
                properties.SetMatrix(ShaderParams.PivotMatrix, parent.localToWorldMatrix);
            }
            if (batchNumber < trailMaterial.Length - 1) {
                batchNumber++;
            }
            else return;

            int batchMeshSubMeshCount = batchMesh.subMeshCount;
            if (supportsGPUInstancing) {
                for (int s = 0; s < batchMeshSubMeshCount; s++) {
                    Material mat = (effect == TrailStyle.Clone && preserveMultiMaterials && trailMultiMaterials != null && batchNumber < trailMultiMaterials.Length && s < trailMultiMaterials[batchNumber].Length) ? trailMultiMaterials[batchNumber][s] : trailMaterial[batchNumber];
                    mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                    Graphics.DrawMeshInstanced(batchMesh, s, mat, matrices, count, properties, UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
                }
                if (clearStencil) {
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        Graphics.DrawMeshInstanced(batchMesh, s, trailClearMask, matrices, count, null, UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
                    }
                }
            }
            else {
                for (int i = 0; i < count; i++) {
                    propertyBlock.SetVector(ShaderParams.ColorArray, colors[i]);
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        Material mat = (effect == TrailStyle.Clone && preserveMultiMaterials && trailMultiMaterials != null && batchNumber < trailMultiMaterials.Length && s < trailMultiMaterials[batchNumber].Length) ? trailMultiMaterials[batchNumber][s] : trailMaterial[batchNumber];
                        mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                        Graphics.DrawMesh(batchMesh, matrices[i], mat, layer, null, s, propertyBlock, false, false);
                    }
                }
                if (clearStencil) {
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        for (int i = 0; i < count; i++) {
                            Graphics.DrawMesh(batchMesh, matrices[i], trailClearMask, layer, null, s, null, false, false);
                        }
                    }
                }
            }
        }
        
        void RenderClearStencilForMain() {
            if (!theRenderer.isVisible && !ignoreVisibility) return;
            Mesh mesh;
            Vector3 sca;
            if (isSkinned || isParticle) {
                mesh = meshPool != null && meshPoolIndex < meshPool.Length ? meshPool[meshPoolIndex] : null;
                sca = Vector3.one;
            } else {
                mesh = cachedMeshFilter != null ? cachedMeshFilter.sharedMesh : null;
                sca = target.lossyScale;
            }
            if (mesh != null) {
                DrawMeshWithMaterial(mesh, target.position, GetRotation(), sca, target.gameObject.layer, trailClearMask);
            }
        }
        
        void RenderClearStencilForChild(ChildTrailData child) {
            if (child.renderer == null || (!child.renderer.isVisible && !ignoreVisibility)) return;
            Mesh mesh;
            Vector3 sca;
            if (child.isSkinned && child.skinnedMeshRenderer != null) {
                int thisFrame = Time.frameCount;
                mesh = (thisFrame == child.lastBakedFrame && child.meshPool[child.meshPoolIndex] != null) 
                    ? child.meshPool[child.meshPoolIndex] : child.maskMesh;
                sca = Vector3.one;
            } else {
                mesh = child.meshFilter != null ? child.meshFilter.sharedMesh : null;
                sca = child.target.lossyScale;
            }
            if (mesh != null) {
                DrawMeshWithMaterial(mesh, child.target.position, child.target.rotation, sca, child.target.gameObject.layer, trailClearMask);
            }
        }
        
        void RenderMaskForMain() {
            if (renderOrder != TrailRenderOrder.DrawBehind) return;
            if (!theRenderer.isVisible && !ignoreVisibility) return;
            Mesh mesh;
            Vector3 sca;
            if (isSkinned || isParticle) {
                mesh = SetupMesh(false);
                sca = Vector3.one;
            } else {
                mesh = cachedMeshFilter != null ? cachedMeshFilter.sharedMesh : null;
                sca = target.lossyScale;
            }
            if (mesh != null) {
                DrawMeshWithMaterial(mesh, target.position, GetRotation(), sca, target.gameObject.layer, trailMask);
            }
        }
        
        void RenderMaskForChild(ChildTrailData child) {
            if (renderOrder != TrailRenderOrder.DrawBehind) return;
            if (child.renderer == null || (!child.renderer.isVisible && !ignoreVisibility)) return;
            Mesh mesh;
            Vector3 sca;
            if (child.isSkinned && child.skinnedMeshRenderer != null) {
                int thisFrame = Time.frameCount;
                if (thisFrame == child.lastBakedFrame && child.meshPool[child.meshPoolIndex] != null) {
                    mesh = child.meshPool[child.meshPoolIndex];
                } else {
                    mesh = child.GetMaskMesh();
                    child.skinnedMeshRenderer.BakeMesh(mesh);
                }
                sca = Vector3.one;
            } else {
                mesh = child.meshFilter != null ? child.meshFilter.sharedMesh : null;
                sca = child.target.lossyScale;
            }
            if (mesh != null) {
                DrawMeshWithMaterial(mesh, child.target.position, child.target.rotation, sca, child.target.gameObject.layer, trailMask);
            }
        }
        
        void DrawMeshWithMaterial(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, int layer, Material mat) {
            Matrix4x4 m = Matrix4x4.TRS(pos, rot, scale);
            int subMeshCount = mesh.subMeshCount;
            for (int k = 0; k < subMeshCount; k++) {
                if (((1 << k) & subMeshMask) != 0) {
                    Graphics.DrawMesh(mesh, m, mat, layer, null, k);
                }
            }
        }
        
        void RenderChildTrailEffects(ChildTrailData child) {
            if (child.trail == null || child.renderer == null) return;
            
            EnsureChildMaterials(child);
            
            int count = CollectValidTrailEntries(child.trail);
            if (count == 0) return;
            
            int layer = child.target.gameObject.layer;
            batchNumber = 0;
            
            bool singleBatch = !child.isSkinned || useLastAnimationState || effect == TrailStyle.TextureStamp;
            if (singleBatch && count <= MAX_BATCH_INSTANCES) {
                int meshIdx = child.isSkinned ? child.meshPoolIndex : 0;
                SendChildToGPU(child, meshIdx, 0, count, layer);
            } else {
                int batchMeshIndex = child.trail[sortIndices[0].index].meshIndex;
                int batchStartIndex = 0;
                int batchInstancesCount = 1;
                
                for (int k = 1; k < count; k++) {
                    int i = sortIndices[k].index;
                    int meshIndex = child.trail[i].meshIndex;
                    if (meshIndex != batchMeshIndex || batchInstancesCount >= MAX_BATCH_INSTANCES) {
                        SendChildToGPU(child, batchMeshIndex, batchStartIndex, batchInstancesCount, layer);
                        batchMeshIndex = meshIndex;
                        batchStartIndex += batchInstancesCount;
                        batchInstancesCount = 0;
                    }
                    batchInstancesCount++;
                }
                if (batchInstancesCount > 0) {
                    SendChildToGPU(child, batchMeshIndex, batchStartIndex, batchInstancesCount, layer);
                }
            }
        }
        
        void SendChildToGPU(ChildTrailData child, int meshIndex, int startIndex, int count, int layer) {
            if (child.meshPool == null || meshIndex < 0 || meshIndex >= child.meshPool.Length) return;
            
            Mesh batchMesh = effect == TrailStyle.TextureStamp ? quadMesh : child.meshPool[meshIndex];
            if (batchMesh == null) return;
            
            // Pack for instancing
            for (int o = 0; o < count; o++, startIndex++) {
                int index = sortIndices[startIndex].index;
                float t = sortIndices[startIndex].t;
                if (t < 0) t = 0;
                int it = (int)(BAKED_GRADIENTS_LENGTH * t) % BAKED_GRADIENTS_LENGTH;
                
                Color baseColor = child.trail[index].color;
                Color c = bakedColorOverTime[it];
                colors[o].x = c.r * baseColor.r;
                colors[o].y = c.g * baseColor.g;
                colors[o].z = c.b * baseColor.b;
                colors[o].w = fadeOut ? c.a * baseColor.a * (1f - t) : c.a * baseColor.a;
                
                Matrix4x4 matrix = child.trail[index].matrix;
                float scaleT = bakedScaleOverTime[it];
                matrix.m00 *= scaleT; matrix.m01 *= scaleT; matrix.m02 *= scaleT;
                matrix.m10 *= scaleT; matrix.m11 *= scaleT; matrix.m12 *= scaleT;
                matrix.m20 *= scaleT; matrix.m21 *= scaleT; matrix.m22 *= scaleT;
                matrices[o] = matrix;
                
                if (interpolating) {
                    subFrameKeys[o] = t;
                }
            }
            
            properties.SetVectorArray(ShaderParams.ColorArray, colors);
            if (interpolating) {
                properties.SetFloatArray(ShaderParams.SubFrameKeys, subFrameKeys);
            }
            
            // Use child's own materials for proper render queue control
            Material[] mats = child.materials != null ? child.materials : trailMaterial;
            if (batchNumber < mats.Length - 1) {
                batchNumber++;
            } else return;
            
            int batchMeshSubMeshCount = batchMesh.subMeshCount;
            if (supportsGPUInstancing) {
                for (int s = 0; s < batchMeshSubMeshCount; s++) {
                    if (((1 << s) & subMeshMask) != 0) {
                        Material mat = mats[batchNumber];
                        mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                        Graphics.DrawMeshInstanced(batchMesh, s, mat, matrices, count, properties, UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
                    }
                }
            } else {
                for (int i = 0; i < count; i++) {
                    propertyBlock.SetVector(ShaderParams.ColorArray, colors[i]);
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        if (((1 << s) & subMeshMask) != 0) {
                            Material mat = mats[batchNumber];
                            mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                            Graphics.DrawMesh(batchMesh, matrices[i], mat, layer, null, s, propertyBlock, false, false);
                        }
                    }
                }
            }
        }
        
        void RenderTrailEffectsOnly() {
            int count = CollectValidTrailEntries(trail);
            if (count == 0) return;

            batchNumber = 0;
            bool singleBatch = (useLastAnimationState || effect == TrailStyle.TextureStamp) && !isParticle;
            if (singleBatch && count <= MAX_BATCH_INSTANCES) {
                SendToGPU(meshPoolIndex, 0, count, false);
            }
            else {
                int batchMeshIndex = trail[sortIndices[0].index].meshIndex;
                int batchStartIndex = 0;
                int batchInstancesCount = 1;

                for (int k = 1; k < count; k++) {
                    int i = sortIndices[k].index;
                    int meshIndex = trail[i].meshIndex;
                    if (meshIndex != batchMeshIndex || batchInstancesCount >= MAX_BATCH_INSTANCES) {
                        SendToGPU(batchMeshIndex, batchStartIndex, batchInstancesCount, false);
                        batchMeshIndex = meshIndex;
                        batchStartIndex += batchInstancesCount;
                        batchInstancesCount = 0;
                    }
                    batchInstancesCount++;
                }
                if (batchInstancesCount > 0) {
                    SendToGPU(batchMeshIndex, batchStartIndex, batchInstancesCount, false);
                }
            }
        }


        void OnCollisionEnter (Collision collision) {
            if (!checkCollisions || !_active)
                return;

            if (((1 << collision.gameObject.layer) & collisionLayerMask) == 0)
                return;

            Quaternion rotation;
            ContactPoint contact = collision.contacts[0];
            Vector3 pos = contact.point;
            pos += contact.normal * surfaceOffset;
            if (orientToSurface) {
                rotation = Quaternion.LookRotation(-contact.normal);
            }
            else {
                if (lookTarget != null) {
                    rotation = Quaternion.LookRotation(pos - lookTarget.transform.position);
                }
                else if (lookToCamera) {
                    Camera camera = cam;
                    if (camera == null) {
                        camera = Camera.main;
                    }
                    if (camera != null) {
                        rotation = Quaternion.LookRotation(pos - camera.transform.position);
                    }
                    else {
                        rotation = target.rotation;
                    }
                }
                else {
                    rotation = target.rotation;
                }
            }
            AddSnapshot(pos, rotation);
        }



        public void CheckEditorSettings () {
            if (target == null) {
                target = transform;
            }
            if (colorOverTime == null) {
                colorOverTime = new Gradient();
                GradientColorKey[] colorKeys = new GradientColorKey[2];
                colorKeys[0].color = Color.yellow;
                colorKeys[0].time = 0f;
                colorKeys[1].color = Color.yellow;
                colorKeys[1].time = 1f;
                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
                alphaKeys[0].alpha = 1f;
                alphaKeys[0].time = 0f;
                alphaKeys[1].alpha = 1f;
                alphaKeys[1].time = 1f;
                colorOverTime.SetKeys(colorKeys, alphaKeys);
            }
            if (scaleOverTime == null) {
                scaleOverTime = new AnimationCurve();
                Keyframe[] keys = new Keyframe[2];
                keys[0].value = 1f;
                keys[0].time = 0;
                keys[1].value = 1f;
                keys[1].time = 1;
                scaleOverTime.keys = keys;
            }
            if (colorStartPalette == null) {
                colorStartPalette = new Gradient();
                GradientColorKey[] colorKeys = new GradientColorKey[3];
                colorKeys[0].color = Color.red;
                colorKeys[0].time = 0f;
                colorKeys[1].color = Color.green;
                colorKeys[1].time = 0.5f;
                colorKeys[2].color = Color.blue;
                colorKeys[2].time = 1f;
                colorStartPalette.colorKeys = colorKeys;
            }
            rimPower = Mathf.Max(0.1f, rimPower);
        }

        /// <summary>
        /// Clears current trail and restarts cycle
        /// </summary>
        public void Clear () {
            UpdateMaterialProperties();
            if (theRenderer != null) {
                StoreCurrentPositions();
            }
            lastRandomizedPosition = GetRandomizedPosition();
            lastRotation = GetRotation();
            meshPoolIndex = 0;
            prevBakedMeshVertices.Clear();
            if (trail != null) {
                for (int k = 0; k < trail.Length; k++) {
                    trail[k].time = float.MinValue;
                }
            }
            trailIndex = -1;
            startFrameCount = Time.frameCount;
            startTime = Time.time;
            if (colorRampStart != null) rampLocalLastStart = target.InverseTransformPoint(colorRampStart.position);
            if (colorRampEnd != null) rampLocalLastEnd = target.InverseTransformPoint(colorRampEnd.position);
            
            // Clear children trail data
            if (childrenTrailData != null) {
                int childCount = childrenTrailData.Count;
                for (int c = 0; c < childCount; c++) {
                    childrenTrailData[c].Clear();
                }
            }
        }

        /// <summary>
        /// Restarts current trail cycle but keeps existing trail
        /// </summary>
        public void Restart () {
            startFrameCount = Time.frameCount;
            startTime = Time.time;
        }

        /// <summary>
        /// Marks the cached hierarchy occluders as dirty so they are rebuilt on the next frame.
        /// </summary>
        public void RefreshHierarchyOccluders () {
            hierarchyOccludersDirty = true;
        }



        public void UpdateMaterialProperties () {

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= ExecuteInEditor;
            if (executeInEditMode) {
                UnityEditor.EditorApplication.update += ExecuteInEditor;
            }

#endif
            CheckEditorSettings();
            if (bakedColorOverTime == null || bakedColorOverTime.Length != BAKED_GRADIENTS_LENGTH) {
                bakedColorOverTime = new Color[BAKED_GRADIENTS_LENGTH];
            }
            if (bakedScaleOverTime == null || bakedScaleOverTime.Length != BAKED_GRADIENTS_LENGTH) {
                bakedScaleOverTime = new float[BAKED_GRADIENTS_LENGTH];
            }
            if (bakedColorStartPalette == null || bakedColorStartPalette.Length != BAKED_GRADIENTS_LENGTH) {
                bakedColorStartPalette = new Color[BAKED_GRADIENTS_LENGTH];
            }
            for (int k = 0; k < BAKED_GRADIENTS_LENGTH; k++) {
                float t = (float)k / BAKED_GRADIENTS_LENGTH;
                bakedColorOverTime[k] = colorOverTime.Evaluate(t);
                bakedScaleOverTime[k] = scaleOverTime.Evaluate(t);
                bakedColorStartPalette[k] = colorStartPalette.Evaluate(t);
            }

            groundNormal = Vector3.up;
            skinnedMeshRenderer = null;
            particleRenderer = null;
            theRenderer = target.GetComponentInChildren<Renderer>();
            if (theRenderer == null) {
                trail = null;
                if (Application.isPlaying) {
                    enabled = false;
                }
                return;
            }
            hierarchyOccludersDirty = true;

            isLimitedToAnimationStates = false;
            if (!string.IsNullOrEmpty(animationStates)) {
                if (animator == null) {
                    animator = target.GetComponentInChildren<Animator>();
                    if (animator == null) {
                        animator = target.GetComponentInParent<Animator>();
                    }
                }
                isLimitedToAnimationStates = animator != null;
                if (isLimitedToAnimationStates) {
                    string[] names = animationStates.Split(commaSeparator, StringSplitOptions.RemoveEmptyEntries);
                    int hashCount = names.Length;
                    stateHashes = new AnimationStatesInfo[hashCount];
                    for (int k = 0; k < hashCount; k++) {
                        string name = null;
                        float startTime = 0, endTime = 0;
                        string data = names[k].Trim();
                        int par0 = data.IndexOf("(");
                        int par1 = data.IndexOf(")");
                        if (par1 > par0 && par0 > 0) {
                            name = data.Substring(0, par0).Trim();
                            string interval = data.Substring(par0 + 1, par1 - par0 - 1);
                            string[] times = interval.Split(dashSeparator, StringSplitOptions.RemoveEmptyEntries);
                            if (times.Length == 2) {
                                float.TryParse(times[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out startTime);
                                float.TryParse(times[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out endTime);
                            }
                        }
                        else {
                            name = data;
                        }
                        stateHashes[k].hash = Animator.StringToHash(name);
                        stateHashes[k].startTime = startTime;
                        stateHashes[k].endTime = endTime;
                    }
                }
            }

            isParticle = theRenderer is ParticleSystemRenderer;
            isSkinned = theRenderer is SkinnedMeshRenderer;
            if (isSkinned) {
                skinnedMeshRenderer = (SkinnedMeshRenderer)theRenderer;
            } else if (isParticle) {
                particleRenderer = (ParticleSystemRenderer)theRenderer;
            }
            
            if (isSkinned || isParticle) {
                int poolSize = useLastAnimationState ? 1 : meshPoolSize;
                if (meshPool == null || meshPool.Length != poolSize) {
                    meshPool = new Mesh[poolSize];
                }
                int meshPoolLength = meshPool.Length;
                for (int k = 0; k < meshPoolLength; k++) {
                    if (meshPool[k] == null) {
                        meshPool[k] = new Mesh();
                        meshPool[k].hideFlags = HideFlags.DontSave;
                    }
                }
            }
            else {
                cachedMeshFilter = theRenderer.GetComponent<MeshFilter>();
                MeshCollider mc = theRenderer.GetComponent<MeshCollider>();
                if (meshPool == null || meshPool.Length != 1) {
                    meshPool = new Mesh[1];
                }
                if (mc != null) {
                    meshPool[0] = mc.sharedMesh;
                }
                else if (cachedMeshFilter != null) {
                    meshPool[0] = cachedMeshFilter.sharedMesh;
                }
            }
            
            SetupCombinedMeshPool();

            // Runtime only setup
            if (!executeInEditMode && !Application.isPlaying)
                return;


            orient = false;
            if (trailMask == null) return;
            trailMask.DisableKeyword(ShaderParams.SKW_ALPHACLIP);
            trailMask.mainTexture = null;
            trailMask.SetInt(ShaderParams.Cull, (int)cullMode);

            Material trailMat = null;
            switch (effect) {
                case TrailStyle.Color:
                    trailMat = GetEffectMaterial("TrailEffectColor");
                    break;
                case TrailStyle.TextureStamp:
                    trailMat = GetEffectMaterial("TrailEffectTextureStamp");
                    if (quadMesh == null) {
                        quadMesh = BuildQuadMesh();
                    }
                    orient = (ground && orientToSurface) || lookToCamera || lookTarget != null;
                    break;
                case TrailStyle.Clone:
                    trailMat = GetEffectMaterial("TrailEffectClone");
                    break;
                case TrailStyle.Outline:
                    trailMat = GetEffectMaterial("TrailEffectOutline");
                    break;
                case TrailStyle.SpaceDistortion:
                    trailMat = GetEffectMaterial("TrailEffectDistort");
                    break;
                case TrailStyle.Dash:
                    trailMat = GetEffectMaterial("TrailEffectLaser");
                    break;
                case TrailStyle.Custom:
                    trailMat = customMaterial;
                    break;
            }
            if (trailMat == null) {
                trail = null;
                enabled = false;
                return;
            }

            interpolating = isSkinned && interpolate && !useLastAnimationState;
            interpolating = interpolating || (isParticle && interpolate);
            usingColorRamp = colorRamp && colorRampTexture != null && colorRampStart != null && colorRampEnd != null;

            if (trailMaterial == null || trailMaterial.Length != maxBatches) {
                if (trailMaterial != null) {
                    for (int k = 0; k < trailMaterial.Length; k++) {
                        DestroyMaterial(trailMaterial[k]);
                    }
                }
                trailMaterial = new Material[maxBatches];
            }

            if (effect == TrailStyle.Clone && preserveMultiMaterials) {
                Material[] sharedMaterials = theRenderer.sharedMaterials;
                int subMeshCount = sharedMaterials.Length;
                if (trailMultiMaterials == null || trailMultiMaterials.Length != maxBatches) {
                    trailMultiMaterials = new Material[maxBatches][];
                }
                for (int k = 0; k < maxBatches; k++) {
                    if (trailMultiMaterials[k] == null || trailMultiMaterials[k].Length != subMeshCount) {
                        trailMultiMaterials[k] = new Material[subMeshCount];
                    }
                    for (int j = 0; j < subMeshCount; j++) {
                        if (trailMultiMaterials[k][j] == null || trailMultiMaterials[k][j].shader != trailMat.shader) {
                            trailMultiMaterials[k][j] = Instantiate(trailMat);
                            trailMultiMaterials[k][j].hideFlags = HideFlags.DontSave;
                        }
                        SetMaterialProperties(trailMultiMaterials[k][j], sharedMaterials[j]);
                        trailMultiMaterials[k][j].renderQueue = renderQueue + k + 1;
                    }
                }
            }
            else {
                for (int k = 0; k < trailMaterial.Length; k++) {
                    if (trailMaterial[k] == null || trailMaterial[k].shader != trailMat.shader) {
                        trailMaterial[k] = Instantiate(trailMat);
                        trailMaterial[k].hideFlags = HideFlags.DontSave;
                    }
                    SetMaterialProperties(trailMaterial[k], null);
                    trailMaterial[k].renderQueue = renderQueue + k + 1;
                }
            }
            trailClearMask.renderQueue = renderQueue + maxBatches + 1;
            trailClearMask.SetInt(ShaderParams.Cull, (int)cullMode);

            if (trail == null || trail.Length != stepsBufferSize) {
                trail = new SnapshotTransform[stepsBufferSize];
                for (int k = 0; k < trail.Length; k++) {
                    trail[k].time = float.MinValue;
                }
                trailIndex = -1;
            }
            if (sortIndices == null || sortIndices.Length != stepsBufferSize) {
                sortIndices = new SnapshotIndex[stepsBufferSize];
            }
            if (matrices == null || matrices.Length != MAX_BATCH_INSTANCES) {
                matrices = new Matrix4x4[MAX_BATCH_INSTANCES];
            }
            if (parentMatrices == null || parentMatrices.Length != MAX_BATCH_INSTANCES) {
                parentMatrices = new Matrix4x4[MAX_BATCH_INSTANCES];
            }
            if (colors == null || colors.Length != MAX_BATCH_INSTANCES) {
                colors = new Vector4[MAX_BATCH_INSTANCES];
            }
            if (subFrameKeys == null || subFrameKeys.Length != MAX_BATCH_INSTANCES) {
                subFrameKeys = new float[MAX_BATCH_INSTANCES];
            }
            if (rampStartPositions == null || rampStartPositions.Length != MAX_BATCH_INSTANCES) {
                rampStartPositions = new Vector4[MAX_BATCH_INSTANCES];
            }
            if (rampEndPositions == null || rampEndPositions.Length != MAX_BATCH_INSTANCES) {
                rampEndPositions = new Vector4[MAX_BATCH_INSTANCES];
            }

            StoreCurrentPositions();
        }

        /// <summary>
        /// Loads and applies a different profile
        /// </summary>
        public void SetProfile (TrailEffectProfile profile) {
            if (profile != null) {
                profile.Load(this);
            }
        }

        void SetMaterialProperties (Material trailMat, Material sourceMat) {
            trailMat.SetInt(ShaderParams.Cull, (int)cullMode);
            trailMat.SetFloat(ShaderParams.ZOffset, renderOrder == TrailRenderOrder.BeforeObject ? 0.001f : 0);
            trailMat.SetInt(ShaderParams.ZTest, renderOrder == TrailRenderOrder.AlwaysOnTop ? (int)UnityEngine.Rendering.CompareFunction.Always : (int)UnityEngine.Rendering.CompareFunction.LessEqual);

            switch (effect) {
                case TrailStyle.Color:
                    trailMat.SetTexture(ShaderParams.ColorRamp, colorRampTexture);
                    break;
                case TrailStyle.TextureStamp:
                    trailMat.renderQueue = renderQueue + 1;
                    trailMat.mainTexture = texture;
                    trailMat.SetFloat(ShaderParams.CutOff, textureCutOff);
                    trailMask.mainTexture = texture;
                    trailMask.SetFloat(ShaderParams.CutOff, textureCutOff);
                    trailMask.EnableKeyword(ShaderParams.SKW_ALPHACLIP);
                    break;
                case TrailStyle.Clone:
                    Material origMat = sourceMat != null ? sourceMat : theRenderer.sharedMaterial;
                    if (origMat != null) {
                        trailMat.mainTexture = origMat.mainTexture;
                        trailMat.mainTextureScale = origMat.mainTextureScale;
                        trailMat.mainTextureOffset = origMat.mainTextureOffset;
                        trailMat.SetFloat(ShaderParams.CutOff, textureCutOff);
                        if (origMat.HasProperty(ShaderParams.BaseColor)) {
                            trailMat.SetColor(ShaderParams.BaseColor, origMat.GetColor(ShaderParams.BaseColor));
                        }
                        if (textureCutOff > 0) {
                            trailMat.EnableKeyword(ShaderParams.SKW_ALPHACLIP);
                        }
                        else {
                            trailMat.DisableKeyword(ShaderParams.SKW_ALPHACLIP);
                        }
                    }
                    break;
                case TrailStyle.Outline:
                    if (outlineMethod == OutlineMethod.Rim) {
                        trailMat.SetFloat(ShaderParams.RimPower, rimPower);
                        trailMat.EnableKeyword(ShaderParams.SKW_RIM);
                    }
                    else {
                        trailMat.SetFloat(ShaderParams.NormalThreshold, normalThreshold);
                        trailMat.DisableKeyword(ShaderParams.SKW_RIM);
                    }
                    bool hasChildren = include != null && include.Length > 0;
                    trailMat.SetInt(ShaderParams.ZWrite, hasChildren ? 1 : 0);
                    break;
                case TrailStyle.SpaceDistortion:
                    trailMat.SetColor(ShaderParams.AdditiveTint, trailTint);
                    break;
                case TrailStyle.Dash:
                    trailMat.SetVector(ShaderParams.LaserData, new Vector3(laserBandWidth, laserIntensity, laserFlash));
                    break;
            }
            if (mask != null) {
                trailMat.SetTexture(ShaderParams.MaskTex, mask);
                trailMat.EnableKeyword(ShaderParams.SKW_MASK);
            }
            else {
                trailMat.DisableKeyword(ShaderParams.SKW_MASK);
            }
            if (interpolating) {
                trailMat.EnableKeyword(ShaderParams.SKW_INTERPOLATE);
            }
            else {
                trailMat.DisableKeyword(ShaderParams.SKW_INTERPOLATE);
            }
            if (usingColorRamp) {
                trailMat.EnableKeyword(ShaderParams.SKW_COLOR_RAMP);
            }
            else {
                trailMat.DisableKeyword(ShaderParams.SKW_COLOR_RAMP);
            }
            hasParent = parent != null;
            if (hasParent) {
                trailMat.EnableKeyword(ShaderParams.SKW_LOCAL);
            }
            else {
                trailMat.DisableKeyword(ShaderParams.SKW_LOCAL);
            }
        }

        Material GetEffectMaterial (string materialName) {
            if (effectMaterials == null) {
                effectMaterials = new Dictionary<string, Material>();
            }
            Material mat;
            if (!effectMaterials.TryGetValue(materialName, out mat)) {
                mat = Resources.Load<Material>("TrailsFX/" + materialName);
                if (mat == null) {
                    Debug.LogError("Could not find trail material " + materialName);
                    return null;
                }
                mat = Instantiate(mat);
                mat.hideFlags = HideFlags.DontSave;
                effectMaterials[materialName] = mat;
            }
            return mat;
        }


        Mesh BuildQuadMesh () {
            Mesh mesh = new Mesh();
            mesh.name = "TrailQuadMesh";

            // Setup vertices
            Vector3[] newVertices = new Vector3[4];
            float halfHeight = 0.5f;
            float halfWidth = 0.5f;
            newVertices[0] = new Vector3(-halfWidth, -halfHeight, 0);
            newVertices[1] = new Vector3(-halfWidth, halfHeight, 0);
            newVertices[2] = new Vector3(halfWidth, -halfHeight, 0);
            newVertices[3] = new Vector3(halfWidth, halfHeight, 0);

            // Setup UVs
            Vector2[] newUVs = new Vector2[newVertices.Length];
            newUVs[0] = new Vector2(0, 0);
            newUVs[1] = new Vector2(0, 1);
            newUVs[2] = new Vector2(1, 0);
            newUVs[3] = new Vector2(1, 1);

            // Setup triangles
            int[] newTriangles = new int[] { 0, 1, 2, 3, 2, 1 };

            // Setup normals
            Vector3[] newNormals = new Vector3[newVertices.Length];
            for (int i = 0; i < newNormals.Length; i++) {
                newNormals[i] = Vector3.forward;
            }

            // Create quad
            mesh.vertices = newVertices;
            mesh.uv = newUVs;
            mesh.triangles = newTriangles;
            mesh.normals = newNormals;

            mesh.RecalculateBounds();

            return mesh;
        }


        Vector3 GetSnapshotScale () {
            return GetSnapshotScale(false);
        }
        
        Vector3 GetSnapshotScale (bool forCombinedMesh) {
            bool skipObjectScale = isSkinned || isParticle || forCombinedMesh;
            Vector3 objectScale = (ignoreTransformScale || skipObjectScale) ? Vector3.one : target.lossyScale;
            if (scaleUniform) {
                float t = UnityEngine.Random.Range(scaleStartRandomMin.x, scaleStartRandomMax.x);
                return new Vector3(objectScale.x * scale.x * t, objectScale.y * scale.y * t, objectScale.z * scale.z * t);
            }
            else {
                return new Vector3(objectScale.x * scale.x * UnityEngine.Random.Range(scaleStartRandomMin.x, scaleStartRandomMax.x),
                    objectScale.y * scale.y * UnityEngine.Random.Range(scaleStartRandomMin.y, scaleStartRandomMax.y),
                    objectScale.z * scale.z * UnityEngine.Random.Range(scaleStartRandomMin.z, scaleStartRandomMax.z));
            }
        }

        Quaternion GetRotation () {
            if (isParticle) return Quaternion.identity;
            Quaternion rot = target.rotation;
            return rot;
        }


        Vector3 GetRandomizedPosition () {
            Vector3 localPos = new Vector3(UnityEngine.Random.Range(localPositionRandomMin.x, localPositionRandomMax.x),
                                            UnityEngine.Random.Range(localPositionRandomMin.y, localPositionRandomMax.y),
                                            UnityEngine.Random.Range(localPositionRandomMin.z, localPositionRandomMax.z));
            Vector3 wpos = target.position;
            Vector3 pos;
            if (lastPosition == wpos) {
                pos = localPos + wpos;
            }
            else {
                pos = (Quaternion.LookRotation(wpos - lastPosition) * localPos) + wpos;
            }
            if (ground) {
                Ray ray = new Ray(target.position, Vector3.down);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit)) {
                    pos = hit.point + pos - target.position;
                    groundNormal = hit.normal;
                }
            }
            else {

                if (effect == TrailStyle.TextureStamp) {
                    pos += theRenderer.bounds.center - target.position;
                }
            }

            return pos;
        }

        Color GetSnapshotColor () {
            Color snapshotColor;
            if (effect == TrailStyle.SpaceDistortion) {
                Vector2 scrPos0 = cam.WorldToViewportPoint(target.position);
                Vector2 scrPos1 = cam.WorldToViewportPoint(lastPosition);
                Vector2 diff = (scrPos0 - scrPos1).normalized;
                diff.x += 0.5f;
                diff.y += 0.5f;
                snapshotColor.r = diff.x;
                snapshotColor.g = diff.y;
                snapshotColor.b = 0;
                snapshotColor.a = 1f;
            }
            else {
                switch (colorSequence) {
                    case ColorSequence.Random:
                        snapshotColor = bakedColorStartPalette[UnityEngine.Random.Range(0, BAKED_GRADIENTS_LENGTH)];
                        break;
                    case ColorSequence.FixedRandom:
                        snapshotColor = colorRandomAtStart;
                        break;
                    case ColorSequence.Cycle: {
                            if (colorCycleDuration <= 0) {
                                colorCycleDuration = 0.01f;
                            }
                            float t = (Time.time - startTime) / colorCycleDuration;
                            if (t > 1f && !colorCycleLoop) {
                                return colorTransparent;
                            }
                            int it = (int)((t - (int)t) * BAKED_GRADIENTS_LENGTH);
                            snapshotColor = bakedColorStartPalette[it];
                        }
                        break;
                    case ColorSequence.PingPong: {
                            float t = Mathf.PingPong((Time.time - startTime) * pingPongSpeed, 0.999f);
                            int it = (int)(t * BAKED_GRADIENTS_LENGTH);
                            snapshotColor = bakedColorStartPalette[it];
                        }
                        break;
                    default:
                        snapshotColor = color;
                        break;
                }
            }
            if (cameraDistanceFade) {
                snapshotColor.a *= ComputeCameraDistanceFade(target.position, cam.transform);
            }
            return snapshotColor;
        }

        float ComputeCameraDistanceFade (Vector3 position, Transform cameraTransform) {
            Vector3 heading = position - cameraTransform.position;
            float distance = Vector3.Dot(heading, cameraTransform.forward);
            if (distance < cameraDistanceFadeNear) {
                return 1f - Mathf.Min(1f, cameraDistanceFadeNear - distance);
            }
            if (distance > cameraDistanceFadeFar) {
                return 1f - Mathf.Min(1f, distance - cameraDistanceFadeFar);
            }
            return 1f;
        }

        void StoreCurrentPositions () {
            if (executeInEditMode || Application.isPlaying) {
                Bounds bounds = theRenderer.bounds;
                lastCornerMinPos = bounds.min;
                lastCornerMaxPos = bounds.max;
                lastPosition = target.position;
                lastRelativePosition = lastPosition;
                if (worldPositionRelativeOption == PositionChangeRelative.OtherGameObject && worldPositionRelativeTransform != null) {
                    lastRelativePosition -= worldPositionRelativeTransform.position;

                }
                lastRotation = GetRotation();
                rampLocalLastStart = rampLocalCurrentStart;
                rampLocalLastEnd = rampLocalCurrentEnd;
                if (parent != null) {
                    lastParentPosition = parent.position;
                    lastParentRotation = parent.rotation;
                }

            }
        }


        bool AddSnapshot () {
            if (!_active || (!theRenderer.enabled && !ignoreVisibility)) {
                wasInactive = true;
                return false;
            }

            if (wasInactive) {
                wasInactive = false;
                lastRandomizedPosition = GetRandomizedPosition();
                StoreCurrentPositions();
            }

            bool skip = Time.frameCount - startFrameCount < ignoreFrames || Time.timeScale == 0;
            if (isLimitedToAnimationStates && !skip) {
                skip = true;
                int layersCount = animator.layerCount;
                int stateHashesLength = stateHashes.Length;
                float windowStart = Mathf.Clamp01(animationStateNormalizedStart);
                float windowEnd = Mathf.Clamp01(animationStateNormalizedEnd);
                for (int l = 0; l < layersCount && skip; l++) {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(l);
                    int shortNameHash = stateInfo.shortNameHash;
                    for (int k = 0; k < stateHashesLength; k++) {
                        if (stateHashes[k].hash != shortNameHash) continue;
                        // normalized playback window check (loops via Repeat)
                        float nt = Mathf.Repeat(stateInfo.normalizedTime, 1f);
                        if (nt < windowStart || nt > windowEnd) continue;
                        // check animation interval constraint
                        if (stateHashes[k].endTime == 0) { // no constraint
                            skip = false;
                            break;
                        }
                        float animTime = stateInfo.normalizedTime * stateInfo.length;
                        if (animTime < stateHashes[k].startTime || animTime > stateHashes[k].endTime) continue;
                        skip = false;
                        break;
                    }
                }
            }
            if (skip) {
                prevBakedMeshVertices.Clear();
                lastRandomizedPosition = GetRandomizedPosition();
                StoreCurrentPositions();
                return false;
            }

            float now = Time.time;

            int steps = continuous ? maxStepsPerFrame : 0;
            if (steps == 0) {
                if (checkWorldPosition) {
                    Vector3 referencePosition = target.position;
                    Vector3 referenceLastPos = lastPosition;
                    if (worldPositionRelativeOption == PositionChangeRelative.OtherGameObject && worldPositionRelativeTransform != null) {
                        referencePosition -= worldPositionRelativeTransform.position;
                        referenceLastPos = lastRelativePosition;
                    }
                    float distance = Vector3.Distance(referencePosition, referenceLastPos);
                    if (distance >= minDistance) {
                        if (smooth) {
                            smoothDuration = now + 1f;
                        }
                        else {
                            steps = (int)(distance / minDistance);
                        }
                    }
                }

                if (checkScreenPosition) {
                    if (minPixelDistance <= 0) {
                        minPixelDistance = 1;
                    }

                    // Difference of corners in viewport from last frame
                    Vector2 viewportPos0 = cam.WorldToViewportPoint(lastCornerMinPos);
                    Vector2 viewportPos1 = cam.WorldToViewportPoint(theRenderer.bounds.min);
                    int pixelDistance = Mathf.Max(Mathf.CeilToInt(Mathf.Abs(viewportPos1.x - viewportPos0.x) * cam.pixelWidth), Mathf.CeilToInt(Mathf.Abs(viewportPos1.y - viewportPos0.y) * cam.pixelHeight));
                    int stepsCornerMin = pixelDistance / minPixelDistance;

                    viewportPos0 = cam.WorldToViewportPoint(lastCornerMaxPos);
                    viewportPos1 = cam.WorldToViewportPoint(theRenderer.bounds.max);
                    pixelDistance = Mathf.Max((int)(Mathf.Abs(viewportPos1.x - viewportPos0.x) * cam.pixelWidth), (int)(Mathf.Abs(viewportPos1.y - viewportPos0.y) * cam.pixelHeight));
                    if (pixelDistance >= minPixelDistance) {
                        if (smooth) {
                            smoothDuration = now + 1f;
                        }
                        else {
                            int stepsCornerMax = pixelDistance / minPixelDistance;
                            steps = Mathf.Max(steps, Mathf.Max(stepsCornerMax, stepsCornerMin));
                        }
                    }
                }

                if (checkTime) {
                    if (now - lastIntervalTimeCheck >= timeInterval) {
                        lastIntervalTimeCheck = now;
                        steps = Mathf.Max(1, steps);
                    }
                }
            }

            if (now < smoothDuration) {
                steps = maxStepsPerFrame;
            }

            if (steps <= 0)
                return false;

            Color color = GetSnapshotColor();
            if (color.a == 0) return false;

            if (steps > maxStepsPerFrame) {
                steps = maxStepsPerFrame;
            }

            bool usingCombinedMesh = combineMeshesWithChildren && combinedMeshPool != null && childrenTrailData != null && childrenTrailData.Count > 0;
            if (usingCombinedMesh) {
                BakeCombinedMesh();
            } else {
                SetupMesh(true);
            }
            
            Vector3 snapshotScale = GetSnapshotScale(usingCombinedMesh);

            Vector3 pos = GetRandomizedPosition();
            Vector3 targetPos = Vector3.zero;
            Vector3 upwards = Vector3.up;
            if (ground && orientToSurface) {
                targetPos = pos + groundNormal;
                if (target.position != lastPosition) {
                    upwards = target.position - lastPosition;
                }
                else {
                    upwards = target.forward;
                }
            }
            else if (orient) {
                if (lookTarget != null) {
                    targetPos = lookTarget.position;
                }
                else {
                    Camera camera = cam;
                    if (camera == null) {
                        camera = Camera.main;
                    }
                    if (camera != null) {
                        targetPos = camera.transform.position;
                    }
                    else {
                        orient = false;
                    }
                }
            }

            float lastFrameTime = now - Time.deltaTime;
            Quaternion rotation = GetRotation();
            bool hasParent = parent != null;
            if (hasParent) {
                parentPosition = parent.position;
                parentRotation = parent.rotation;
            }

            if (usingColorRamp) {
                // Convert positions to local space, interpolate, then back to world space
                rampLocalCurrentStart = target.InverseTransformPoint(colorRampStart.position);
                rampLocalCurrentEnd = target.InverseTransformPoint(colorRampEnd.position);
            }


            for (int k = 0; k < steps; k++) {
                trailIndex++;
                if (trailIndex >= trail.Length) {
                    trailIndex = 0;
                }
                float t = (k + 1f) / steps;
                Vector3 p = Vector3.Lerp(lastRandomizedPosition, pos, t);

                if (orient) {
                    trail[trailIndex].matrix = Matrix4x4.TRS(p, Quaternion.LookRotation(p - targetPos, upwards), snapshotScale);
                }
                else {
                    Quaternion rot = Quaternion.SlerpUnclamped(lastRotation, rotation, t);
                    trail[trailIndex].matrix = Matrix4x4.TRS(p, rot, snapshotScale);
                    if (hasParent) {
                        Vector3 ppos = Vector3.Lerp(lastParentPosition, parentPosition, t);
                        Quaternion prot = Quaternion.SlerpUnclamped(lastParentRotation, parentRotation, t);
                        trail[trailIndex].parentMatrix = Matrix4x4.TRS(ppos, prot, parent.localScale).inverse;
                    }
                }

                trail[trailIndex].time = (lastFrameTime * (1f - t)) + now * t;
                trail[trailIndex].meshIndex = usingCombinedMesh ? combinedMeshPoolIndex : meshPoolIndex;
                trail[trailIndex].color = color;
                if (usingColorRamp) {
                    Vector3 localLerpedStart = Vector3.SlerpUnclamped(rampLocalLastStart, rampLocalCurrentStart, t);
                    Vector3 localLerpedEnd = Vector3.SlerpUnclamped(rampLocalLastEnd, rampLocalCurrentEnd, t);
                    trail[trailIndex].rampStartPos = localLerpedStart;
                    trail[trailIndex].rampEndPos = localLerpedEnd;
                }
            }

            lastRandomizedPosition = pos;
            StoreCurrentPositions();
            
            return true;
        }

        void AddSnapshot (Vector3 pos, Quaternion rotation) {
            if (!_active || (!theRenderer.enabled && !ignoreVisibility))
                return;

            Color color = GetSnapshotColor();
            if (color.a == 0) return;

            bool usingCombinedMesh = combineMeshesWithChildren && combinedMeshPool != null && childrenTrailData != null && childrenTrailData.Count > 0;
            if (usingCombinedMesh) {
                BakeCombinedMesh();
            } else {
                SetupMesh(true);
            }

            Vector3 snapshotScale = GetSnapshotScale(usingCombinedMesh);
            trailIndex++;
            if (trailIndex >= trail.Length) {
                trailIndex = 0;
            }

            trail[trailIndex].matrix = Matrix4x4.TRS(pos, rotation, snapshotScale);
            trail[trailIndex].time = Time.time;
            trail[trailIndex].meshIndex = usingCombinedMesh ? combinedMeshPoolIndex : meshPoolIndex;
            trail[trailIndex].color = color;
        }

        int CollectValidTrailEntries(SnapshotTransform[] trailData) {
            if (duration < 0) duration = 0.001f;
            int count = 0;
            float now = Time.time;
            int trailLength = trailData.Length;
            int sortIndicesLength = sortIndices.Length;
            for (int i = 0; i < trailLength; i++) {
                float t = now - trailData[i].time;
                if (t < duration && t >= 0) {
                    sortIndices[count].t = t / duration;
                    sortIndices[count].index = i;
                    count++;
                    if (count >= sortIndicesLength) break;
                }
            }
            if (count > 0) QuickSort(0, count - 1);
            return count;
        }

        void RenderTrail () {
            int count = CollectValidTrailEntries(trail);
            if (count == 0) return;

            batchNumber = 0;
            bool singleBatch = (useLastAnimationState || effect == TrailStyle.TextureStamp) && !isParticle;
            if (singleBatch && count <= MAX_BATCH_INSTANCES) {
                SendToGPU(meshPoolIndex, 0, count);
            }
            else {
                int batchMeshIndex = trail[sortIndices[0].index].meshIndex;
                int batchStartIndex = 0;
                int batchInstancesCount = 1;

                for (int k = 1; k < count; k++) {
                    int i = sortIndices[k].index;
                    int meshIndex = trail[i].meshIndex;
                    if (meshIndex != batchMeshIndex || batchInstancesCount >= MAX_BATCH_INSTANCES) {
                        // send previous batch
                        SendToGPU(batchMeshIndex, batchStartIndex, batchInstancesCount);
                        // prepare new batch
                        batchMeshIndex = meshIndex;
                        batchStartIndex += batchInstancesCount;
                        batchInstancesCount = 0;
                    }
                    batchInstancesCount++;
                }
                if (batchInstancesCount > 0) {
                    // send last batch
                    SendToGPU(batchMeshIndex, batchStartIndex, batchInstancesCount);
                }
            }
        }

        void SendToGPU (int meshIndex, int startIndex, int count) {
            SendToGPU(meshIndex, startIndex, count, true);
        }
        
        void SendToGPU (int meshIndex, int startIndex, int count, bool renderMasks) {
            if (meshIndex < 0 || meshIndex >= meshPool.Length)
                return;

            Mesh batchMesh = effect == TrailStyle.TextureStamp ? quadMesh : meshPool[meshIndex];
            if (batchMesh == null)
                return;

            int layer = target.gameObject.layer;

            // When using children mode, masks are rendered separately in RenderAllTrails
            if (renderMasks && renderOrder == TrailRenderOrder.DrawBehind && batchNumber == 0 && (theRenderer.isVisible || ignoreVisibility)) {
                Vector3 pos = target.position;
                Vector3 sca;
                Mesh mesh;
                if (isSkinned || isParticle) {
                    mesh = SetupMesh(false);
                    sca = Vector3.one;
                }
                else {
                    mesh = meshPool[meshIndex];
                    sca = target.lossyScale;
                }
                if (mesh != null) {
                    Matrix4x4 m = Matrix4x4.TRS(pos, GetRotation(), sca);
                    if (subMeshMask != 0) {
                        int subMeshCount = mesh.subMeshCount;
                        for (int k = 0; k < subMeshCount; k++) {
                            if (((1 << k) & subMeshMask) != 0) {
                                Graphics.DrawMesh(mesh, m, trailMask, layer, null, k);
                                if (clearStencil) {
                                    Graphics.DrawMesh(mesh, m, trailClearMask, layer, null, k);
                                }
                            }
                        }
                    }
                    if (hierarchyOccluder) {
                        DrawHierarchyOccluders();
                    }
                }
            }

            // Pack for instancing
            for (int o = 0; o < count; o++, startIndex++) {
                int index = sortIndices[startIndex].index;
                float t = sortIndices[startIndex].t;
                if (t < 0) t = 0;
                int it = (int)(BAKED_GRADIENTS_LENGTH * t) % BAKED_GRADIENTS_LENGTH;

                // Assign RGBA
                Color baseColor = trail[index].color;
                Color color = bakedColorOverTime[it];
                colors[o].x = color.r * baseColor.r;
                colors[o].y = color.g * baseColor.g;
                colors[o].z = color.b * baseColor.b;
                colors[o].w = color.a * baseColor.a;
                if (fadeOut) colors[o].w *= 1f - t;

                // Pass subframe key
                subFrameKeys[o] = (float)o / count;

                // Set matrix
                float scale = bakedScaleOverTime[it];
                matrices[o].m00 = trail[index].matrix.m00 * scale;
                matrices[o].m01 = trail[index].matrix.m01 * scale;
                matrices[o].m02 = trail[index].matrix.m02 * scale;
                matrices[o].m03 = trail[index].matrix.m03;
                matrices[o].m10 = trail[index].matrix.m10 * scale;
                matrices[o].m11 = trail[index].matrix.m11 * scale;
                matrices[o].m12 = trail[index].matrix.m12 * scale;
                matrices[o].m13 = trail[index].matrix.m13;
                matrices[o].m20 = trail[index].matrix.m20 * scale;
                matrices[o].m21 = trail[index].matrix.m21 * scale;
                matrices[o].m22 = trail[index].matrix.m22 * scale;
                matrices[o].m23 = trail[index].matrix.m23;
                matrices[o].m30 = trail[index].matrix.m30;
                matrices[o].m31 = trail[index].matrix.m31;
                matrices[o].m32 = trail[index].matrix.m32;
                matrices[o].m33 = trail[index].matrix.m33;

                // Color ramp positions
                if (usingColorRamp) {
                    rampStartPositions[o].x = trail[index].rampStartPos.x;
                    rampStartPositions[o].y = trail[index].rampStartPos.y;
                    rampStartPositions[o].z = trail[index].rampStartPos.z;
                    rampEndPositions[o].x = trail[index].rampEndPos.x;
                    rampEndPositions[o].y = trail[index].rampEndPos.y;
                    rampEndPositions[o].z = trail[index].rampEndPos.z;
                }

                if (hasParent) {
                    parentMatrices[o].m00 = trail[index].parentMatrix.m00;
                    parentMatrices[o].m01 = trail[index].parentMatrix.m01;
                    parentMatrices[o].m02 = trail[index].parentMatrix.m02;
                    parentMatrices[o].m03 = trail[index].parentMatrix.m03;
                    parentMatrices[o].m10 = trail[index].parentMatrix.m10;
                    parentMatrices[o].m11 = trail[index].parentMatrix.m11;
                    parentMatrices[o].m12 = trail[index].parentMatrix.m12;
                    parentMatrices[o].m13 = trail[index].parentMatrix.m13;
                    parentMatrices[o].m20 = trail[index].parentMatrix.m20;
                    parentMatrices[o].m21 = trail[index].parentMatrix.m21;
                    parentMatrices[o].m22 = trail[index].parentMatrix.m22;
                    parentMatrices[o].m23 = trail[index].parentMatrix.m23;
                    parentMatrices[o].m30 = trail[index].parentMatrix.m30;
                    parentMatrices[o].m31 = trail[index].parentMatrix.m31;
                    parentMatrices[o].m32 = trail[index].parentMatrix.m32;
                    parentMatrices[o].m33 = trail[index].parentMatrix.m33;
                }
            }

            // Send batch to pipeline
            properties.SetVectorArray(ShaderParams.ColorArray, colors);
            if (interpolating || usingColorRamp) {
                properties.SetFloatArray(ShaderParams.SubFrameKeys, subFrameKeys);
            }
            if (usingColorRamp) {
                properties.SetVectorArray(ShaderParams.RampStartPositions, rampStartPositions);
                properties.SetVectorArray(ShaderParams.RampEndPositions, rampEndPositions);
            }
            if (hasParent) {
                properties.SetMatrixArray(ShaderParams.ParentMatricesArray, parentMatrices);
                properties.SetMatrix(ShaderParams.PivotMatrix, parent.localToWorldMatrix);
            }
            if (batchNumber < trailMaterial.Length - 1) {
                batchNumber++;
            }
            else return;

            int batchMeshSubMeshCount = batchMesh.subMeshCount;
            if (supportsGPUInstancing) {
                for (int s = 0; s < batchMeshSubMeshCount; s++) {
                    if (((1 << s) & subMeshMask) != 0) {
                        Material mat = (effect == TrailStyle.Clone && preserveMultiMaterials && trailMultiMaterials != null && batchNumber < trailMultiMaterials.Length && s < trailMultiMaterials[batchNumber].Length) ? trailMultiMaterials[batchNumber][s] : trailMaterial[batchNumber];
                        mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                        Graphics.DrawMeshInstanced(batchMesh, s, mat, matrices, count, properties, UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
                    }
                }
                if (clearStencil) {
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        if (((1 << s) & subMeshMask) != 0) {
                            Graphics.DrawMeshInstanced(batchMesh, s, trailClearMask, matrices, count, null, UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
                        }
                    }
                }
            }
            else {
                // Fallback for GPUs not supporting instancing; better than nothing :(
                for (int i = 0; i < count; i++) {
                    propertyBlock.SetVector(ShaderParams.ColorArray, colors[i]);
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        if (((1 << s) & subMeshMask) != 0) {
                            Material mat = (effect == TrailStyle.Clone && preserveMultiMaterials && trailMultiMaterials != null && batchNumber < trailMultiMaterials.Length && s < trailMultiMaterials[batchNumber].Length) ? trailMultiMaterials[batchNumber][s] : trailMaterial[batchNumber];
                            mat.renderQueue = renderQueue + batchNumber + 1 + currentRenderQueueOffset;
                            Graphics.DrawMesh(batchMesh, matrices[i], mat, layer, null, s, propertyBlock, false, false);
                        }
                    }
                }
                if (clearStencil) {
                    for (int s = 0; s < batchMeshSubMeshCount; s++) {
                        if (((1 << s) & subMeshMask) != 0) {
                            for (int i = 0; i < count; i++) {
                                Graphics.DrawMesh(batchMesh, matrices[i], trailClearMask, layer, null, s, null, false, false);
                            }
                        }
                    }
                }
            }
        }


        Mesh SetupMesh (bool bakeMeshNow) {
            if (bakeMeshNow) {
                if (isSkinned || isParticle) {
                    int thisFrame = Time.frameCount;
                    if (thisFrame != bakeTime) {
                        bakeTime = thisFrame;
                        int prevMeshPoolIndex = meshPoolIndex;
                        meshPoolIndex++;
                        if (meshPoolIndex >= meshPool.Length) {
                            meshPoolIndex = 0;
                        }
                        try {
                            if (isSkinned) {
                                skinnedMeshRenderer.BakeMesh(meshPool[meshPoolIndex]);
                            }
                            else {
#if UNITY_2022_3_OR_NEWER

                            particleRenderer.BakeMesh(meshPool[meshPoolIndex], ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
#else
                                particleRenderer.BakeMesh(meshPool[meshPoolIndex], true);
#endif
                            }
                        }
                        catch {
                            meshPoolIndex = prevMeshPoolIndex;
                            return meshPool[meshPoolIndex];
                        }
                        if (interpolating) {
                            int prevVertexCount = prevBakedMeshVertices.Count;
                            if (prevVertexCount > 0 && meshPool[meshPoolIndex].vertexCount == prevVertexCount) {
                                meshPool[meshPoolIndex].SetUVs(1, prevBakedMeshVertices);
                                meshPool[meshPoolIndex].GetVertices(prevBakedMeshVertices);
                            }
                            else {
                                meshPool[meshPoolIndex].GetVertices(prevBakedMeshVertices);
                                meshPool[meshPoolIndex].SetUVs(1, prevBakedMeshVertices);
                            }
                        }
                    }
                }
            }
            return meshPool[meshPoolIndex];
        }
        
        void SetupCombinedMeshPool() {
            bool needsCombinedPool = combineMeshesWithChildren && include != null && include.Length > 0;
            if (!needsCombinedPool) {
                combinedMeshPool = null;
                return;
            }
            
            int poolSize = useLastAnimationState ? 1 : meshPoolSize;
            if (combinedMeshPool == null || combinedMeshPool.Length != poolSize) {
                combinedMeshPool = new Mesh[poolSize];
                for (int k = 0; k < poolSize; k++) {
                    combinedMeshPool[k] = new Mesh();
                    combinedMeshPool[k].hideFlags = HideFlags.DontSave;
                    combinedMeshPool[k].MarkDynamic();
                }
            }
            
            if (combineInstances == null) {
                combineInstances = new List<CombineInstance>();
            }
            
            if (combinedMaskMesh == null) {
                combinedMaskMesh = new Mesh();
                combinedMaskMesh.hideFlags = HideFlags.DontSave;
                combinedMaskMesh.MarkDynamic();
            }
        }
        
        Mesh BakeCombinedMesh() {
            if (combinedMeshPool == null || combinedMeshPool.Length == 0) return meshPool[meshPoolIndex];
            
            int thisFrame = Time.frameCount;
            if (thisFrame == combinedMeshBakeTime) {
                return combinedMeshPool[combinedMeshPoolIndex];
            }
            combinedMeshBakeTime = thisFrame;
            
            combinedMeshPoolIndex++;
            if (combinedMeshPoolIndex >= combinedMeshPool.Length) {
                combinedMeshPoolIndex = 0;
            }
            
            BakeCombinedMeshInto(combinedMeshPool[combinedMeshPoolIndex]);
            
            return combinedMeshPool[combinedMeshPoolIndex];
        }
        
        Mesh GetCombinedMaskMesh() {
            if (combinedMaskMesh == null) return null;
            
            int thisFrame = Time.frameCount;
            if (thisFrame == combinedMaskBakeTime) {
                return combinedMaskMesh;
            }
            combinedMaskBakeTime = thisFrame;
            
            BakeCombinedMeshInto(combinedMaskMesh);
            
            return combinedMaskMesh;
        }
        
        void BakeCombinedMeshInto(Mesh targetMesh) {
            combineInstances.Clear();
            
            int thisFrame = Time.frameCount;
            Vector3 mainPos = target.position;
            Quaternion mainRotInv = Quaternion.Inverse(target.rotation);
            
            Mesh mainMesh;
            Matrix4x4 mainTransform;
            if (isSkinned || isParticle) {
                SetupMesh(true);
                mainMesh = meshPool[meshPoolIndex];
                mainTransform = Matrix4x4.identity;
            } else {
                mainMesh = meshPool[0];
                mainTransform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, target.lossyScale);
            }
            
            if (mainMesh != null) {
                CombineInstance mainInstance = new CombineInstance {
                    mesh = mainMesh,
                    transform = mainTransform
                };
                combineInstances.Add(mainInstance);
            }
            
            if (childrenTrailData != null) {
                int childCount = childrenTrailData.Count;
                for (int c = 0; c < childCount; c++) {
                    ChildTrailData child = childrenTrailData[c];
                    if (child.renderer == null || !child.renderer.enabled) continue;
                    
                    Mesh childMesh = null;
                    Matrix4x4 childTransform;
                    
                    Vector3 relativePos = mainRotInv * (child.target.position - mainPos);
                    Quaternion relativeRot = mainRotInv * child.target.rotation;
                    
                    if (child.isSkinned && child.skinnedMeshRenderer != null) {
                        if (thisFrame != child.lastBakedFrame) {
                            child.lastBakedFrame = thisFrame;
                            child.meshPoolIndex++;
                            if (child.meshPoolIndex >= child.meshPool.Length) {
                                child.meshPoolIndex = 0;
                            }
                            if (child.meshPool[child.meshPoolIndex] == null) {
                                child.meshPool[child.meshPoolIndex] = new Mesh();
                                child.meshPool[child.meshPoolIndex].MarkDynamic();
                            }
                            child.skinnedMeshRenderer.BakeMesh(child.meshPool[child.meshPoolIndex]);
                        }
                        childMesh = child.meshPool[child.meshPoolIndex];
                        childTransform = Matrix4x4.TRS(relativePos, relativeRot, Vector3.one);
                    } else if (child.meshFilter != null) {
                        childMesh = child.meshFilter.sharedMesh;
                        childTransform = Matrix4x4.TRS(relativePos, relativeRot, child.target.lossyScale);
                    } else {
                        continue;
                    }
                    
                    if (childMesh == null) continue;
                    
                    CombineInstance childInstance = new CombineInstance {
                        mesh = childMesh,
                        transform = childTransform
                    };
                    combineInstances.Add(childInstance);
                }
            }
            
            if (combineInstances.Count > 0) {
                targetMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            }
        }

        void DrawHierarchyOccluders () {
            if (!EnsureHierarchyOccludersReady() || hierarchyOccluders == null) return;

            int count = hierarchyOccluders.Count;
            for (int i = 0; i < count; i++) {
                HierarchyOccluderEntry entry = hierarchyOccluders[i];
                Renderer entryRenderer = entry.renderer;
                if (entryRenderer == null) {
                    hierarchyOccludersDirty = true;
                    continue;
                }
                if (!entryRenderer.enabled && !hierarchyIncludeInactive) continue;
                Mesh entryMesh = entry.mesh;
                if (entryMesh == null) {
                    if (entry.skinned != null) {
                        entryMesh = new Mesh();
                        entryMesh.MarkDynamic();
                        entry.mesh = entryMesh;
                        entry.ownsMesh = true;
                    }
                    else {
                        hierarchyOccludersDirty = true;
                        continue;
                    }
                }
                if (entry.skinned != null) {
                    entry.skinned.BakeMesh(entryMesh);
                }
                if (entryMesh == null || entryMesh.vertexCount == 0) continue;
                Vector3 scale;
                    Transform t = entryRenderer.transform;
                if (entry.skinned != null) {
                    scale = Vector3.one;
                } else {
                    scale = t.lossyScale;
                }
                Matrix4x4 matrix = Matrix4x4.TRS(t.position, t.rotation, scale);
                int rendererLayer = entryRenderer.gameObject.layer;
                int subMeshCount = entryMesh.subMeshCount;
                if (subMeshCount == 0) subMeshCount = 1;
                for (int sm = 0; sm < subMeshCount; sm++) {
                    Graphics.DrawMesh(entryMesh, matrix, trailMask, rendererLayer, null, sm);
                }
            }
        }

        bool EnsureHierarchyOccludersReady () {
            if (!hierarchyOccluder) {
                if (hierarchyOccluderLastState) {
                    ClearHierarchyOccluders();
                    hierarchyOccluderCurrentRoot = null;
                }
                hierarchyOccluderLastState = false;
                return false;
            }

            if (!hierarchyOccluderLastState) {
                hierarchyOccludersDirty = true;
            }
            hierarchyOccluderLastState = true;

            Transform root = GetHierarchyOccluderRoot();
            if (root == null) {
                return false;
            }

            if (hierarchyOccluderCurrentRoot != root || hierarchyOccluderCurrentLayerMask != hierarchyOccluderLayerMask.value || hierarchyOccluderCurrentIncludeInactive != hierarchyIncludeInactive) {
                hierarchyOccludersDirty = true;
            }

            if (hierarchyOccluders == null) {
                hierarchyOccluders = new List<HierarchyOccluderEntry>();
            }

            if (!hierarchyOccludersDirty) {
                return hierarchyOccluders.Count > 0;
            }

            hierarchyOccludersDirty = false;
            hierarchyOccluderCurrentRoot = root;
            hierarchyOccluderCurrentLayerMask = hierarchyOccluderLayerMask.value;
            hierarchyOccluderCurrentIncludeInactive = hierarchyIncludeInactive;

            ClearHierarchyOccluders();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(hierarchyIncludeInactive);
            if (renderers == null || renderers.Length == 0) {
                return false;
            }

            int mask = hierarchyOccluderLayerMask.value;
            for (int k = 0; k < renderers.Length; k++) {
                Renderer occluderRenderer = renderers[k];
                if (occluderRenderer == null || occluderRenderer == theRenderer) continue;
                if (!occluderRenderer.enabled && !hierarchyIncludeInactive) continue;
                if (((1 << occluderRenderer.gameObject.layer) & mask) == 0) continue;

                SkinnedMeshRenderer skinned = occluderRenderer as SkinnedMeshRenderer;
                MeshRenderer meshRenderer = occluderRenderer as MeshRenderer;
                if (skinned == null && meshRenderer == null) continue;

                Mesh occluderMesh = null;
                bool ownsMesh = false;
                if (skinned != null) {
                    occluderMesh = new Mesh();
                    occluderMesh.MarkDynamic();
                    ownsMesh = true;
                }
                else {
                    MeshFilter mf = meshRenderer.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    occluderMesh = mf.sharedMesh;
                    if (occluderMesh == null) continue;
                }

                HierarchyOccluderEntry entry = new HierarchyOccluderEntry {
                    renderer = occluderRenderer,
                    mesh = occluderMesh,
                    skinned = skinned,
                    ownsMesh = ownsMesh
                };
                hierarchyOccluders.Add(entry);
            }

            return hierarchyOccluders.Count > 0;
        }

        Transform GetHierarchyOccluderRoot () {
            Transform root = hierarchyOccluderRoot != null ? hierarchyOccluderRoot : target;
            return root;
        }

        void ClearHierarchyOccluders () {
            if (hierarchyOccluders == null) return;
            for (int i = 0; i < hierarchyOccluders.Count; i++) {
                HierarchyOccluderEntry entry = hierarchyOccluders[i];
                if (entry != null && entry.ownsMesh && entry.mesh != null) {
                    DestroyHierarchyMesh(entry.mesh);
                }
            }
            hierarchyOccluders.Clear();
        }

        void DestroyHierarchyMesh (Mesh mesh) {
            if (mesh == null) return;
            if (Application.isPlaying) {
                Destroy(mesh);
            }
            else {
                DestroyImmediate(mesh);
            }
        }

        void QuickSort (int min, int max) {
            int i = min;
            int j = max;

            float x = sortIndices[(min + max) / 2].t;

            do {
                while (sortIndices[i].t < x) {
                    i++;
                }
                while (sortIndices[j].t > x) {
                    j--;
                }
                if (i <= j) {
                    SnapshotIndex h = sortIndices[i];
                    sortIndices[i] = sortIndices[j];
                    sortIndices[j] = h;
                    i++;
                    j--;
                }
            } while (i <= j);

            if (min < j) {
                QuickSort(min, j);
            }
            if (i < max) {
                QuickSort(i, max);
            }
        }


        /// <summary>
        /// Returns the position of a trail snapshot
        /// </summary>
        /// <param name="index">Index of the trail snapshot (0 to step buffer size defined in Trail Effect component)</param>
        /// <returns>Returns the world space position of the trail snapshot</returns>
        public Vector3 GetTrailPosition (int index) {
            return new Vector3(trail[index].matrix.m03, trail[index].matrix.m13, trail[index].matrix.m23);
        }
    }



}

