using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// LODGroup integration helpers.
    /// </summary>
    public partial class MeshColliderOptimizer
    {
#region LOD
        private LODGroup m_LODGroup;
        private int m_LODIndex = -1;
        private int m_LastKnownActiveLodIndex;
        private bool m_LODBindingInitialized;

        private void UpdateLodColliderActivation()
        {
            if (!enableCollidersOnlyOnActiveLod) return;
            if (!Application.isPlaying) return;

            EnsureLodBinding();
            if (m_LODGroup == null || m_LODIndex < 0) return;

            int activeIndex = GetActiveLodIndex(m_LODGroup);
            bool shouldEnable = activeIndex == m_LODIndex;

            if (generatedColliders == null || generatedColliders.Count == 0) return;

            for (int i = generatedColliders.Count - 1; i >= 0; i--)
            {
                Collider c = generatedColliders[i];
                if (c == null)
                {
                    generatedColliders.RemoveAt(i);
                    continue;
                }

                if (c.enabled != shouldEnable)
                    c.enabled = shouldEnable;
            }
        }

        private void ApplyLodColliderEnableState(Collider col)
        {
            if (col == null) return;
            if (!enableCollidersOnlyOnActiveLod) return;
            if (!Application.isPlaying) return;

            EnsureLodBinding();
            if (m_LODGroup == null || m_LODIndex < 0) return;

            int activeIndex = GetActiveLodIndex(m_LODGroup);
            col.enabled = activeIndex == m_LODIndex;
        }

        private void EnsureLodBinding()
        {
            if (m_LODBindingInitialized) return;
            m_LODBindingInitialized = true;

            m_LODGroup = GetComponentInParent<LODGroup>();
            if (m_LODGroup == null) return;

            Renderer r = GetComponent<Renderer>();
            if (r == null)
                r = GetComponentInChildren<Renderer>();

            if (r == null) return;

            LOD[] lods = m_LODGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                Renderer[] rs = lods[i].renderers;
                if (rs == null) continue;

                foreach (var t in rs)
                {
                    if (t == r)
                    {
                        m_LODIndex = i;
                        return;
                    }
                }
            }
        }

        private int GetActiveLodIndex(LODGroup group)
        {
            if (group == null) return 0;

            LOD[] lods = group.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                Renderer[] rs = lods[i].renderers;
                if (rs == null) continue;

                foreach (var rr in rs)
                {
                    if (rr == null) continue;

                    if (rr.enabled && rr.gameObject.activeInHierarchy)
                    {
                        m_LastKnownActiveLodIndex = i;
                        return i;
                    }
                }
            }

            if (m_LastKnownActiveLodIndex >= 0 && m_LastKnownActiveLodIndex < lods.Length)
                return m_LastKnownActiveLodIndex;

            return 0;
        }
#endregion
    }
}
