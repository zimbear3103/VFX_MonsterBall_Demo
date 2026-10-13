using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터의 '보이는 몸' 기준 본(힙/골반) 탐색 공용 헬퍼 (2026-09-09).
    ///
    /// 잡기 연출·카메라 앵커는 본 변위로 움직이는 몸의 위치를 루트와 별도로 재야 한다.
    /// 기존에는 Humanoid Hips → 첫 SkinnedMeshRenderer.rootBone 순으로 찾았는데, 플레이어의 활성 메시
    /// (Body_JangHyu_mesh_NEW)는 rootBone이 jnt_root(연출 중 고정)라서 "몸 = 루트"로 오판했고,
    /// 그 결과 잡기 종료 정착(TickSettle)이 본 복귀를 상쇄하지 못해 종료 순간 위치가 튀었다.
    ///
    /// 우선순위:
    ///   1. Humanoid 아바타면 Animator.GetBoneTransform(Hips)
    ///   2. 활성 SkinnedMeshRenderer들의 rootBone 중 이름이 골반 계열(hips/pelvis/root_m/_hip)인 것
    ///   3. 본 계층(Animator 아래) 전체에서 이름이 골반 계열인 Transform (jnt_hip 등)
    ///   4. 첫 활성 SkinnedMeshRenderer.rootBone (기존 폴백)
    /// </summary>
    public static class CharacterVisualBone
    {
        // 골반 본으로 인정하는 이름 규칙 (소문자 비교):
        //   - "hips", "pelvis"로 끝남 (Humanoid/Mixamo 계열: Hips, mixamorig:Hips)
        //   - "root_m" (AdvancedSkeleton 계열 골반: Root_M)
        //   - "_hip" / ":hip"로 끝남 (jnt_hip 등)
        // 단독 "hip"은 제외 — AdvancedSkeleton 리그에서 "Hip"은 허벅지 조인트라 골반이 아니다 (2026-09-09 오판 사례).
        private static bool IsHipName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.EndsWith("hips") || n.EndsWith("pelvis") || n == "root_m" || n.EndsWith(":root_m")
                || n.EndsWith("_hip") || n.EndsWith(":hip");
        }

        /// <summary>root 아래에서 몸 기준 본을 찾는다. 없으면 null.</summary>
        public static Transform Resolve(Transform root)
        {
            if (root == null) return null;

            var animator = root.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }

            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var rb = renderers[i].rootBone;
                if (rb != null && IsHipName(rb.name)) return rb;
            }

            Transform searchRoot = animator != null ? animator.transform : root;
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (IsHipName(all[i].name)) return all[i];

            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr.rootBone != null) return smr.rootBone;
            }
            return renderers.Length > 0 ? renderers[0].transform : null;
        }

        /// <summary>몸 기준 본의 지면 위치 — XZ는 본, 높이는 루트. 본이 없으면 루트 위치.</summary>
        public static Vector3 GroundPosition(Transform root, Transform bone)
        {
            Vector3 rootPos = root.position;
            if (bone == null) return rootPos;
            return new Vector3(bone.position.x, rootPos.y, bone.position.z);
        }
    }
}
