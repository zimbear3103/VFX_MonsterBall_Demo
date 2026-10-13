using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// persistEffect가 켜진 Attack Visual(TimedVisual) 전용 지속 타이머.
    /// 이펙트가 재생되는 순간 AttackAction이 이 컴포넌트로 지속을 시작하면, 공격이 끊기든 끝나든
    /// 상관없이 지정된 지속 시간(초) 동안 계속 재생된 뒤 이펙트를 끈다(비활성화, 다음 스윙 재사용 대비).
    /// 지속 중에는 시작 시점의 월드 위치/회전을 LateUpdate에서 고정해 플레이어를 따라가지 않는다.
    /// 부모는 떼지 않는다 — 플레이어 좌우 반전이 visualRoot 음수 스케일(미러)로 구현돼 있어
    /// SetParent(null)로 분리하면 미러가 깨져 잔상/파티클이 반대편에 나오기 때문.
    /// 지속 중 플레이어가 방향을 바꾸면(부모 스케일 부호 변화) 자신의 스케일 부호를 반대로 보정해 겉보기를 유지한다.
    /// 지속 종료 시 로컬 트랜스폼을 원래대로 복귀시켜 프리팹 배치를 유지한다.
    /// 종료를 파티클이 아니라 '시간'으로 판정하므로 자체 제작 파티클(ParticleSystem.IsAlive로 감지 불가)에도 안전하다.
    /// 소유 이펙트에 한 번만 AddComponent되어 재사용되며, 게임플레이 로직은 없다.
    /// </summary>
    public class DetachedVisualReturner : MonoBehaviour
    {
        private float _duration;
        private float _elapsed;
        private bool _active;

        private Vector3 _homeLocalPos;
        private Quaternion _homeLocalRot;
        private Vector3 _homeLocalScale;
        private bool _hasHome;

        private Vector3 _worldPos;
        private Quaternion _worldRot;
        private Vector3 _detachScale; // 지속 시작 시점 로컬 스케일 (부모 부호 변화 보정의 기준)
        private Vector3 _parentSignAtStart;

        /// <summary>현재 지속(공격과 무관하게 재생) 중이라 종료 처리를 기다리는가.</summary>
        public bool IsDetached => _active;

        /// <summary>
        /// 시작 시점 월드 위치에 고정한 채 duration(초) 뒤 이펙트를 끄고 시작 시점 로컬 포즈로 복귀한다. 0 이하면 5초.
        /// 홈 포즈는 호출 시점의 현재 로컬 포즈로 매번 갱신한다 — 플레이 중 인스펙터에서 이펙트 위치/회전/크기를 고치면 그대로 다음 재생에 반영 (2026-09-08).
        /// </summary>
        public void BeginDetach(float duration)
        {
            _duration = duration > 0f ? duration : 5f;
            _elapsed = 0f;
            _active = true;

            _homeLocalPos = transform.localPosition;
            _homeLocalRot = transform.localRotation;
            _homeLocalScale = transform.localScale;
            _hasHome = true;

            _worldPos = transform.position;
            _worldRot = transform.rotation;
            _detachScale = transform.localScale;
            _parentSignAtStart = ParentSign();
        }

        private void LateUpdate()
        {
            if (!_active) return;

            Vector3 sign = ParentSign();
            Vector3 scale = _detachScale;
            if (sign.x != _parentSignAtStart.x) scale.x = -scale.x;
            if (sign.y != _parentSignAtStart.y) scale.y = -scale.y;
            if (sign.z != _parentSignAtStart.z) scale.z = -scale.z;
            transform.localScale = scale;

            transform.rotation = _worldRot;
            transform.position = _worldPos;
        }

        private void Update()
        {
            if (!_active) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration) ReturnHome();
        }

        private Vector3 ParentSign()
        {
            Transform p = transform.parent;
            if (p == null) return Vector3.one;
            Vector3 s = p.lossyScale;
            return new Vector3(Mathf.Sign(s.x), Mathf.Sign(s.y), Mathf.Sign(s.z));
        }

        /// <summary>
        /// 지속 종료 — 파티클 정리 + 비활성화 + 원래 로컬 트랜스폼 복귀. 컴포넌트는 남겨 재사용한다.
        /// 지속 시간 경과 시, 또는 재사용 회수(AttackAction.PlayTimedVisual) 시 호출된다.
        /// </summary>
        public void ReturnHome()
        {
            if (!_active) return;
            _active = false;

            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            gameObject.SetActive(false);

            if (_hasHome)
            {
                transform.localPosition = _homeLocalPos;
                transform.localRotation = _homeLocalRot;
                transform.localScale = _homeLocalScale;
            }
        }
    }
}
