using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 활성 CameraZone 스택을 관리하고, 카메라 노드가 읽어갈
    /// Override 값들을 부드럽게 블렌드하는 컨트롤러.
    ///
    /// 규칙:
    ///   - Zone 중첩 시 Priority가 가장 높은 Zone(동률이면 나중에 들어온 Zone)이 적용된다.
    ///   - Zone 진입/이탈 시 Snap하지 않고 Zone의 BlendTime으로 블렌드한다.
    ///   - Zone 이탈 시 남아있는 하위 Zone 또는 기본 상태로 복귀한다.
    ///
    /// 노드 사용법:
    ///   SpringArm : EvaluateDistance(baseDistance)
    ///   Pivot     : PitchOffset / YawOffset
    ///   Follow    : FollowOffset (HeightOffset 포함)
    /// </summary>
    public class CameraZoneController : MonoBehaviour
    {
        /// <summary>단일 float 파라미터의 블렌드 상태 (weight 0 = 기본값, 1 = Zone 값)</summary>
        private struct BlendedFloat
        {
            public float Value;
            public float Weight;

            public void Update(bool active, float target, float blendTime, float dt, bool asAngle)
            {
                float k = blendTime > 0.001f ? 1f - Mathf.Exp(-dt * (4f / blendTime)) : 1f;

                if (active)
                {
                    // 완전히 비활성 상태에서 켜질 때는 값부터 스냅 (0 가중치라 화면에는 안 보임)
                    if (Weight < 0.001f)
                        Value = target;
                    else
                        Value = asAngle ? Mathf.LerpAngle(Value, target, k) : Mathf.Lerp(Value, target, k);

                    Weight = Mathf.Lerp(Weight, 1f, k);
                }
                else
                {
                    Weight = Mathf.Lerp(Weight, 0f, k);
                }
            }
        }

        private struct BlendedVector
        {
            public Vector3 Value;
            public float Weight;

            public void Update(bool active, Vector3 target, float blendTime, float dt)
            {
                float k = blendTime > 0.001f ? 1f - Mathf.Exp(-dt * (4f / blendTime)) : 1f;

                if (active)
                {
                    if (Weight < 0.001f)
                        Value = target;
                    else
                        Value = Vector3.Lerp(Value, target, k);

                    Weight = Mathf.Lerp(Weight, 1f, k);
                }
                else
                {
                    Weight = Mathf.Lerp(Weight, 0f, k);
                }
            }
        }

        [Tooltip("Zone이 없을 때 사용할 기본 블렌드 시간 (Zone 이탈 복귀에도 사용)")]
        [SerializeField] private float defaultBlendTime = 0.6f;

        private readonly List<CameraZone> _activeZones = new List<CameraZone>();

        private BlendedFloat _distance;
        private BlendedFloat _pitch;
        private BlendedFloat _yaw;
        private BlendedFloat _height;
        private BlendedVector _followOffset;

        private float _lastBlendTime;

        private void Awake()
        {
            _lastBlendTime = defaultBlendTime;
        }

        // ─────────────── Zone 등록 (CameraZone이 호출) ───────────────

        public void PushZone(CameraZone zone)
        {
            if (zone == null || _activeZones.Contains(zone))
                return;
            _activeZones.Add(zone);
        }

        public void PopZone(CameraZone zone)
        {
            _activeZones.Remove(zone);
        }

        /// <summary>현재 적용 대상 Zone (Priority 최고, 동률이면 나중 진입).</summary>
        public CameraZone ActiveZone
        {
            get
            {
                CameraZone best = null;
                for (int i = 0; i < _activeZones.Count; i++)
                {
                    CameraZone zone = _activeZones[i];
                    if (zone == null || !zone.isActiveAndEnabled)
                        continue;
                    if (best == null || zone.Priority >= best.Priority)
                        best = zone;
                }
                return best;
            }
        }

        // ─────────────── 블렌드 갱신 ───────────────

        private void Update()
        {
            float dt = Time.deltaTime;

            // 파괴된 Zone 정리
            for (int i = _activeZones.Count - 1; i >= 0; i--)
            {
                if (_activeZones[i] == null)
                    _activeZones.RemoveAt(i);
            }

            CameraZone zone = ActiveZone;
            float blendTime = zone != null ? zone.BlendTime : _lastBlendTime;
            if (zone != null)
                _lastBlendTime = zone.BlendTime > 0f ? zone.BlendTime : defaultBlendTime;

            _distance.Update(zone != null && zone.OverrideDistance, zone != null ? zone.Distance : 0f, blendTime, dt, false);
            _pitch.Update(zone != null && zone.OverridePitchOffset, zone != null ? zone.PitchOffset : 0f, blendTime, dt, true);
            _yaw.Update(zone != null && zone.OverrideYawOffset, zone != null ? zone.YawOffset : 0f, blendTime, dt, true);
            _height.Update(zone != null && zone.OverrideHeightOffset, zone != null ? zone.HeightOffset : 0f, blendTime, dt, false);
            _followOffset.Update(zone != null && zone.OverrideFollowOffset, zone != null ? zone.FollowOffset : Vector3.zero, blendTime, dt);
        }

        // ─────────────── 노드 조회용 API ───────────────

        /// <summary>SpringArm 기본 거리에 Zone 거리 Override를 블렌드해 반환.</summary>
        public float EvaluateDistance(float baseDistance)
            => Mathf.Lerp(baseDistance, _distance.Value, _distance.Weight);

        /// <summary>Pivot에 더할 Pitch Offset (deg).</summary>
        public float PitchOffset => _pitch.Value * _pitch.Weight;

        /// <summary>Pivot에 더할 Yaw Offset (deg).</summary>
        public float YawOffset => _yaw.Value * _yaw.Weight;

        /// <summary>Follow 위치에 더할 오프셋 (FollowOffset + HeightOffset 합성).</summary>
        public Vector3 EvaluateFollowOffset()
            => _followOffset.Value * _followOffset.Weight
             + Vector3.up * (_height.Value * _height.Weight);
    }
}
