using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 추적 노드. 타겟들의 가중 무게중심을 계산하고 Damping으로 따라간다.
    ///
    /// 현재 단계에서는 Player 한 명만 등록하지만,
    /// 향후 교전 Enemy Group을 위한 멀티 타겟 API를 미리 제공한다.
    /// Confiner Clamp는 ApplyConfiner 확장 지점만 준비한다.
    /// </summary>
    public class FollowCameraNode : CameraNode
    {
        private class TargetEntry
        {
            public Transform Target;
            public float Weight;
        }

        [Header("Follow")]
        [Tooltip("추적 지점에 더할 기본 오프셋 (예: 캐릭터 가슴 높이)")]
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 1.2f, 0f);

        [Tooltip("추적 Damping (SmoothDamp smoothTime, 초). 클수록 느리게 따라간다.")]
        [SerializeField, Range(0f, 2f)] private float followSmoothTime = 0.25f;

        [Tooltip("추적 최대 속도 (m/s). 낙하/텔레포트 등 극단 상황에서 Damping 속도 폭주 방지.")]
        [SerializeField] private float maxFollowSpeed = 60f;

        [Tooltip("플레이어 Flip(좌/우 전환) 시 followOffset.z 부호를 반전한다.")]
        [SerializeField] private bool flipOffsetZWithFacing = true;

        private readonly List<TargetEntry> _targets = new List<TargetEntry>();
        private Vector3 _dampVelocity;
        private bool _snapped;

        // Facing 조회용 캐시 (Rig.PlayerTarget 기준). 타겟 교체 시 재조회.
        private Transform _facingSource;
        private CharacterMovement _facingMovement;

        // ─────────────── 타겟 API (향후 MultiTarget 확장 지점) ───────────────

        public void AddTarget(Transform target, float weight = 1f)
        {
            if (target == null)
                return;

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Target == target)
                {
                    _targets[i].Weight = weight;
                    return;
                }
            }

            _targets.Add(new TargetEntry { Target = target, Weight = weight });
        }

        public void RemoveTarget(Transform target)
        {
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                if (_targets[i].Target == target)
                    _targets.RemoveAt(i);
            }
        }

        public void ClearTargets() => _targets.Clear();

        /// <summary>등록된 타겟들의 가중 무게중심. 현재는 Player 위치와 동일하다.</summary>
        public Vector3 WeightedCenter()
        {
            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;

            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                TargetEntry entry = _targets[i];
                if (entry.Target == null)
                {
                    _targets.RemoveAt(i);
                    continue;
                }

                sum += entry.Target.position * entry.Weight;
                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0f)
                return transform.position;

            return sum / totalWeight;
        }

        // ─────────────── 평가 ───────────────

        /// <summary>유효한 타겟이 하나라도 있는가.</summary>
        public bool HasTargets
        {
            get
            {
                for (int i = 0; i < _targets.Count; i++)
                {
                    if (_targets[i].Target != null)
                        return true;
                }
                return false;
            }
        }

        public override void Evaluate(float dt)
        {
            // 타겟이 없으면 이동하지 않는다.
            // (자기 위치 + 오프셋을 목표로 삼아 매 프레임 표류하는 것 방지.
            //  도메인 리로드 등으로 타겟이 유실된 경우 Rig에서 재등록을 시도한다.)
            if (!HasTargets)
            {
                if (Rig != null && Rig.PlayerTarget != null)
                    AddTarget(Rig.PlayerTarget, 1f);
                else
                    return;
            }

            Vector3 zoneOffset = Rig != null && Rig.ZoneController != null
                ? Rig.ZoneController.EvaluateFollowOffset()
                : Vector3.zero;

            CameraNodeState state = CameraNodeState.Default;
            state.Position = WeightedCenter() + ResolveFollowOffset() + zoneOffset;

            ApplyChannels(dt, ref state);

            Vector3 desired = ApplyConfiner(state.Position + state.PositionOffset);

            if (!_snapped)
            {
                transform.position = desired;
                _dampVelocity = Vector3.zero;
                _snapped = true;
                return;
            }

            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref _dampVelocity, followSmoothTime,
                maxFollowSpeed, dt);
        }

        /// <summary>
        /// Facing 반영 followOffset. flipOffsetZWithFacing이 켜져 있고 플레이어가 왼쪽을 보면 z 부호 반전.
        /// </summary>
        private Vector3 ResolveFollowOffset()
        {
            Vector3 offset = followOffset;
            if (!flipOffsetZWithFacing)
                return offset;

            CharacterMovement movement = ResolveFacingMovement();
            if (movement != null && !movement.FacingRight)
                offset.z = -offset.z;

            return offset;
        }

        private CharacterMovement ResolveFacingMovement()
        {
            Transform source = Rig != null ? Rig.PlayerTarget : null;
            if (source != _facingSource)
            {
                _facingSource = source;
                Character character = source != null ? source.GetComponentInParent<Character>() : null;
                _facingMovement = character != null ? character.Movement : null;
            }
            return _facingMovement;
        }

        /// <summary>
        /// 타겟 텔레포트/리스폰 직후 호출. 다음 Evaluate에서 Damping 없이 즉시 위치를 맞춘다.
        /// </summary>
        public void Snap()
        {
            _snapped = false;
            _dampVelocity = Vector3.zero;
        }

        /// <summary>
        /// Camera Confiner 확장 지점(현재 미사용). 카메라 이동 경계는 CameraRig가
        /// 최종 카메라 위치 기준으로 처리하므로 여기서는 아무 제한도 하지 않는다.
        /// </summary>
        protected virtual Vector3 ApplyConfiner(Vector3 position) => position;


    }
}
