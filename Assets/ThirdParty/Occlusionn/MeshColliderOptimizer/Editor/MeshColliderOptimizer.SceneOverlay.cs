using UnityEditor;
using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Editor
{
    /// <summary>
    /// Scene overlay fallback for animation-mode debug drawing when SceneView Gizmos toggle is off.
    /// </summary>
    [InitializeOnLoad]
    internal static class MeshColliderOptimizerSceneOverlay
    {
        private const string HiddenRootName = "Hidden_Root_Collider";
        private static readonly Color DefaultColliderColor = new Color(0.2f, 1f, 0.2f, 0.9f);

        static MeshColliderOptimizerSceneOverlay()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (sceneView == null) return;

            // Optimizer animation fallback is only needed when SceneView gizmos are disabled.
            if (!Application.isPlaying || sceneView.drawGizmos) return;

            if (DrawAnimationOptimizerOverlays())
                sceneView.Repaint();
        }

        private static bool DrawAnimationOptimizerOverlays()
        {
#if UNITY_2022_2_OR_NEWER
            var optimizers = Object.FindObjectsByType<global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer>(FindObjectsSortMode.None);
#else
            var optimizers = Object.FindObjectsOfType<global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer>();
#endif
            if (optimizers == null || optimizers.Length == 0) return false;

            bool drewAny = false;
            for (int i = 0; i < optimizers.Length; i++)
            {
                var opt = optimizers[i];
                if (opt == null || !opt.isActiveAndEnabled) continue;
                if (!opt.animationMode || !opt.showGizmos) continue;

                drewAny |= DrawOptimizer(opt);
            }

            return drewAny;
        }

        private static bool DrawOptimizer(global::Occlusionn.MeshColliderOptimizer.Runtime.MeshColliderOptimizer opt)
        {
            bool drewAny = false;

            float safeRatio = float.IsNaN(opt.savingsRatio) ? 1f : opt.savingsRatio;
            Color baseColor = Color.Lerp(
                new Color(1f, 0.1f, 0.1f, 0.9f),
                new Color(0.1f, 1f, 0.1f, 0.9f),
                safeRatio);

            var colliders = opt.generatedColliders;
            if (colliders != null && colliders.Count > 0)
            {
                for (int i = 0; i < colliders.Count; i++)
                {
                    var col = colliders[i];
                    if (col == null) continue;

                    if (DrawOptimizerCollider(col, baseColor))
                        drewAny = true;
                }

                return drewAny;
            }

            // Root-only animation fallback when list is empty.
            Transform hiddenRoot = opt.transform.Find(HiddenRootName);
            if (hiddenRoot == null) return false;

            var mc = hiddenRoot.GetComponent<MeshCollider>();
            if (mc == null || mc.sharedMesh == null) return false;

            return DrawWireMesh(mc.sharedMesh, mc.transform.localToWorldMatrix, baseColor);
        }

        private static bool DrawOptimizerCollider(Collider col, Color color)
        {
            if (col is MeshCollider mc)
            {
                if (mc.sharedMesh == null) return false;
                return DrawWireMesh(mc.sharedMesh, mc.transform.localToWorldMatrix, color);
            }

            if (col is BoxCollider bc)
            {
                Matrix4x4 matrix = Matrix4x4.TRS(
                    bc.transform.TransformPoint(bc.center),
                    bc.transform.rotation,
                    Vector3.Scale(bc.transform.lossyScale, bc.size));

                using (new Handles.DrawingScope(color, matrix))
                {
                    Handles.DrawWireCube(Vector3.zero, Vector3.one);
                }
                return true;
            }

            return false;
        }

        private static bool DrawWireMesh(Mesh mesh, Matrix4x4 matrix, Color color)
        {
            if (mesh == null) return false;

            Vector3[] vertices;
            int[] triangles;

            try
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            catch
            {
                return false;
            }

            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
                return false;

            using (new Handles.DrawingScope(color, matrix))
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];

                    if ((uint)a >= (uint)vertices.Length ||
                        (uint)b >= (uint)vertices.Length ||
                        (uint)c >= (uint)vertices.Length)
                    {
                        continue;
                    }

                    Handles.DrawLine(vertices[a], vertices[b]);
                    Handles.DrawLine(vertices[b], vertices[c]);
                    Handles.DrawLine(vertices[c], vertices[a]);
                }
            }

            return true;
        }
    }
}
