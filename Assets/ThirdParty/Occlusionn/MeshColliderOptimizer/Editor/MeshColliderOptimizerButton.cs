#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Optimizer = Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer;

namespace Occlusionn.MeshColliderOptimizer.Editor
{
    /// <summary>
    /// Extends Unity's built-in MeshCollider inspector by adding a one-click conversion button
    /// to MeshColliderOptimizer, copying settings and removing the MeshCollider.
    /// </summary>
    [CustomEditor(typeof(MeshCollider))]
    [CanEditMultipleObjects]
    public sealed class MeshColliderOptimizerButton : UnityEditor.Editor
    {
        private UnityEditor.Editor m_BuiltinInspectorEditor;
        private static Type s_BuiltinEditorType;

        private void OnEnable()
        {
            EnsureDefaultSourceMeshesAssigned();

            if (m_BuiltinInspectorEditor == null)
                CreateBuiltinEditors();
        }

        private void OnDisable()
        {
            if (m_BuiltinInspectorEditor != null)
                DestroyImmediate(m_BuiltinInspectorEditor);
        }

        private void CreateBuiltinEditors()
        {
            // Preserve Unity's built-in MeshCollider inspector if available
            var builtinType = GetBuiltinEditorType();
            if (builtinType == null) return;

            if (targets != null && targets.Length > 0)
                m_BuiltinInspectorEditor = CreateEditor(targets, builtinType);
        }

        public override void OnInspectorGUI()
        {
            EnsureDefaultSourceMeshesAssigned();

            if (m_BuiltinInspectorEditor != null) m_BuiltinInspectorEditor.OnInspectorGUI();
            else DrawDefaultInspector();

            DrawDefaultMeshColliderGizmoHelp();
            EditorGUILayout.Space(10);
            DrawOptimizerConversionUI();
        }

        private static Type GetBuiltinEditorType()
        {
            if (s_BuiltinEditorType == null)
                s_BuiltinEditorType = Type.GetType("UnityEditor.MeshColliderEditor, UnityEditor");
            return s_BuiltinEditorType;
        }

        private void DrawOptimizerConversionUI()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Mesh Collider Optimizer", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Convert this MeshCollider to MeshColliderOptimizer, copy settings, and remove MeshCollider.",
                    EditorStyles.wordWrappedMiniLabel);

                bool hasInvalid = HasAnyInvalidSelection();

                if (hasInvalid)
                {
                    EditorGUILayout.HelpBox(
                        "Some selected MeshColliders have no MeshFilter/SkinnedMeshRenderer and no sharedMesh.\n" +
                        "Conversion will be skipped for those objects (to avoid leaving optimizer without a source mesh).",
                        MessageType.Warning);
                }

                using (new EditorGUI.DisabledScope(targets == null || targets.Length == 0))
                {
                    if (GUILayout.Button("Convert to Mesh Collider Optimizer", GUILayout.Height(28)))
                    {
                        ConvertSelectedDeferred();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawDefaultMeshColliderGizmoHelp()
        {
            if (targets is not { Length: 1 }) return;
            if (target is not MeshCollider mc || mc == null) return;
            if (mc.GetComponentInParent<Optimizer>() != null) return;

            Mesh sourceMesh = GetDefaultMeshColliderSource(mc, out string sourceLabel);
            if (sourceMesh == null)
            {
                if (mc.sharedMesh == null)
                {
                    EditorGUILayout.Space(6);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.HelpBox(
                            "Default Unity MeshCollider gizmo requires a source mesh. No MeshFilter or SkinnedMeshRenderer source was found on this object.",
                            MessageType.Info);
                    }
                }
                return;
            }

            if (mc.sharedMesh != null) return;

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "MeshCollider source mesh was not assigned automatically yet. " +
                    $"You can still assign the source mesh from {sourceLabel} manually if needed.",
                    MessageType.Info);

                if (GUILayout.Button("Assign Source Mesh To MeshCollider", GUILayout.Height(22)))
                {
                    AssignDefaultSourceMesh(mc, sourceMesh);
                }
            }
        }

        private void EnsureDefaultSourceMeshesAssigned()
        {
            if (targets == null || targets.Length == 0) return;

            bool anyAssigned = false;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is not MeshCollider mc || mc == null) continue;
                if (!TryAssignDefaultSourceMesh(mc))
                    continue;

                anyAssigned = true;
            }

            if (!anyAssigned) return;

            if (m_BuiltinInspectorEditor != null)
            {
                DestroyImmediate(m_BuiltinInspectorEditor);
                m_BuiltinInspectorEditor = null;
            }

            CreateBuiltinEditors();
            SceneView.RepaintAll();
            Repaint();
        }

        private static Mesh GetDefaultMeshColliderSource(MeshCollider mc, out string sourceLabel)
        {
            sourceLabel = null;
            if (mc == null) return null;

            var mf = mc.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                sourceLabel = "MeshFilter";
                return mf.sharedMesh;
            }

            var smr = mc.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                sourceLabel = "SkinnedMeshRenderer";
                return smr.sharedMesh;
            }

            return null;
        }

        private static bool TryAssignDefaultSourceMesh(MeshCollider mc)
        {
            if (mc == null) return false;
            if (mc.GetComponentInParent<Optimizer>() != null) return false;
            if (mc.sharedMesh != null) return false;

            Mesh sourceMesh = GetDefaultMeshColliderSource(mc, out _);
            if (sourceMesh == null) return false;

            AssignDefaultSourceMesh(mc, sourceMesh);
            return true;
        }

        private static void AssignDefaultSourceMesh(MeshCollider mc, Mesh sourceMesh)
        {
            if (mc == null || sourceMesh == null) return;

            Undo.RecordObject(mc, "Assign MeshCollider Source Mesh");
            mc.sharedMesh = sourceMesh;
            EditorUtility.SetDirty(mc);
        }

        private bool HasAnyInvalidSelection()
        {
            foreach (var t in targets)
            {
                if (t is not MeshCollider mc || mc == null) continue;

                var go = mc.gameObject;
                if (go == null) continue;

                bool hasSource =
                    go.GetComponent<MeshFilter>() != null ||
                    go.GetComponent<SkinnedMeshRenderer>() != null ||
                    mc.sharedMesh != null;

                if (!hasSource) return true;
            }
            return false;
        }

        private void ConvertSelectedDeferred()
        {
            // Capture instance IDs so changes in selection won't break the deferred execution
            var ids = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                ids[i] = targets[i] != null ? targets[i].GetInstanceID() : 0;

            EditorApplication.delayCall += () =>
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Convert MeshCollider to MeshColliderOptimizer");

                try
                {
                    foreach (var t in ids)
                    {
                        var mc = EditorUtility.InstanceIDToObject(t) as MeshCollider;
                        if (mc == null) continue;

                        ConvertOne(mc);
                    }
                }
                finally
                {
                    Undo.CollapseUndoOperations(group);
                    SceneView.RepaintAll();
                }
            };
        }

        private static void ConvertOne(MeshCollider mc)
        {
            var go = mc != null ? mc.gameObject : null;
            if (go == null) return;

            // Ensure optimizer will have a mesh source:
            // - Prefer existing MeshFilter/SkinnedMeshRenderer
            // - If missing, create MeshFilter from MeshCollider.sharedMesh (if available)
            var mf = go.GetComponent<MeshFilter>();
            var smr = go.GetComponent<SkinnedMeshRenderer>();

            if (mf == null && smr == null)
            {
                if (mc.sharedMesh == null)
                    return; // skip conversion safely

                mf = Undo.AddComponent<MeshFilter>(go);
                mf.sharedMesh = mc.sharedMesh;
                EditorUtility.SetDirty(mf);
            }

            // Add/Get optimizer
            var opt = go.GetComponent<Optimizer>();
            if (opt == null)
                opt = Undo.AddComponent<Optimizer>(go);

            // Copy MeshCollider settings into optimizer
            Undo.RecordObject(opt, "Copy MeshCollider Settings");

            opt.convex = mc.convex;
            opt.isTrigger = mc.isTrigger;
            opt.material = mc.sharedMaterial;
            opt.cookingOptions = mc.cookingOptions;
            CopyLayerOverridesFromMeshCollider(mc, opt);

#if UNITY_2022_2_OR_NEWER
            opt.providesContacts = mc.providesContacts;
#endif

            EditorUtility.SetDirty(opt);

            // Remove MeshCollider
            Undo.DestroyObjectImmediate(mc);

            // Defer optimization to avoid SerializedObject/UI sync assertions
            var optRef = opt;
            EditorApplication.delayCall += () =>
            {
                if (optRef == null) return;
                optRef.RequestOptimization();
                EditorUtility.SetDirty(optRef);
                SceneView.RepaintAll();
            };
        }

        private static void CopyLayerOverridesFromMeshCollider(MeshCollider source, Optimizer target)
        {
            if (source == null || target == null) return;

            if (TryReadColliderLayerOverrideInt(source, "layerOverridePriority", out int priority))
                target.layerOverridePriority = priority;

            if (TryReadColliderLayerOverrideInt(source, "includeLayers", out int include))
                target.includeLayers = include;

            if (TryReadColliderLayerOverrideInt(source, "excludeLayers", out int exclude))
                target.excludeLayers = exclude;
        }

        private static bool TryReadColliderLayerOverrideInt(Collider source, string propertyName, out int value)
        {
            value = 0;
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var prop = source.GetType().GetProperty(propertyName, flags);
            if (prop == null || !prop.CanRead) return false;

            object raw;
            try
            {
                raw = prop.GetValue(source);
            }
            catch
            {
                return false;
            }

            if (raw == null) return false;
            if (raw is int i)
            {
                value = i;
                return true;
            }
            if (raw is LayerMask mask)
            {
                value = mask.value;
                return true;
            }

            return false;
        }
    }
}
#endif

