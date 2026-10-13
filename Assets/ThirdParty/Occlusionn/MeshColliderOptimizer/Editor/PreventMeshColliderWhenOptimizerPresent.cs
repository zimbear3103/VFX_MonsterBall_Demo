#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Optimizer = Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer;

namespace Occlusionn.MeshColliderOptimizer.Editor
{
    /// <summary>
    /// When a MeshCollider is added to a GameObject that already has MeshColliderOptimizer,
    /// shows a modal popup and resolves the conflict based on user choice.
    /// </summary>
    [InitializeOnLoad]
    public static class PreventMeshColliderWhenOptimizerPresent
    {
        private const string DialogTitle = "Mesh Collider Optimizer";
        private static bool _isHandling;

        static PreventMeshColliderWhenOptimizerPresent()
        {
            ObjectFactory.componentWasAdded -= OnComponentAdded;
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        private static void OnComponentAdded(Component component)
        {
            if (_isHandling) return;
            if (component is not MeshCollider mc) return;

            var go = mc.gameObject;
            if (go == null) return;

            var opt = go.GetComponent<Optimizer>();
            if (opt == null) return;

            // Defer to next editor tick to avoid SerializedObject/UI sync assertions.
            EditorApplication.delayCall += () =>
            {
                if (_isHandling) return;
                if (mc == null) return;

                var go2 = mc.gameObject;
                if (go2 == null) return;

                var opt2 = go2.GetComponent<Optimizer>();
                if (opt2 == null) return;

                _isHandling = true;
                try
                {
                    int choice = EditorUtility.DisplayDialogComplex(
                        DialogTitle,
                        "This GameObject already has MeshColliderOptimizer.\n\n" +
                        "MeshCollider cannot be used together with the optimizer on the same object.\n\n" +
                        "Choose what to keep:",
                        "Keep Optimizer",
                        "Add MeshCollider (Remove Optimizer)",
                        "Cancel"
                    );

                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Resolve MeshCollider / Optimizer conflict");

                    try
                    {
                        // 0 = Keep Optimizer, remove MeshCollider
                        // 1 = Keep MeshCollider, remove Optimizer
                        // 2 = Cancel, remove MeshCollider
                        if (choice == 1)
                        {
                            // Keep MeshCollider, remove optimizer
                            var existingOpt = go2.GetComponent<Optimizer>();
                            if (existingOpt != null)
                                Undo.DestroyObjectImmediate(existingOpt);
                        }
                        else
                        {
                            // Keep optimizer (or cancel), remove MeshCollider
                            if (mc != null)
                                Undo.DestroyObjectImmediate(mc);
                        }
                    }
                    finally
                    {
                        Undo.CollapseUndoOperations(group);
                        SceneView.RepaintAll();
                    }
                }
                finally
                {
                    _isHandling = false;
                }
            };
        }
    }
}
#endif

