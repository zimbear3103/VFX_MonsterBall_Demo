using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Preset application, settings copy/reset helpers, and utility API.
    /// </summary>
    public partial class MeshColliderOptimizer
    {
#region Settings
        public void ApplyPreset(OptimizationPreset p)
        {
            if (p == null) return;


            if (p.overrideColliderSettings)
            {
                convex    = p.convex;
                isTrigger = p.isTrigger;
                layerOverridePriority = p.layerOverridePriority;
                includeLayers = p.includeLayers;
                excludeLayers = p.excludeLayers;
            }

            if (p.overrideDecompositionSettings)
            {
                decompositionGrid = p.decompositionGrid;
                strict256Guard     = p.strict256Guard;
            }

            quality                   = p.quality;
            shapeFidelity            = p.shapeFidelity;
            inflationAmount          = p.inflation;
            mergeChildObjects = p.mergeChildren;

            preset = p;
        }


        /// <summary>
        /// Saves current settings into a new <see cref="OptimizationPreset"/> instance.
        /// </summary>
        public OptimizationPreset SaveToPreset()
        {
            var p = ScriptableObject.CreateInstance<OptimizationPreset>();

            p.overrideColliderSettings      = true;
            p.overrideDecompositionSettings = true;

            p.convex            = convex;
            p.isTrigger         = isTrigger;
            p.layerOverridePriority = layerOverridePriority;
            p.includeLayers = includeLayers;
            p.excludeLayers = excludeLayers;
            p.decompositionGrid = decompositionGrid;
            p.strict256Guard    = strict256Guard;
            p.quality           = quality;
            p.shapeFidelity     = shapeFidelity;
            p.inflation         = inflationAmount;
            p.mergeChildren     = mergeChildObjects;
            return p;
        }

        /// <summary>
        /// Resets every setting to factory defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            autoUpdate                  = false;
            convex                      = false;
            isTrigger                   = false;
            material                    = null;
            layerOverridePriority       = 0;
            includeLayers               = ~0;
            excludeLayers               = 0;
            cookingOptions              = MeshColliderCookingOptions.EnableMeshCleaning |
                                          MeshColliderCookingOptions.WeldColocatedVertices |
                                          MeshColliderCookingOptions.CookForFasterSimulation;
            providesContacts            = false;
            externalTargetRenderer      = null;
            externalTargetMeshFilter    = null;
            externalSourceRoot          = null;
            capturePoseSnapshot             = false;
            trackBlendShapes           = false;
            enableCollidersOnlyOnActiveLod = false;
            decompositionGrid          = 2;
            quality                      = 0.5f;
            shapeFidelity               = 0.5f;
            inflationAmount             = 0f;
            strict256Guard              = true;
            mergeChildObjects    = false;
            showHeatmap                 = false;
            showGizmos                  = true;
            maxErrorTolerance                 = 0.1f;
            heatmapStride             = 10;
            drawErrorLines                   = true;
            animationMode               = false;
            animationSourceMode         = AnimationSourceMode.BakeMesh;
            animationUpdateMode     = AnimationUpdateMode.EveryFrame;
            animationEveryNFrames             = 2;
            animationMaxHz              = 30f;
            animationRootOnly = true;
            animationOnlyWhenVisible   = true;
            preset                      = null;
        }

        /// <summary>
        /// Destroys every generated collider and child object.
        /// </summary>
        public void RemoveAllGeneratedColliders()
        {
            DestroyConvexChildrenAndMeshes(true);
            ClearConvexHullGizmoCache();

            if (_targetMeshCollider != null)
            {
                DestroyObjSafe(_targetMeshCollider.gameObject);
                _targetMeshCollider = null;
            }

            if (generatedColliders != null) generatedColliders.Clear();
            FinalMesh = null;

            stat_VertsBefore    = 0;
            stat_VertsAfter     = 0;
            stat_TrisBefore     = 0;
            stat_TrisAfter      = 0;
            savingsRatio       = 0;
            stat_MemoryBefore   = "";
            stat_MemoryAfter    = "";
            stat_BakeTime       = "";
            stat_ConvexPartCount = 0;

        #if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
        #endif
        }


        /// <summary>
        /// Single-line summary of this component's current state.
        /// </summary>
        public string GetStatusSummary()
        {
            if (FinalMesh == null) return "Not optimized";
            string mode = convex ? $"Convex ({stat_ConvexPartCount} parts)" : "Concave";
            return $"{mode}  |  {stat_VertsAfter} verts  |  {savingsRatio * 100f:F0}% savings  |  {stat_BakeTime}";
        }

        /// <summary>
        /// Requests an automatic (debounced) optimization pass.
        /// In Edit Mode this keeps auto-update safety guards enabled.
        /// </summary>
        public void RequestAutoOptimization()
        {
            CancelActiveOptimization();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _pendingEditorAutoUpdateRequest = true;
                _editorAutoUpdateNotBefore = UnityEditor.EditorApplication.timeSinceStartup + EDITOR_AUTO_UPDATE_DEBOUNCE_SECONDS;
            }
            else
            {
                _pendingEditorAutoUpdateRequest = false;
            }
#else
            _pendingEditorAutoUpdateRequest = false;
#endif

            _needsUpdate = true;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
        }

        /// <summary>
        /// Requests an immediate/manual optimization pass.
        /// In Edit Mode this bypasses auto-update debounce/safety preview mode.
        /// </summary>
        public void RequestOptimization()
        {
            CancelActiveOptimization();
            _pendingEditorAutoUpdateRequest = false;
#if UNITY_EDITOR
            _editorAutoUpdateNotBefore = 0d;
#endif
            _needsUpdate = true;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
        }
#endregion
    }
}

