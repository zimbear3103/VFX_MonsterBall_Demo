using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 패링 연출 계층 — 패링 성공 시 카메라 시퀀스 + 화면 임팩트(줌/쉐이크/히트스탑)를 재생한다.
    ///
    /// 방어 판정(창/투혼/프레임스톱/데미지 감소)은 CharacterDefense가 소유하고,
    /// 이 컴포넌트는 "보이는 것"만 담당한다: CharacterDefense.ParrySucceeded 이벤트를 구독해
    /// 방어자 본인의 CharacterCinematics에서 패링 카메라(일반/퍼펙트 각각 좌/우 쌍, 방어자 Facing 선택)를 재생하고,
    /// 그 위에 카메라 FOV 줌펀치 + 카메라 쉐이크 + 전역 히트스탑(타임스케일)을 얹는다.
    ///
    /// 구 프로젝트 규약 유지: 카메라 시퀀스는 웨이포인트 데이터로 프리팹에 배선하고,
    /// 좌/우 선택은 시전자(=여기선 방어자) Facing으로 한다. 잡기/처형 연출과 동일한
    /// CameraSequence + CameraSequenceRunner 경로를 쓴다(Cinemachine 미사용).
    ///
    /// 화면 임팩트는 이미 존재하는 공용 백엔드를 그대로 호출한다:
    ///   - 쉐이크: CameraShakeService.Shake (ShakeCameraNode 채널)
    ///   - 히트스탑: Aiara.FeedbackTime.SlowMotion (전역 단일 라이터, 강한 값 우선, 리얼타임)
    ///   - 줌펀치: CameraZoomService.Punch (LensCameraNode 채널 — Camera.main 직접 조작은 리그가 덮어써 무효)
    /// 히트스탑/쉐이크/줌 모두 unscaled(리얼타임) 기준이라 타임스케일이 0에 가까워도 정상 동작한다.
    ///
    /// 패링은 별도 상태가 없으므로(Guard 상태에서 판정) 상태 전이가 아닌 이벤트로 훅한다.
    /// 시퀀스는 웨이포인트 소모 후 자동 복귀(HoldUntilStopped=false 권장) — 별도 종료 신호 불필요.
    /// </summary>
    [RequireComponent(typeof(Character))]
    [AddComponentMenu("Yeolha/Combat/Character Parry Presentation")]
    public class CharacterParryPresentation : MonoBehaviour
    {
        /// <summary>패링 1회의 화면 임팩트 튜닝 묶음. 일반/퍼펙트 각각 하나씩 가진다.</summary>
        [System.Serializable]
        private class ParryFeel
        {
            [Tooltip("히트스탑 목표 타임스케일 (0=완전 정지, 1=느려짐 없음)")]
            [Range(0f, 5f)] public float HitstopScale = 0.05f;
            [Tooltip("히트스탑 지속 시간(초, 리얼타임). 0이면 히트스탑 없음")]
            public float HitstopDuration = 0.06f;

            [Tooltip("카메라 쉐이크 축별 진폭(구버전 Cinemachine Impulse Velocity 등가)")]
            public Vector3 ShakeVelocity = new Vector3(0.2f, 0.2f, 0f);
            [Tooltip("카메라 쉐이크 지속 시간(초). 0이면 쉐이크 없음")]
            public float ShakeDuration = 0.2f;

            [Tooltip("카메라 줌인 각도(도). 현재 FOV에서 이만큼 줄였다 되돌린다. 0이면 줌 없음")]
            public float ZoomInDegrees = 3f;
            [Tooltip("줌인 최대 상태 유지 시간(초, 리얼타임)")]
            public float ZoomHold = 0.05f;
        }

        [Header("Cinematic")]
        [Tooltip("일반 패링 시 CharacterCinematics의 Parry Camera(일반 패링 전용 쌍)를 재생할지. 퍼펙트는 항상 Perfect Parry Camera")]
        [SerializeField] private bool playCameraOnNormalParry = true;

        [Header("Impact - Toggles")]
        [Tooltip("패링 성공 시 전역 히트스탑(타임스케일) 사용 여부")]
        [SerializeField] private bool enableHitstop = true;
        [Tooltip("패링 성공 시 카메라 쉐이크 사용 여부")]
        [SerializeField] private bool enableShake = true;
        [Tooltip("패링 성공 시 카메라 FOV 줌펀치 사용 여부")]
        [SerializeField] private bool enableZoom = true;

        [Header("Impact - Normal Parry")]
        [Tooltip("일반 패링 화면 임팩트 값(약하게)")]
        [SerializeField] private ParryFeel normalParry = new ParryFeel();

        [Header("Impact - Perfect Parry")]
        [Tooltip("퍼펙트 패링 화면 임팩트 값(강하게)")]
        [SerializeField] private ParryFeel perfectParry = new ParryFeel
        {
            HitstopScale = 0f,
            HitstopDuration = 0.12f,
            ShakeVelocity = new Vector3(0.45f, 0.45f, 0f),
            ShakeDuration = 0.35f,
            ZoomInDegrees = 6f,
            ZoomHold = 0.12f,
        };

        [Header("Impact - Shared Timing")]
        [Tooltip("히트스탑 진입 이징(초). 0이면 즉시 스냅(진짜 히트스탑)")]
        [SerializeField] private float hitstopEaseIn = 0f;
        [Tooltip("히트스탑 복귀 이징(초)")]
        [SerializeField] private float hitstopEaseOut = 0.08f;
        [Tooltip("카메라 쉐이크 주파수(Hz)")]
        [SerializeField] private float shakeFrequency = 25f;
        [Tooltip("줌인에 걸리는 시간(초, 리얼타임)")]
        [SerializeField] private float zoomInDuration = 0.04f;
        [Tooltip("줌아웃(복귀)에 걸리는 시간(초, 리얼타임)")]
        [SerializeField] private float zoomOutDuration = 0.18f;

        private Character _character;
        private CharacterCinematics _cinematics;
        private CameraSequence _camera;
        private bool _subscribed;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _cinematics = GetComponent<CharacterCinematics>();
        }

        private void OnEnable()
        {
            // Defense는 Character가 소유하는 직렬화 클래스 — Bind 이후 접근 가능.
            var defense = _character != null ? _character.Defense : null;
            if (defense != null)
            {
                defense.ParrySucceeded += OnParrySucceeded;
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_subscribed && _character != null && _character.Defense != null)
                _character.Defense.ParrySucceeded -= OnParrySucceeded;
            _subscribed = false;
            StopCamera();
        }

        private void OnParrySucceeded(bool perfect)
        {
            // 1) 시네마틱 카메라(웨이포인트) — 퍼펙트/일반 각각 전용 시퀀스 쌍(CharacterCinematics)을 재생.
            if ((perfect || playCameraOnNormalParry) && _cinematics != null)
            {
                bool facingRight = _character == null || _character.Movement == null || _character.Movement.FacingRight;
                _camera = _cinematics.ResolveParryCamera(perfect, facingRight);
                _camera?.Play();
            }

            // 2) 화면 임팩트(줌/쉐이크/히트스탑) — 일반/퍼펙트 모두, 퍼펙트가 더 강하게. 시네마틱 위에 얹는다.
            PlayImpact(perfect);
        }

        /// <summary>패링 화면 임팩트 재생 — 공용 백엔드(FeedbackTime/CameraShakeService/FOV) 직접 호출.</summary>
        private void PlayImpact(bool perfect)
        {
            ParryFeel feel = perfect ? perfectParry : normalParry;
            if (feel == null) return;

            if (enableHitstop && feel.HitstopDuration > 0f)
                Aiara.FeedbackTime.SlowMotion(feel.HitstopScale, feel.HitstopDuration, hitstopEaseIn, hitstopEaseOut);

            if (enableShake && feel.ShakeDuration > 0f && feel.ShakeVelocity.sqrMagnitude > 0f)
                CameraShakeService.Shake(feel.ShakeVelocity, feel.ShakeDuration, shakeFrequency, unscaledTime: true);

            // 줌: Camera.main FOV를 직접 쓰면 LensCameraNode가 매 프레임 baseFov로 덮어써 무효가 된다
            // (오버레이 UI 카메라(MatchCameraProjection)만 그 사이 값을 복사해 체력바만 줌돼 보이던 원인).
            // 반드시 카메라 리그 채널(CameraZoomService → LensCameraNode)로 요청한다. 실시간(unscaled) 기준.
            if (enableZoom && feel.ZoomInDegrees > 0f)
                CameraZoomService.Punch(-feel.ZoomInDegrees, zoomInDuration, feel.ZoomHold, zoomOutDuration);
        }

        private void StopCamera()
        {
            _camera?.Stop();
            _camera = null;
        }
    }
}
