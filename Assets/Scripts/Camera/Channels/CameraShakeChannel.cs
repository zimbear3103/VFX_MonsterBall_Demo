using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// ShakeCameraNode에 주입되는 감쇠 노이즈 셰이크 채널.
    /// 구버전 Cinemachine Impulse 셰이크를 ver2 카메라 노드 구조로 대체하는 구현.
    /// Velocity(축별 진폭) × 감쇠(1-t/duration) 크기의 Perlin 노이즈 오프셋을 더한다.
    /// </summary>
    public class CameraShakeChannel : ICameraChannel
    {
        private readonly Vector3 _amplitude;
        private readonly float _duration;
        private readonly float _frequency;
        private readonly float _seedX;
        private readonly float _seedY;
        private readonly float _seedZ;
        private readonly bool _unscaledTime;
        private float _time;

        public CameraShakeChannel(Vector3 amplitude, float duration, float frequency = 25f, bool unscaledTime = false)
        {
            _amplitude = amplitude;
            _duration = Mathf.Max(0.01f, duration);
            _frequency = Mathf.Max(0.01f, frequency);
            _unscaledTime = unscaledTime;
            _seedX = Random.value * 100f;
            _seedY = Random.value * 100f;
            _seedZ = Random.value * 100f;
        }

        public bool IsFinished => _time >= _duration;

        public void Sample(float dt, ref CameraNodeState state)
        {
            // unscaledTime = 슬로우모션 중에도 실시간 기준으로 감쇠 (적중 연출용)
            _time += _unscaledTime ? Time.unscaledDeltaTime : dt;
            if (IsFinished) return;

            float decay = 1f - Mathf.Clamp01(_time / _duration);
            float t = _time * _frequency;
            // Perlin(-1~1) 노이즈 × 축별 진폭 × 감쇠
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(_seedX, t) * 2f - 1f,
                Mathf.PerlinNoise(_seedY, t) * 2f - 1f,
                Mathf.PerlinNoise(_seedZ, t) * 2f - 1f);
            state.PositionOffset += Vector3.Scale(noise, _amplitude) * decay;
        }
    }

    /// <summary>
    /// 셰이크 요청 진입점. Feedback(MMF)이나 게임 코드가 카메라 구조를 몰라도
    /// Shake(velocity, duration)만 호출하면 ShakeCameraNode에 채널이 등록된다.
    /// </summary>
    public static class CameraShakeService
    {
        private static ShakeCameraNode _node;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _node = null;

        /// <summary>velocity = 축별 진폭(구버전 Cinemachine Impulse Velocity 등가), duration = 지속 시간(초). unscaledTime = 슬로우모션 무시(적중 연출용).</summary>
        public static void Shake(Vector3 velocity, float duration, float frequency = 25f, bool unscaledTime = false)
        {
            if (_node == null)
            {
                _node = Object.FindFirstObjectByType<ShakeCameraNode>();
                if (_node == null) return; // 카메라 리그 없는 씬(테스트 등)에서는 조용히 무시
            }
            // (diag removed)
            _node.AddChannel(new CameraShakeChannel(velocity, duration, frequency, unscaledTime));
        }
    }
}
