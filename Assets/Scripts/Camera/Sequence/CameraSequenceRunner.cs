using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// CameraSequence 실행기 — 씬 자동 생성 싱글턴.
    ///
    /// CameraRig(ExecutionOrder 100)의 노드 평가가 끝난 뒤(200) 최종 카메라 pose를 시퀀스 값으로
    /// 덮어쓴다. rig는 계속 평가되므로 종료 시 현재 rig pose로 블렌드해 자연스럽게 복귀한다
    /// (구버전 CameraSequencePlayer의 StopFollowing/StartFollowing 대체 — rig 상태를 건드리지 않아
    /// 어떤 시점에 중단돼도 카메라가 고정되는 사고가 없다).
    ///
    /// 안전망: 재생 중 시퀀스/카메라가 파괴되거나 웨이포인트가 사라지면 즉시 복귀를 시작한다.
    /// TimeScale 무관(unscaledDeltaTime) — 슬로모션 중에도 카메라 연출 속도 유지(구버전 동일).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class CameraSequenceRunner : MonoBehaviour
    {
        private enum Phase { Idle, Playing, Returning }

        private static CameraSequenceRunner _instance;

        /// <summary>현재 재생 중인 시퀀스. 없으면 null.</summary>
        public static CameraSequence Current =>
            _instance != null && _instance._phase == Phase.Playing ? _instance._sequence : null;

        /// <summary>시퀀스 재생(전역). 재생 중이면 새 시퀀스로 인계.</summary>
        public static void Play(CameraSequence sequence)
        {
            if (sequence == null || sequence.Waypoints == null || sequence.Waypoints.Count == 0) return;
            EnsureInstance();
            _instance.BeginPlay(sequence);
        }

        /// <summary>현재 시퀀스 종료 — 기본 카메라(rig pose)로 블렌드 복귀 시작.</summary>
        public static void StopCurrent()
        {
            if (_instance != null) _instance.BeginReturn();
        }

        /// <summary>모든 연출 즉시 중단 + 카메라를 rig pose로 즉시 반환(씬 전환 등 비상 복구).</summary>
        public static void StopImmediate()
        {
            if (_instance == null) return;
            _instance.RestoreFovImmediate();
            _instance.RestoreCameraLocalPose(_instance.Cam); // 로컬 오프셋 즉시 복원
            _instance._phase = Phase.Idle;
            _instance._sequence = null;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("CameraSequenceRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CameraSequenceRunner>();
        }

        private Phase _phase = Phase.Idle;
        private CameraSequence _sequence;
        private int _index;
        private float _timer;
        private bool _holding;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private float _fromFov;
        private float _baseFov = -1f;      // 시퀀스 시작 전 FOV (복원용)
        private float _returnTimer;
        private float _returnDuration;
        private Vector3 _returnFromPos;
        private Quaternion _returnFromRot;
        private float _returnFromFov;
        // 리그 pose 기준선: 시퀀스가 cam.transform(월드)을 직접 쓰면 Camera의 '로컬' 오프셋이 오염되고,
        // 리그는 부모 노드만 갱신하므로 아무도 이를 복원하지 않는다. 시작 전 로컬 pose를 저장해 두고
        // 복귀 목표를 부모×원래 로컬(진짜 리그 pose)로 계산, 종료 시 로컬 pose를 원상 복원한다.
        private Transform _camParent;
        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private bool _hasBaseline;
        // 웨이포인트 흔들림 — 러너가 pose를 덮어쓰므로 리그 ShakeNode 대신 여기서 오프셋을 얹는다.
        private float _shakeTimer = -1f;   // 음수 = 비활성
        private float _shakeDuration;
        private float _shakeAmplitude;
        private float _shakeFrequency;
        private float _shakeSeed;
        private int _shakeFiredIndex = -1; // 같은 웨이포인트에서 중복 발화 방지

        private UnityEngine.Camera Cam => UnityEngine.Camera.main;

        /// <summary>이번 프레임의 진짜 리그 pose (부모 노드 체인 × 시퀀스 시작 전 로컬 오프셋).</summary>
        private void GetRigPose(UnityEngine.Camera cam, out Vector3 pos, out Quaternion rot)
        {
            if (_hasBaseline && _camParent != null && cam.transform.parent == _camParent)
            {
                pos = _camParent.TransformPoint(_baseLocalPos);
                rot = _camParent.rotation * _baseLocalRot;
                return;
            }
            // 기준선 없음/부모 변경(씬 전환 등) — 현재 값 폴백 (기존 동작).
            pos = cam.transform.position;
            rot = cam.transform.rotation;
        }

        /// <summary>Camera 로컬 트랜스폼을 시퀀스 시작 전 값으로 복원 — 이후 리그가 온전히 소유.</summary>
        private void RestoreCameraLocalPose(UnityEngine.Camera cam)
        {
            if (_hasBaseline && cam != null && _camParent != null && cam.transform.parent == _camParent)
            {
                cam.transform.localPosition = _baseLocalPos;
                cam.transform.localRotation = _baseLocalRot;
            }
            _hasBaseline = false;
            _camParent = null;
        }

        private void BeginPlay(CameraSequence sequence)
        {
            var cam = Cam;
            if (cam == null) return;
            // 첫 시작(Idle)에서만 로컬 기준선 캡처 — 인계 재생/복귀 중 재시작은 원래 기준선 유지.
            if (!_hasBaseline)
            {
                _camParent = cam.transform.parent;
                _baseLocalPos = cam.transform.localPosition;
                _baseLocalRot = cam.transform.localRotation;
                _hasBaseline = _camParent != null;
            }
            _sequence = sequence;
            _index = 0;
            _timer = 0f;
            _holding = false;
            _fromPos = cam.transform.position;
            _fromRot = cam.transform.rotation;
            _fromFov = cam.fieldOfView;
            if (_baseFov < 0f) _baseFov = cam.fieldOfView; // 인계 재생 시 원래 FOV 유지
            _shakeTimer = -1f;
            _shakeFiredIndex = -1;
            _phase = Phase.Playing;
        }

        private void BeginReturn()
        {
            if (_phase != Phase.Playing || Cam == null)
            {
                if (_phase == Phase.Playing) { _phase = Phase.Idle; _sequence = null; _hasBaseline = false; _camParent = null; }
                return;
            }
            _returnFromPos = Cam.transform.position;
            _returnFromRot = Cam.transform.rotation;
            _returnFromFov = Cam.fieldOfView;
            _returnDuration = _sequence != null ? Mathf.Max(0.01f, _sequence.ReturnBlendTime) : 0.3f;
            _returnTimer = 0f;
            _phase = Phase.Returning;
            _sequence = null;
        }

        private void RestoreFovImmediate()
        {
            if (Cam != null && _baseFov > 0f) Cam.fieldOfView = _baseFov;
            _baseFov = -1f;
        }

        private void LateUpdate()
        {
            if (_phase == Phase.Idle) return;
            var cam = Cam;
            if (cam == null) { _phase = Phase.Idle; _sequence = null; _baseFov = -1f; _hasBaseline = false; _camParent = null; return; }

            float dt = Time.unscaledDeltaTime;

            if (_phase == Phase.Playing)
            {
                if (_sequence == null || _sequence.Waypoints.Count == 0) { BeginReturn(); return; }
                TickPlaying(cam, dt);
            }
            else if (_phase == Phase.Returning)
            {
                TickReturning(cam, dt);
            }
        }

        private void TickPlaying(UnityEngine.Camera cam, float dt)
        {
            var wps = _sequence.Waypoints;
            if (_index >= wps.Count)
            {
                // 웨이포인트 소모 — holdUntilStopped면 마지막 지점 유지, 아니면 복귀.
                if (!_sequence.HoldUntilStopped) { BeginReturn(); return; }
                _index = wps.Count - 1;
                _holding = true;
            }

            var wp = wps[_index];
            if (wp == null || wp.point == null) { BeginReturn(); return; }

            _timer += dt;
            float travel = Mathf.Max(0.01f, wp.travelTime);
            float t = _holding ? 1f : Mathf.Clamp01(_timer / travel);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            // 목표는 매 프레임 재샘플(웨이포인트가 캐릭터 자식이라 함께 움직임 — 구버전 동일). 미러 재생 반영.
            _sequence.GetWaypointPose(wp, out Vector3 targetPos, out Quaternion wpRot);
            cam.transform.position = Vector3.Lerp(_fromPos, targetPos, eased);

            // 회전: 웨이포인트 Transform 회전(구버전 방식) 또는 LookTarget 추적.
            Quaternion want;
            if (_sequence.UseWaypointRotation)
            {
                want = wpRot;
            }
            else
            {
                Vector3 lookPoint = _sequence.TransformLookPoint(_sequence.LookTarget.position + _sequence.LookOffset);
                Vector3 dir = lookPoint - cam.transform.position;
                want = dir.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(dir.normalized, Vector3.up)
                    : cam.transform.rotation;
            }
            float rotLerp = _holding ? 1f - Mathf.Exp(-_sequence.LookRotateSpeed * dt) : eased;
            cam.transform.rotation = Quaternion.Slerp(_holding ? cam.transform.rotation : _fromRot, want, rotLerp);

            // FOV
            float targetFov = wp.fov > 0f ? wp.fov : (_baseFov > 0f ? _baseFov : cam.fieldOfView);
            cam.fieldOfView = _holding ? Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-6f * dt))
                                       : Mathf.Lerp(_fromFov, targetFov, eased);

            // 웨이포인트 도착 시 흔들림 발화 + 진행 중 오프셋 적용.
            if (t >= 1f && wp.shake && _shakeFiredIndex != _index)
            {
                _shakeFiredIndex = _index;
                _shakeTimer = 0f;
                _shakeDuration = Mathf.Max(0.01f, wp.shakeDuration);
                _shakeAmplitude = wp.shakeAmplitude;
                _shakeFrequency = Mathf.Max(0.01f, wp.shakeFrequency);
                _shakeSeed = Random.value * 100f;
            }
            ApplyShakeOffset(cam, dt);

            if (_holding) return;

            if (t >= 1f && _timer >= travel + Mathf.Max(0f, wp.holdTime))
            {
                // 다음 웨이포인트로.
                _index++;
                _timer = 0f;
                _fromPos = cam.transform.position;
                _fromRot = cam.transform.rotation;
                _fromFov = cam.fieldOfView;
            }
        }

        /// <summary>진행 중인 흔들림 오프셋을 카메라 pose 위에 얹는다 (Perlin, 시간 감쇠).</summary>
        private void ApplyShakeOffset(UnityEngine.Camera cam, float dt)
        {
            if (_shakeTimer < 0f) return;
            _shakeTimer += dt;
            if (_shakeTimer >= _shakeDuration) { _shakeTimer = -1f; return; }

            float decay = 1f - _shakeTimer / _shakeDuration;
            float px = (Mathf.PerlinNoise(_shakeSeed, _shakeTimer * _shakeFrequency) - 0.5f) * 2f;
            float py = (Mathf.PerlinNoise(_shakeSeed + 17.3f, _shakeTimer * _shakeFrequency) - 0.5f) * 2f;
            cam.transform.position += (cam.transform.right * px + cam.transform.up * py) * (_shakeAmplitude * decay);
        }

        private void TickReturning(UnityEngine.Camera cam, float dt)
        {
            // 목표 = 진짜 리그 pose(부모 노드 체인 × 시퀀스 시작 전 로컬 오프셋).
            // cam.transform을 그대로 쓰면 안 된다 — 시퀀스가 남긴 로컬 오프셋이 섞여 있어
            // 복귀해도 원래 카메라 위치/회전(스플라인 뷰)으로 돌아가지 않는다.
            GetRigPose(cam, out Vector3 rigPos, out Quaternion rigRot);
            float rigFov = _baseFov > 0f ? _baseFov : cam.fieldOfView;

            _returnTimer += dt;
            float t = Mathf.Clamp01(_returnTimer / _returnDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            cam.transform.position = Vector3.Lerp(_returnFromPos, rigPos, eased);
            cam.transform.rotation = Quaternion.Slerp(_returnFromRot, rigRot, eased);
            cam.fieldOfView = Mathf.Lerp(_returnFromFov, rigFov, eased);

            if (t >= 1f)
            {
                RestoreFovImmediate();
                RestoreCameraLocalPose(cam); // 로컬 오프셋 원상 복원 — 이후 리그가 온전히 카메라 소유
                _phase = Phase.Idle;
            }
        }
    }
}
