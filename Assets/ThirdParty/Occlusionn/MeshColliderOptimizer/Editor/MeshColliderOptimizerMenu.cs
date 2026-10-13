#if UNITY_EDITOR
using System;
using Occlusionn.MeshColliderOptimizer.Runtime;
using UnityEditor;

namespace Occlusionn.MeshColliderOptimizer.Editor
{
    /// <summary>
    /// Unity Editor menu shortcuts for MeshColliderOptimizer.
    /// Uses delayed execution to avoid SerializedObject/UI synchronization assertions.
    /// </summary>
    public static class MeshColliderOptimizerMenu
    {
        #region Optimize

        /// <summary>
        /// Optimizes selected objects only if they already have an optimizer (does not add components).
        /// The actual optimization is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Optimize Selected (Existing Only)")]
        public static void OptimizeSelectedExistingOnly()
        {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    var opt = MeshColliderOptimizerApi.GetOptimizer(go);
                    if (opt == null) continue;
                    MeshColliderOptimizerApi.Optimize(opt);
                }

                SceneView.RepaintAll();
            };
        }

        /// <summary>
        /// Adds optimizer to selected objects if missing, then triggers optimization.
        /// The actual work is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Add Optimizer + Optimize Selected")]
        public static void AddAndOptimizeSelected()
        {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    var opt = MeshColliderOptimizerApi.GetOrAddOptimizer(go);
                    if (opt == null) continue;
                    MeshColliderOptimizerApi.Optimize(opt);
                }

                SceneView.RepaintAll();
            };
        }

        /// <summary>
        /// Optimizes all optimizers in the scene (including inactive objects).
        /// Runs in a deferred editor callback and shows a cancelable progress bar.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Optimize All In Scene (Including Inactive)")]
        public static void OptimizeAllInSceneIncludingInactive()
        {
            EditorApplication.delayCall += () =>
            {
                var all = MeshColliderOptimizerApi.FindAllOptimizersInScene(includeInactive: true);
                if (all == null || all.Count == 0) return;

                try
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var opt = all[i];
                        if (opt == null) continue;

                        float p = all.Count <= 1 ? 1f : i / (float)(all.Count - 1);
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Mesh Collider Optimizer",
                                $"Optimizing: {opt.gameObject.name}",
                                p))
                        {
                            break;
                        }

                        MeshColliderOptimizerApi.Optimize(opt);
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    SceneView.RepaintAll();
                }
            };
        }

        #endregion

        #region Clear

        /// <summary>
        /// Clears generated colliders on selected objects (if they have an optimizer).
        /// The actual work is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Clear Generated On Selected")]
        public static void ClearGeneratedOnSelected()
        {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    var opt = MeshColliderOptimizerApi.GetOptimizer(go);
                    if (opt == null) continue;
                    MeshColliderOptimizerApi.ClearGenerated(opt);
                }

                SceneView.RepaintAll();
            };
        }

        #endregion

        #region Preset

        /// <summary>
        /// Prompts for an OptimizationPreset asset and applies it to selected objects, then optimizes.
        /// Application is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Apply Preset To Selected...")]
        public static void ApplyPresetToSelected()
        {
            var path = EditorUtility.OpenFilePanel("Select OptimizationPreset", "Assets", "asset");
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!path.Contains("/Assets/"))
                return;

            var assetPath = "Assets" + path.Split(new[] { "/Assets/" }, StringSplitOptions.None)[1];

            var preset = MeshColliderOptimizerApi.LoadPresetAtPath(assetPath);
            if (preset == null) return;

            EditorApplication.delayCall += () =>
            {
                MeshColliderOptimizerApi.ApplyPresetToSelection(preset, includeChildren: false, optimizeAfter: true);
                SceneView.RepaintAll();
            };
        }

        #endregion

        #region LOD

        /// <summary>
        /// Enables "Colliders Follow Active LOD" on selected objects and applies immediately.
        /// Execution is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/LOD/Enable Colliders Follow Active LOD")]
        public static void EnableCollidersFollowActiveLod() => SetLodFollowOnSelection(true);

        /// <summary>
        /// Disables "Colliders Follow Active LOD" on selected objects and applies immediately.
        /// Execution is deferred to the next editor tick.
        /// </summary>
        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/LOD/Disable Colliders Follow Active LOD")]
        public static void DisableCollidersFollowActiveLod() => SetLodFollowOnSelection(false);

        private static void SetLodFollowOnSelection(bool enabled)
        {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (var go in gos)
                {
                    if (go == null) continue;

                    var opt = MeshColliderOptimizerApi.GetOrAddOptimizer(go);
                    if (opt == null) continue;

                    MeshColliderOptimizerApi.SetCollidersFollowActiveLod(opt, enabled, applyNow: true);
                }

                SceneView.RepaintAll();
            };
        }

        #endregion
    }
}
#endif

