using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 잡기/처형 연출용 카메라 웨이포인트 시퀀스 — 구 프로젝트 CameraSequencePlayer 이관(ver2 재작성).
    ///
    /// 구버전과 동일한 규약:
    ///   - 웨이포인트는 이 컴포넌트의 자식 Transform(피격자 프리팹 안에 배선) → 캐릭터가 움직여도 매 프레임 재샘플.
    ///   - 좌/우 쌍으로 만들고 시전자 Facing으로 선택한다(선택은 호출자 몫 — CharacterCinematics).
    ///   - 재생/카메라 구동은 CameraSequenceRunner가 담당(Cinemachine/CameraRig 노드 체인 미사용,
    ///     rig 평가 후 최종 카메라 pose를 덮어쓰고 종료 시 rig pose로 블렌드 복귀).
    /// </summary>
    [AddComponentMenu("Yeolha/Camera/Camera Sequence")]
    public class CameraSequence : MonoBehaviour
    {
        [System.Serializable]
        public class Waypoint
        {
            [Tooltip("카메라가 이동할 지점(자식 Transform 권장 — 캐릭터를 따라 움직인다)")]
            public Transform point;
            [Tooltip("이 지점까지 이동하는 시간(초). 첫 웨이포인트는 진입 블렌드 시간")]
            public float travelTime = 0.3f;
            [Tooltip("도착 후 머무는 시간(초)")]
            public float holdTime = 0f;
            [Tooltip("이 구간의 FOV. 0 이하 = 기존 FOV 유지")]
            public float fov = 0f;

            [Header("Shake")]
            [Tooltip("이 웨이포인트 도착 순간 카메라 흔들림 재생 (구버전 웨이포인트 Shake 체크 대응)")]
            public bool shake = false;
            [Tooltip("흔들림 세기(m)")]
            public float shakeAmplitude = 0.15f;
            [Tooltip("흔들림 주파수(Hz)")]
            public float shakeFrequency = 25f;
            [Tooltip("흔들림 지속 시간(초) — 시간에 따라 감쇠")]
            public float shakeDuration = 0.25f;
        }

        [Header("Waypoints")]
        [Tooltip("순서대로 재생될 웨이포인트 목록")]
        [SerializeField] private List<Waypoint> waypoints = new List<Waypoint>();

        [Header("Rotation")]
        [Tooltip("true = 웨이포인트 Transform의 회전을 카메라 회전으로 사용 (구버전 방식). false = LookTarget을 바라본다")]
        [SerializeField] private bool useWaypointRotation = true;

        [Header("Look At")]
        [Tooltip("useWaypointRotation=false일 때 카메라가 바라볼 대상. 비우면 이 컴포넌트의 루트(캐릭터)")]
        [SerializeField] private Transform lookTarget;
        [Tooltip("바라볼 지점 오프셋(대상 로컬 아님, 월드 Y 위주)")]
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);
        [Tooltip("시선 회전 속도(클수록 즉각적)")]
        [SerializeField] private float lookRotateSpeed = 12f;

        [Header("End")]
        [Tooltip("마지막 웨이포인트 후 기본 카메라(rig)로 복귀하는 블렌드 시간(초)")]
        [SerializeField] private float returnBlendTime = 0.35f;
        [Tooltip("웨이포인트를 전부 소모해도 Stop()이 불릴 때까지 마지막 지점을 유지한다 (처형처럼 종료 시점이 가변일 때)")]
        [SerializeField] private bool holdUntilStopped = true;

        public IReadOnlyList<Waypoint> Waypoints => waypoints;
        public bool UseWaypointRotation => useWaypointRotation;
        public Transform LookTarget => lookTarget != null ? lookTarget : transform;
        public Vector3 LookOffset => lookOffset;
        public float LookRotateSpeed => lookRotateSpeed;
        public float ReturnBlendTime => returnBlendTime;
        public bool HoldUntilStopped => holdUntilStopped;

        /// <summary>재생 중인가 (Runner 기준).</summary>
        public bool IsPlaying => CameraSequenceRunner.Current == this;

        // ── 미러 재생 — 오른쪽 기준으로만 제작하고 Flip 시 자동 반전 ──
        // 반대쪽 시퀀스가 배선돼 있지 않으면 CharacterCinematics가 이 시퀀스를 미러로 재생한다.
        // 스플라인 진행축을 법선으로 하는 수직 평면(피벗=캐릭터 루트 통과) 기준 반사 — 매 프레임 계산이라
        // 캐릭터 이동/스테이지 굴곡을 그대로 따라간다.
        private bool _mirror;
        private Transform _mirrorPivot;
        private CharacterMovement _mirrorMovement;

        // ── 본 변위 추종 — 웨이포인트는 캐릭터 루트의 자식이라 잡기/처형처럼 본(bone)으로만 움직이는
        // 모션에서는 루트에 남는다. 부모 캐릭터의 CharacterCameraAnchor가 계산한 루트→몸 XZ 변위를
        // 웨이포인트·시선·미러 피벗에 더해 카메라가 모델을 따라가게 한다 (없으면 0 = 기존 동작).
        [Header("Body Follow")]
        [Tooltip("부모 캐릭터의 CharacterCameraAnchor(힙 본 변위)를 웨이포인트에 더한다. 끄면 루트 기준(기존 동작)")]
        [SerializeField] private bool followBodyOffset = true;

        private CharacterCameraAnchor _bodyAnchor;
        private bool _bodyAnchorResolved;

        /// <summary>이번 프레임의 루트→몸 XZ 변위. followBodyOffset이 꺼졌거나 앵커가 없으면 0.</summary>
        public Vector3 BodyOffset
        {
            get
            {
                if (!followBodyOffset) return Vector3.zero;
                if (!_bodyAnchorResolved)
                {
                    _bodyAnchorResolved = true;
                    _bodyAnchor = GetComponentInParent<CharacterCameraAnchor>();
                }
                return _bodyAnchor != null && _bodyAnchor.isActiveAndEnabled ? _bodyAnchor.CurrentOffset : Vector3.zero;
            }
        }

        /// <summary>미러 재생 설정. Resolve 시 호출자가 켜고/끈다 (재생 간 상태 잔류 방지).</summary>
        public void SetMirror(bool active, Transform pivot = null, CharacterMovement movement = null)
        {
            _mirror = active && pivot != null;
            _mirrorPivot = pivot;
            _mirrorMovement = movement;
        }

        private Vector3 MirrorNormal()
        {
            Vector3 n = _mirrorMovement != null ? _mirrorMovement.SplineForward
                : (_mirrorPivot != null ? _mirrorPivot.forward : Vector3.right);
            n.y = 0f;
            return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.right;
        }

        /// <summary>미러 피벗 — 캐릭터 루트 + 몸 변위(미러 캐릭터가 이 시퀀스의 부모일 때). 몸이 루트에서 떨어져도 몸 기준으로 반사.</summary>
        private Vector3 MirrorPivotPosition()
        {
            Vector3 p = _mirrorPivot.position;
            if (_mirrorPivot == transform.root || transform.IsChildOf(_mirrorPivot))
                p += BodyOffset;
            return p;
        }

        private Vector3 ReflectPoint(Vector3 p)
        {
            Vector3 n = MirrorNormal();
            Vector3 pivot = MirrorPivotPosition();
            Vector3 d = p - pivot;
            return pivot + d - 2f * Vector3.Dot(d, n) * n;
        }

        /// <summary>웨이포인트의 재생용 pose (몸 변위 + 미러 반영). Runner가 매 프레임 호출.</summary>
        public void GetWaypointPose(Waypoint wp, out Vector3 pos, out Quaternion rot)
        {
            pos = wp.point.position + BodyOffset;
            rot = wp.point.rotation;
            if (!_mirror || _mirrorPivot == null) return;

            Vector3 n = MirrorNormal();
            pos = ReflectPoint(pos);
            Vector3 f = rot * Vector3.forward;
            Vector3 u = rot * Vector3.up;
            f -= 2f * Vector3.Dot(f, n) * n;
            u -= 2f * Vector3.Dot(u, n) * n;
            rot = f.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(f.normalized, u) : rot;
        }

        /// <summary>LookTarget 지점 (몸 변위 + 미러 반영). LookTarget이 이 캐릭터 안의 Transform이면 몸 변위를 더한다.</summary>
        public Vector3 TransformLookPoint(Vector3 worldPoint)
        {
            Vector3 off = BodyOffset; // _bodyAnchor 해석 포함
            Transform look = LookTarget;
            if (look != null && (look == transform.root || look.IsChildOf(transform.root)))
            {
                // 본 계층 안의 Transform(힙 등)은 이미 모션을 따라 움직이므로 변위를 더하지 않는다.
                Transform visual = _bodyAnchor != null ? _bodyAnchor.VisualRoot : null;
                bool boneDriven = visual != null && (look == visual || look.IsChildOf(visual));
                if (!boneDriven) worldPoint += off;
            }
            return _mirror && _mirrorPivot != null ? ReflectPoint(worldPoint) : worldPoint;
        }

        /// <summary>이 시퀀스 재생 시작. 이미 다른 시퀀스가 재생 중이면 인계한다(구버전 TakeOverFromCurrent).</summary>
        public void Play() => CameraSequenceRunner.Play(this);

        /// <summary>이 시퀀스가 재생 중이면 기본 카메라로 복귀 시작.</summary>
        public void Stop()
        {
            if (IsPlaying) CameraSequenceRunner.StopCurrent();
        }
    }
}
