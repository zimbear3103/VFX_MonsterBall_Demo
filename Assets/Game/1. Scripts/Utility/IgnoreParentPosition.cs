using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 부모의 "이동"이 이 오브젝트에 전파되지 않게 한다 (월드 위치 유지).
    /// 매 프레임 부모가 움직인 만큼 반대로 보정하므로,
    /// 자기 자신의 로컬 애니메이션/스크립트 이동은 그대로 동작한다.
    /// 회전/스케일은 건드리지 않는다 (위치만 차단).
    /// VFX처럼 캐릭터 자식으로 두되 스폰된 자리에 남아야 하는 오브젝트에 사용.
    /// </summary>
    public class IgnoreParentPosition : MonoBehaviour
    {
        [Header("Options")]
        [Tooltip("활성화(OnEnable)될 때마다 기준을 다시 잡는다. VFX 재사용(SetActive 토글) 시 권장.")]
        [SerializeField] private bool resetOnEnable = true;

        [Tooltip("켜질 때 최초 로컬 위치로 복귀한 뒤 월드 고정을 시작한다. 꺼두면 마지막 위치에서 그대로 시작. (재사용 VFX가 항상 정위치에서 나오게)")]
        [SerializeField] private bool restoreLocalPositionOnEnable = true;

        [Tooltip("차단할 축 선택 (체크된 축만 부모 이동을 무시)")]
        [SerializeField] private bool ignoreX = true;
        [SerializeField] private bool ignoreY = true;
        [SerializeField] private bool ignoreZ = true;

        private Transform _parent;
        private Vector3 _lastParentPos;
        private Vector3 _initialLocalPos;
        private bool _initialCaptured;

        private void Awake()
        {
            // 프리팹/씬에 배치된 최초 로컬 위치를 기억 (매 활성화 시 복귀 기준)
            _initialLocalPos = transform.localPosition;
            _initialCaptured = true;
        }

        private void OnEnable()
        {
            if (!_initialCaptured)
            {
                _initialLocalPos = transform.localPosition;
                _initialCaptured = true;
            }

            if (restoreLocalPositionOnEnable)
                transform.localPosition = _initialLocalPos; // 정위치 복귀 후 월드 고정 시작

            if (resetOnEnable || _parent == null)
                CaptureParent();
        }

        private void CaptureParent()
        {
            _parent = transform.parent;
            if (_parent != null)
                _lastParentPos = _parent.position;
        }

        private void LateUpdate()
        {
            if (_parent != transform.parent) // 부모가 바뀌면 기준 재설정
            {
                CaptureParent();
                return;
            }
            if (_parent == null) return;

            Vector3 delta = _parent.position - _lastParentPos;
            _lastParentPos = _parent.position;
            if (delta == Vector3.zero) return;

            if (!ignoreX) delta.x = 0f;
            if (!ignoreY) delta.y = 0f;
            if (!ignoreZ) delta.z = 0f;

            transform.position -= delta; // 부모가 움직인 만큼 되돌려 월드 위치 유지
        }
    }
}
