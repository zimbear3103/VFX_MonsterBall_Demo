using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 파편 더미의 물리를 정리하는 컴포넌트. BreakableProp이 debris 스폰 시 런타임에 부착한다.
    /// 각 파편이 충분히 느려지면 개별적으로 kinematic 동결하고, MaxSettleTime이 지나면 전부 강제 동결.
    /// (구 Aiara.DebrisSettle의 ver2 포팅 — 자기완결형, Unity6 Rigidbody.linearVelocity 사용.)
    ///
    /// 이유: 파편 Rigidbody가 영구 시뮬레이션으로 남아 상자 몇 개만 부숴도 비키네마틱 바디가
    /// 대량 누적되어 물리 스파이크를 만들었음. 서드파티 debris 프리팹은 수정하지 않고 코드 부착으로 처리.
    /// </summary>
    public class DebrisSettle : MonoBehaviour
    {
        [Tooltip("동결 판정 시작 전 대기(폭발 직후 파편이 튀는 시간)")]
        public float SettleDelay = 1.5f;

        [Tooltip("이 속도(m/s) 미만이면 개별 동결")]
        public float SleepSpeed = 0.15f;

        [Tooltip("스폰 후 이 시간이 지나면 남은 파편 전부 강제 동결")]
        public float MaxSettleTime = 5f;

        [Tooltip("동결 검사 주기(초)")]
        public float CheckInterval = 0.25f;

        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        private float _elapsed;
        private float _nextCheck;

        private void OnEnable()
        {
            _bodies.Clear();
            GetComponentsInChildren(true, _bodies);
            _elapsed = 0f;
            _nextCheck = SettleDelay;
        }

        private void FixedUpdate()
        {
            _elapsed += Time.fixedDeltaTime;
            if (_elapsed < _nextCheck) return;
            _nextCheck = _elapsed + CheckInterval;

            bool forceAll = _elapsed >= MaxSettleTime;
            int remaining = 0;
            for (int i = 0; i < _bodies.Count; i++)
            {
                Rigidbody rb = _bodies[i];
                if (rb == null || rb.isKinematic) continue;

                if (forceAll || rb.linearVelocity.sqrMagnitude < SleepSpeed * SleepSpeed)
                    rb.isKinematic = true;
                else
                    remaining++;
            }

            if (remaining == 0) enabled = false;
        }
    }
}
