using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// SceneView gizmo and heatmap rendering helpers.
    /// </summary>
    public partial class MeshColliderOptimizer
    {
        #region Gizmos
        private const int PERFORMANCE_MODE_COLLIDER_THRESHOLD = 50;

        private void OnDrawGizmosSelected()
        {
            DrawGeneratedColliderGizmos();
        }

        private void DrawGeneratedColliderGizmos()
        {
            if (!ShouldDrawGizmosForCurrentCamera()) return;

            if (NeedsGeneratedColliderRefresh())
            {
                RefreshGeneratedColliderList();
            }

            if (generatedColliders == null || generatedColliders.Count == 0)
            {
                if (TryDrawHiddenRootFallback())
                    return;

                return;
            }

            bool isPerformanceMode = generatedColliders.Count > PERFORMANCE_MODE_COLLIDER_THRESHOLD;
            Color dynamicColor = GetDynamicGizmoColor();

            for (int i = 0; i < generatedColliders.Count; i++)
            {
                var col = generatedColliders[i];
                if (col == null) continue;

                if (isPerformanceMode)
                    DrawColliderGizmo_Performance(col, dynamicColor);
                else
                    DrawColliderGizmo_Quality(col, dynamicColor);
            }

            if (showHeatmap && !isPerformanceMode && _heatmapCache.Count > 0)
            {
                Matrix4x4 prevMatrix = Gizmos.matrix;
                Color prevColor = Gizmos.color;

                Gizmos.matrix = transform.localToWorldMatrix;
                for (int i = 0; i < _heatmapCache.Count; i++)
                {
                    var point = _heatmapCache[i];
                    Gizmos.color = point.color;
                    Gizmos.DrawCube(point.localPos, Vector3.one * 0.01f);
                    if (drawErrorLines && point.hasError)
                        Gizmos.DrawLine(point.localPos, point.localClosest);
                }

                Gizmos.matrix = prevMatrix;
                Gizmos.color = prevColor;
            }
        }

        private bool ShouldDrawGizmosForCurrentCamera()
        {
            if (!enabled || !showGizmos) return false;

            Camera gizmoCam = Camera.current;
            if (gizmoCam == null) return false;

            // In edit mode, avoid drawing when object is outside SceneView frustum.
            if (!Application.isPlaying && !IsVisibleToCamera(gizmoCam)) return false;

            return true;
        }

        private bool NeedsGeneratedColliderRefresh()
        {
            if (generatedColliders == null || generatedColliders.Count == 0)
                return true;

            for (int i = 0; i < generatedColliders.Count; i++)
            {
                if (generatedColliders[i] == null) return true;
            }

            return false;
        }

        private Color GetDynamicGizmoColor()
        {
            float safeRatio = float.IsNaN(savingsRatio) ? 1f : savingsRatio;
            return Color.Lerp(
                new Color(1f, 0.1f, 0.1f, 0.8f),
                new Color(0.1f, 1f, 0.1f, 0.8f),
                safeRatio);
        }

        // Ensures gizmo draw calls do not fail on malformed meshes.
        private static bool EnsureMeshDrawable(Mesh mesh)
        {
            if (mesh == null) return false;
            if (mesh.vertexCount == 0) return false;
            if (mesh.triangles == null || mesh.triangles.Length == 0) return false;

            // Gizmos.DrawMesh / DrawWireMesh require valid normals.
            if (mesh.normals == null || mesh.normals.Length != mesh.vertexCount)
            {
                mesh.RecalculateNormals();
            }
            return true;
        }

        // Draws hidden root collider when list is empty but generated root still exists.
        private bool TryDrawHiddenRootFallback()
        {
            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot == null) return false;

            var mc = hiddenRoot.GetComponent<MeshCollider>();
            if (mc == null || mc.sharedMesh == null) return false;
            if (!EnsureMeshDrawable(mc.sharedMesh)) return false;

            Matrix4x4 prevMatrix = Gizmos.matrix;
            Color prevColor = Gizmos.color;

            Gizmos.color = new Color(0.1f, 1f, 0.1f, 0.8f);
            Gizmos.matrix = hiddenRoot.localToWorldMatrix;
            Gizmos.DrawWireMesh(mc.sharedMesh);
            Gizmos.color = new Color(0.1f, 1f, 0.1f, 0.15f);
            Gizmos.DrawMesh(mc.sharedMesh);

            Gizmos.matrix = prevMatrix;
            Gizmos.color = prevColor;
            return true;
        }

        // Lightweight draw mode for large collider counts.
        private void DrawColliderGizmo_Performance(Collider col, Color dynamicColor)
        {
            if (col is MeshCollider mc && mc.sharedMesh != null && EnsureMeshDrawable(mc.sharedMesh))
            {
                Matrix4x4 prevMatrix = Gizmos.matrix;
                Color prevColor = Gizmos.color;

                Gizmos.matrix = mc.transform.localToWorldMatrix;
                Gizmos.color = new Color(dynamicColor.r, dynamicColor.g, dynamicColor.b, 0.35f);
                Gizmos.DrawWireMesh(mc.sharedMesh);

                Gizmos.matrix = prevMatrix;
                Gizmos.color = prevColor;
            }
            else if (col is BoxCollider bc)
            {
                Color prevColor = Gizmos.color;
                Gizmos.color = new Color(dynamicColor.r, dynamicColor.g, dynamicColor.b, 0.35f);
                Gizmos.DrawWireCube(bc.bounds.center, bc.bounds.size);
                Gizmos.color = prevColor;
            }
        }

        // Full-quality draw mode for small collider counts.
        private void DrawColliderGizmo_Quality(Collider col, Color dynamicColor)
        {
            Matrix4x4 prevMatrix = Gizmos.matrix;
            Color prevColor = Gizmos.color;

            Gizmos.matrix = col.transform.localToWorldMatrix;
            Gizmos.color = dynamicColor;

            if (col is MeshCollider mc && mc.sharedMesh != null)
            {
                bool useExactHull = mc.convex && !animationMode;
                Mesh drawMesh = useExactHull ? GetOrBuildConvexHullGizmoMesh(mc) : mc.sharedMesh;

                if (drawMesh != null && EnsureMeshDrawable(drawMesh))
                {
                    Gizmos.DrawWireMesh(drawMesh);
                    Gizmos.color = new Color(dynamicColor.r, dynamicColor.g, dynamicColor.b, 0.15f);
                    Gizmos.DrawMesh(drawMesh);
                }
            }
            else if (col is BoxCollider bc)
            {
                Gizmos.DrawWireCube(bc.center, bc.size);
                Gizmos.color = new Color(dynamicColor.r, dynamicColor.g, dynamicColor.b, 0.12f);
                Gizmos.DrawCube(bc.center, bc.size);
            }

            Gizmos.matrix = prevMatrix;
            Gizmos.color = prevColor;
        }

        private bool IsVisibleToCamera(Camera cam)
        {
            if (cam == null) return false;

            Bounds b = GetVisibilityBounds();
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            return GeometryUtility.TestPlanesAABB(planes, b);
        }

        private Bounds GetVisibilityBounds()
        {
            EnsureSourceReferences();
            if (m_MeshRenderer == null) m_MeshRenderer = GetComponent<MeshRenderer>();

            if (_skinnedMeshRenderer != null)
            {
                Bounds b = _skinnedMeshRenderer.bounds;
                if (b.size.sqrMagnitude > 0.000001f) return b;
            }

            if (externalTargetRenderer != null)
            {
                Bounds b = externalTargetRenderer.bounds;
                if (b.size.sqrMagnitude > 0.000001f) return b;
            }

            if (m_MeshRenderer != null)
            {
                Bounds b = m_MeshRenderer.bounds;
                if (b.size.sqrMagnitude > 0.000001f) return b;
            }

            if (generatedColliders is { Count: > 0 })
            {
                Bounds b = generatedColliders[0] != null
                    ? generatedColliders[0].bounds
                    : new Bounds(transform.position, Vector3.one * 0.01f);

                for (int i = 1; i < generatedColliders.Count; i++)
                {
                    var c = generatedColliders[i];
                    if (c == null) continue;
                    b.Encapsulate(c.bounds);
                }

                if (b.size.sqrMagnitude > 0.000001f) return b;
            }

            return new Bounds(transform.position, Vector3.one * 0.01f);
        }

        private MeshRenderer m_MeshRenderer;

        private class MeshData { public Vector3[] Vertices; public int[] Triangles; }
        #endregion
    }
}
