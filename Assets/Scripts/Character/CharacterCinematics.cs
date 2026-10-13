using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터별 연출 데이터 — 잡기/처형 카메라 시퀀스(좌/우 쌍) + 애니 이벤트→Feedback 키 매핑.
    ///
    /// 구 프로젝트 규약 유지: 카메라 시퀀스(웨이포인트)는 피격자 프리팹에 배선하고,
    /// 좌/우 선택은 시전자 Facing(IsFacingRight)으로 한다.
    /// 이펙트는 애니메이션 이벤트 이름 → Aiara.FeedbackManager 키 매핑을 데이터로 관리한다
    /// (구버전 JangHyuAnimationEventReceiver의 하드코딩 분기를 데이터화).
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Cinematics")]
    public class CharacterCinematics : MonoBehaviour
    {
        [System.Serializable]
        public class ExecutionCameraEntry
        {
            [Tooltip("이 카메라 쌍을 사용할 ExecuteType (0 = ExecuteA, 1 = ExecuteB, 2 = ExecuteC — CharacterActionPoints.ExecuteType과 동일)")]
            public int executeType = 0;
            [Tooltip("시전자가 오른쪽을 볼 때 재생할 시퀀스")]
            public CameraSequence right;
            [Tooltip("시전자가 왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
            public CameraSequence left;
        }

        [System.Serializable]
        public class GrabCameraEntry
        {
            [Tooltip("이 카메라 쌍을 사용할 GrabType")]
            public int grabType = 1;
            [Tooltip("시전자가 오른쪽을 볼 때 재생할 시퀀스")]
            public CameraSequence right;
            [Tooltip("시전자가 왼쪽을 볼 때 재생할 시퀀스")]
            public CameraSequence left;
        }

        [System.Serializable]
        public class EventEffectEntry
        {
            [Tooltip("애니메이션 이벤트 이름 (예: ExecuteA_Hit1)")]
            public string eventName;
            [Tooltip("재생할 FeedbackManager 키 (예: Hit, HeavySmoke)")]
            public string feedbackKey;
            [Tooltip("이펙트 기준 위치 오프셋(피격자 루트 기준, 월드 Y 위주)")]
            public Vector3 offset = new Vector3(0f, 1f, 0f);
        }

        [Header("Execution Camera")]
        [Tooltip("ExecuteType별 처형 카메라 시퀀스 쌍 (시전자 Facing으로 좌/우 선택, 좌 비우면 우를 미러로 재생). 잡기 카메라와 동일 규약")]
        [SerializeField] private List<ExecutionCameraEntry> executionCameras = new List<ExecutionCameraEntry>();

        [Header("Grab Camera")]
        [Tooltip("GrabType별 잡기 카메라 시퀀스 쌍 (이 캐릭터가 피격자)")]
        [SerializeField] private List<GrabCameraEntry> grabCameras = new List<GrabCameraEntry>();

        [Header("Carry Finisher Camera")]
        [Tooltip("체인 캐리 피니셔(발차기) 시 시전자가 오른쪽을 볼 때 재생할 시퀀스")]
        [SerializeField] private CameraSequence carryFinisherCameraRight;
        [Tooltip("왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
        [SerializeField] private CameraSequence carryFinisherCameraLeft;

        [Header("Parry Camera")]
        [Tooltip("일반 패링 성공 시 방어자가 오른쪽을 볼 때 재생할 시퀀스 (이 캐릭터가 방어자). 비우면 일반 패링은 시네마틱 없음")]
        [SerializeField] private CameraSequence parryCameraRight;
        [Tooltip("왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
        [SerializeField] private CameraSequence parryCameraLeft;

        [Header("Perfect Parry Camera")]
        [Tooltip("퍼펙트 패링 성공 시 방어자가 오른쪽을 볼 때 재생할 시퀀스 (이 캐릭터가 방어자)")]
        [SerializeField] private CameraSequence perfectParryCameraRight;
        [Tooltip("왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
        [SerializeField] private CameraSequence perfectParryCameraLeft;

        [Header("Self-Destruct Camera")]
        [Tooltip("자폭 시 시전자가 오른쪽을 볼 때 재생할 시퀀스 (이 캐릭터가 자폭 시전자). holdUntilStopped=true 권장(자폭 종료 시 Stop)")]
        [SerializeField] private CameraSequence selfDestructCameraRight;
        [Tooltip("왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
        [SerializeField] private CameraSequence selfDestructCameraLeft;

        [Header("Latent Ability Camera")]
        [Tooltip("잠재능력(나찰) 발동 시 시전자가 오른쪽을 볼 때 재생할 시퀀스. 짧은 발동 연출이면 holdUntilStopped=false로 자동 복귀")]
        [SerializeField] private CameraSequence latentCameraRight;
        [Tooltip("왼쪽을 볼 때 재생할 시퀀스 (비우면 오른쪽 시퀀스를 미러로 재생)")]
        [SerializeField] private CameraSequence latentCameraLeft;

        [Header("Event Effects")]
        [Tooltip("시전자 클립 이벤트 → Feedback 키 매핑 (처형/잡기 공용). 위치는 피격자 루트 + 오프셋")]
        [SerializeField] private List<EventEffectEntry> eventEffects = new List<EventEffectEntry>();

        /// <summary>처형 카메라 시퀀스 선택 (ExecuteType + 시전자 Facing 기준). 매칭 항목 없으면 null.</summary>
        public CameraSequence ResolveExecutionCamera(int executeType, bool attackerFacingRight)
        {
            for (int i = 0; i < executionCameras.Count; i++)
            {
                var e = executionCameras[i];
                if (e != null && e.executeType == executeType)
                    return ResolveSide(e.right, e.left, attackerFacingRight);
            }
            return null;
        }

        /// <summary>캐리 피니셔 카메라 시퀀스 선택 (시전자 Facing 기준). 없으면 null.</summary>
        public CameraSequence ResolveCarryFinisherCamera(bool attackerFacingRight)
            => ResolveSide(carryFinisherCameraRight, carryFinisherCameraLeft, attackerFacingRight);

        /// <summary>일반 패링 카메라 시퀀스 선택 (방어자 Facing 기준). 없으면 null.</summary>
        public CameraSequence ResolveNormalParryCamera(bool defenderFacingRight)
            => ResolveSide(parryCameraRight, parryCameraLeft, defenderFacingRight);

        /// <summary>퍼펙트 패링 카메라 시퀀스 선택 (방어자 Facing 기준). 없으면 null.</summary>
        public CameraSequence ResolvePerfectParryCamera(bool defenderFacingRight)
            => ResolveSide(perfectParryCameraRight, perfectParryCameraLeft, defenderFacingRight);

        /// <summary>패링 카메라 시퀀스 선택 — perfect면 퍼펙트 쌍, 아니면 일반 쌍. 없으면 null.</summary>
        public CameraSequence ResolveParryCamera(bool perfect, bool defenderFacingRight)
            => perfect ? ResolvePerfectParryCamera(defenderFacingRight) : ResolveNormalParryCamera(defenderFacingRight);

        /// <summary>자폭 카메라 시퀀스 선택 (시전자 Facing 기준). 없으면 null.</summary>
        public CameraSequence ResolveSelfDestructCamera(bool attackerFacingRight)
            => ResolveSide(selfDestructCameraRight, selfDestructCameraLeft, attackerFacingRight);

        /// <summary>잠재능력(나찰) 카메라 시퀀스 선택 (시전자 Facing 기준). 없으면 null.</summary>
        public CameraSequence ResolveLatentCamera(bool attackerFacingRight)
            => ResolveSide(latentCameraRight, latentCameraLeft, attackerFacingRight);

        /// <summary>잡기 카메라 시퀀스 선택 (GrabType + 시전자 Facing). 없으면 null.</summary>
        public CameraSequence ResolveGrabCamera(int grabType, bool attackerFacingRight)
        {
            for (int i = 0; i < grabCameras.Count; i++)
            {
                var e = grabCameras[i];
                if (e != null && e.grabType == grabType)
                    return ResolveSide(e.right, e.left, attackerFacingRight);
            }
            return null;
        }

        /// <summary>
        /// Facing에 맞는 시퀀스 선택. 해당 방향이 비어 있으면 반대쪽 시퀀스를 미러로 재생 —
        /// 오른쪽 기준으로만 제작해도 Flip 시 스플라인 진행축 기준 자동 반전된다.
        /// </summary>
        private CameraSequence ResolveSide(CameraSequence right, CameraSequence left, bool facingRight)
        {
            var primary = facingRight ? right : left;
            if (primary != null)
            {
                primary.SetMirror(false);
                return primary;
            }
            var opposite = facingRight ? left : right;
            if (opposite != null)
            {
                var ch = GetComponent<Character>();
                opposite.SetMirror(true, transform, ch != null ? ch.Movement : null);
                return opposite;
            }
            return null;
        }

        /// <summary>애니 이벤트에 매핑된 이펙트 재생. 매핑 없으면 false.</summary>
        public bool TryPlayEventEffect(string eventName, Vector3 basePosition)
        {
            if (string.IsNullOrEmpty(eventName)) return false;
            bool played = false;
            for (int i = 0; i < eventEffects.Count; i++)
            {
                var e = eventEffects[i];
                if (e == null || e.eventName != eventName || string.IsNullOrEmpty(e.feedbackKey)) continue;
                Aiara.FeedbackManager.PlayFeedbackAtWorld(e.feedbackKey, basePosition + e.offset, Quaternion.identity);
                played = true;
            }
            return played;
        }
    }
}
