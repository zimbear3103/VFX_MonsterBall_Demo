using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Runtime animated collider update (SkinnedMesh pose baking) and update scheduling.
    /// </summary>
    public partial class MeshColliderOptimizer
    {
#region Animation
        public enum AnimationSourceMode
        {
            BakeMesh,
            SharedMesh
        }

        public enum AnimationUpdateMode
        {
            EveryFrame,
            EveryNFrames,
            TargetHz
        }

        [Header("Animated Collider (Runtime)")]
        public bool animationMode;
        public AnimationSourceMode animationSourceMode = AnimationSourceMode.BakeMesh;
        public AnimationUpdateMode animationUpdateMode = AnimationUpdateMode.EveryFrame;

        [Range(1, 60)] public int animationEveryNFrames = 15;

        [Range(5f, 120f)] public float animationMaxHz = 30f;

        [Tooltip("In animation mode, only the root MeshCollider is updated (no convex decomposition per update).")]
        public bool animationRootOnly = true;

        [Tooltip("Skip updates when not visible (performance).")]
        public bool animationOnlyWhenVisible = true;
        
        private bool m_AnimModePrev;
        private int m_AnimFrameCounter;
        private float m_NextAnimUpdateTime;
        private Mesh m_PoseOptimizedMesh;
        private bool m_PlaySessionInitialized;
        private float m_LastObservedPlayTime = -1f;
        
        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                m_PlaySessionInitialized = false;
                m_LastObservedPlayTime = -1f;
                return;
            }

            if (m_LastObservedPlayTime >= 0f && Time.time + 0.0001f < m_LastObservedPlayTime)
            {
                // Play session restarted without full reload; force clean runtime state.
                m_PlaySessionInitialized = false;
                ResetAnimationRuntimeState();
            }
            m_LastObservedPlayTime = Time.time;

            // Important when Enter Play Mode options disable reload:
            // runtime state can persist from previous play session.
            if (!m_PlaySessionInitialized)
            {
                m_PlaySessionInitialized = true;
                ResetAnimationRuntimeState();
            }

            if (!animationMode) { ResetAnimationRuntimeState(); return; }

            if (!EnsureAnimationSourceRenderer()) return;

            if (animationOnlyWhenVisible && !Application.isEditor)
            {
                Renderer sourceRenderer = _skinnedMeshRenderer != null
                    ? _skinnedMeshRenderer
                    : (_meshFilter != null ? _meshFilter.GetComponent<Renderer>() : externalTargetRenderer);

                bool isSourceVisible = sourceRenderer != null ? sourceRenderer.isVisible : _isVisible;
                if (!isSourceVisible) return;
            }

            bool shouldBootstrap = !m_AnimModePrev || !HasValidAnimationRuntimeCollider();
            if (!shouldBootstrap && Time.time < 0.1f)
                shouldBootstrap = true;

            // Bootstrap if mode just enabled, play session just started, or runtime collider is missing/stale.
            if (shouldBootstrap)
            {
                m_AnimModePrev = true;
                m_AnimFrameCounter = 0;
                m_NextAnimUpdateTime = Time.time;

                if (animationRootOnly)
                    ClearProxyChildren();

                UpdateColliderFromCurrentPose();
                return;
            }

        #if UNITY_EDITOR
            if (showGizmos) UnityEditor.SceneView.RepaintAll();
        #endif

            if (animationUpdateMode == AnimationUpdateMode.TargetHz && m_NextAnimUpdateTime > Time.time + 1f)
                m_NextAnimUpdateTime = Time.time;

            if (!ShouldUpdateAnimCollider()) return;

            UpdateColliderFromCurrentPose();
        }
        private bool EnsureAnimationSourceRenderer()
        {
            EnsureSourceReferences();
            if (_skinnedMeshRenderer != null && _skinnedMeshRenderer.sharedMesh != null) return true;
            if (_meshFilter != null && _meshFilter.sharedMesh != null) return true;
            return false;
        }
        private bool HasValidAnimationRuntimeCollider()
        {
            if (generatedColliders != null)
            {
                for (int i = 0; i < generatedColliders.Count; i++)
                {
                    var col = generatedColliders[i];
                    if (col == null) continue;

                    if (col is MeshCollider mc && mc.sharedMesh != null)
                        return true;

                    if (col is BoxCollider)
                        return true;
                }
            }

            Transform hiddenRoot = transform.Find(HIDDEN_ROOT_NAME);
            if (hiddenRoot != null)
            {
                var rootMc = hiddenRoot.GetComponent<MeshCollider>();
                if (rootMc != null && rootMc.sharedMesh != null)
                    return true;

                var rootBc = hiddenRoot.GetComponent<BoxCollider>();
                if (rootBc != null)
                    return true;
            }

            return false;
        }
        private void ResetAnimationRuntimeState()
        {
            m_AnimModePrev = false;
            m_AnimFrameCounter = 0;
            m_NextAnimUpdateTime = 0f;
        }
        private bool ShouldUpdateAnimCollider()
        {
            switch (animationUpdateMode)
            {
                case AnimationUpdateMode.EveryFrame:
                    return true;

                case AnimationUpdateMode.EveryNFrames:
                    m_AnimFrameCounter++;
                    if (m_AnimFrameCounter >= Mathf.Max(1, animationEveryNFrames))
                    {
                        m_AnimFrameCounter = 0;
                        return true;
                    }
                    return false;

                case AnimationUpdateMode.TargetHz:
                {
                    float hz = Mathf.Clamp(animationMaxHz, 1f, 240f);
                    float interval = 1f / hz;

                    if (Time.time >= m_NextAnimUpdateTime)
                    {
                        m_NextAnimUpdateTime = Time.time + interval;
                        return true;
                    }
                    return false;
                }
            }

            return true;
        }

        private void UpdateColliderFromCurrentPose()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var poseSources = new System.Collections.Generic.List<SourceMeshSnapshot>(8);
            CollectSourceMeshSnapshots(
                poseSources,
                bakeSkinnedMeshes: animationSourceMode != AnimationSourceMode.SharedMesh);
            if (poseSources.Count == 0) return;

            Mesh poseOptimized = BuildOptimizedPoseMesh(poseSources);
            if (poseOptimized == null) return;

            if (animationRootOnly)
            {
                ApplyPoseMeshToRootCollider(poseOptimized);
            }
            else
            {
                ClearProxyChildren();
                FinalMesh = poseOptimized;
                ApplyMeshToColliders();
            }

            RefreshGeneratedColliderList();
            sw.Stop();
            stat_BakeTime = sw.ElapsedMilliseconds + " ms";

        #if UNITY_EDITOR
            if (showGizmos)
            {
                UnityEditor.SceneView.RepaintAll();
            }
        #endif
        }
        private Mesh BuildOptimizedPoseMesh(System.Collections.Generic.List<SourceMeshSnapshot> poseSources)
        {
            if (poseSources == null || poseSources.Count == 0) return null;

            AccumulateSourceStats(poseSources, out int beforeVerts, out int beforeTris);
            MeshData md = BuildOptimizedMeshDataFromSources(poseSources, applyPreviewQualityCap: false, token: default);

            if (md?.Vertices == null || md.Vertices.Length < 3 || md.Triangles == null || md.Triangles.Length < 3)
            {
                ApplyAnimatedStats(beforeVerts, beforeTris, beforeVerts, beforeTris);
                return null;
            }

            m_PoseOptimizedMesh = ApplyMeshDataToMesh(
                m_PoseOptimizedMesh,
                md,
                OPT_MESH_PREFIX + "RuntimePose",
                recalculateNormals: false);

            ApplyAnimatedStats(beforeVerts, beforeTris, md.Vertices.Length, md.Triangles.Length);

            return m_PoseOptimizedMesh;
        }

        private void ApplyAnimatedStats(int beforeVerts, int beforeTris, int afterVerts, int afterTris)
        {
            stat_VertsBefore = beforeVerts;
            stat_TrisBefore = beforeTris;
            stat_VertsAfter = afterVerts;
            stat_TrisAfter = afterTris;
            stat_MemoryBefore = CalculateMemory(stat_VertsBefore, stat_TrisBefore);
            stat_MemoryAfter = CalculateMemory(stat_VertsAfter, stat_TrisAfter);
            savingsRatio = 1f - (float)stat_VertsAfter / (stat_VertsBefore == 0 ? 1 : stat_VertsBefore);
        }
        private void ApplyPoseMeshToRootCollider(Mesh poseMesh)
        {
            if (poseMesh == null) return;
            if (!HasAnyNonDegenerateTriangle(poseMesh)) return;

            MeshCollider targetMc = GetOrCreateHiddenRootCollider();

            targetMc.enabled = true;
            targetMc.hideFlags = COLLIDER_HIDE_FLAGS;

            bool forceConvex = HasNonKinematicRigidbody(out _) || isTrigger;
            targetMc.convex = convex || forceConvex;
            targetMc.isTrigger = isTrigger;
            targetMc.sharedMaterial = material;

            targetMc.sharedMesh = null;
            targetMc.sharedMesh = poseMesh;

            RegisterGeneratedCollider(targetMc);

            stat_ConvexPartCount = 1;
        }

#endregion
    }
}

