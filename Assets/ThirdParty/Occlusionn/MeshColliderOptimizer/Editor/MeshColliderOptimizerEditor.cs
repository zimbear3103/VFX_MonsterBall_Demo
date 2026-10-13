using Occlusionn.MeshColliderOptimizer.Runtime;
using UnityEditor;
using UnityEngine;
using Optimizer = global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer;

namespace Occlusionn.MeshColliderOptimizer.Editor
{
    /// <summary>
    /// Custom inspector and editor utilities for MeshColliderOptimizer.
    /// </summary>

    [CustomEditor(typeof(Optimizer))]
    public class MeshOptimizerEditor : UnityEditor.Editor
    {
        private static Optimizer _clipboard;

        private bool _foldCollider  = true;
        private bool _foldSource    = true;
        private bool _foldOptimize  = true;
        private bool _foldSkinned   = true;
        private bool _foldAnimation = true;
        private bool _foldLod;
        private bool _foldStats     = true;
        private bool _foldHeatmap;
        private bool _foldPreset;
        private bool _foldTools;
        private bool _foldLayerOverrides;
        private static GUIContent _icnRefresh, _icnSave, _icnTrash, _icnSettings;
        private static GUIContent _icnEye, _icnEyeOff;
        private static bool _iconsReady;

        private GUIStyle _toolbarBtnStyle;
        private GUIStyle _gridLabelStyle;
        private GUIStyle _gridValueStyle;
        private GUIStyle _gridHeaderStyle;
        
        private static Texture2D _gradTex;
        private static Color _gC1, _gC2;
        private static double _nextLiveDragOptimizeTime;
        private const double LIVE_DRAG_OPTIMIZE_INTERVAL_SECONDS = 0.05d;

        private void EnsureIcons()
        {
            if (_iconsReady) return;
            _icnRefresh  = EditorGUIUtility.IconContent("d_Refresh");
            _icnSave     = EditorGUIUtility.IconContent("d_SaveAs");
            _icnTrash    = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
            _icnSettings = EditorGUIUtility.IconContent("d_Settings");
            _icnEye      = EditorGUIUtility.IconContent("d_scenevis_visible_hover");
            _icnEyeOff   = EditorGUIUtility.IconContent("d_scenevis_hidden_hover");
            _iconsReady  = true;
        }

        private void EnsureStyles()
        {
            if (_gridLabelStyle != null) return;

            _toolbarBtnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 22,
                fontStyle = FontStyle.Normal,
                padding = new RectOffset(6, 6, 2, 2)
            };

            _gridLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _gridValueStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };

            _gridHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
            };
        }


        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Optimize Selected Objects")]
        public static void OptimizeSelected()
        {
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0) { EditorUtility.DisplayDialog("Mesh Collider Optimizer", "Select at least one GameObject.", "OK"); return; }
            
            foreach (var go in sel)
            {
                if (go == null) continue;

                bool hasSource =
                    go.GetComponent<MeshFilter>() || go.GetComponent<SkinnedMeshRenderer>() ||
                    go.GetComponentInChildren<MeshFilter>(true) || go.GetComponentInChildren<SkinnedMeshRenderer>(true);

                if (!hasSource)
                {
                    var existing = go.GetComponent<Optimizer>();
                    hasSource = existing != null &&
                                (existing.externalTargetRenderer != null || existing.externalTargetMeshFilter != null || existing.externalSourceRoot != null);
                }

                if (!hasSource) continue;

                var opt = go.GetComponent<Optimizer>() ?? Undo.AddComponent<Optimizer>(go);
                opt.RequestOptimization();
            }
        }

        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Optimize ALL in Scene")]
        public static void OptimizeAll()
        {
            var all = FindObjectsOfType<Optimizer>();
            if (all.Length == 0) { EditorUtility.DisplayDialog("Mesh Collider Optimizer", "No components found in the scene.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Batch Optimize", $"Re-optimize {all.Length} component(s)?", "Optimize All", "Cancel")) return;
            for (int i = 0; i < all.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Batch Optimization", $"Processing {all[i].gameObject.name}...", (float)i / all.Length);
                all[i].RequestOptimization();
            }
            EditorUtility.ClearProgressBar();
        }

        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Remove All Optimizers in Scene")]
        public static void RemoveAllInScene()
        {
            var all = FindObjectsOfType<Optimizer>();
            if (all.Length == 0) { EditorUtility.DisplayDialog("Mesh Collider Optimizer", "Nothing to remove.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Remove All", $"Remove {all.Length} optimizer(s) and their generated colliders?\nThis cannot be undone.", "Remove All", "Cancel")) return;
            foreach (var o in all) { o.RemoveAllGeneratedColliders(); DestroyImmediate(o); }
        }

        [MenuItem("Tools/Occlusionn/Mesh Collider Optimizer/Create Built-in Presets")]
        public static void CreateBuiltInPresets()
        {
            string folder = EditorUtility.OpenFolderPanel("Save Presets To", "Assets", "");
            if (string.IsNullOrEmpty(folder)) return;
            if (folder.StartsWith(Application.dataPath)) folder = "Assets" + folder.Substring(Application.dataPath.Length);
            AssetDatabase.CreateAsset(OptimizationPreset.CreateHighQuality(),  $"{folder}/Preset_HighQuality.asset");
            AssetDatabase.CreateAsset(OptimizationPreset.CreateBalanced(),     $"{folder}/Preset_Balanced.asset");
            AssetDatabase.CreateAsset(OptimizationPreset.CreatePerformance(),  $"{folder}/Preset_Performance.asset");
            AssetDatabase.CreateAsset(OptimizationPreset.CreateMobile(),       $"{folder}/Preset_Mobile.asset");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
        
        private static Mesh GetSourceMesh(Optimizer s)
        {
            if (s == null) return null;

            if (s.externalSourceRoot != null)
            {
                var rootMf = s.externalSourceRoot.GetComponentInChildren<MeshFilter>(true);
                if (rootMf && rootMf.sharedMesh) return rootMf.sharedMesh;

                var rootSmr = s.externalSourceRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (rootSmr && rootSmr.sharedMesh) return rootSmr.sharedMesh;
            }

            if (s.externalTargetMeshFilter && s.externalTargetMeshFilter.sharedMesh)
                return s.externalTargetMeshFilter.sharedMesh;

            if (s.externalTargetRenderer != null)
            {
                if (s.externalTargetRenderer is SkinnedMeshRenderer extSkinned && extSkinned.sharedMesh)
                    return extSkinned.sharedMesh;

                if (s.externalTargetRenderer is MeshRenderer extMeshRenderer)
                {
                    var extMeshFilter = extMeshRenderer.GetComponent<MeshFilter>();
                    if (extMeshFilter && extMeshFilter.sharedMesh)
                        return extMeshFilter.sharedMesh;
                }
            }

            if (s.mergeChildObjects)
            {
                // When merge is enabled, children are already combined; suppress inspector warnings.
                var mfs = s.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in mfs) if (mf && mf.sharedMesh) return mf.sharedMesh;

                var smrs = s.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs) if (smr && smr.sharedMesh) return smr.sharedMesh;

                return null;
            }

            // Check self first.
            var mfSelf = s.GetComponent<MeshFilter>();
            if (mfSelf && mfSelf.sharedMesh) return mfSelf.sharedMesh;

            var smrSelf = s.GetComponent<SkinnedMeshRenderer>();
            if (smrSelf && smrSelf.sharedMesh) return smrSelf.sharedMesh;

            // Child fallback (optimizer attached on parent scenario).
            var smrChild = s.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smrChild && smrChild.sharedMesh) return smrChild.sharedMesh;

            var mfChild = s.GetComponentInChildren<MeshFilter>(true);
            if (mfChild && mfChild.sharedMesh) return mfChild.sharedMesh;

            return null;
        }

        private static bool TryGetExternalSourceMesh(Optimizer s, out Mesh mesh, out string issue)
        {
            mesh = null;
            issue = null;

            if (s == null || (s.externalTargetRenderer == null && s.externalTargetMeshFilter == null && s.externalSourceRoot == null))
                return false;

            if (s.externalSourceRoot != null)
            {
                var rootMf = s.externalSourceRoot.GetComponentInChildren<MeshFilter>(true);
                if (rootMf != null && rootMf.sharedMesh != null)
                {
                    mesh = rootMf.sharedMesh;
                    return true;
                }

                var rootSmr = s.externalSourceRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (rootSmr != null && rootSmr.sharedMesh != null)
                {
                    mesh = rootSmr.sharedMesh;
                    return true;
                }

                issue = "Assigned External Source Root has no MeshFilter/SkinnedMeshRenderer with a shared mesh.";
                return false;
            }

            if (s.externalTargetMeshFilter != null)
            {
                if (s.externalTargetMeshFilter.sharedMesh == null)
                {
                    issue = "Assigned external MeshFilter has no shared mesh.";
                    return false;
                }

                mesh = s.externalTargetMeshFilter.sharedMesh;
                return true;
            }

            if (s.externalTargetRenderer is SkinnedMeshRenderer extSkinned)
            {
                if (extSkinned.sharedMesh == null)
                {
                    issue = "Assigned SkinnedMeshRenderer has no shared mesh.";
                    return false;
                }
                mesh = extSkinned.sharedMesh;
                return true;
            }

            if (s.externalTargetRenderer is MeshRenderer extMeshRenderer)
            {
                var extMeshFilter = extMeshRenderer.GetComponent<MeshFilter>();
                if (extMeshFilter == null)
                    extMeshFilter = extMeshRenderer.GetComponentInChildren<MeshFilter>(true);
                if (extMeshFilter == null)
                {
                    issue = "Assigned MeshRenderer has no MeshFilter (same object or child).";
                    return false;
                }

                if (extMeshFilter.sharedMesh == null)
                {
                    issue = "Assigned MeshRenderer's MeshFilter has no shared mesh.";
                    return false;
                }

                mesh = extMeshFilter.sharedMesh;
                return true;
            }

            issue = "Unsupported renderer type. Use SkinnedMeshRenderer or MeshRenderer + MeshFilter.";
            return false;
        }

        private static bool IsMeshReadable(Optimizer s)
        {
            Mesh m = GetSourceMesh(s);
            return m == null || m.isReadable;
        }

        private static void FixReadWrite(Optimizer s)
        {
            Mesh m = GetSourceMesh(s);
            if (m == null) return;
            string path = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(path)) return;
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp != null) { imp.isReadable = true; imp.SaveAndReimport(); }
        }

        private static void EnsureGradient(Color c1, Color c2)
        {
            const int w = 256;
            if (_gradTex == null) { _gradTex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave }; _gC1 = new Color(float.NaN, 0, 0, 0); }
            if (_gC1 == c1 && _gC2 == c2) return;
            _gC1 = c1; _gC2 = c2;
            for (int x = 0; x < w; x++) _gradTex.SetPixel(x, 0, Color.Lerp(c1, c2, x / (float)(w - 1)));
            _gradTex.Apply(false, false);
        }

        public override bool RequiresConstantRepaint()
        {
            if (!Application.isPlaying) return false;
            if (target is not Optimizer s || s == null) return false;
            return s.animationMode;
        }

        private static void ForceImmediateSceneRepaint()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        
        public override void OnInspectorGUI()
        {
            EnsureIcons();
            EnsureStyles();
            
            if (targets is { Length: > 1 })
            {
                EditorGUILayout.HelpBox("Multi-object editing is disabled for Mesh Collider Optimizer. Please select a single object.", MessageType.Info);
                return;
            }

            var s = (Optimizer)target;
            LODGroup lodGrp = s.GetComponent<LODGroup>();

            if (!IsMeshReadable(s))
            {
                EditorGUILayout.HelpBox("Mesh Read/Write is disabled. The optimizer cannot access vertex data.", MessageType.Error);
                if (GUILayout.Button("Enable Read/Write"))
                    FixReadWrite(s);
                GUI.enabled = false;
            }

            if (GetSourceMesh(s) == null)
                EditorGUILayout.HelpBox("No mesh source found. Attach a MeshFilter/SkinnedMeshRenderer or assign External Target Renderer (MeshRenderer/SkinnedMeshRenderer).", MessageType.Warning);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            bool newAuto = GUILayout.Toggle(s.autoUpdate,
                new GUIContent(" Auto", "Automatically re-optimize when any setting changes."),
                _toolbarBtnStyle, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Toggle Auto Update");
                s.autoUpdate = newAuto;
                PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                EditorUtility.SetDirty(s);
            }

            GUI.enabled = !s.autoUpdate;
            if (GUILayout.Button(new GUIContent(" Rebuild", _icnRefresh.image, "Run the optimization pipeline now."), _toolbarBtnStyle))
                s.RequestOptimization();
            GUI.enabled = true;

            GUIContent gzIcn = s.showGizmos
                ? new GUIContent(" Gizmos", _icnEye.image, "Hide collider gizmos.")
                : new GUIContent(" Gizmos", _icnEyeOff.image, "Show collider gizmos.");
            EditorGUI.BeginChangeCheck();
            bool gzToggle = GUILayout.Toggle(s.showGizmos, gzIcn, _toolbarBtnStyle, GUILayout.Width(84));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Toggle Gizmos");
                s.showGizmos = gzToggle;
                PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                EditorUtility.SetDirty(s);
                ForceImmediateSceneRepaint();
            }

            EditorGUILayout.EndHorizontal();

            if (s.FinalMesh != null)
                EditorGUILayout.LabelField(s.GetStatusSummary(), EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();

            // Values below are assigned directly to fields, so undo must be recorded before drawing controls.
            Undo.RecordObject(s, "Mesh Collider Optimizer");
            EditorGUI.BeginChangeCheck();

            _foldSource = EditorGUILayout.BeginFoldoutHeaderGroup(_foldSource, "Source");
            if (_foldSource)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                s.externalTargetRenderer = (Renderer)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "External Target Renderer",
                        "Optional external source on a separate hierarchy. Supports SkinnedMeshRenderer, or MeshRenderer with MeshFilter."),
                    s.externalTargetRenderer,
                    typeof(Renderer),
                    true);
                s.externalTargetMeshFilter = (MeshFilter)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "External Target MeshFilter",
                        "Optional direct mesh source override for non-skinned meshes. If assigned, this is used before MeshRenderer lookup."),
                    s.externalTargetMeshFilter,
                    typeof(MeshFilter),
                    true);
                s.externalSourceRoot = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "External Source Root",
                        "Optional root transform used when Merge Children is enabled. All child meshes under this root are included."),
                    s.externalSourceRoot,
                    typeof(Transform),
                    true);

                if (s.externalTargetRenderer != null || s.externalTargetMeshFilter != null || s.externalSourceRoot != null)
                {
                    if (TryGetExternalSourceMesh(s, out Mesh extMesh, out string issue))
                    {
                        EditorGUILayout.HelpBox(
                            $"External source override is active. Source mesh: {extMesh.name}.",
                            MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            $"External source is assigned but not valid: {issue}\nOptimizer will fall back to local/child source discovery.",
                            MessageType.Warning);
                    }
                }

                if (s.externalTargetRenderer != null && s.externalTargetMeshFilter != null)
                {
                    EditorGUILayout.HelpBox(
                        "Both external fields are assigned. External Target MeshFilter takes priority over External Target Renderer.",
                        MessageType.Info);
                }

                if (s.externalSourceRoot != null && !s.mergeChildObjects)
                {
                    EditorGUILayout.HelpBox(
                        "External Source Root is most useful with Merge Children enabled.",
                        MessageType.None);
                }

                if (s.externalTargetRenderer is MeshRenderer && s.externalTargetMeshFilter == null && !s.mergeChildObjects)
                {
                    EditorGUILayout.HelpBox(
                        "External MeshRenderer override uses a single mesh by default. For multi-part characters, enable Merge Children.",
                        MessageType.None);
                }

                if (s.externalTargetMeshFilter != null && s.mergeChildObjects)
                {
                    EditorGUILayout.HelpBox(
                        "Merge Children starts from the assigned External Target MeshFilter transform. Sibling meshes outside that branch are not included.",
                        MessageType.None);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            bool hasExternalOverride = s.externalTargetRenderer != null || s.externalTargetMeshFilter != null || s.externalSourceRoot != null;
            bool hasSkinnedSource = s.externalTargetRenderer is SkinnedMeshRenderer ||
                                    (s.externalSourceRoot != null && s.externalSourceRoot.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) ||
                                    (!hasExternalOverride && s.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);
            bool hasAnySource = GetSourceMesh(s) != null;

            _foldCollider = EditorGUILayout.BeginFoldoutHeaderGroup(_foldCollider, "Collider");
            if (_foldCollider)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (s.animationMode)
                {
                    EditorGUILayout.HelpBox("Convex mode cannot be enabled while 'Animated Collider' is active. Please disable Animation mode first.", MessageType.Warning);
                    EditorGUI.BeginDisabledGroup(true);
                }

                s.convex = EditorGUILayout.Toggle(
                    new GUIContent("Convex", "Generate convex colliders via spatial decomposition. Required for non-kinematic Rigidbodies."),
                    s.convex);

                if (s.animationMode) EditorGUI.EndDisabledGroup();

                if (s.convex)
                {
                    EditorGUI.indentLevel++;
                    s.isTrigger = EditorGUILayout.Toggle(new GUIContent("Is Trigger", "Mark all generated colliders as triggers."), s.isTrigger);
                    s.decompositionGrid = EditorGUILayout.IntSlider(new GUIContent("Decomposition Grid", "Subdivisions per axis. Higher = more parts, better fit."), s.decompositionGrid, 2, 6);
                    s.strict256Guard = EditorGUILayout.Toggle(new GUIContent("Strict 256 Guard", "Aggressively simplify parts to stay under PhysX 256-polygon limit."), s.strict256Guard);

                    if (s.autoUpdate && s.decompositionGrid >= 5)
                    {
                        EditorGUILayout.HelpBox(
                            "Auto Update + high decomposition can stall the editor on dense meshes. Disable Auto while tuning, then press Rebuild.",
                            MessageType.Warning);
                    }
                    EditorGUI.indentLevel--;
                }

#if UNITY_2022_1_OR_NEWER
                s.providesContacts = EditorGUILayout.Toggle(
                    new GUIContent("Provides Contacts",
                        "When enabled, this collider provides detailed contact data via OnCollisionStay / Physics.ContactEvent. Useful for custom physics responses, surface detection, or VFX at contact points."),
                    s.providesContacts);
#endif
                
                s.mergeChildObjects = EditorGUILayout.Toggle(
                    new GUIContent("Merge Children", "Combine child MeshFilter / SkinnedMeshRenderer meshes into a single collider."),
                    s.mergeChildObjects);
                
                s.material = (PhysicsMaterial)EditorGUILayout.ObjectField(
                    new GUIContent("Physic Material", "Physics material applied to every generated collider."),
                    s.material, typeof(PhysicsMaterial), false);

                s.cookingOptions = (MeshColliderCookingOptions)EditorGUILayout.EnumFlagsField(
                    new GUIContent("Cooking Options",
                        "Controls how PhysX processes the mesh data.\n\n" +
                        "- EnableMeshCleaning - Removes degenerate triangles.\n" +
                        "- WeldColocatedVertices - Merges overlapping vertices.\n" +
                        "- CookForFasterSimulation - Trades cook time for faster runtime.\n" +
                        "- UseFastMidphase - R-tree midphase (Unity 2022+)."),
                    s.cookingOptions);

                DrawLayerOverrides(s);
                
                DrawColliderWarnings(s);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _foldOptimize = EditorGUILayout.BeginFoldoutHeaderGroup(_foldOptimize, "Optimization");
            if (_foldOptimize)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                s.quality = EditorGUILayout.Slider(new GUIContent("Quality", "1 = keep all vertices, 0 = maximum simplification."), s.quality, 0.001f, 1f);
                s.shapeFidelity = EditorGUILayout.Slider(new GUIContent("Shape Fidelity", "How closely the simplified mesh follows the original surface."), s.shapeFidelity, 0.01f, 1f);
                s.inflationAmount = EditorGUILayout.Slider(new GUIContent("Inflate / Deflate", "Vertex offset along normals. Positive = outward, negative = inward."), s.inflationAmount, -0.2f, 0.2f);

                string hint;
                if      (s.quality > 0.8f) hint = "Very high quality - minimal simplification.";
                else if (s.quality > 0.5f) hint = "Balanced - moderate simplification.";
                else if (s.quality > 0.2f) hint = "Performance - aggressive simplification.";
                else                      hint = "Ultra performance - maximum simplification.";
                EditorGUILayout.HelpBox(hint, MessageType.None);

                if (s.autoUpdate && s.convex && s.quality > 0.85f)
                {
                    EditorGUILayout.HelpBox(
                        "Auto Update preview may cap effective quality on dense meshes to keep the editor responsive. Use Rebuild for final full-quality convex generation.",
                        MessageType.Info);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (hasSkinnedSource)
            {
                _foldSkinned = EditorGUILayout.BeginFoldoutHeaderGroup(_foldSkinned, "Skinned Mesh");
                if (_foldSkinned)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    s.capturePoseSnapshot = EditorGUILayout.Toggle(new GUIContent("Capture Current Pose", "Bake the current animation pose into the collider mesh."), s.capturePoseSnapshot);
                    s.trackBlendShapes = EditorGUILayout.Toggle(new GUIContent("Track Blend Shapes", "Re-optimize when blend shape weights change (requires Auto Update)."), s.trackBlendShapes);
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (hasAnySource)
            {
                _foldAnimation = EditorGUILayout.BeginFoldoutHeaderGroup(_foldAnimation, "Animated Collider (Runtime)");
                if (_foldAnimation)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    if (s.convex)
                    {
                        EditorGUILayout.HelpBox("Animated Collider cannot be enabled while 'Convex' is active. Please disable Convex mode first.", MessageType.Warning);
                        EditorGUI.BeginDisabledGroup(true);
                    }

                    s.animationMode = EditorGUILayout.Toggle(new GUIContent("Enable", "Continuously update the collider to match the animation pose during Play Mode."), s.animationMode);

                    if (s.convex) EditorGUI.EndDisabledGroup();

                    if (s.animationMode)
                    {
                        EditorGUI.indentLevel++;

                        s.animationSourceMode = (Optimizer.AnimationSourceMode)EditorGUILayout.EnumPopup(
                            new GUIContent("Source", "BakeMesh = exact pose every frame. SharedMesh = cheaper, ignores current pose."),
                            s.animationSourceMode);

                        s.animationUpdateMode = (Optimizer.AnimationUpdateMode)EditorGUILayout.EnumPopup(
                            new GUIContent("Update Rate", "How often the collider is rebuilt at runtime."),
                            s.animationUpdateMode);

                        if (s.animationUpdateMode == Optimizer.AnimationUpdateMode.EveryNFrames)
                            s.animationEveryNFrames = EditorGUILayout.IntSlider(new GUIContent("Every N Frames", "Update once every N rendered frames."), s.animationEveryNFrames, 1, 60);
                        else if (s.animationUpdateMode == Optimizer.AnimationUpdateMode.TargetHz)
                            s.animationMaxHz = EditorGUILayout.Slider(new GUIContent("Target Hz", "Maximum updates per second."), s.animationMaxHz, 5f, 120f);

                        s.animationRootOnly = EditorGUILayout.Toggle(new GUIContent("Root Collider Only", "Update only the single root MeshCollider (much cheaper than full decomposition)."), s.animationRootOnly);
                        s.animationOnlyWhenVisible = EditorGUILayout.Toggle(new GUIContent("Only When Visible", "Skip updates while the renderer is off-screen."), s.animationOnlyWhenVisible);

                        if (!s.animationRootOnly)
                            EditorGUILayout.HelpBox("Full decomposition every frame is expensive. Consider 'Root Collider Only' or a lower update rate.", MessageType.Warning);

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (lodGrp != null)
            {
                _foldLod = EditorGUILayout.BeginFoldoutHeaderGroup(_foldLod, "LOD Group");
                if (_foldLod)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"{lodGrp.lodCount} level(s) found", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Propagates settings to every LOD renderer with decreasing quality per level.", MessageType.Info);

                    s.enableCollidersOnlyOnActiveLod = EditorGUILayout.Toggle(
                        new GUIContent("Colliders Follow Active LOD", "Runtime: enable generated colliders only on the currently active LOD level (prevents multiple LOD colliders from being active at once)."),
                        s.enableCollidersOnlyOnActiveLod);

                    if (s.enableCollidersOnlyOnActiveLod)
                        EditorGUILayout.HelpBox("When no LOD renderers are enabled (e.g., off-screen), the system keeps the last active LOD to avoid disabling all colliders.", MessageType.None);

                    if (GUILayout.Button(new GUIContent(" Setup All LODs", _icnRefresh.image), GUILayout.Height(22)))
                        ApplyToLoDs(s, lodGrp);
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (EditorGUI.EndChangeCheck())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                EditorUtility.SetDirty(s);
                if (s.autoUpdate)
                {
                    bool isDraggingControl = Event.current.type == EventType.MouseDrag && GUIUtility.hotControl != 0;
                    if (isDraggingControl)
                    {
                        double now = EditorApplication.timeSinceStartup;
                        if (now >= _nextLiveDragOptimizeTime)
                        {
                            _nextLiveDragOptimizeTime = now + LIVE_DRAG_OPTIMIZE_INTERVAL_SECONDS;
                            s.RequestOptimization();
                        }
                    }
                    else
                    {
                        _nextLiveDragOptimizeTime = 0d;
                        s.RequestAutoOptimization();
                    }
                }
                ForceImmediateSceneRepaint();
            }

            _foldPreset = EditorGUILayout.BeginFoldoutHeaderGroup(_foldPreset, "Presets");
            if (_foldPreset)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                OptimizationPreset newPreset = (OptimizationPreset)EditorGUILayout.ObjectField(
                    new GUIContent("Preset Asset", "Drag a saved OptimizationPreset and press Apply."),
                    s.preset, typeof(OptimizationPreset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(s, "Assign Preset");
                    s.preset = newPreset;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                    EditorUtility.SetDirty(s);
                }
                GUI.enabled = s.preset != null;
                if (GUILayout.Button("Apply", GUILayout.Width(50)))
                { Undo.RecordObject(s, "Apply Preset"); s.ApplyPreset(s.preset); EditorUtility.SetDirty(s); if (s.autoUpdate) s.RequestAutoOptimization(); }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Quick Apply", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                QuickPresetBtn(s, "High Quality",  OptimizationPreset.CreateHighQuality);
                QuickPresetBtn(s, "Balanced",       OptimizationPreset.CreateBalanced);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                QuickPresetBtn(s, "Performance",    OptimizationPreset.CreatePerformance);
                QuickPresetBtn(s, "Mobile",         OptimizationPreset.CreateMobile);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button(new GUIContent(" Save Current as Preset...", _icnSave.image, "Export current settings to a .asset file.")))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Preset", "MyPreset", "asset", "Save optimization preset");
                    if (!string.IsNullOrEmpty(path))
                    { var p = s.SaveToPreset(); AssetDatabase.CreateAsset(p, path); AssetDatabase.SaveAssets(); s.preset = p; EditorUtility.SetDirty(s); }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _foldTools = EditorGUILayout.BeginFoldoutHeaderGroup(_foldTools, "Tools");
            if (_foldTools)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUI.enabled = s.FinalMesh != null;
                if (GUILayout.Button(new GUIContent(" Save Generated Mesh...", _icnSave.image, "Save the optimized mesh as a project .asset file."), GUILayout.Height(22)))
                    SaveMesh(s.FinalMesh, s.gameObject.name);
                GUI.enabled = true;

                if (GUILayout.Button(new GUIContent(" Reset to Defaults", _icnSettings.image, "Revert every setting to factory defaults.")))
                {
                    if (EditorUtility.DisplayDialog("Reset", "Reset all settings to defaults?", "Reset", "Cancel"))
                    { Undo.RecordObject(s, "Reset Defaults"); s.ResetToDefaults(); EditorUtility.SetDirty(s); }
                }

                if (GUILayout.Button(new GUIContent(" Remove Generated Colliders", _icnTrash.image, "Delete all generated colliders and child objects.")))
                {
                    if (EditorUtility.DisplayDialog("Remove", "Delete all generated colliders?", "Remove", "Cancel"))
                    { Undo.RecordObject(s, "Remove Colliders"); s.RemoveAllGeneratedColliders(); EditorUtility.SetDirty(s); }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            _foldHeatmap = EditorGUILayout.BeginFoldoutHeaderGroup(_foldHeatmap, "Visual Error Heatmap");
            if (_foldHeatmap)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Undo.RecordObject(s, "Heatmap Settings");
                EditorGUI.BeginChangeCheck();

                s.showHeatmap = EditorGUILayout.Toggle(new GUIContent("Show Heatmap", "Per-vertex distance visualisation between source and generated collider."), s.showHeatmap);

                if (s.showHeatmap)
                {
                    EditorGUI.indentLevel++;
                    Rect bar = EditorGUILayout.GetControlRect(false, 4);
                    EnsureGradient(Color.green, Color.red);
                    GUI.DrawTexture(bar, _gradTex);

                    s.maxErrorTolerance     = EditorGUILayout.Slider(new GUIContent("Error Threshold (m)", "Distance mapped to 100% red."), s.maxErrorTolerance, 0.001f, 0.5f);
                    s.heatmapStride = EditorGUILayout.IntSlider(new GUIContent("Sample Density", "Vertex skip. 1 = every vertex, higher = faster."), s.heatmapStride, 1, 100);
                    s.drawErrorLines       = EditorGUILayout.Toggle(new GUIContent("Draw Error Lines", "Line from sample to closest collider surface."), s.drawErrorLines);

                    if (GUILayout.Button(new GUIContent(" Recalculate", _icnRefresh.image), GUILayout.Height(22)))
                    { s.RecalculateHeatmap(); ForceImmediateSceneRepaint(); }

                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                    EditorUtility.SetDirty(s);
                    s.RecalculateHeatmap();
                    ForceImmediateSceneRepaint();
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            _foldStats = EditorGUILayout.BeginFoldoutHeaderGroup(_foldStats, "Statistics");
            if (_foldStats)
                DrawStatsGrid(s);
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (GUI.changed) EditorUtility.SetDirty(s);
        }
        
        private void DrawStatsGrid(Optimizer s)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            float p = Mathf.Clamp01(s.savingsRatio);

            Rect barRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.DrawRect(barRect, new Color(0.12f, 0.12f, 0.12f));
            Color barColor = Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.2f, 0.8f, 0.25f), p);
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * p, barRect.height), barColor);
            var prevColor = GUI.color; GUI.color = Color.white;
            EditorGUI.LabelField(barRect, $"  Total Savings: {p * 100f:F1}%", EditorStyles.whiteBoldLabel);
            GUI.color = prevColor;

            EditorGUILayout.Space(2);

            Rect hdr = EditorGUILayout.GetControlRect(false, 16);
            float cw = hdr.width / 4f;
            GUI.Label(new Rect(hdr.x,        hdr.y, cw, 16), "",          _gridHeaderStyle);
            GUI.Label(new Rect(hdr.x + cw,   hdr.y, cw, 16), "Before",   _gridHeaderStyle);
            GUI.Label(new Rect(hdr.x + cw*2, hdr.y, cw, 16), "After",    _gridHeaderStyle);
            GUI.Label(new Rect(hdr.x + cw*3, hdr.y, cw, 16), "Reduction",_gridHeaderStyle);

            Rect sep = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sep, new Color(0.3f, 0.3f, 0.3f));

            DrawGridRow("Vertices",  s.stat_VertsBefore,    s.stat_VertsAfter);
            DrawGridRow("Triangles", s.stat_TrisBefore / 3, s.stat_TrisAfter / 3);

            sep = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sep, new Color(0.25f, 0.25f, 0.25f));

            Rect mem = EditorGUILayout.GetControlRect(false, 18);
            cw = mem.width / 4f;
            GUI.Label(new Rect(mem.x,        mem.y, cw, 18), "Memory",            _gridLabelStyle);
            GUI.Label(new Rect(mem.x + cw,   mem.y, cw, 18), s.stat_MemoryBefore, _gridValueStyle);
            GUI.Label(new Rect(mem.x + cw*2, mem.y, cw, 18), s.stat_MemoryAfter,  _gridValueStyle);

            Rect ext = EditorGUILayout.GetControlRect(false, 18);
            cw = ext.width / 4f;
            GUI.Label(new Rect(ext.x,        ext.y, cw, 18), "Bake Time",     _gridLabelStyle);
            GUI.Label(new Rect(ext.x + cw,   ext.y, cw, 18), s.stat_BakeTime, _gridValueStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawGridRow(string label, int before, int after)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 18);
            float cw = row.width / 4f;

            GUI.Label(new Rect(row.x,        row.y, cw, 18), label,               _gridLabelStyle);
            GUI.Label(new Rect(row.x + cw,   row.y, cw, 18), before.ToString("N0"), _gridValueStyle);
            GUI.Label(new Rect(row.x + cw*2, row.y, cw, 18), after.ToString("N0"),  _gridValueStyle);

            if (before > 0)
            {
                float pct = 100f * (before - after) / before;
                Color c = pct > 50 ? new Color(0.3f, 0.9f, 0.3f) : pct > 20 ? new Color(0.9f, 0.8f, 0.3f) : new Color(0.7f, 0.7f, 0.7f);
                var prev = _gridValueStyle.normal.textColor;
                _gridValueStyle.normal.textColor = c;
                GUI.Label(new Rect(row.x + cw*3, row.y, cw, 18), $"v {pct:F0}%", _gridValueStyle);
                _gridValueStyle.normal.textColor = prev;
            }
        }

        private void DrawLayerOverrides(Optimizer s)
        {
            _foldLayerOverrides = EditorGUILayout.Foldout(
                _foldLayerOverrides,
                new GUIContent("Layer Overrides"),
                true);

            if (!_foldLayerOverrides) return;

            EditorGUI.indentLevel++;
            s.layerOverridePriority = EditorGUILayout.IntField(
                new GUIContent("Priority", "Layer override priority applied to generated colliders."),
                s.layerOverridePriority);

            s.includeLayers = DrawLayerMaskField(
                new GUIContent("Include Layers", "Layers this collider can collide with."),
                s.includeLayers);

            s.excludeLayers = DrawLayerMaskField(
                new GUIContent("Exclude Layers", "Layers this collider should not collide with."),
                s.excludeLayers);
            EditorGUI.indentLevel--;
        }

        private static LayerMask DrawLayerMaskField(GUIContent label, LayerMask value)
        {
            string[] layerNames = new string[32];
            for (int i = 0; i < 32; i++)
            {
                string ln = LayerMask.LayerToName(i);
                layerNames[i] = string.IsNullOrEmpty(ln) ? $"Layer {i}" : $"{i}: {ln}";
            }

            int mask = EditorGUILayout.MaskField(label, value.value, layerNames);
            value.value = mask;
            return value;
        }

        private static void DrawColliderWarnings(Optimizer s)
        {
            if (s.isTrigger && !s.convex)
                EditorGUILayout.HelpBox("Non-convex triggers are not directly supported. Compound convex colliders will be generated automatically.", MessageType.Info);

            var rb = s.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic && !s.convex)
            {
                EditorGUILayout.HelpBox("Non-convex MeshCollider + non-kinematic Rigidbody is not supported by PhysX.", MessageType.Warning);
                if (GUILayout.Button("Make Rigidbody Kinematic"))
                { Undo.RecordObject(rb, "Set Kinematic"); rb.isKinematic = true; EditorUtility.SetDirty(rb); }
            }
        }

        private static void QuickPresetBtn(Optimizer s, string label, System.Func<OptimizationPreset> factory)
        {
            if (GUILayout.Button(new GUIContent(label, $"Apply the '{label}' preset."), GUILayout.Height(22)))
            { Undo.RecordObject(s, $"Preset: {label}"); s.ApplyPreset(factory()); EditorUtility.SetDirty(s); if (s.autoUpdate) s.RequestAutoOptimization(); }
        }
        
        private static void ApplyToLoDs(Optimizer src, LODGroup grp)
        {
            var lods = grp.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                foreach (var rend in lods[i].renderers)
                {
                    if (rend == null) continue;
                    var opt = rend.GetComponent<Optimizer>() ?? Undo.AddComponent<Optimizer>(rend.gameObject);
                    opt.convex              = src.convex;
                    opt.isTrigger           = src.isTrigger;
                    opt.material            = src.material;
                    opt.layerOverridePriority = src.layerOverridePriority;
                    opt.includeLayers       = src.includeLayers;
                    opt.excludeLayers       = src.excludeLayers;
                    opt.cookingOptions      = src.cookingOptions;
                    opt.providesContacts    = src.providesContacts;
                    opt.decompositionGrid  = src.decompositionGrid;
                    opt.inflationAmount     = src.inflationAmount;
                    opt.strict256Guard      = src.strict256Guard;
                    opt.quality              = Mathf.Clamp(src.quality * (1f - i * 0.2f), 0.1f, 1f);
                    opt.autoUpdate          = false;
                    opt.RequestOptimization();
                }
            }
        }

        private static void SaveMesh(Mesh mesh, string objName)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Mesh", "Collider_" + objName, "asset", "Save the generated collider mesh as a project asset.");
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.CreateAsset(Instantiate(mesh), path);
            AssetDatabase.SaveAssets();
        }
    }
}

