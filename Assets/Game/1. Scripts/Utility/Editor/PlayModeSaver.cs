// PlayModeSaver
// Editor-side logic for PlayModeChangeRecorder.
//
// Flow:
//   ExitingEditMode  -> snapshot baseline (pure edit-mode values, before any Awake/Start runs)
//   ExitingPlayMode  -> snapshot current values, diff against baseline, keep ONLY the changed props
//   EnteredEditMode  -> restore the changed props, optionally apply each one as a prefab override
//
// Previous version dumped every serialized property of every component and applied the whole
// hierarchy with PrefabUtility.ApplyPrefabInstance. That turned runtime state (particle systems
// mutated by gameplay code, faded colors, curves, activeSelf toggles) into hundreds of prefab
// overrides on nested effect prefabs, so edits to the source effect prefab were shadowed forever.
//
// Object reference fields are intentionally skipped so scene references are never broken.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Yeolha.Utility;

namespace Yeolha.EditorTools
{
    [InitializeOnLoad]
    internal static class PlayModeSaver
    {
        private const string BaselineKey = "PlayModeSaver.Baseline";
        private const string DiffKey = "PlayModeSaver.Diff";
        private const float FloatTolerance = 1e-4f;

        // Components whose serialized state is driven by runtime code (playback, physics, fades).
        // Changes on these are never treated as user tweaks. Tune them in the source prefab instead.
        // Objects carrying any of these also get their active state ignored (gameplay toggles them).
        private static readonly HashSet<Type> SkipTypes = new HashSet<Type>
        {
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(TrailRenderer),
            typeof(LineRenderer),
            typeof(Animator),
            typeof(Animation),
            typeof(Rigidbody),
            typeof(Rigidbody2D),
            typeof(AudioSource),
        };

        // Effect components whose property edits ARE recorded when the recorder opts in
        // (PlayModeChangeRecorder.includeEffectComponents). Their active state stays ignored.
        private static readonly HashSet<Type> EffectTypes = new HashSet<Type>
        {
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(TrailRenderer),
            typeof(LineRenderer),
        };

        static PlayModeSaver()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ---------- Snapshot data ----------

        [Serializable] private class PropData { public string path; public int kind; public string value; }
        [Serializable] private class CompData { public string typeName; public int typeIndex; public List<PropData> props = new List<PropData>(); }
        [Serializable] private class ObjData { public string relativePath; public bool activeSelf; public bool activeChanged; public bool skipActive; public List<CompData> comps = new List<CompData>(); }
        [Serializable] private class RootData { public string rootPath; public bool applyToPrefab; public bool includeEffects; public List<ObjData> objects = new List<ObjData>(); }
        [Serializable] private class Snapshot { public List<RootData> roots = new List<RootData>(); }

        private enum Kind
        {
            Float = 0, Int = 1, Bool = 2, String = 3, Enum = 4,
            Vector2 = 5, Vector3 = 6, Vector4 = 7, Quaternion = 8,
            Color = 9, Rect = 10, Bounds = 11, ArraySize = 12,
            Vector2Int = 13, Vector3Int = 14, LayerMask = 15, Char = 16
        }

        // ---------- Play mode hooks ----------

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    CaptureBaseline();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    CaptureDiff();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    // Defer until the editor finishes restoring scene state
                    // (prefab apply inside this callback caused native crashes).
                    EditorApplication.delayCall += Restore;
                    break;
            }
        }

        private static Snapshot CaptureAll()
        {
            var recorders = UnityEngine.Object.FindObjectsByType<PlayModeChangeRecorder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var snap = new Snapshot();
            foreach (var rec in recorders)
            {
                if (!rec.recordChanges) continue;
                var rd = new RootData
                {
                    rootPath = GetHierarchyPath(rec.transform),
                    applyToPrefab = rec.applyToPrefab,
                    includeEffects = rec.includeEffectComponents
                };
                CaptureRecursive(rec.transform, "", rd);
                snap.roots.Add(rd);
            }
            return snap;
        }

        private static void CaptureBaseline()
        {
            SessionState.SetString(DiffKey, "");
            var snap = CaptureAll();
            SessionState.SetString(BaselineKey, snap.roots.Count > 0 ? JsonUtility.ToJson(snap) : "");
        }

        private static void CaptureDiff()
        {
            var baseRaw = SessionState.GetString(BaselineKey, "");
            SessionState.SetString(BaselineKey, "");
            if (string.IsNullOrEmpty(baseRaw)) return;

            var baseline = JsonUtility.FromJson<Snapshot>(baseRaw);
            var current = CaptureAll();
            if (baseline == null || current.roots.Count == 0) return;

            var baseRoots = new Dictionary<string, RootData>();
            foreach (var r in baseline.roots) baseRoots[r.rootPath] = r;

            var diff = new Snapshot();
            int changedProps = 0;
            foreach (var cur in current.roots)
            {
                if (!baseRoots.TryGetValue(cur.rootPath, out var bas)) continue; // recorder added during play, ignore

                var baseObjs = new Dictionary<string, ObjData>();
                foreach (var o in bas.objects) baseObjs[o.relativePath] = o;

                var rd = new RootData { rootPath = cur.rootPath, applyToPrefab = cur.applyToPrefab, includeEffects = cur.includeEffects };
                foreach (var co in cur.objects)
                {
                    if (!baseObjs.TryGetValue(co.relativePath, out var bo)) continue; // object added during play, ignore

                    var od = new ObjData
                    {
                        relativePath = co.relativePath,
                        activeSelf = co.activeSelf,
                        activeChanged = !co.skipActive && co.activeSelf != bo.activeSelf
                    };

                    var baseComps = new Dictionary<string, CompData>();
                    foreach (var c in bo.comps) baseComps[c.typeName + "#" + c.typeIndex] = c;

                    foreach (var cc in co.comps)
                    {
                        if (!baseComps.TryGetValue(cc.typeName + "#" + cc.typeIndex, out var bc)) continue;
                        var baseProps = new Dictionary<string, PropData>();
                        foreach (var p in bc.props) baseProps[p.path] = p;

                        var cd = new CompData { typeName = cc.typeName, typeIndex = cc.typeIndex };
                        foreach (var p in cc.props)
                        {
                            if (baseProps.TryGetValue(p.path, out var bp) && SameValue(bp, p)) continue;
                            cd.props.Add(p);
                        }
                        if (cd.props.Count > 0) { od.comps.Add(cd); changedProps += cd.props.Count; }
                    }

                    if (od.activeChanged || od.comps.Count > 0) rd.objects.Add(od);
                }
                if (rd.objects.Count > 0) diff.roots.Add(rd);
            }

            if (diff.roots.Count == 0)
            {
                Debug.Log("[PlayModeSaver] No play mode changes detected.");
                return;
            }

            SessionState.SetString(DiffKey, JsonUtility.ToJson(diff));
            Debug.Log($"[PlayModeSaver] Captured {changedProps} changed property(ies) in {diff.roots.Count} recorder hierarchy(ies). Restoring after play mode exits.");
        }

        private static bool SameValue(PropData a, PropData b)
        {
            if (a.kind != b.kind) return false;
            if (a.value == b.value) return true;
            switch ((Kind)a.kind)
            {
                case Kind.Float:
                case Kind.Vector2:
                case Kind.Vector3:
                case Kind.Vector4:
                case Kind.Quaternion:
                case Kind.Color:
                case Kind.Rect:
                    return NearlyEqual(a.value, b.value);
                case Kind.Bounds:
                {
                    var ah = a.value.Split(';'); var bh = b.value.Split(';');
                    return ah.Length == 2 && bh.Length == 2 && NearlyEqual(ah[0], bh[0]) && NearlyEqual(ah[1], bh[1]);
                }
                default:
                    return false;
            }
        }

        private static bool NearlyEqual(string a, string b)
        {
            var fa = F(a); var fb = F(b);
            if (fa.Length != fb.Length) return false;
            for (int i = 0; i < fa.Length; i++)
                if (Mathf.Abs(fa[i] - fb[i]) > FloatTolerance) return false;
            return true;
        }

        private static void CaptureRecursive(Transform t, string relPath, RootData rd)
        {
            var od = new ObjData { relativePath = relPath, activeSelf = t.gameObject.activeSelf };
            var typeCount = new Dictionary<string, int>();
            foreach (var comp in t.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                var typeName = type.AssemblyQualifiedName;
                typeCount.TryGetValue(typeName, out var idx);
                typeCount[typeName] = idx + 1;

                if (SkipTypes.Contains(type))
                {
                    // Effect / playback objects: their active state is toggled by gameplay code too.
                    od.skipActive = true;
                    // Effect components are recorded only when the recorder opted in.
                    if (!(rd.includeEffects && EffectTypes.Contains(type)))
                        continue;
                }

                var cd = new CompData { typeName = typeName, typeIndex = idx };
                var so = new SerializedObject(comp);
                var prop = so.GetIterator();
                var enterChildren = true;
                while (prop.Next(enterChildren))
                {
                    enterChildren = true;
                    if (prop.propertyType == SerializedPropertyType.ObjectReference ||
                        prop.propertyType == SerializedPropertyType.ManagedReference ||
                        prop.propertyType == SerializedPropertyType.ExposedReference ||
                        // Curves / gradients are not restored; do not descend into their keyframe arrays
                        // (a ParticleSystem hierarchy otherwise snapshots to ~100 MB of keyframe floats).
                        prop.propertyType == SerializedPropertyType.AnimationCurve ||
                        prop.propertyType == SerializedPropertyType.Gradient)
                    {
                        enterChildren = false;
                        continue;
                    }
                    var pd = ReadProp(prop);
                    if (pd != null) cd.props.Add(pd);
                }
                if (cd.props.Count > 0) od.comps.Add(cd);
            }
            rd.objects.Add(od);

            // Sibling names are not unique in effect hierarchies (e.g. two "circle00_sp2" under one ATK_02).
            // A plain name path made the diff compare one sibling against the other's baseline (spurious
            // changes) and Restore write the second sibling's values into the first. Disambiguate with "#n".
            var nameCount = new Dictionary<string, int>();
            foreach (Transform child in t)
            {
                nameCount.TryGetValue(child.name, out var dup);
                nameCount[child.name] = dup + 1;
                var segment = dup > 0 ? child.name + "#" + dup : child.name;
                var childPath = string.IsNullOrEmpty(relPath) ? segment : relPath + "/" + segment;
                CaptureRecursive(child, childPath, rd);
            }
        }

        // Resolves a relative path produced by CaptureRecursive ("a/b#1/c" = second child named "b").
        private static Transform FindRelative(Transform root, string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return root;
            var cur = root;
            foreach (var seg in relPath.Split('/'))
            {
                var name = seg;
                var index = 0;
                var hash = seg.LastIndexOf('#');
                if (hash > 0 && int.TryParse(seg.Substring(hash + 1), out var parsed)) { name = seg.Substring(0, hash); index = parsed; }
                Transform next = null;
                var seen = 0;
                foreach (Transform child in cur)
                {
                    if (child.name != name) continue;
                    if (seen == index) { next = child; break; }
                    seen++;
                }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        private static PropData ReadProp(SerializedProperty p)
        {
            var ci = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    return Make(p, Kind.Float, p.doubleValue.ToString("R", ci));
                case SerializedPropertyType.Integer:
                    return Make(p, Kind.Int, p.longValue.ToString(ci));
                case SerializedPropertyType.Boolean:
                    return Make(p, Kind.Bool, p.boolValue ? "1" : "0");
                case SerializedPropertyType.String:
                    return Make(p, Kind.String, p.stringValue);
                case SerializedPropertyType.Enum:
                    return Make(p, Kind.Enum, p.intValue.ToString(ci));
                case SerializedPropertyType.Vector2:
                    return Make(p, Kind.Vector2, V(p.vector2Value.x, p.vector2Value.y));
                case SerializedPropertyType.Vector3:
                    return Make(p, Kind.Vector3, V(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z));
                case SerializedPropertyType.Vector4:
                    return Make(p, Kind.Vector4, V(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w));
                case SerializedPropertyType.Quaternion:
                    var q = p.quaternionValue;
                    return Make(p, Kind.Quaternion, V(q.x, q.y, q.z, q.w));
                case SerializedPropertyType.Color:
                    var c = p.colorValue;
                    return Make(p, Kind.Color, V(c.r, c.g, c.b, c.a));
                case SerializedPropertyType.Rect:
                    var r = p.rectValue;
                    return Make(p, Kind.Rect, V(r.x, r.y, r.width, r.height));
                case SerializedPropertyType.Bounds:
                    var b = p.boundsValue;
                    return Make(p, Kind.Bounds, V(b.center.x, b.center.y, b.center.z) + ";" + V(b.size.x, b.size.y, b.size.z));
                case SerializedPropertyType.Vector2Int:
                    return Make(p, Kind.Vector2Int, p.vector2IntValue.x + "," + p.vector2IntValue.y);
                case SerializedPropertyType.Vector3Int:
                    var vi = p.vector3IntValue;
                    return Make(p, Kind.Vector3Int, vi.x + "," + vi.y + "," + vi.z);
                case SerializedPropertyType.LayerMask:
                    return Make(p, Kind.LayerMask, p.intValue.ToString(ci));
                case SerializedPropertyType.ArraySize:
                    return Make(p, Kind.ArraySize, p.intValue.ToString(ci));
                case SerializedPropertyType.Character:
                    return Make(p, Kind.Char, p.intValue.ToString(ci));
                default:
                    return null;
            }
        }

        private static PropData Make(SerializedProperty p, Kind k, string v)
            => new PropData { path = p.propertyPath, kind = (int)k, value = v };

        private static string V(params float[] vals)
        {
            var s = new string[vals.Length];
            for (int i = 0; i < vals.Length; i++) s[i] = vals[i].ToString("R", CultureInfo.InvariantCulture);
            return string.Join(",", s);
        }

        // ---------- Restore ----------

        private static void Restore()
        {
            var raw = SessionState.GetString(DiffKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            SessionState.SetString(DiffKey, "");

            var snap = JsonUtility.FromJson<Snapshot>(raw);
            if (snap == null || snap.roots.Count == 0) return;

            foreach (var rd in snap.roots)
            {
                var rootGo = FindByPath(rd.rootPath);
                if (rootGo == null)
                {
                    Debug.LogWarning($"[PlayModeSaver] Root not found in edit mode: {rd.rootPath}");
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(rootGo, "PlayModeSaver Restore");

                string prefabPath = null;
                if (rd.applyToPrefab)
                {
                    var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(rootGo);
                    if (outermost != null)
                        prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outermost);
                    if (string.IsNullOrEmpty(prefabPath))
                        Debug.LogWarning($"[PlayModeSaver] '{rd.rootPath}' is not part of a prefab instance, skipped prefab apply.");
                }

                int applied = 0, missing = 0, appliedToPrefab = 0;
                var detail = new System.Text.StringBuilder();
                foreach (var od in rd.objects)
                {
                    var target = FindRelative(rootGo.transform, od.relativePath);
                    if (target == null) { missing++; continue; }

                    if (od.activeChanged && target.gameObject.activeSelf != od.activeSelf)
                    {
                        var goSo = new SerializedObject(target.gameObject);
                        var activeProp = goSo.FindProperty("m_IsActive");
                        activeProp.boolValue = od.activeSelf;
                        goSo.ApplyModifiedProperties();
                        if (prefabPath != null && TryApplyOverride(activeProp, prefabPath)) appliedToPrefab++;
                    }

                    var comps = target.GetComponents<Component>();
                    foreach (var cd in od.comps)
                    {
                        var comp = FindComponent(comps, cd);
                        if (comp == null) { missing++; detail.AppendLine($"  - {od.relativePath} :: {ShortType(cd.typeName)} (component missing)"); continue; }
                        appliedToPrefab += ApplyComp(comp, cd, prefabPath, od.relativePath, detail);
                        applied++;
                    }
                }

                EditorUtility.SetDirty(rootGo);

                Debug.Log($"[PlayModeSaver] Restored '{rd.rootPath}': {applied} component(s) updated" +
                          (prefabPath != null ? $", {appliedToPrefab} property override(s) applied to '{prefabPath}'" : "") +
                          (missing > 0 ? $", {missing} target(s) missing (objects added/removed during play are not supported)." : ".") +
                          (detail.Length > 0 ? "\n" + detail.ToString().TrimEnd() : ""));
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private static Component FindComponent(Component[] comps, CompData cd)
        {
            int idx = 0;
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                if (comp.GetType().AssemblyQualifiedName != cd.typeName) continue;
                if (idx == cd.typeIndex) return comp;
                idx++;
            }
            return null;
        }

        private static string ShortType(string assemblyQualifiedName)
        {
            var comma = assemblyQualifiedName.IndexOf(',');
            var full = comma > 0 ? assemblyQualifiedName.Substring(0, comma) : assemblyQualifiedName;
            var dot = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
        }

        // Returns number of properties applied to the prefab asset. Appends one line per property to detail.
        private static int ApplyComp(Component comp, CompData cd, string prefabPath, string objPath, System.Text.StringBuilder detail)
        {
            var so = new SerializedObject(comp);
            var ci = CultureInfo.InvariantCulture;
            var written = new List<string>();

            foreach (var pd in cd.props)
            {
                var p = so.FindProperty(pd.path);
                if (p == null) continue;
                var k = (Kind)pd.kind;
                try
                {
                    switch (k)
                    {
                        case Kind.Float: p.doubleValue = double.Parse(pd.value, ci); break;
                        case Kind.Int: p.longValue = long.Parse(pd.value, ci); break;
                        case Kind.Bool: p.boolValue = pd.value == "1"; break;
                        case Kind.String: p.stringValue = pd.value; break;
                        case Kind.Enum: p.intValue = int.Parse(pd.value, ci); break;
                        case Kind.Vector2: { var f = F(pd.value); p.vector2Value = new Vector2(f[0], f[1]); break; }
                        case Kind.Vector3: { var f = F(pd.value); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                        case Kind.Vector4: { var f = F(pd.value); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
                        case Kind.Quaternion: { var f = F(pd.value); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); break; }
                        case Kind.Color: { var f = F(pd.value); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
                        case Kind.Rect: { var f = F(pd.value); p.rectValue = new Rect(f[0], f[1], f[2], f[3]); break; }
                        case Kind.Bounds:
                        {
                            var halves = pd.value.Split(';');
                            var cf = F(halves[0]); var sf = F(halves[1]);
                            p.boundsValue = new Bounds(new Vector3(cf[0], cf[1], cf[2]), new Vector3(sf[0], sf[1], sf[2]));
                            break;
                        }
                        case Kind.Vector2Int: { var f = I(pd.value); p.vector2IntValue = new Vector2Int(f[0], f[1]); break; }
                        case Kind.Vector3Int: { var f = I(pd.value); p.vector3IntValue = new Vector3Int(f[0], f[1], f[2]); break; }
                        case Kind.LayerMask: p.intValue = int.Parse(pd.value, ci); break;
                        case Kind.ArraySize: p.arraySize = int.Parse(pd.value, ci); break;
                        case Kind.Char: p.intValue = int.Parse(pd.value, ci); break;
                    }
                    written.Add(pd.path);
                }
                catch (Exception)
                {
                    // Skip properties whose layout changed between play and edit mode.
                }
            }
            so.ApplyModifiedProperties();

            string typeShort = ShortType(cd.typeName);
            string objLabel = string.IsNullOrEmpty(objPath) ? "(root)" : objPath;
            if (prefabPath == null)
            {
                foreach (var path in written) detail.AppendLine($"  - {objLabel} :: {typeShort}.{path} (scene only)");
                return 0;
            }
            int count = 0;
            so.Update();
            foreach (var path in written)
            {
                var p = so.FindProperty(path);
                if (p == null) continue;
                string state;
                if (!p.prefabOverride) state = "same as prefab, nothing to apply";
                else if (TryApplyOverride(p, prefabPath)) { count++; state = "applied to prefab"; }
                else state = "apply FAILED";
                detail.AppendLine($"  - {objLabel} :: {typeShort}.{path} = {ValueString(p)} ({state})");
            }
            return count;
        }

        private static string ValueString(SerializedProperty p)
        {
            var pd = ReadProp(p);
            return pd != null ? pd.value : "?";
        }

        private static bool TryApplyOverride(SerializedProperty p, string prefabPath)
        {
            try
            {
                if (!p.prefabOverride) return false;
                PrefabUtility.ApplyPropertyOverride(p, prefabPath, InteractionMode.AutomatedAction);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayModeSaver] Could not apply '{p.propertyPath}' to '{prefabPath}': {e.Message}");
                return false;
            }
        }

        private static float[] F(string s)
        {
            var parts = s.Split(',');
            var f = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) f[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return f;
        }

        private static int[] I(string s)
        {
            var parts = s.Split(',');
            var v = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) v[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            return v;
        }

        // ---------- Helpers ----------

        private static string GetHierarchyPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static GameObject FindByPath(string path)
        {
            var parts = path.Split('/');
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0]) continue;
                    var cur = root.transform;
                    var ok = true;
                    for (int i = 1; i < parts.Length && ok; i++)
                    {
                        var child = cur.Find(parts[i]);
                        if (child == null) ok = false;
                        else cur = child;
                    }
                    if (ok) return cur.gameObject;
                }
            }
            return null;
        }
    }
}
