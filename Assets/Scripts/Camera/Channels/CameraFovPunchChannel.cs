using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// LensCameraNode에 주입되는 FOV 펀치(줌인/줌아웃) 채널.
    /// 구버전 HitImpactDirector.ZoomRoutine(Cinemachine Lens.FieldOfView 직접 조작)을
    /// ver2 카메라 노드 구조로 대체하는 구현.
    ///
    /// fovDelta 음수 = 줌인(타격감 펀치), 양수 = 줌아웃.
    /// EaseIn → Hold → EaseOut 순서로 baseFov 대비 delta를 가감한다.
    /// 슬로우모션(FeedbackTime)과 함께 쓰이므로 시간은 실시간(unscaled) 기준.
    /// </summary>
    public class CameraFovPunchChannel : ICameraChannel
    {
        private readonly float _fovDelta;
        private readonly float _easeIn;
        private readonly float _hold;
        private readonly float _easeOut;
        private float _time;

        public CameraFovPunchChannel(float fovDelta, float easeIn, float hold, float easeOut)
        {
            _fovDelta = fovDelta;
            _easeIn = Mathf.Max(0f, easeIn);
            _hold = Mathf.Max(0f, hold);
            _easeOut = Mathf.Max(0f, easeOut);
        }

        public bool IsFinished => _time >= _easeIn + _hold + _easeOut;

        public void Sample(float dt, ref CameraNodeState state)
        {
            // 슬로우모션 중에도 연출 길이가 실시간과 일치하도록 unscaled 사용
            // (CameraRig가 넘기는 dt는 timeScale 영향을 받는다)
            _time += Time.unscaledDeltaTime;
            if (IsFinished) return;

            float weight;
            if (_time < _easeIn)
                weight = _easeIn > 0f ? _time / _easeIn : 1f;
            else if (_time < _easeIn + _hold)
                weight = 1f;
            else
                weight = _easeOut > 0f ? 1f - (_time - _easeIn - _hold) / _easeOut : 0f;

            state.Fov = Mathf.Clamp(state.Fov + _fovDelta * Mathf.Clamp01(weight), 1f, 179f);
        }
    }

    /// <summary>
    /// 지속형(홀드) FOV 줌의 해제 핸들. 호출부(예: 차징)가 카메라 구조를 몰라도
    /// 이 핸들의 Release()만 호출하면 줌이 easeOut으로 원복된다. Release 전까지는 목표 FOV를 유지한다.
    /// </summary>
    public interface ICameraZoomHold
    {
        /// <summary>줌 유지 종료 — 현재 가중치에서 easeOut 시간 동안 기본 FOV로 복귀한다.</summary>
        void Release();
    }

    /// <summary>
    /// LensCameraNode에 주입되는 지속형(홀드) FOV 줌 채널.
    /// 펀치(고정 길이)와 달리 Release()가 호출될 때까지 목표 FOV를 유지한다:
    ///   EaseIn(러프 커브로 서서히 줌인) → Hold(무한 유지) → Release() → EaseOut(현재 가중치에서 원복).
    /// 차징(윈드업 정지)처럼 유지 시간이 가변인 연출에 쓴다. 슬로우모션과 함께 쓰이므로 실시간(unscaled) 기준.
    /// </summary>
    public class CameraHoldZoomChannel : ICameraChannel, ICameraZoomHold
    {
        private readonly float _fovDelta;
        private readonly float _easeIn;
        private readonly float _easeOut;
        private float _time;
        private bool _released;
        private float _releaseWeight; // 릴리즈 시점의 가중치 — easeOut을 현재 값에서 시작(끊김 방지)
        private float _releaseElapsed;
        private bool _finished;

        public CameraHoldZoomChannel(float fovDelta, float easeIn, float easeOut)
        {
            _fovDelta = fovDelta;
            _easeIn = Mathf.Max(0f, easeIn);
            _easeOut = Mathf.Max(0f, easeOut);
        }

        public bool IsFinished => _finished;

        /// <summary>현재 진입 가중치(0~1)를 러프(스무스) 커브로 계산.</summary>
        private float RiseWeight()
        {
            float p = _easeIn > 0f ? Mathf.Clamp01(_time / _easeIn) : 1f;
            return Mathf.SmoothStep(0f, 1f, p);
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            _releaseWeight = RiseWeight(); // 지금 가중치에서 부드럽게 빠져나간다
            _releaseElapsed = 0f;
            if (_easeOut <= 0f) _finished = true;
        }

        public void Sample(float dt, ref CameraNodeState state)
        {
            // 슬로우모션 중에도 실시간 기준으로 진행 (차징 커밋 후 timeScale 감속과 무관하게 일정한 연출)
            _time += Time.unscaledDeltaTime;
            if (_finished) return;

            float weight;
            if (!_released)
            {
                weight = RiseWeight();
            }
            else
            {
                _releaseElapsed += Time.unscaledDeltaTime;
                float q = _easeOut > 0f ? Mathf.Clamp01(_releaseElapsed / _easeOut) : 1f;
                weight = _releaseWeight * (1f - Mathf.SmoothStep(0f, 1f, q));
                if (q >= 1f) { _finished = true; return; }
            }

            state.Fov = Mathf.Clamp(state.Fov + _fovDelta * weight, 1f, 179f);
        }
    }

    /// <summary>
    /// FOV 펀치(줌) 요청 진입점. CameraShakeService와 동일 패턴 —
    /// 호출부가 카메라 구조를 몰라도 Punch(delta, ...)만 호출하면 LensCameraNode에 채널이 등록된다.
    /// </summary>
    public static class CameraZoomService
    {
        private static LensCameraNode _node;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _node = null;

        private static bool EnsureNode()
        {
            if (_node == null)
                _node = Object.FindFirstObjectByType<LensCameraNode>();
            return _node != null; // 카메라 리그 없는 씬(테스트 등)에서는 조용히 무시
        }

        /// <summary>
        /// FOV 펀치 재생. fovDelta 음수 = 줌인, 양수 = 줌아웃.
        /// 구버전 HitImpactDirector 등가: 일반 히트 (-2, 0.1, 0.1, 0.1) / 강 히트 (-4, 0.1, 0.1, 0.1).
        /// </summary>
        public static void Punch(float fovDelta, float easeIn, float hold, float easeOut)
        {
            if (Mathf.Approximately(fovDelta, 0f)) return;
            if (!EnsureNode()) return;
            _node.AddChannel(new CameraFovPunchChannel(fovDelta, easeIn, hold, easeOut));
        }

        /// <summary>
        /// 지속형 줌 시작. fovDelta 음수 = 줌인. easeIn 동안 서서히 줌인 후 목표 FOV를 유지하며,
        /// 반환된 핸들의 Release()를 호출하면 easeOut 동안 원복한다(차징 홀드 등 가변 길이 연출용).
        /// 카메라 리그가 없거나 delta가 0이면 null을 반환하므로 호출부는 null 체크만 하면 된다.
        /// </summary>
        public static ICameraZoomHold BeginHold(float fovDelta, float easeIn, float easeOut)
        {
            if (Mathf.Approximately(fovDelta, 0f)) return null;
            if (!EnsureNode()) return null;
            var channel = new CameraHoldZoomChannel(fovDelta, easeIn, easeOut);
            _node.AddChannel(channel);
            return channel;
        }
    }
}
