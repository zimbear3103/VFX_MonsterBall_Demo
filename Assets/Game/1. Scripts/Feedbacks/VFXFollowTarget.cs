using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 생성된 VFX가 부모-자식 관계 없이(계층/스케일/반전 꼬임 없이) 코드로 대상 Transform을
    /// 따라가게 한다. FeedbackManager가 스폰 직후 SetTarget으로 추종 대상을 주입한다.
    ///
    /// 부모화(SetParent)는 스케일/좌우반전이 자식에 상속되어 슬래시가 찌그러지는 문제가 있어,
    /// 대신 '스폰 시점의 대상 로컬 기준 위치/회전'만 캡처해 매 LateUpdate에 재적용한다.
    /// (강체 추종 — 위치·회전만 따라가고 스케일은 스폰값 그대로 유지)
    /// 대상의 스케일에 영향받지 않도록 InverseTransformPoint가 아닌 회전+평행이동만 사용한다.
    /// </summary>
    public class VFXFollowTarget : MonoBehaviour
    {
        [Tooltip("대상의 위치를 따라간다.")]
        public bool FollowPosition = true;

        [Tooltip("대상의 회전을 따라간다(캐릭터가 도는 동안 이펙트도 함께 돈다).")]
        public bool FollowRotation = true;

        protected Transform _target;
        protected Vector3 _localOffset;      // 대상 회전 기준 평행이동 오프셋
        protected Quaternion _rotOffset;     // 대상 회전 대비 상대 회전
        protected bool _active;

        /// <summary>
        /// 추종 대상을 지정하고, 현재(스폰 직후) 자신의 위치/회전을 대상 기준 상대값으로 캡처한다.
        /// target이 null이면 추종을 끈다.
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            if (target == null)
            {
                _active = false;
                return;
            }
            _localOffset = Quaternion.Inverse(target.rotation) * (transform.position - target.position);
            _rotOffset = Quaternion.Inverse(target.rotation) * transform.rotation;
            _active = true;
        }

        public void ClearTarget()
        {
            _target = null;
            _active = false;
        }

        protected virtual void OnDisable()
        {
            // 풀 반환 시 추종 중단 (다음 스폰에서 SetTarget으로 재설정)
            _target = null;
            _active = false;
        }

        protected virtual void LateUpdate()
        {
            if (!_active || _target == null) return;
            if (FollowPosition)
            {
                transform.position = _target.position + (_target.rotation * _localOffset);
            }
            if (FollowRotation)
            {
                transform.rotation = _target.rotation * _rotOffset;
            }
        }
    }
}
