using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Serializable preset asset for MeshColliderOptimizer settings.
    /// </summary>

    [CreateAssetMenu(fileName = "New Optimization Preset", menuName = "Mesh Collider Optimizer/Preset")]
    public class OptimizationPreset : ScriptableObject
    {
        [Header("Apply Overrides")]
        [Tooltip("If enabled, applying this preset will override Convex / IsTrigger settings on the component.")]
        public bool overrideColliderSettings;

        [Tooltip("If enabled, applying this preset will override convex decomposition settings (grid / 256-guard).")]
        public bool overrideDecompositionSettings;

        [Header("Collider Settings")]
        public bool convex;
        public bool isTrigger;
        public int layerOverridePriority;
        public LayerMask includeLayers = ~0;
        public LayerMask excludeLayers;

        [Header("Convex Decomposition")]
        [Range(2, 10)] public int decompositionGrid = 2;
        public bool strict256Guard = true;

        [Header("Optimization & Shape")]
        [Range(0.001f, 1f)] public float quality = 0.5f;
        [Range(0.01f, 1f)] public float shapeFidelity = 0.5f;
        [Range(-0.2f, 0.2f)] public float inflation;

        [Header("Merge")]
        public bool mergeChildren;

        public static OptimizationPreset CreateHighQuality()
        {
            var p = CreateInstance<OptimizationPreset>();
            p.name = "High Quality";
            p.overrideDecompositionSettings = true;
            p.overrideColliderSettings = false;

            p.quality = 0.95f;
            p.shapeFidelity = 0.9f;
            p.inflation = 0f;
            p.decompositionGrid = 4;
            p.strict256Guard = true;
            return p;
        }

        public static OptimizationPreset CreateBalanced()
        {
            var p = CreateInstance<OptimizationPreset>();
            p.name = "Balanced";
            p.overrideDecompositionSettings = true;
            p.overrideColliderSettings = false;

            p.quality = 0.5f;
            p.shapeFidelity = 0.5f;
            p.inflation = 0f;
            p.decompositionGrid = 3;
            p.strict256Guard = true;
            return p;
        }

        public static OptimizationPreset CreatePerformance()
        {
            var p = CreateInstance<OptimizationPreset>();
            p.name = "Performance";
            p.overrideDecompositionSettings = true;
            p.overrideColliderSettings = false;

            p.quality = 0.15f;
            p.shapeFidelity = 0.3f;
            p.inflation = 0f;
            p.decompositionGrid = 2;
            p.strict256Guard = true;
            return p;
        }

        public static OptimizationPreset CreateMobile()
        {
            var p = CreateInstance<OptimizationPreset>();
            p.name = "Mobile";
            p.overrideDecompositionSettings = true;
            p.overrideColliderSettings = false;

            p.quality = 0.08f;
            p.shapeFidelity = 0.2f;
            p.inflation = 0.01f;
            p.decompositionGrid = 2;
            p.strict256Guard = true;
            return p;
        }
    }
}

