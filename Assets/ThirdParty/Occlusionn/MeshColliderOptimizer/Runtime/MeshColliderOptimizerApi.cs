using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// User-friendly helper API for MeshColliderOptimizer.
    /// Reflection-based so it keeps working even if the optimizer namespace/class gets refactored.
    /// </summary>
    public static class MeshColliderOptimizerApi
    {
        #region Public - Discovery

        /// <summary>
        /// Returns true if MeshColliderOptimizer is present in the project.
        /// </summary>
        public static bool IsInstalled() => TryGetOptimizerType(out _);

        /// <summary>
        /// Gets an existing MeshColliderOptimizer component on the given GameObject (or null if missing).
        /// </summary>
        public static Component GetOptimizer(GameObject go)
        {
            if (go == null) return null;
            if (!TryGetOptimizerType(out var t)) return null;
            return go.GetComponent(t);
        }

        /// <summary>
        /// Gets an existing MeshColliderOptimizer component on the given GameObject;
        /// if missing, adds it and returns it.
        /// </summary>
        public static Component GetOrAddOptimizer(GameObject go)
        {
            if (go == null) return null;
            if (!TryGetOptimizerType(out var t)) return null;

            var existing = go.GetComponent(t);
            if (existing != null) return existing;

            return go.AddComponent(t);
        }

        /// <summary>
        /// Finds all MeshColliderOptimizer components in the active scene(s).
        /// If includeInactive is true, includes inactive objects too.
        /// </summary>
        public static List<Component> FindAllOptimizersInScene(bool includeInactive = false)
        {
            var list = new List<Component>();
            if (!TryGetOptimizerType(out var t)) return list;

            var all = Resources.FindObjectsOfTypeAll(t);
            foreach (var obj in all)
            {
                if (obj is not Component c) continue;
                var go = c.gameObject;
                if (!go.scene.IsValid()) continue;
                if (!includeInactive && !go.activeInHierarchy) continue;
                list.Add(c);
            }

            list.Sort((a, b) =>
            {
                var an = a != null ? a.name : "";
                var bn = b != null ? b.name : "";
                return string.Compare(an, bn, StringComparison.Ordinal);
            });

            return list;
        }

        #endregion

        #region Public - Optimize / Clear

        /// <summary>
        /// Triggers optimization. Calls the best available method in the optimizer (RequestOptimization/Optimize/Regenerate).
        /// If immediate is supported by the underlying method, it will be passed through.
        /// </summary>
        public static void Optimize(Component optimizer, bool immediate = false)
        {
            if (optimizer == null) return;

            if (TryInvoke(optimizer, "RequestOptimization", immediate)) return;
            if (TryInvoke(optimizer, "RequestOptimization")) return;

            if (TryInvoke(optimizer, "Optimize", immediate)) return;
            if (TryInvoke(optimizer, "Optimize")) return;

            if (TryInvoke(optimizer, "Regenerate", immediate)) return;
            if (TryInvoke(optimizer, "Regenerate")) return;
        }

        /// <summary>
        /// Triggers optimization and, if the optimizer exposes a busy flag, waits until it becomes idle.
        /// If no busy flag is found, this method will trigger and return immediately.
        /// </summary>
        public static async Task OptimizeAsync(Component optimizer, bool immediate = false, int maxFrames = 600)
        {
            Optimize(optimizer, immediate);

            if (optimizer == null) return;

            if (!TryGetBusyAccessor(optimizer, out var isBusy))
                return;

            int frames = 0;
            while (frames < maxFrames && isBusy())
            {
                frames++;
                await Task.Yield();
            }
        }

        /// <summary>
        /// Clears generated colliders produced by the system.
        /// If the optimizer exposes an internal clear method, it will be used.
        /// Otherwise, falls back to destroying Hidden_Root_Collider and Convex_Part_* objects.
        /// </summary>
        public static void ClearGenerated(Component optimizer, bool alsoRemoveComponent = false)
        {
            if (optimizer == null) return;

            if (TryInvoke(optimizer, "ClearGenerated", alsoRemoveComponent)) return;
            if (TryInvoke(optimizer, "ClearGenerated")) return;

            if (TryInvoke(optimizer, "DestroyGenerated", alsoRemoveComponent)) return;
            if (TryInvoke(optimizer, "DestroyGenerated")) return;

            if (TryInvoke(optimizer, "DestroyConvexChildrenAndMeshes")) return;
            if (TryInvoke(optimizer, "RemoveGeneratedColliders")) return;

            FallbackDestroyHiddenRoot(optimizer.gameObject);

            if (alsoRemoveComponent)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(optimizer);
                else UnityEngine.Object.DestroyImmediate(optimizer);
            }
        }

        #endregion

        #region Public - Settings Helpers

        /// <summary>
        /// Convenience setter for common settings in one call.
        /// Tries multiple likely field/property names to be resilient to refactors.
        /// </summary>
        public static void SetCommonSettings(
            Component optimizer,
            float? quality = null,
            bool? convex = null,
            bool? isTrigger = null,
            int? decompositionGrid = null,
            float? inflationAmount = null,
            bool? boxFallback = null)
        {
            if (optimizer == null) return;
            _ = boxFallback; // legacy no-op (box fallback removed)

            if (quality.HasValue)
                TrySetFloat(optimizer, quality.Value, "quality", "Quality");

            if (convex.HasValue)
                TrySetBool(optimizer, convex.Value, "convex", "Convex", "useConvex", "UseConvex");

            if (isTrigger.HasValue)
                TrySetBool(optimizer, isTrigger.Value, "isTrigger", "IsTrigger");

            if (decompositionGrid.HasValue)
                TrySetInt(optimizer, decompositionGrid.Value, "decompositionGrid", "DecompositionGrid", "grid", "Grid");

            if (inflationAmount.HasValue)
                TrySetFloat(optimizer, inflationAmount.Value, "inflationAmount", "InflationAmount", "inflateAmount", "InflateAmount");
        }

        /// <summary>
        /// Enables/disables "colliders follow active LOD" behavior.
        /// When enabled, only the currently active LOD's colliders should remain enabled.
        /// If applyNow is true, attempts to call the optimizer's refresh/apply method immediately.
        /// </summary>
        public static void SetCollidersFollowActiveLod(Component optimizer, bool enabled, bool applyNow = true)
        {
            if (optimizer == null) return;

            TrySetBool(optimizer, enabled,
                "collidersFollowActiveLod",
                "CollidersFollowActiveLod",
                "enableCollidersOnlyOnActiveLod",
                "EnableCollidersOnlyOnActiveLod",
                "followActiveLodColliders",
                "FollowActiveLodColliders");

            if (!applyNow) return;

            if (TryInvoke(optimizer, "RefreshLodColliderState")) return;
            if (TryInvoke(optimizer, "ApplyLodColliderState")) return;
            if (TryInvoke(optimizer, "UpdateLodColliderState")) return;
        }

        /// <summary>
        /// Forces the optimizer to refresh its cached list of generated colliders (if supported).
        /// </summary>
        public static void RefreshGeneratedList(Component optimizer)
        {
            if (optimizer == null) return;

            if (TryInvoke(optimizer, "RefreshGeneratedColliderList")) return;
            if (TryInvoke(optimizer, "RefreshGeneratedList")) return;
            if (TryInvoke(optimizer, "RebuildGeneratedList")) return;
        }

        /// <summary>
        /// Returns a snapshot list of generated colliders if the optimizer exposes it.
        /// If unsupported, returns an empty list.
        /// </summary>
        public static List<Collider> GetGeneratedCollidersSnapshot(Component optimizer)
        {
            var result = new List<Collider>();
            if (optimizer == null) return result;

            if (TryInvokeWithReturn(optimizer, "GetGeneratedCollidersSnapshot", out var ret) && ret is IEnumerable<Collider> cols)
            {
                result.AddRange(cols.Where(c => c != null));
                return result;
            }

            if (TryReadListFieldOrProperty(optimizer, out var anyList, "generatedColliders", "GeneratedColliders"))
            {
                foreach (var c in anyList)
                {
                    if (c is Collider col && col != null) result.Add(col);
                }
            }

            return result;
        }

        #endregion

        #region Public - Preset Helpers (Editor)

#if UNITY_EDITOR
        /// <summary>
        /// Loads an OptimizationPreset asset by asset path (Editor-only).
        /// </summary>
        public static ScriptableObject LoadPresetAtPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            var presetType = FindTypeByNameSuffix("OptimizationPreset");
            if (presetType == null) return null;

            var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(assetPath, presetType);
            return obj as ScriptableObject;
        }

        /// <summary>
        /// Loads an OptimizationPreset asset by GUID (Editor-only).
        /// </summary>
        public static ScriptableObject LoadPresetByGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) return null;
            return LoadPresetAtPath(path);
        }

        /// <summary>
        /// Applies a preset to the currently selected GameObjects.
        /// If includeChildren is true, also applies to all children.
        /// If optimizeAfter is true, triggers optimization after applying.
        /// </summary>
        public static void ApplyPresetToSelection(ScriptableObject preset, bool includeChildren, bool optimizeAfter)
        {
            if (preset == null) return;

            var selection = UnityEditor.Selection.gameObjects;
            if (selection == null || selection.Length == 0) return;

            foreach (var root in selection)
            {
                if (root == null) continue;

                if (!includeChildren)
                {
                    var opt = GetOrAddOptimizer(root);
                    ApplyPreset(opt, preset, optimizeAfter);
                }
                else
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null) continue;
                        var opt = GetOrAddOptimizer(t.gameObject);
                        ApplyPreset(opt, preset, optimizeAfter);
                    }
                }
            }
        }

        /// <summary>
        /// Optimizes all optimizers in the scene (Editor-only).
        /// </summary>
        public static async Task OptimizeAllInSceneAsync(bool includeInactive = false, int maxFramesPerItem = 600)
        {
            var all = FindAllOptimizersInScene(includeInactive);
            foreach (var opt in all)
            {
                if (opt == null) continue;
                await OptimizeAsync(opt, immediate: false, maxFrames: maxFramesPerItem);
            }
        }
#endif

        /// <summary>
        /// Applies a preset to a given optimizer instance.
        /// Uses the best available method/property path inside the optimizer.
        /// If optimizeAfter is true, triggers optimization after applying.
        /// </summary>
        public static void ApplyPreset(Component optimizer, ScriptableObject preset, bool optimizeAfter)
        {
            if (optimizer == null || preset == null) return;

            if (TryInvoke(optimizer, "ApplyPreset", preset)) { if (optimizeAfter) Optimize(optimizer); return; }
            if (TrySetObject(optimizer, preset, "preset", "Preset", "optimizationPreset", "OptimizationPreset")) { if (optimizeAfter) Optimize(optimizer); return; }

            if (!TryInvoke(optimizer, "LoadPreset", preset)) return;
            if (optimizeAfter) Optimize(optimizer);
        }

        #endregion

        #region Internal - Type Resolve

        private static bool TryGetOptimizerType(out Type optimizerType)
        {
            optimizerType = FindTypeByNameSuffix("MeshColliderOptimizer");
            return optimizerType != null;
        }

        private static Type FindTypeByNameSuffix(string typeNameSuffix)
        {
            if (string.IsNullOrWhiteSpace(typeNameSuffix)) return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in assemblies)
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract) continue;

                    if (typeNameSuffix == "MeshColliderOptimizer" && !typeof(Component).IsAssignableFrom(t))
                        continue;

                    if (t.Name.Equals(typeNameSuffix, StringComparison.Ordinal))
                        return t;
                }
            }
            return null;
        }

        #endregion

        #region Internal - Busy Detection

        private static bool TryGetBusyAccessor(Component optimizer, out Func<bool> isBusy)
        {
            isBusy = null;
            if (optimizer == null) return false;

            var t = optimizer.GetType();

            var prop = t.GetProperty("IsBusy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? t.GetProperty("IsOptimizing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? t.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (prop != null && prop.PropertyType == typeof(bool) && prop.GetGetMethod(true) != null)
            {
                isBusy = () => (bool)prop.GetValue(optimizer);
                return true;
            }

            var field = t.GetField("isBusy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? t.GetField("isOptimizing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? t.GetField("_isOptimizing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? t.GetField("isRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null && field.FieldType == typeof(bool))
            {
                isBusy = () => (bool)field.GetValue(optimizer);
                return true;
            }

            return false;
        }

        #endregion

        #region Internal - Invoke Helpers

        private static bool TryInvoke(Component target, string methodName, params object[] args)
        {
            if (target == null) return false;
            if (string.IsNullOrWhiteSpace(methodName)) return false;

            var t = target.GetType();
            var m = FindBestMethod(t, methodName, args);
            if (m == null) return false;

            try { m.Invoke(target, args); return true; }
            catch { return false; }
        }

        private static bool TryInvokeWithReturn(Component target, string methodName, out object returnValue, params object[] args)
        {
            returnValue = null;

            if (target == null) return false;
            if (string.IsNullOrWhiteSpace(methodName)) return false;

            var t = target.GetType();
            var m = FindBestMethod(t, methodName, args);
            if (m == null) return false;

            try { returnValue = m.Invoke(target, args); return true; }
            catch { returnValue = null; return false; }
        }

        private static MethodInfo FindBestMethod(Type t, string methodName, object[] args)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = t.GetMethods(flags);
            int argCount = args?.Length ?? 0;

            foreach (var m in methods)
            {
                if (!m.Name.Equals(methodName, StringComparison.Ordinal)) continue;

                var ps = m.GetParameters();
                if (ps.Length != argCount) continue;

                bool ok = true;
                for (int pi = 0; pi < ps.Length; pi++)
                {
                    var pType = ps[pi].ParameterType;
                    if (args == null) continue;
                    var a = args[pi];

                    if (a == null)
                    {
                        if (pType.IsValueType) { ok = false; break; }
                        continue;
                    }

                    if (pType.IsInstanceOfType(a)) continue;
                    if (pType.IsEnum && a is int) continue;
                    if (pType == typeof(float) && a is double) continue;
                    ok = false;
                    break;
                }

                if (!ok) continue;
                return m;
            }

            return null;
        }

        #endregion

        #region Internal - Set Helpers

        private static void TrySetBool(Component target, bool value, params string[] names)
        {
            foreach (var n in names) if (TrySetValue(target, n, value))
                return;
        }

        private static void TrySetInt(Component target, int value, params string[] names)
        {
            foreach (var n in names) if (TrySetValue(target, n, value))
                return;
        }

        private static void TrySetFloat(Component target, float value, params string[] names)
        {
            foreach (var n in names) if (TrySetValue(target, n, value))
                return;
        }

        private static bool TrySetObject(Component target, object value, params string[] names)
        {
            foreach (var n in names) if (TrySetValue(target, n, value)) return true;
            return false;
        }

        private static bool TrySetValue(Component target, string name, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name)) return false;

            var t = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var prop = t.GetProperty(name, flags);
            if (prop != null && prop.CanWrite && prop.SetMethod != null)
            {
                if (CanAssign(prop.PropertyType, value))
                {
                    try { prop.SetValue(target, value); return true; }
                    catch
                    {
                        // ignored
                    }
                }
            }

            var field = t.GetField(name, flags);
            if (field != null)
            {
                if (CanAssign(field.FieldType, value))
                {
                    try { field.SetValue(target, value); return true; }
                    catch
                    {
                        // ignored
                    }
                }
            }

            return false;
        }

        private static bool CanAssign(Type targetType, object value)
        {
            if (value == null) return !targetType.IsValueType;
            if (targetType.IsInstanceOfType(value)) return true;
            if (targetType == typeof(float) && value is double) return true;
            if (targetType.IsEnum && value is int) return true;
            return false;
        }

        #endregion

        #region Internal - List Read

        private static bool TryReadListFieldOrProperty(Component target, out IEnumerable<object> list, params string[] names)
        {
            list = null;
            if (target == null) return false;

            var t = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = t.GetProperty(name, flags);
                if (prop != null && prop.CanRead && prop.GetMethod != null)
                {
                    try
                    {
                        var v = prop.GetValue(target);
                        if (v is System.Collections.IEnumerable en)
                        {
                            list = EnumerateObjects(en);
                            return true;
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                var field = t.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        var v = field.GetValue(target);
                        if (v is System.Collections.IEnumerable en)
                        {
                            list = EnumerateObjects(en);
                            return true;
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            return false;
        }

        private static IEnumerable<object> EnumerateObjects(System.Collections.IEnumerable en)
        {
            foreach (var o in en) yield return o;
        }

        #endregion

        #region Internal - Fallback Cleanup

        private static void FallbackDestroyHiddenRoot(GameObject owner)
        {
            if (owner == null) return;

            Transform hiddenRoot = owner.transform.Find("Hidden_Root_Collider");
            if (hiddenRoot != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(hiddenRoot.gameObject);
                else UnityEngine.Object.DestroyImmediate(hiddenRoot.gameObject);
                return;
            }

            var toDestroy = new List<GameObject>();
            for (int i = owner.transform.childCount - 1; i >= 0; i--)
            {
                var c = owner.transform.GetChild(i);
                if (c == null) continue;

                var n = c.name;
                if (n.StartsWith("Convex_Part_", StringComparison.Ordinal) ||
                    n.StartsWith("Hidden_Root_Collider", StringComparison.Ordinal))
                {
                    toDestroy.Add(c.gameObject);
                }
            }

            foreach (var go in toDestroy)
            {
                if (go == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion
    }
}

